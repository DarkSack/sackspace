using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SackSpace.Core;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Widgets;
using Rect = SackSpace.Core.Rect;
using WpfRect = System.Windows.Rect;

namespace SackSpace.Shell;

/// <summary>
/// One panel on the desktop: glass, shadow, the widget inside, and the edit-mode handles.
/// Positions are DIPs in its HostView; they are written back to the PanelState when a
/// drag ends, so a move costs one save, not one per mouse move.
/// </summary>
public partial class PanelHost : UserControl
{
    private readonly HostView _host;
    private readonly DispatcherTimer _longPress = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private Point _pressAt;
    private Rect _dragStart;
    private Point _mouseStart;
    private FrameworkElement? _widget;

    public PanelState Panel { get; }
    public WidgetDescriptor Descriptor { get; }
    public Rect Bounds { get; private set; }

    public PanelHost(PanelState panel, WidgetDescriptor descriptor, HostView host)
    {
        InitializeComponent();
        Panel = panel;
        Descriptor = descriptor;
        _host = host;
        SettingsButton.Visibility = descriptor.Settings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        MoveThumb.DragStarted += (_, _) => BeginDrag();
        MoveThumb.DragDelta += (_, _) => DragMove();
        MoveThumb.DragCompleted += (_, _) => EndDrag();
        ResizeThumb.DragStarted += (_, _) => BeginDrag();
        ResizeThumb.DragDelta += (_, _) => DragResize();
        ResizeThumb.DragCompleted += (_, _) => EndDrag();
        SettingsButton.Click += (_, _) => _host.OpenWidgetSettings(this);
        HideButton.Click += (_, _) => _host.HidePanel(this);
        RemoveButton.Click += (_, _) => _host.RemovePanel(this);

        _longPress.Tick += (_, _) => { _longPress.Stop(); Anim.ScaleTo(this, 1); _host.Manager.SetEditing(true); };
        PreviewMouseLeftButtonDown += OnPress;
        PreviewMouseMove += (_, e) => { if (_longPress.IsEnabled && (e.GetPosition(this) - _pressAt).Length > 8) CancelPress(); };
        PreviewMouseLeftButtonUp += (_, _) => CancelPress();
        MouseLeave += (_, _) => CancelPress();
        ContextMenuOpening += (_, _) => ContextMenu = _host.BuildPanelMenu(this);
        ContextMenu = new ContextMenu(); // placeholder so ContextMenuOpening fires

        Bounds = panel.Bounds;
        CreateWidget();
    }

    public void CreateWidget()
    {
        DisposeWidget();
        var factory = WidgetRegistry.Find(Panel.Widget);
        try
        {
            _widget = factory?.Create(new WidgetContext(Panel, _host.Services, _host))
                ?? Ui.Text("Este widget no está disponible en esta versión.", wrap: true);
        }
        catch (Exception ex)
        {
            Log.Error($"El widget '{Panel.Widget}' falló al crearse", ex);
            _widget = Ui.Text("Este widget no se pudo cargar.", wrap: true);
        }
        Body.Content = _widget;
    }

    public void DisposeWidget()
    {
        (_widget as IDisposable)?.Dispose();
        _widget = null;
        Body.Content = null;
    }

    /// <summary>Where the panel is drawn (not saved until a drag ends).</summary>
    public void Place(Rect r)
    {
        Bounds = r;
        Canvas.SetLeft(this, r.X);
        Canvas.SetTop(this, r.Y);
        Width = r.Width;
        Height = r.Height;
        UpdateGlass();
    }

    public void UpdateGlass()
    {
        GlassBrush.ImageSource = _host.WallpaperImage;
        var vb = _host.GlassViewbox(Bounds);
        if (!vb.IsEmpty) GlassBrush.Viewbox = vb;
    }

    public void SetEditing(bool editing)
    {
        EditLayer.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        Body.IsHitTestVisible = !editing;
        if (editing) Anim.Fade(EditLayer, 1, 160);
    }

    // ── Long press: like Android, hold a panel to start arranging ──────────

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (_host.Manager.IsEditing || e.ClickCount > 1) return;
        _pressAt = e.GetPosition(this);
        _longPress.Start();
        Anim.ScaleTo(this, 0.985, 400);
    }

    private void CancelPress()
    {
        if (!_longPress.IsEnabled) return;
        _longPress.Stop();
        Anim.ScaleTo(this, 1);
    }

    // ── Drag and resize (mouse position in the host, so snapping never drifts) ──

    private void BeginDrag()
    {
        _dragStart = Bounds;
        _mouseStart = Mouse.GetPosition(_host);
        Panel.Z = _host.NextZ();
        Canvas.SetZIndex(this, Panel.Z);
        ShadowFx.Opacity = 0.42;
        ShadowFx.BlurRadius = 36;
        Anim.ScaleTo(this, 1.015);
    }

    private void DragMove()
    {
        var p = Mouse.GetPosition(_host);
        var raw = _dragStart.WithPosition(_dragStart.X + p.X - _mouseStart.X, _dragStart.Y + p.Y - _mouseStart.Y);
        Place(LayoutMath.SnapMove(raw, _host.OtherBounds(this), _host.LayoutBounds));
    }

    private void DragResize()
    {
        var p = Mouse.GetPosition(_host);
        var raw = _dragStart with
        {
            Width = _dragStart.Width + p.X - _mouseStart.X,
            Height = _dragStart.Height + p.Y - _mouseStart.Y,
        };
        Place(LayoutMath.SnapResize(raw, _host.OtherBounds(this), _host.LayoutBounds, Descriptor.MinWidth, Descriptor.MinHeight));
    }

    private void EndDrag()
    {
        ShadowFx.Opacity = 0.26;
        ShadowFx.BlurRadius = 24;
        Anim.ScaleTo(this, 1);
        Panel.Bounds = Bounds;
        Panel.Monitor = _host.Monitor.Key;
        _host.Services.Session.MarkDirty();
    }

    public static WpfRect ToWpf(Rect r) => new(r.X, r.Y, r.Width, r.Height);
}
