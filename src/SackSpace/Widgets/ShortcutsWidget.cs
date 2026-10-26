using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Interop;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>
/// A group of apps, files and folders. Drop things from Explorer onto it, or use + to
/// pick an app from the Start menu, a file or a folder.
/// </summary>
public sealed class ShortcutsWidget : DockPanel
{
    private readonly WidgetContext _c;
    private readonly List<ShortcutItem> _items;
    private readonly WrapPanel _tiles = new();
    private readonly double _icon;
    private readonly bool _labels;

    public ShortcutsWidget(WidgetContext c)
    {
        _c = c;
        _items = ShortcutItem.ReadAll(c.Settings);
        _icon = c.Settings.GetString("iconSize", "Mediano") switch { "Pequeño" => 28, "Grande" => 48, _ => 36 };
        _labels = c.Settings.GetBool("labels", true);

        var add = Ui.IconButton(Glyphs.Add, "Añadir", () => { }, 28);
        add.Click += (_, _) => AddMenu(add).IsOpen = true;
        var header = Ui.Header(Glyphs.Apps, c.Settings.GetString("title", "Accesos"), add);
        SetDock(header, Dock.Top);
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = _tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(-6, 0, -6, 0) });

        AllowDrop = true;
        Background = Brushes.Transparent;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) Add(paths);
            e.Handled = true;
        };
        Render();
    }

    private ContextMenu AddMenu(UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(Ui.Item("Una app…", Glyphs.Apps, () => _c.Host.OpenSheet(new AppPickerSheet(_c.Host, _c.Services, app => Add(app.Name, app.ParsingName)))));
        menu.Items.Add(Ui.Item("Archivos…", Glyphs.Document, () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Elige archivos para " + Title };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) Add(dialog.FileNames);
        }));
        menu.Items.Add(Ui.Item("Una carpeta…", Glyphs.Folder, () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true, Title = "Elige carpetas para " + Title };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) Add(dialog.FolderNames);
        }));
        return menu;
    }

    private string Title => "«" + _c.Settings.GetString("title", "Accesos") + "»";

    private void Add(IEnumerable<string> paths)
    {
        foreach (var p in paths) Add(ShellNames.DisplayName(p), p);
    }

    private void Add(string name, string path)
    {
        if (_items.Any(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        _items.Add(new ShortcutItem(name, path));
        Save();
    }

    private void Save()
    {
        ShortcutItem.WriteAll(_c.Settings, _items);
        _c.Save();
        Render();
    }

    private void Render()
    {
        _tiles.Children.Clear();
        foreach (var item in _items) _tiles.Children.Add(Tile(item));
        if (_items.Count == 0)
        {
            var hint = Ui.Text("Arrastra aquí apps, archivos o carpetas, o pulsa +.", wrap: true);
            hint.Margin = new Thickness(6, 0, 6, 0);
            _tiles.Children.Add(hint);
        }
    }

    private Button Tile(ShortcutItem item)
    {
        var image = new Image { Width = _icon, Height = _icon };
        // The shell hands the icon at its exact pixel size: Fant scaling (HighQuality) would cost a lot in software for nothing.
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.LowQuality);
        LoadIcon(image, item.Path);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(image);
        var width = _labels ? Math.Max(84, _icon + 40) : _icon + 18;
        if (_labels)
        {
            stack.Children.Add(new TextBlock
            {
                Text = item.Name, FontSize = 11, TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0),
            });
        }

        var face = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Child = stack, Width = width, Background = Brushes.Transparent };
        var exists = InstalledApps.Exists(item.Path);
        var tile = new Button
        {
            Content = face,
            Template = GallerySheet.BareButton(),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = exists ? item.Name : $"No se encuentra: {item.Path}",
            Opacity = exists ? 1 : 0.45,
        };
        tile.MouseEnter += (_, _) => face.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        tile.MouseLeave += (_, _) => face.Background = Brushes.Transparent;
        tile.Click += (_, _) =>
        {
            if (!InstalledApps.Launch(item.Path, out var error)) _c.Toast($"No se pudo abrir «{item.Name}»: {error}");
        };

        var menu = new ContextMenu();
        menu.Items.Add(Ui.Item("Abrir", Glyphs.View, () => InstalledApps.Launch(item.Path, out _)));
        if (!item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            menu.Items.Add(Ui.Item("Abrir su ubicación", Glyphs.Folder, () => InstalledApps.ShowInFolder(item.Path)));
        var index = _items.IndexOf(item);
        if (index > 0) menu.Items.Add(Ui.Item("Mover antes", Glyphs.ChevronLeft, () => Move(index, -1)));
        if (index < _items.Count - 1) menu.Items.Add(Ui.Item("Mover después", Glyphs.ChevronRight, () => Move(index, +1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item("Quitar del grupo", Glyphs.Delete, () => { _items.Remove(item); Save(); }));
        tile.ContextMenu = menu;
        return tile;
    }

    private void Move(int index, int delta)
    {
        (_items[index], _items[index + delta]) = (_items[index + delta], _items[index]);
        Save();
    }

    private async void LoadIcon(Image image, string path)
    {
        try { image.Source = await ShellIcons.GetAsync(path, (int)Math.Round(_icon * _c.Scale)); }
        catch (Exception ex) { Log.Warn($"Sin icono para '{path}'", ex); }
    }
}

/// <summary>Searchable list of the Start menu's apps.</summary>
public sealed class AppPickerSheet : SheetBase
{
    private readonly Action<InstalledApp> _picked;
    private readonly StackPanel _list = new();
    private readonly TextBox _search = new() { Style = Ui.Style("FieldTextBox") };
    private IReadOnlyList<InstalledApp> _apps = [];
    private readonly System.Windows.Threading.DispatcherTimer _typing = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int _fill; // the latest list; icons of an older one are dropped

    public AppPickerSheet(HostView host, AppServices services, Action<InstalledApp> picked) : base(host, Glyphs.Apps, "Elige una app", 440)
    {
        _picked = picked;
        Height = Math.Min(560, host.AreaSize.Height * 0.8);
        // Every redraw of the window is done in software: rebuild the list once typing pauses.
        _typing.Tick += (_, _) => { _typing.Stop(); Fill(); };
        _search.TextChanged += (_, _) => { _typing.Stop(); _typing.Start(); };
        Unloaded += (_, _) => _typing.Stop();
        var search = Ui.WithPlaceholder(_search, "Buscar");
        search.Margin = new Thickness(0, 0, 0, 12);
        Body.Children.Add(search);
        Body.Children.Add(_list);
        _list.Children.Add(Ui.Text("Leyendo el menú Inicio…"));
        Loaded += (_, _) => _search.Focus();
        Load(services);
    }

    private async void Load(AppServices services)
    {
        try { _apps = await services.AppsAsync(); }
        catch (Exception ex) { Log.Warn("No se pudo leer la lista de apps", ex); }
        Fill();
    }

    private async void Fill()
    {
        var fill = ++_fill;
        _list.Children.Clear();
        var icons = new List<(Image Image, Task<ImageSource?> Icon)>();
        var q = _search.Text.Trim();
        var found = _apps.Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).Take(60).ToList();
        if (found.Count == 0) _list.Children.Add(Ui.Text(_apps.Count == 0 ? "No se encontraron apps." : "Nada coincide con la búsqueda."));
        foreach (var app in found)
        {
            var image = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0) };
            icons.Add((image, ShellIcons.GetAsync(app.ParsingName, (int)Math.Round(24 * Host.DpiScale))));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(image);
            row.Children.Add(new TextBlock { Text = app.Name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
            var face = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Child = row, Background = Brushes.Transparent };
            var button = new Button { Content = face, Template = GallerySheet.BareButton(), Cursor = System.Windows.Input.Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.MouseEnter += (_, _) => face.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            button.MouseLeave += (_, _) => face.Background = Brushes.Transparent;
            button.Click += (_, _) => { Host.CloseSheet(); _picked(app); };
            _list.Children.Add(button);
        }

        // All the icons at once: one redraw instead of one per icon as each arrives.
        try { await Task.WhenAll(icons.Select(i => i.Icon)); }
        catch (Exception ex) { Log.Warn("Sin algunos iconos", ex); }
        if (fill != _fill) return;
        foreach (var (image, icon) in icons)
            if (icon.IsCompletedSuccessfully) image.Source = icon.Result;
    }
}
