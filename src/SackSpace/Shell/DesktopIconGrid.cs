using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using SackSpace.Core.Model;
using SackSpace.Interop;
using SackSpace.Services;

namespace SackSpace.Shell;

/// <summary>
/// The desktop icons a workspace shows, in columns from the top-left like Windows'
/// "Organizar automáticamente". Click selects, double click or Enter opens, right click
/// has the options. Lives under the panels, in the workspace's own layer, so it slides
/// with it when the workspace changes.
/// </summary>
public sealed class DesktopIconGrid : WrapPanel
{
    // Nearly transparent rather than transparent: SackSpace's window lets clicks through where nothing is drawn.
    private static readonly Brush Clickable = Frozen(0x01, 0xFF, 0xFF, 0xFF);
    private static readonly Brush Hover = Frozen(0x2E, 0xFF, 0xFF, 0xFF);
    private static readonly Brush Selected = Frozen(0x55, 0x9C, 0xC8, 0xFF);

    private readonly HostView _host;
    private readonly Workspace _ws;
    private readonly double _icon;

    public DesktopIconGrid(HostView host, Workspace ws)
    {
        _host = host;
        _ws = ws;
        _icon = IconSize();
        Orientation = Orientation.Vertical;
        ItemWidth = _icon + 30;
        ItemHeight = _icon + 50;
        // Below the workspace bar, like the panels: an icon under it could not be clicked.
        var area = host.LayoutBounds;
        Height = Math.Max(ItemHeight, area.Height - 8);
        Canvas.SetLeft(this, 4);
        Canvas.SetTop(this, area.Y + 4);
        Panel.SetZIndex(this, int.MinValue);
        // One shadow for every label keeps white text readable on any wallpaper.
        Effect = new DropShadowEffect { ShadowDepth = 1, BlurRadius = 3, Opacity = 0.9, Color = Colors.Black, RenderingBias = RenderingBias.Performance };

        foreach (var item in host.Services.Icons.Items.Where(i => DesktopIconLists.Shows(ws, i.Path)))
            Children.Add(Icon(item));
    }

    private Border Icon(DesktopItem item)
    {
        var image = new Image { Width = _icon, Height = _icon };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Load(image, item.Path);
        var label = new TextBlock
        {
            Text = item.Name, Foreground = Brushes.White, FontSize = 12, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 33, Margin = new Thickness(0, 3, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(image);
        stack.Children.Add(label);

        var cell = new Border
        {
            Child = stack, Padding = new Thickness(2, 4, 2, 2), Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
            Background = Clickable, VerticalAlignment = VerticalAlignment.Top, ToolTip = item.Name,
            Focusable = true, FocusVisualStyle = null,
        };
        AutomationProperties.SetName(cell, item.Name);
        cell.MouseEnter += (_, _) => { if (!cell.IsKeyboardFocused) cell.Background = Hover; };
        cell.MouseLeave += (_, _) => { if (!cell.IsKeyboardFocused) cell.Background = Clickable; };
        // Selected while it has the focus; clicking anywhere else (even the desktop) takes it away.
        cell.GotKeyboardFocus += (_, _) => cell.Background = Selected;
        cell.LostKeyboardFocus += (_, _) => cell.Background = cell.IsMouseOver ? Hover : Clickable;
        cell.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2) Open(item);
            else cell.Focus();
        };
        cell.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Open(item); } };
        cell.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            cell.Focus();
            var menu = Menu(item);
            menu.PlacementTarget = cell;
            menu.IsOpen = true;
        };
        return cell;
    }

    private ContextMenu Menu(DesktopItem item)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Ui.Item("Abrir", Glyphs.View, () => Open(item)));
        if (Path.GetExtension(item.Path).ToLowerInvariant() is ".lnk" or ".exe" or ".bat" or ".cmd" or ".msi")
            menu.Items.Add(Ui.Item("Ejecutar como administrador", Glyphs.Shield, () => RunAsAdmin(item)));
        if (!item.Path.StartsWith("::"))
            menu.Items.Add(Ui.Item("Abrir su ubicación", Glyphs.Folder, () => InstalledApps.ShowInFolder(item.Path)));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Se ve en estos espacios:", IsEnabled = false });
        var icons = _host.Services.Icons;
        foreach (var ws in _host.Services.Session.Config.Workspaces)
        {
            var shows = DesktopIconLists.Shows(ws, item.Path);
            menu.Items.Add(Ui.Item(ws.Name, shows ? Glyphs.Accept : null, () => icons.Choose(ws, item.Path, !shows), isChecked: shows));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item("Elegir los iconos de este espacio…", Glyphs.Edit, () => _host.OpenSheet(new DesktopIconsSheet(_host, _ws))));
        return menu;
    }

    private void Open(DesktopItem item)
    {
        if (!InstalledApps.Launch(item.Path, out var error)) _host.ShowSnackbar($"No se pudo abrir «{item.Name}»: {error}");
    }

    private void RunAsAdmin(DesktopItem item)
    {
        try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true, Verb = "runas" })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { } // the user said no to the UAC prompt
        catch (Exception ex) { _host.ShowSnackbar($"No se pudo abrir «{item.Name}»: {ex.Message}"); }
    }

    private async void Load(Image image, string path)
    {
        try { image.Source = await ShellIcons.GetAsync(path, (int)Math.Round(_icon * _host.Monitor.Scale)); }
        catch (Exception ex) { Log.Warn($"Sin icono para '{path}'", ex); }
    }

    /// <summary>Windows' own icon size (Ver › Iconos pequeños / medianos / grandes), in DIPs.</summary>
    private static double IconSize() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Bags\1\Desktop", "IconSize", null) is int size
            ? Math.Clamp(size, 24, 96)
            : 48;

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Ticks for which desktop icons a workspace shows.</summary>
public sealed class DesktopIconsSheet : SheetBase
{
    private readonly Workspace _ws;
    private readonly StackPanel _list = new();
    private readonly TextBox _search = new() { Style = Ui.Style("FieldTextBox") };

    public DesktopIconsSheet(HostView host, Workspace ws) : base(host, Glyphs.Apps, $"Iconos del escritorio · {ws.Name}", 480)
    {
        _ws = ws;
        Height = Math.Min(640, host.AreaSize.Height * 0.85);
        var hint = Ui.Text("Solo se ven en este espacio los iconos marcados. Lo que guardes en el escritorio aparece en el espacio en el que estés.", wrap: true);
        hint.Margin = new Thickness(0, 0, 0, 12);
        Body.Children.Add(hint);

        var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var none = Ui.Pill("Ninguno", () => { Host.Services.Icons.ChooseAll(_ws, false); Fill(); });
        var all = Ui.Pill("Todos", () => { Host.Services.Icons.ChooseAll(_ws, true); Fill(); });
        none.Margin = all.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(none, Dock.Right);
        DockPanel.SetDock(all, Dock.Right);
        tools.Children.Add(none);
        tools.Children.Add(all);
        _search.TextChanged += (_, _) => Fill();
        tools.Children.Add(Ui.WithPlaceholder(_search, "Buscar"));
        Body.Children.Add(tools);
        Body.Children.Add(_list);
        Fill();
    }

    private void Fill()
    {
        _list.Children.Clear();
        var q = _search.Text.Trim();
        var icons = Host.Services.Icons;
        foreach (var item in icons.Items.Where(i => q.Length == 0 || i.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
        {
            var image = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0) };
            Load(image, item.Path);
            var toggle = new CheckBox { Style = Ui.Style("ToggleSwitch"), IsChecked = DesktopIconLists.Shows(_ws, item.Path), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(toggle, item.Name);
            toggle.Checked += (_, _) => icons.Choose(_ws, item.Path, true);
            toggle.Unchecked += (_, _) => icons.Choose(_ws, item.Path, false);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(toggle, Dock.Right);
            row.Children.Add(toggle);
            row.Children.Add(image);
            row.Children.Add(new TextBlock { Text = item.Name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _list.Children.Add(row);
        }
        if (_list.Children.Count == 0) _list.Children.Add(Ui.Text(icons.Items.Count == 0 ? "El escritorio está vacío." : "Nada coincide con la búsqueda."));
    }

    private async void Load(Image image, string path)
    {
        try { image.Source = await ShellIcons.GetAsync(path, (int)Math.Round(24 * Host.DpiScale)); }
        catch (Exception ex) { Log.Warn($"Sin icono para '{path}'", ex); }
    }
}
