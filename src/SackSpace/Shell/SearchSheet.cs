using Loc = SackSpace.Core.Loc;
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
    private readonly AppServices _services;
    private List<Entry> _entries;
    private int _fill;
    private CancellationTokenSource? _cts;
    private bool _exactOnly;

    public SearchSheet(HostView host, AppServices services, Action opened) : base(host, Glyphs.Search, Loc.T("Buscar y abrir"), 520)
    {
        _opened = opened;
        _services = services;
        Height = Math.Min(560, host.AreaSize.Height * 0.8);
        _entries = Shortcuts(services);
        services.Files?.Preload(); // reading the list takes about a second: start before the first key
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
        Unloaded += (_, _) => { _typing.Stop(); _cts?.Cancel(); };
        var search = Ui.WithPlaceholder(_search, services.Files is null
            ? Loc.T("Escribe una app, un juego, un acceso…")
            : Loc.T("Una app, un juego, un archivo… (ext:pdf, en:carpeta, \"frase exacta\")"));
        search.Margin = new Thickness(0, 0, 0, 12);
        Body.Children.Add(search);
        if (services.Files is not null)
        {
            Button? exact = null;
            exact = Ui.Pill(Loc.T("Solo nombres exactos"), () =>
            {
                _exactOnly = !_exactOnly;
                exact!.Style = Ui.Style(_exactOnly ? "AccentPillButton" : "PillButton");
                Fill();
            });
            exact.HorizontalAlignment = HorizontalAlignment.Left;
            exact.Margin = new Thickness(0, -4, 0, 10);
            exact.ToolTip = Loc.T("Apagado, también salen archivos con un nombre parecido (letras cambiadas o que faltan)");
            Body.Children.Add(exact);
        }
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
                .Concat(games.Where(g => !gameKeys.Contains(g.Key) && !GameCatalog.IsTool(g)).Select(g => new Entry(g.Name, g.LaunchPath, Loc.T("Juego instalado"))))
                .Concat(apps.Where(a => !known.Contains(a.ParsingName)).Select(a => new Entry(a.Name, a.ParsingName, Loc.T("Menú Inicio"))))
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
        if (found.Count == 0) _list.Children.Add(Ui.Text(q.Length == 0 ? Loc.T("Aún no tienes accesos: escribe para buscar en el menú Inicio.") : Loc.T("Nada coincide.")));

        var icons = new List<(Image Image, Task<ImageSource?> Icon)>();
        foreach (var e in found) Add(e.Name, e.Where, e.Path, icons);

        // Files: after the apps, exact names first, then similar ones.
        if (_services.Files is { } files && q.Length >= 2)
        {
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            var waiting = Ui.Text(files.Exists ? Loc.T("Buscando en tus archivos…") : Loc.T("Preparando la lista de tus archivos (solo la primera vez, un minuto o dos)…"), wrap: true);
            waiting.Margin = new Thickness(10, 10, 0, 0);
            _list.Children.Add(waiting);
            try
            {
                var results = await files.SearchAsync(q, allowSimilar: !_exactOnly, max: 40, cts.Token);
                if (fill != _fill) return;
                _list.Children.Remove(waiting);
                var title = Ui.Text(results.Count == 0 ? Loc.T("Ningún archivo coincide.") : Loc.T("Archivos") + (files.BuiltAt is { } at ? Loc.T(" · lista del {0:d MMM, HH:mm}", at) : ""));
                title.Margin = new Thickness(10, 12, 0, 4);
                _list.Children.Add(title);
                foreach (var f in results)
                    Add(f.Name, (f.Similar ? "Parecido · " : "") + $"{Size(f.Bytes)} · {f.Modified:d MMM yyyy} · {f.Folder}", f.Path, icons, folder: f.Folder);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Log.Warn("Buscar archivos", ex);
                waiting.Text = Loc.T("No se pudo buscar en tus archivos.");
            }
        }

        try { await Task.WhenAll(icons.Select(i => i.Icon)); }
        catch (Exception ex) { Log.Warn("Sin algunos iconos", ex); }
        if (fill != _fill) return;
        foreach (var (image, icon) in icons)
            if (icon.IsCompletedSuccessfully) image.Source = icon.Result;
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes >> 10} KB",
        _ => $"{bytes} B",
    };

    // A result row: icon, name, where it is; Enter or click opens it, Shift opens its folder.
    private void Add(string name, string where, string path, List<(Image Image, Task<ImageSource?> Icon)> icons, string? folder = null)
    {
        var image = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0) };
        icons.Add((image, ShellIcons.GetAsync(path, (int)Math.Round(24 * Host.DpiScale))));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = where, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Style = Ui.Style("Caption") });
        var row = new DockPanel();
        row.Children.Add(image);
        row.Children.Add(text);
        var button = Ui.Row(row, () =>
        {
            Host.CloseSheet();
            _opened();
            if (folder is not null && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
                return;
            }
            if (!InstalledApps.Launch(path, out var error)) Log.Warn($"Buscar: no se pudo abrir '{path}': {error}");
        });
        if (folder is not null) button.ToolTip = Loc.T("Mayús + clic: abrir la carpeta");
        button.PreviewKeyDown += (_, k) =>
        {
            var buttons = _list.Children.OfType<Button>().ToList();
            var i = buttons.IndexOf(button);
            if (k.Key == Key.Down && i + 1 < buttons.Count) { buttons[i + 1].Focus(); k.Handled = true; }
            else if (k.Key == Key.Up) { (i > 0 ? (UIElement)buttons[i - 1] : _search).Focus(); k.Handled = true; }
        };
        _list.Children.Add(button);
    }
}
