using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SackSpace.Core;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Interop;
using SackSpace.Services;
using SackSpace.Theming;
using SackSpace.Widgets;
using Rect = SackSpace.Core.Rect;
using WpfRect = System.Windows.Rect;

namespace SackSpace.Shell;

/// <summary>
/// Everything SackSpace draws on one monitor: the active workspace's panels, the
/// workspace bar (primary monitor), and the edit-mode extras. Coordinates are DIPs
/// from the top-left of the monitor's work area, the same ones PanelState stores.
/// </summary>
public partial class HostView : UserControl
{
    /// <summary>Room kept free under the workspace bar.</summary>
    public const double BarReserve = 72;

    private readonly DispatcherTimer _snackTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Canvas? _layer;
    private string? _workspaceId;
    private WallpaperImage? _wallpaper;
    private Action? _snackAction;

    public HostManager Manager { get; }
    public AppServices Services { get; }
    public MonitorInfo Monitor { get; private set; }
    public WorkspaceBar? Bar => BarSlot.Content as WorkspaceBar;

    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;
    public ImageSource? WallpaperImage => _wallpaper?.Blurred;
    /// <summary>Panels show the live desktop through a tint instead of the blurred copy.</summary>
    public bool ClearGlass => Services.Session.Config.Appearance.ClearGlass;
    public IEnumerable<PanelHost> Panels => _layer?.Children.OfType<PanelHost>() ?? [];
    public Workspace? CurrentWorkspace => Services.Session.Config.Workspaces.FirstOrDefault(w => w.Id == _workspaceId);
    public bool SheetOpen => Overlay.Visibility == Visibility.Visible;

    /// <summary>The work area in DIPs (known before the window is laid out).</summary>
    public Size AreaSize => new(Monitor.WorkArea.Width / Monitor.Scale, Monitor.WorkArea.Height / Monitor.Scale);

    /// <summary>Where panels may go: the work area minus the bar's strip.</summary>
    public Rect LayoutBounds
    {
        get
        {
            var top = Bar is not null ? BarReserve : 0;
            return new Rect(0, top, AreaSize.Width, Math.Max(100, AreaSize.Height - top));
        }
    }

    public HostView(HostManager manager, AppServices services, MonitorInfo monitor)
    {
        InitializeComponent();
        Manager = manager;
        Services = services;
        Monitor = monitor;
        DotGrid.Fill = MakeDots();

        AddWidgetButton.Click += (_, _) => OpenGallery();
        ArrangeButton.Click += (_, _) => ArrangePanels();
        IconsButton.Click += (_, _) => { if (CurrentWorkspace is { } ws) OpenSheet(new DesktopIconsSheet(this, ws)); };
        DoneButton.Click += (_, _) => Manager.SetEditing(false);
        // A click on the blurred backdrop is a click "on the desktop": back to normal.
        Backdrop.MouseLeftButtonDown += (_, e) => { e.Handled = true; Manager.SetRaised(false); };
        OverlayScrim.MouseLeftButtonDown += (_, e) => { e.Handled = true; CloseSheet(); };
        SnackAction.Click += (_, _) => { var a = _snackAction; HideSnackbar(); a?.Invoke(); };
        _snackTimer.Tick += (_, _) => HideSnackbar();
    }

    public void SetMonitor(MonitorInfo monitor)
    {
        Monitor = monitor;
        UpdateWallpaper();
        ReloadPanels();
    }

    // ── Workspaces ───────────────────────────────────────────

    /// <summary>Shows a workspace; with a direction the old one slides out and the new one in.</summary>
    public void ShowWorkspace(Workspace ws, int direction = 0, bool animate = false)
    {
        var old = _layer;
        _workspaceId = ws.Id;
        _layer = BuildLayer(ws);
        if (animate && direction != 0 && Anim.Enabled) _layer.Opacity = 0;
        Layers.Children.Add(_layer);
        if (old is not null) Retire(old);
        if (!animate) return;
        // Every frame of a full-screen layer is redrawn in software: one short fade, nothing sliding.
        if (direction != 0) Anim.Fade(_layer, 1, 160);
        else foreach (var p in Panels) Anim.Pop(p);
    }

    /// <summary>Rebuilds the current workspace's panels (after a move between monitors or an import).</summary>
    public void ReloadPanels()
    {
        var ws = CurrentWorkspace ?? Services.Session.Config.ActiveWorkspace;
        if (ws is not null) ShowWorkspace(ws);
    }

    private Canvas BuildLayer(Workspace ws)
    {
        var canvas = new Canvas();
        if (ShowsIcons) canvas.Children.Add(new DesktopIconGrid(this, ws));
        foreach (var panel in ws.Panels.Where(p => !p.Hidden && Monitor.Owns(p.Monitor, Manager.Monitors)).OrderBy(p => p.Z))
            canvas.Children.Add(MakeHost(panel));
        return canvas;
    }

    /// <summary>Desktop icons go on the primary monitor, like Windows' own on a fresh setup.</summary>
    private bool ShowsIcons => Services.Icons.Enabled && Monitor.IsPrimary;

    /// <summary>Redraws the desktop icons of the workspace shown, leaving its panels alone.</summary>
    public void ReloadIcons()
    {
        if (_layer is null || CurrentWorkspace is not { } ws) return;
        foreach (var old in _layer.Children.OfType<DesktopIconGrid>().ToList()) _layer.Children.Remove(old);
        if (ShowsIcons) _layer.Children.Insert(0, new DesktopIconGrid(this, ws));
        IconsButton.Visibility = ShowsIcons ? Visibility.Visible : Visibility.Collapsed;
    }

    private PanelHost MakeHost(PanelState panel)
    {
        var descriptor = WidgetRegistry.Find(panel.Widget)?.Descriptor
            ?? new WidgetDescriptor(panel.Widget, "Widget no disponible", "", Glyphs.Info, 300, 200, 160, 110, []);
        var host = new PanelHost(panel, descriptor, this);
        // Off-screen after a resolution change: drawn inside, saved only if the user moves it.
        host.Place(LayoutMath.Clamp(panel.Bounds, new Rect(0, 0, AreaSize.Width, AreaSize.Height)));
        Canvas.SetZIndex(host, panel.Z);
        host.SetEditing(Manager.IsEditing);
        return host;
    }

    private PanelHost AddHost(PanelState panel)
    {
        var host = MakeHost(panel);
        _layer?.Children.Add(host);
        return host;
    }

    private void Retire(Canvas old)
    {
        Layers.Children.Remove(old);
        foreach (var p in old.Children.OfType<PanelHost>()) p.DisposeWidget();
    }

    public void DisposeAll()
    {
        foreach (var p in Panels) p.DisposeWidget();
        Layers.Children.Clear();
        _layer = null;
        _snackTimer.Stop();

        BarSlot.Content = null;
    }

    // ── Modes ────────────────────────────────────────────────

    public void SetMode(bool raised, bool editing)
    {
        Backdrop.IsHitTestVisible = raised;
        // Full-screen layers: shown at once, a fade would redraw the whole screen for each frame.
        Anim.Set(Backdrop, raised ? 1 : 0);
        Anim.Set(DotGrid, editing ? 1 : 0);
        if (editing && EditToolbar.Visibility != Visibility.Visible)
        {
            EditToolbar.Visibility = Visibility.Visible;
            Anim.SlideIn(EditToolbar, 0, 30);
        }
        else if (!editing) EditToolbar.Visibility = Visibility.Collapsed;
        IconsButton.Visibility = ShowsIcons ? Visibility.Visible : Visibility.Collapsed;
        foreach (var p in Panels) p.SetEditing(editing);
        Bar?.SetEditing(editing);
        if (!raised) CloseSheet();
    }

    /// <summary>Escape: closes the sheet, then leaves edit mode, then lowers the panels.</summary>
    public void HandleEscape()
    {
        if (SheetOpen) CloseSheet();
        else Manager.SetRaised(false);
    }

    /// <summary>Behind a maximized window: draw nothing until it goes.</summary>
    public void SetResting(bool resting) => Visibility = resting ? Visibility.Hidden : Visibility.Visible;

    public void SetBar(WorkspaceBar? bar)
    {
        if (ReferenceEquals(BarSlot.Content, bar)) return;

        BarSlot.Content = bar;
    }

    // ── Glass ────────────────────────────────────────────────

    public void UpdateWallpaper()
    {
        _wallpaper = Services.Wallpaper.Get(Monitor);
        BackdropBrush.ImageSource = _wallpaper.Blurred;
        BackdropBrush.Viewbox = GlassViewbox(new Rect(0, 0, AreaSize.Width, AreaSize.Height));
        foreach (var p in Panels) p.UpdateGlass();
        Bar?.UpdateGlass();
    }

    /// <summary>
    /// The part of the small blurred wallpaper that lies behind a rectangle of this view.
    /// The bitmap covers the whole monitor; the view covers the work area.
    /// </summary>
    public WpfRect GlassViewbox(Rect r)
    {
        if (_wallpaper is null) return WpfRect.Empty;
        var b = _wallpaper.Bounds;
        var k = _wallpaper.Blurred.PixelWidth / (double)Math.Max(1, b.Width);
        var s = Monitor.Scale;
        return new WpfRect(
            (Monitor.WorkArea.Left - b.Left + r.X * s) * k,
            (Monitor.WorkArea.Top - b.Top + r.Y * s) * k,
            Math.Max(1, r.Width * s * k),
            Math.Max(1, r.Height * s * k));
    }

    // ── Panels ───────────────────────────────────────────────

    public IEnumerable<Rect> OtherBounds(PanelHost except) => Panels.Where(p => p != except).Select(p => p.Bounds);

    public int NextZ() => Panels.Select(p => p.Panel.Z).DefaultIfEmpty(0).Max() + 1;

    public void AddWidget(WidgetDescriptor d)
    {
        var ws = CurrentWorkspace;
        if (ws is null) return;
        var spot = LayoutMath.FindFreeSpot(d.DefaultWidth, d.DefaultHeight, Panels.Select(p => p.Bounds), LayoutBounds);
        var panel = new PanelState { Widget = d.Id, Monitor = Monitor.Key, Bounds = spot, Z = NextZ() };
        ws.Panels.Add(panel);
        Services.Session.MarkDirty();
        Anim.Pop(AddHost(panel));
    }

    /// <summary>A widget asks for another height (the schedule folding up); kept on screen and saved.</summary>
    public void ResizePanel(PanelState panel, double height, double? width = null)
    {
        var host = Panels.FirstOrDefault(p => p.Panel == panel);
        if (host is null) return;
        var r = LayoutMath.Clamp(host.Bounds with
        {
            Height = Math.Max(host.Descriptor.MinHeight, height),
            Width = Math.Max(host.Descriptor.MinWidth, width ?? host.Bounds.Width),
        }, LayoutBounds);
        host.Place(r);
        panel.Bounds = r;
        Services.Session.MarkDirty();
    }

    public void RestorePanel(PanelState panel)
    {
        panel.Hidden = false;
        panel.Monitor = Monitor.Key;
        var others = Panels.Select(p => p.Bounds).ToList();
        var bounds = LayoutMath.Clamp(panel.Bounds, LayoutBounds);
        panel.Bounds = others.Any(o => o.IntersectsWith(bounds))
            ? LayoutMath.FindFreeSpot(bounds.Width, bounds.Height, others, LayoutBounds)
            : bounds;
        panel.Z = NextZ();
        Services.Session.MarkDirty();
        Anim.Pop(AddHost(panel));
    }

    public void HidePanel(PanelHost p)
    {
        var ws = CurrentWorkspace;
        p.Panel.Hidden = true;
        Services.Session.MarkDirty();
        DropPanel(p);
        ShowSnackbar($"«{p.Descriptor.Name}» oculto. Lo tienes en «Añadir widget».", "Deshacer", () =>
        {
            p.Panel.Hidden = false;
            Services.Session.MarkDirty();
            if (ws?.Id == _workspaceId) Anim.Pop(AddHost(p.Panel));
        });
    }

    public void RemovePanel(PanelHost p)
    {
        var ws = CurrentWorkspace;
        if (ws is null) return;
        var index = ws.Panels.IndexOf(p.Panel);
        ws.Panels.Remove(p.Panel);
        Services.Session.MarkDirty();
        DropPanel(p);
        ShowSnackbar($"«{p.Descriptor.Name}» quitado.", "Deshacer", () =>
        {
            ws.Panels.Insert(Math.Clamp(index, 0, ws.Panels.Count), p.Panel);
            Services.Session.MarkDirty();
            if (ws.Id == _workspaceId) Anim.Pop(AddHost(p.Panel));
        });
    }

    private void DropPanel(PanelHost p)
    {
        p.IsHitTestVisible = false;
        Anim.ScaleTo(p, 0.92, 160);
        Anim.Fade(p, 0, 160, () =>
        {
            (p.Parent as Panel)?.Children.Remove(p);
            p.DisposeWidget();
        });
    }

    /// <summary>"Ordenar": every panel into a free place, no overlaps, right side first.</summary>
    public void ArrangePanels()
    {
        var hosts = Panels.OrderByDescending(p => p.Bounds.Right).ThenBy(p => p.Bounds.Y).ToList();
        var rects = LayoutMath.Arrange(hosts.Select(h => h.Bounds).ToList(), LayoutBounds);
        for (var i = 0; i < hosts.Count; i++)
        {
            var h = hosts[i];
            var from = h.Bounds;
            h.Place(rects[i]);
            h.Panel.Bounds = rects[i];
            h.Panel.Monitor = Monitor.Key;
            if (Anim.Enabled)
            {
                var t = Anim.Translate(h);
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from.X - rects[i].X, 0, Anim.Ms(320)) { EasingFunction = Anim.EaseInOut });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from.Y - rects[i].Y, 0, Anim.Ms(320)) { EasingFunction = Anim.EaseInOut });
            }
        }
        Services.Session.MarkDirty();
    }

    public ContextMenu BuildPanelMenu(PanelHost p)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Manager.IsEditing
            ? Ui.Item("Terminar de editar", Glyphs.Accept, () => Manager.SetEditing(false))
            : Ui.Item("Editar el diseño", Glyphs.Edit, () => Manager.SetEditing(true)));
        menu.Items.Add(Ui.Item("Opciones", Glyphs.Settings, () => OpenWidgetSettings(p)));
        menu.Items.Add(new Separator());
        for (var i = 0; i < Manager.Monitors.Count; i++)
        {
            var m = Manager.Monitors[i];
            if (m.Key == Monitor.Key) continue;
            menu.Items.Add(Ui.Item("Mover a " + m.Label(i), Glyphs.Monitor, () => Manager.MovePanel(p.Panel, m)));
        }
        menu.Items.Add(Ui.Item("Ocultar", Glyphs.Hide, () => HidePanel(p)));
        menu.Items.Add(Ui.Item("Quitar", Glyphs.Delete, () => RemovePanel(p)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item("Ajustes de SackSpace", Glyphs.Settings, () => Manager.OpenSettings()));
        return menu;
    }

    // ── Sheets ───────────────────────────────────────────────

    public void OpenGallery() => OpenSheet(new GallerySheet(this));

    public void OpenWidgetSettings(PanelHost p) => OpenSheet(new WidgetSettingsSheet(p, this));

    /// <summary>Shows a card over the raised desktop (it needs the keyboard, so the panels come up).</summary>
    public void OpenSheet(FrameworkElement sheet)
    {
        if (!Manager.IsRaised) Manager.SetRaised(true);
        (Window.GetWindow(this) as DesktopHost)?.FocusHost();
        // The sheet's shadow, sized and placed with it.
        var card = new Grid { HorizontalAlignment = sheet.HorizontalAlignment, VerticalAlignment = sheet.VerticalAlignment };
        var shadow = new BakedShadow();
        shadow.SetResourceReference(BakedShadow.RadiusProperty, "PanelCornerRadiusValue"); // the Sheet style's corners
        card.Children.Add(shadow);
        card.Children.Add(sheet);
        SheetSlot.Content = card;
        Overlay.Visibility = Visibility.Visible;
        Anim.Set(OverlayScrim, 1);
        Anim.SlideIn(card, 0, 40);
    }

    public void CloseSheet()
    {
        if (!SheetOpen) return;
        Anim.Set(OverlayScrim, 0);
        Overlay.Visibility = Visibility.Collapsed;
        SheetSlot.Content = null;
    }

    // ── Snackbar ─────────────────────────────────────────────

    public void ShowSnackbar(string text) => ShowSnackbar(text, null, null);

    public void ShowSnackbar(string text, string? action, Action? onAction)
    {
        SnackText.Text = text;
        _snackAction = onAction;
        SnackAction.Content = action;
        SnackAction.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        Snackbar.Visibility = Visibility.Visible;
        Anim.SlideIn(Snackbar, 0, 24);
        _snackTimer.Stop();
        _snackTimer.Interval = TimeSpan.FromSeconds(action is null ? 4 : 7);
        _snackTimer.Start();
    }

    private void HideSnackbar()
    {
        _snackTimer.Stop();
        _snackAction = null;
        Anim.Fade(Snackbar, 0, 180, () => Snackbar.Visibility = Visibility.Collapsed);
    }

    // ── Helpers ──────────────────────────────────────────────

    private static Brush MakeDots()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new WpfRect(0, 0, 24, 24))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), null,
            new EllipseGeometry(new Point(12, 12), 1.4, 1.4)));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new WpfRect(0, 0, 24, 24),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new WpfRect(0, 0, 24, 24),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }
}
