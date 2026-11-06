using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Interop;
using SackSpace.Services;
using SackSpace.Widgets;

namespace SackSpace.Shell;

/// <summary>
/// Type to open anything: the shortcuts of every workspace, the Start menu's apps and the
/// installed Steam/Epic games. Enter opens the first match; arrows move through the list.
/// </summary>
public sealed class SearchSheet : SheetBase
{
    private sealed record Entry(string Name, string Path, string Where);

    private readonly Action _opened;
    private readonly StackPanel _list = new();
    private readonly TextBox _search = new() { Style = Ui.Style("FieldTextBox"), FontSize = 15 };
    private readonly System.Windows.Threading.DispatcherTimer _typing = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private List<Entry> _entries;
    private int _fill;

    public SearchSheet(HostView host, AppServices services, Action opened) : base(host, Glyphs.Search, "Buscar y abrir", 520)
    {
        _opened = opened;
        Height = Math.Min(560, host.AreaSize.Height * 0.8);
        _entries = Shortcuts(services);
        _typing.Tick += (_, _) => { _typing.Stop(); Fill(); };
        _search.TextChanged += (_, _) => { _typing.Stop(); _typing.Start(); };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _list.Children.OfType<Button>().FirstOrDefault() is { } first)
            {
                _typing.Stop();
                Fill(); // what was typed in the last 120 ms counts
                (_list.Children.OfType<Button>().FirstOrDefault() ?? first).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
            else if (e.Key == Key.Down && _list.Children.OfType<Button>().FirstOrDefault() is { } top)
            {
                top.Focus();
                e.Handled = true;
            }
        };
        Unloaded += (_, _) => _typing.Stop();
        var search = Ui.WithPlaceholder(_search, "Escribe una app, un juego, un acceso…");
        search.Margin = new Thickness(0, 0, 0, 12);
        Body.Children.Add(search);
        Body.Children.Add(_list);
        Loaded += (_, _) => { _search.Focus(); Keyboard.Focus(_search); };
        Fill();
        LoadMore(services);
    }

    /// <summary>Every "Accesos" item of every workspace, named after its group and workspace.</summary>
    private static List<Entry> Shortcuts(AppServices services) =>
        services.Session.Config.Workspaces
            .SelectMany(ws => ws.Panels.Where(p => p.Widget == BuiltInWidgets.Shortcuts)
                .SelectMany(p => ShortcutItem.ReadAll(p.Settings)
                    .Select(i => new Entry(i.Name, i.Path, $"{ws.Name} › {p.Settings.GetString("title", "Accesos")}"))))
            .ToList();

    private async void LoadMore(AppServices services)
    {
        try
        {
            var apps = await services.AppsAsync();
            var (_, games) = await ShortcutCheck.FactsAsync();
            var known = _entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var gameKeys = _entries.Select(e => GameCatalog.KeyOf(e.Path)).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            _entries = _entries
                .Concat(games.Where(g => !gameKeys.Contains(g.Key) && !GameCatalog.IsTool(g)).Select(g => new Entry(g.Name, g.LaunchPath, "Juego instalado")))
                .Concat(apps.Where(a => !known.Contains(a.ParsingName)).Select(a => new Entry(a.Name, a.ParsingName, "Menú Inicio")))
                .ToList();
        }
        catch (Exception ex) { Log.Warn("Buscar: no se pudo leer la lista de apps", ex); }
        Fill();
    }

    /// <summary>Names that start with the text first, then those with a word that does, then any match.</summary>
    private static int Rank(string name, string q) =>
        name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 0
        : name.Contains(" " + q, StringComparison.CurrentCultureIgnoreCase) ? 1
        : name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ? 2
        : -1;

    private async void Fill()
    {
        var fill = ++_fill;
        _list.Children.Clear();
        var q = _search.Text.Trim();
        var found = (q.Length == 0
                ? _entries.Where(e => e.Where != "Menú Inicio").Select(e => (Entry: e, Rank: 0))
                : _entries.Select(e => (Entry: e, Rank: Rank(e.Name, q))).Where(x => x.Rank >= 0))
            .OrderBy(x => x.Rank).ThenBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Entry)
            .DistinctBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .Take(40).ToList();
        if (found.Count == 0) _list.Children.Add(Ui.Text(q.Length == 0 ? "Aún no tienes accesos: escribe para buscar en el menú Inicio." : "Nada coincide."));

        var icons = new List<(Image Image, Task<ImageSource?> Icon)>();
        foreach (var e in found)
        {
            var image = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0) };
            icons.Add((image, ShellIcons.GetAsync(e.Path, (int)Math.Round(24 * Host.DpiScale))));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = e.Name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Ui.Text(e.Where));
            var row = new DockPanel();
            row.Children.Add(image);
            row.Children.Add(text);
            var face = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), Child = row, Background = Brushes.Transparent };
            var button = new Button
            {
                Content = face, Template = GallerySheet.BareButton(), Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, FocusVisualStyle = null,
            };
            void Lit(bool on)
            {
                if (on) face.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
                else face.Background = Brushes.Transparent;
            }
            button.MouseEnter += (_, _) => Lit(true);
            button.MouseLeave += (_, _) => Lit(button.IsKeyboardFocused);
            button.GotKeyboardFocus += (_, _) => Lit(true);
            button.LostKeyboardFocus += (_, _) => Lit(false);
            button.PreviewKeyDown += (_, k) =>
            {
                var buttons = _list.Children.OfType<Button>().ToList();
                var i = buttons.IndexOf(button);
                if (k.Key == Key.Down && i + 1 < buttons.Count) { buttons[i + 1].Focus(); k.Handled = true; }
                else if (k.Key == Key.Up) { (i > 0 ? (UIElement)buttons[i - 1] : _search).Focus(); k.Handled = true; }
            };
            button.Click += (_, _) =>
            {
                Host.CloseSheet();
                _opened();
                if (!InstalledApps.Launch(e.Path, out var error)) Log.Warn($"Buscar: no se pudo abrir '{e.Path}': {error}");
            };
            _list.Children.Add(button);
        }

        try { await Task.WhenAll(icons.Select(i => i.Icon)); }
        catch (Exception ex) { Log.Warn("Sin algunos iconos", ex); }
        if (fill != _fill) return;
        foreach (var (image, icon) in icons)
            if (icon.IsCompletedSuccessfully) image.Source = icon.Result;
    }
}
