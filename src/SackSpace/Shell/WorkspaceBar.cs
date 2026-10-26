using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SackSpace.Core.Hotkeys;
using SackSpace.Core.Model;
using SackSpace.Services;
using Rect = SackSpace.Core.Rect;

namespace SackSpace.Shell;

/// <summary>
/// The workspace tabs at the top of the primary monitor: a glass pill with one chip per
/// workspace and an accent indicator that slides to the active one. The wheel over it
/// switches workspace too.
/// </summary>
public sealed class WorkspaceBar : UserControl
{
    private readonly HostManager _manager;
    private readonly AppServices _services;
    private readonly HostView _host;
    private readonly ImageBrush _glass = new() { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
    private readonly Border _shadow;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal };
    private readonly Border _indicator = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(18) };
    private readonly Button _editButton;
    private DateTime _lastWheel;
    private bool _placed;

    public WorkspaceBar(HostManager manager, AppServices services, HostView host)
    {
        _manager = manager;
        _services = services;
        _host = host;
        _indicator.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _indicator.RenderTransform = new TranslateTransform();

        var radius = new CornerRadius(26);
        var root = new Grid();
        _shadow = new Border
        {
            CornerRadius = radius, Background = Brushes.Black, Margin = new Thickness(2), IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 5, Direction = 270, Opacity = 0.26, RenderingBias = RenderingBias.Performance },
        };
        root.Children.Add(_shadow);
        root.Children.Add(new Border { CornerRadius = radius, Background = _glass });
        root.Children.Add(Layer(radius, "GlassTintBrush"));
        root.Children.Add(Layer(radius, "GlassHighlightBrush"));

        var track = new Grid();
        track.Children.Add(_indicator);
        track.Children.Add(_chips);

        _editButton = Ui.IconButton(Glyphs.Edit, "Editar el diseño", () => _manager.SetEditing(!_manager.IsEditing), 36);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        row.Children.Add(track);
        row.Children.Add(new Border { Width = 1, Height = 20, Margin = new Thickness(8, 0, 6, 0), Background = Ui.Brush("DividerBrush") });
        row.Children.Add(_editButton);
        row.Children.Add(Ui.IconButton(Glyphs.Settings, "Ajustes", () => _manager.OpenSettings(), 36));
        root.Children.Add(row);

        var outline = new Border { CornerRadius = radius, BorderThickness = new Thickness(1), IsHitTestVisible = false };
        outline.SetResourceReference(Border.BorderBrushProperty, "GlassBorderBrush");
        root.Children.Add(outline);
        Content = root;

        SizeChanged += (_, _) => { UpdateGlass(); MoveIndicator(_placed); };
        MouseWheel += (_, e) =>
        {
            e.Handled = true;
            if ((DateTime.UtcNow - _lastWheel).TotalMilliseconds < 180) return;
            _lastWheel = DateTime.UtcNow;
            if (e.Delta < 0) _manager.Next(); else _manager.Previous();
        };
        Rebuild();
    }

    private static Border Layer(CornerRadius radius, string brush)
    {
        var b = new Border { CornerRadius = radius, IsHitTestVisible = false };
        b.SetResourceReference(Border.BackgroundProperty, brush);
        return b;
    }

    /// <summary>The list of workspaces changed (added, renamed, recoloured).</summary>
    public void Rebuild()
    {
        _chips.Children.Clear();
        var config = _services.Session.Config;
        for (var i = 0; i < config.Workspaces.Count; i++)
        {
            var ws = config.Workspaces[i];
            var icon = new TextBlock { Text = ws.Icon, Style = Ui.Style("Icon"), FontSize = 13, Margin = new Thickness(0, 0, 8, 0) };
            var name = new TextBlock { Text = ws.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(name);
            var chip = new Button
            {
                Content = content,
                Style = Ui.Style("PillButton"),
                Background = Brushes.Transparent,
                Padding = new Thickness(16, 9, 16, 9),
                Tag = ws.Id,
            };
            var shortcut = config.Hotkeys.GetValueOrDefault(HotkeyActions.Workspace(i + 1));
            chip.ToolTip = string.IsNullOrEmpty(shortcut) ? ws.Name : $"{ws.Name} · {shortcut}";
            chip.Click += (_, _) => _manager.SwitchTo(ws.Id);
            chip.ContextMenu = new ContextMenu();
            chip.ContextMenu.Items.Add(Ui.Item("Personalizar los espacios…", Glyphs.Edit, () => _manager.OpenSettings("espacios")));
            _chips.Children.Add(chip);
        }
        _placed = false;
        Update();
    }

    /// <summary>The active workspace changed.</summary>
    public void Update()
    {
        var active = _services.Session.Config.ActiveWorkspace?.Id;
        foreach (var chip in _chips.Children.OfType<Button>())
            chip.SetResourceReference(ForegroundProperty, (string)chip.Tag == active ? "AccentForegroundBrush" : "TextBrush");
        Dispatcher.BeginInvoke(() => MoveIndicator(_placed), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void MoveIndicator(bool animate)
    {
        var active = _services.Session.Config.ActiveWorkspace?.Id;
        var chip = _chips.Children.OfType<Button>().FirstOrDefault(c => (string)c.Tag == active);
        if (chip is null || chip.ActualWidth == 0) { _indicator.Width = 0; return; }
        var x = chip.TranslatePoint(new Point(0, 0), _chips).X;
        var t = (TranslateTransform)_indicator.RenderTransform;
        animate &= Anim.Enabled;
        if (animate)
        {
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, Anim.Ms(300)) { EasingFunction = Anim.Spring });
            _indicator.BeginAnimation(WidthProperty, new DoubleAnimation(chip.ActualWidth, Anim.Ms(300)) { EasingFunction = Anim.Ease });
        }
        else
        {
            t.BeginAnimation(TranslateTransform.XProperty, null);
            _indicator.BeginAnimation(WidthProperty, null);
            t.X = x;
            _indicator.Width = chip.ActualWidth;
        }
        _placed = true;
    }

    public void SetEditing(bool editing)
    {
        _editButton.Content = editing ? Glyphs.Accept : Glyphs.Edit;
        _editButton.ToolTip = editing ? "Listo" : "Editar el diseño";
    }

    public void UpdateGlass()
    {
        if (ActualWidth == 0) return;
        var origin = TranslatePoint(new Point(0, 0), _host);
        _shadow.Visibility = _host.ClearGlass ? Visibility.Collapsed : Visibility.Visible;
        _glass.ImageSource = _host.ClearGlass ? null : _host.WallpaperImage;
        var vb = _host.GlassViewbox(new Rect(origin.X, origin.Y, ActualWidth, ActualHeight));
        if (!vb.IsEmpty) _glass.Viewbox = vb;
    }
}
