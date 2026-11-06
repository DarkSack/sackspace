using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Interop;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>A drive as last measured: what each category takes, its biggest folders, and the measurement before.</summary>
public sealed record DriveUsage(
    string Name, string Label, long Total, long Free,
    IReadOnlyList<(string Category, long Bytes)> Parts,
    IReadOnlyDictionary<string, IReadOnlyList<StorageItem>> Items,
    DriveBaseline? Baseline);

/// <summary>An earlier measurement, to say how much a drive and each category grew since.</summary>
public sealed record DriveBaseline(DateTimeOffset At, long Used, IReadOnlyDictionary<string, long> Parts);

/// <summary>
/// What takes the space on each fixed drive. Every folder is walked on low-priority threads, paused
/// while a game is at full screen; known folders (Windows, Program Files, AppData, the user's folders, the Steam/Epic/Xbox
/// libraries, developer caches, the recycle bins, the user's own folders) get their category, other
/// folders one from their name or "Sin clasificar". A category opens to its biggest folders.
/// The result is kept in the panel's settings and measured again on the chosen interval. Nothing leaves the PC.
/// </summary>
public sealed class StorageWidget : DockPanel, IDisposable
{
    private const double Gb = 1024d * 1024 * 1024;

    private readonly WidgetContext _c;
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = Ui.Text("", wrap: true);
    private readonly HashSet<string> _open; // "C:|Juegos": breakdowns shown, kept in the settings

    public StorageWidget(WidgetContext c)
    {
        _c = c;
        _open = (c.Settings["open"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToHashSet() ?? [];
        var refresh = Ui.IconButton(Glyphs.Refresh, "Medir de nuevo", () => Scan(), 28);
        var more = Ui.IconButton(Glyphs.More, "Más", () => { }, 28);
        more.Click += (_, _) => MoreMenu(more).IsOpen = true;
        var header = Ui.Header(Glyphs.Storage, "Almacenamiento", more, refresh);
        SetDock(header, Dock.Top);
        Children.Add(header);
        _status.Margin = new Thickness(0, -4, 0, 10);
        SetDock(_status, Dock.Top);
        Children.Add(_status);
        _list.Margin = new Thickness(0, 0, 12, 0); // room for the scroll bar
        Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        StorageScanner.Progress += OnProgress;
        c.Services.Clock.Minute += OnMinute;
        c.Services.Governor.PausedChanged += OnPaused;
        StorageScanner.Hold = c.Services.Governor.Paused;
        Render();
        if (StorageScanner.Running is { } running) Wait(running);
        else if (Due() && !StorageScanner.Hold) Scan();
        else ShowStatus();
        LoadGames();
    }

    private IReadOnlyList<InstalledGame> _games = [];

    private async void LoadGames()
    {
        try { (_, _games) = await ShortcutCheck.FactsAsync(); }
        catch (Exception ex) { Log.Warn("No se pudieron leer los juegos instalados", ex); return; }
        if (_open.Any(k => k.EndsWith("|" + StorageUsage.Games))) Render();
    }

    // ── Games not played for months ──

    private static readonly TimeSpan Unused = TimeSpan.FromDays(90);

    private void AddForgotten(StackPanel box, DriveUsage d)
    {
        var forgotten = GameCatalog.Forgotten(_games, DateTimeOffset.Now, Unused, 1L << 30)
            .Where(g => string.Equals(Path.GetPathRoot(g.Folder)?.TrimEnd('\\'), d.Name, StringComparison.OrdinalIgnoreCase))
            .Take(8).ToList();
        if (forgotten.Count == 0) return;
        var title = new TextBlock
        {
            Text = $"Sin jugar hace más de 3 meses ({Size(forgotten.Sum(g => g.Bytes))})",
            FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4),
        };
        box.Children.Add(title);
        foreach (var g in forgotten)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var uninstall = Ui.Pill("Desinstalar", () => Open(g.Uninstall!), glyph: Glyphs.Delete);
            uninstall.ToolTip = "Abre el desinstalador de Steam (te pide confirmar)";
            uninstall.Margin = new Thickness(8, 0, 0, 0);
            SetDock(uninstall, Dock.Right);
            row.Children.Add(uninstall);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = $"{g.Name}  ·  {Size(g.Bytes)}", FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Ui.Text("Última vez: " + g.LastPlayed!.Value.LocalDateTime.ToString("d 'de' MMMM 'de' yyyy", Ui.Culture)));
            row.Children.Add(text);
            box.Children.Add(row);
        }
    }

    // ── Cleanup ──

    /// <summary>Caches and temporary files that their programs rebuild on their own: safe to empty.</summary>
    private static IEnumerable<(string Name, string Path)> CleanupCandidates()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return ("Archivos temporales", Path.GetTempPath());
        yield return ("Volcados de programas que fallaron", Path.Combine(local, "CrashDumps"));
        yield return ("Informes de errores de Windows", Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive"));
        yield return ("Caché de npm", Path.Combine(local, "npm-cache"));
        yield return ("Caché de Yarn", Path.Combine(local, @"Yarn\Cache"));
        yield return ("Caché de pnpm", Path.Combine(local, "pnpm-cache"));
        yield return ("Caché de pip", Path.Combine(local, @"pip\cache"));
        yield return ("Caché de NuGet (descargas)", Path.Combine(local, @"NuGet\v3-cache"));
        yield return ("Caché de Gradle", Path.Combine(profile, @".gradle\caches"));
    }

    private bool _cleanupOpen;
    private List<(string Name, string Path, long Bytes)>? _cleanup;

    private FrameworkElement CleanupSection()
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var head = new DockPanel();
        var toggle = Ui.IconButton(_cleanupOpen ? Glyphs.ChevronUp : Glyphs.ChevronDown, _cleanupOpen ? "Ocultar" : "Revisar", ToggleCleanup, 22);
        SetDock(toggle, Dock.Right);
        head.Children.Add(toggle);
        head.Children.Add(new TextBlock { Text = "Limpieza sugerida", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        box.Children.Add(head);
        if (!_cleanupOpen)
        {
            box.Children.Add(Ui.Text("Temporales y cachés que sus programas vuelven a crear.", wrap: true));
            return box;
        }
        if (_cleanup is null)
        {
            box.Children.Add(Ui.Text("Midiendo…"));
            return box;
        }
        var worth = _cleanup.Where(c => c.Bytes >= 50L * 1024 * 1024).OrderByDescending(c => c.Bytes).ToList();
        if (worth.Count == 0) box.Children.Add(Ui.Text("Nada que valga la pena limpiar ahora.", wrap: true));
        foreach (var (name, path, bytes) in worth)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var clean = Ui.Pill("Limpiar", () => Clean(name, path, bytes), glyph: Glyphs.Delete);
            clean.Margin = new Thickness(8, 0, 0, 0);
            SetDock(clean, Dock.Right);
            row.Children.Add(clean);
            var text = new TextBlock { Text = $"{name}  ·  {Size(bytes)}", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path };
            row.Children.Add(text);
            box.Children.Add(row);
        }
        return box;
    }

    private async void ToggleCleanup()
    {
        _cleanupOpen = !_cleanupOpen;
        Render();
        if (!_cleanupOpen || _cleanup is not null) return;
        _cleanup = await Task.Run(() => CleanupCandidates()
            .Where(c => Directory.Exists(c.Path))
            .Select(c => (c.Name, c.Path, StorageScanner.Measure(c.Path)))
            .ToList());
        Render();
    }

    private async void Clean(string name, string path, long bytes)
    {
        var owner = Window.GetWindow(this);
        var answer = MessageBox.Show(owner, $"¿Vaciar «{name}» ({Size(bytes)})?\n\n{path}\n\nSe borra lo que hay dentro; lo que esté en uso se deja. Su programa lo vuelve a crear cuando lo necesite.",
            "SackSpace", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        var left = await Task.Run(() =>
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                try
                {
                    // Directory.Delete removes a junction itself, never what it points to.
                    if (entry is DirectoryInfo dir) dir.Delete(true);
                    else entry.Delete();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // in use: stays
            }
            return StorageScanner.Measure(path);
        });
        var i = _cleanup?.FindIndex(c => c.Path == path) ?? -1;
        if (i >= 0) _cleanup![i] = (name, path, left);
        Render();
        _c.Toast($"Liberados {Size(Math.Max(bytes - left, 0))} de «{name}».");
    }

    private TimeSpan? Interval => _c.Settings.GetString("interval", "1 día") switch
    {
        "6 h" => TimeSpan.FromHours(6),
        "12 h" => TimeSpan.FromHours(12),
        "1 semana" => TimeSpan.FromDays(7),
        "Solo a mano" => null,
        _ => TimeSpan.FromDays(1),
    };

    private bool Due() => ReadAt() is not { } at || (Interval is { } every && DateTimeOffset.Now - at > every);

    private void OnPaused(bool paused)
    {
        StorageScanner.Hold = paused;
        if (StorageScanner.Running is not null) _status.Text = paused ? "Medición en pausa mientras juegas o ves algo a pantalla completa." : "Midiendo…";
    }

    private void OnMinute(DateTime _)
    {
        if (StorageScanner.Running is not null) return;
        if (Due() && !StorageScanner.Hold) Scan();
        else ShowStatus();
    }

    private void OnProgress(string text)
    {
        if (!StorageScanner.Hold) _status.Text = text;
    }

    private void Scan() => Wait(StorageScanner.Start(_c.Settings.GetString("extra", "")));

    private async void Wait(Task<IReadOnlyList<DriveUsage>> scan)
    {
        _status.Text = "Midiendo…";
        try
        {
            var drives = await scan;
            _c.Settings["scan"] = ToJson(drives, _c.Settings["scan"]);
            _c.Save();
            WarnIfFull(drives);
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo medir el almacenamiento", ex);
            _c.Toast("No se pudo medir el almacenamiento: " + ex.Message);
        }
        Render();
        ShowStatus();
    }

    private void WarnIfFull(IReadOnlyList<DriveUsage> drives)
    {
        var full = drives.Where(d => d.Free < d.Total * 0.1).Select(d => $"{d.Name} ({Size(d.Free)} libres)").ToList();
        if (full.Count > 0) _c.Toast("Queda poco espacio en " + string.Join(", ", full));
    }

    private ContextMenu MoreMenu(UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(Ui.Item("Medir de nuevo", Glyphs.Refresh, Scan));
        menu.Items.Add(Ui.Item("Vaciar la papelera…", Glyphs.Delete, EmptyRecycleBin));
        menu.Items.Add(Ui.Item("Liberar espacio (Sensor de almacenamiento)", Glyphs.Settings, () => Open("ms-settings:storagesense")));
        menu.Items.Add(Ui.Item("Liberador de espacio de Windows", Glyphs.Drive, () => Open("cleanmgr.exe")));
        return menu;
    }

    private void Open(string target)
    {
        if (!InstalledApps.Launch(target, out var error)) _c.Toast($"No se pudo abrir: {error}");
    }

    // ── Status ──

    private DateTimeOffset? ReadAt() => Date(_c.Settings["scan"]?["at"]);

    private static DateTimeOffset? Date(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s)
        && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null;

    private void ShowStatus()
    {
        if (ReadAt() is not { } at) { _status.Text = "Aún sin medir."; return; }
        var drives = FromJson(_c.Settings["scan"]);
        var total = drives.Sum(d => d.Total);
        var used = drives.Sum(d => d.Total - d.Free);
        _status.Text = (drives.Count > 1 ? $"{Size(used)} usados de {Size(total)} · " : "") + "medido " + Ago(at);
    }

    private static string Ago(DateTimeOffset at)
    {
        var ago = DateTimeOffset.Now - at;
        return ago.TotalMinutes < 1 ? "ahora mismo"
            : ago.TotalHours < 1 ? $"hace {(int)ago.TotalMinutes} min"
            : ago.TotalDays < 1 ? $"hace {(int)ago.TotalHours} h"
            : (int)ago.TotalDays == 1 ? "hace 1 día" : $"hace {(int)ago.TotalDays} días";
    }

    /// <summary>"512 MB", "4.3 GB", "372 GB", "1.25 TB".</summary>
    public static string Size(long bytes)
    {
        var gb = bytes / Gb;
        return gb < 1 ? string.Format(Ui.Culture, "{0:0} MB", bytes / (1024d * 1024))
            : gb < 10 ? string.Format(Ui.Culture, "{0:0.0} GB", gb)
            : gb < 1000 ? string.Format(Ui.Culture, "{0:0} GB", gb)
            : string.Format(Ui.Culture, "{0:0.00} TB", gb / 1024);
    }

    private static string Delta(long bytes) => (bytes >= 0 ? "+" : "−") + Size(Math.Abs(bytes));

    // ── View ──

    private void Render()
    {
        _list.Children.Clear();
        var drives = FromJson(_c.Settings["scan"]);
        foreach (var d in drives) _list.Children.Add(Drive(d));
        if (drives.Count == 0) _list.Children.Add(Ui.Text("La primera medición recorre todo el disco y tarda unos minutos; puedes seguir usando el PC.", wrap: true));
        _list.Children.Add(CleanupSection());
    }

    private FrameworkElement Drive(DriveUsage d)
    {
        var top = new DockPanel();
        var used = Ui.Text($"{Size(d.Total - d.Free)} de {Size(d.Total)}");
        SetDock(used, Dock.Right);
        top.Children.Add(used);
        top.Children.Add(new TextBlock { Text = d.Label.Length > 0 ? $"{d.Name}  {d.Label}" : d.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        row.Children.Add(top);
        row.Children.Add(StackedBar(d));
        row.Children.Add(FreeLine(d));

        var legend = new WrapPanel { Margin = new Thickness(-4, 6, 0, 0) };
        foreach (var (category, bytes) in d.Parts.Where(p => p.Bytes / Gb >= 0.1)) legend.Children.Add(Chip(d, category, bytes));
        row.Children.Add(legend);
        foreach (var (category, bytes) in d.Parts.Where(p => _open.Contains(Key(d, p.Category))))
            row.Children.Add(Breakdown(d, category, bytes));
        return row;
    }

    private static string Key(DriveUsage d, string category) => d.Name + "|" + category;

    /// <summary>Free space, a warning under 10 % (icon and words, not colour alone) and the change since the baseline.</summary>
    private FrameworkElement FreeLine(DriveUsage d)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var low = d.Free < d.Total * 0.1;
        if (low)
        {
            var warn = Ui.Glyph(Glyphs.Warning, 12);
            warn.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x67, 0x67));
            warn.Margin = new Thickness(0, 0, 6, 0);
            warn.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(warn);
        }
        var text = (low ? "Queda poco espacio: " : "") + $"{Size(d.Free)} libres ({d.Free * 100 / Math.Max(d.Total, 1)} %)";
        if (d.Baseline is { } b && Math.Abs(d.Total - d.Free - b.Used) >= 100L * 1024 * 1024)
            text += $" · {Delta(d.Total - d.Free - b.Used)} usados desde {Ago(b.At)}";
        line.Children.Add(Ui.Text(text));
        return line;
    }

    /// <summary>One segment per category, then the free space as the bare track; 2 px gaps between segments.</summary>
    private Grid StackedBar(DriveUsage d)
    {
        var bar = new Grid { Height = 10, Margin = new Thickness(0, 6, 0, 0) };
        var track = new Border { CornerRadius = new CornerRadius(5) };
        track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        bar.Children.Add(track);
        var segments = new Grid();
        bar.Children.Add(segments);
        var parts = d.Parts.Where(p => p.Bytes > 0).ToList();
        var total = (double)Math.Max(d.Total, 1);
        for (var i = 0; i < parts.Count; i++)
        {
            var (category, bytes) = parts[i];
            segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(bytes / total, GridUnitType.Star) });
            var seg = new Border
            {
                Background = new SolidColorBrush(ColorOf(category)),
                CornerRadius = new CornerRadius(i == 0 ? 5 : 0, 0, 0, i == 0 ? 5 : 0),
                Margin = new Thickness(0, 0, 2, 0),
                ToolTip = Tip(d, category, bytes),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            seg.MouseLeftButtonUp += (_, _) => Toggle(d, category);
            Grid.SetColumn(seg, i);
            segments.Children.Add(seg);
        }
        segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(d.Free, 0) / total + 0.0001, GridUnitType.Star) });
        return bar;
    }

    private static string Tip(DriveUsage d, string category, long bytes)
    {
        var tip = $"{category}: {Size(bytes)} ({bytes * 100.0 / Math.Max(d.Total, 1):0.#} % del disco)";
        if (d.Baseline?.Parts.TryGetValue(category, out var before) == true && Math.Abs(bytes - before) >= 100L * 1024 * 1024)
            tip += $"\n{Delta(bytes - before)} desde {Ago(d.Baseline.At)}";
        return tip + "\nClic para ver qué ocupa";
    }

    private void Toggle(DriveUsage d, string category)
    {
        if (!_open.Remove(Key(d, category))) _open.Add(Key(d, category));
        _c.Settings["open"] = new JsonArray(_open.Select(k => (JsonNode)k).ToArray());
        _c.Save();
        Render();
    }

    private Button Chip(DriveUsage d, string category, long bytes)
    {
        var swatch = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(ColorOf(category)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(swatch);
        content.Children.Add(Ui.Text($"{category} {Size(bytes)}"));
        var open = _open.Contains(Key(d, category));
        var face = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 2, 6, 2), Child = content, Background = Brushes.Transparent };
        if (open) face.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        var chip = new Button
        {
            Content = face, Template = GallerySheet.BareButton(), Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 4, 2), ToolTip = Tip(d, category, bytes),
        };
        chip.MouseEnter += (_, _) => { if (!open) face.SetResourceReference(Border.BackgroundProperty, "HoverBrush"); };
        chip.MouseLeave += (_, _) => { if (!open) face.Background = Brushes.Transparent; };
        chip.Click += (_, _) => Toggle(d, category);
        return chip;
    }

    /// <summary>A category's biggest folders on this drive, each with a bar against the category; a click opens it in Explorer.</summary>
    private FrameworkElement Breakdown(DriveUsage d, string category, long bytes)
    {
        var box = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var close = Ui.IconButton(Glyphs.Close, "Cerrar", () => Toggle(d, category), 22);
        SetDock(close, Dock.Right);
        title.Children.Add(close);
        title.Children.Add(new TextBlock { Text = $"{category} en {d.Name}", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        box.Children.Add(title);

        var items = d.Items.GetValueOrDefault(category) ?? [];
        foreach (var item in items) box.Children.Add(ItemRow(item, bytes, ColorOf(category)));
        if (category == StorageUsage.Games) AddForgotten(box, d);

        var note = category switch
        {
            StorageUsage.Hidden => "Puntos de restauración, datos del sistema de archivos y carpetas que piden permisos de administrador. Para reducirlo: Liberador de espacio › Limpiar archivos del sistema.",
            StorageUsage.Other => "Carpetas que SackSpace no sabe de qué son. Clasifícalas en las opciones del panel: Categoría=carpeta (p. ej. Juegos=E:\\MisJuegos).",
            StorageUsage.Trash => null,
            _ => items.Count == 0 ? "Sin detalle: vuelve a medir." : null,
        };
        if (note is not null)
        {
            var hint = Ui.Text(note, wrap: true);
            hint.Margin = new Thickness(0, 4, 0, 0);
            box.Children.Add(hint);
        }
        if (category == StorageUsage.Trash)
        {
            var empty = Ui.Pill("Vaciar la papelera", EmptyRecycleBin, glyph: Glyphs.Delete);
            empty.HorizontalAlignment = HorizontalAlignment.Left;
            empty.Margin = new Thickness(0, 6, 0, 0);
            box.Children.Add(empty);
        }
        return box;
    }

    private Button ItemRow(StorageItem item, long categoryBytes, Color color)
    {
        var name = Path.GetFileName(item.Path.TrimEnd('\\'));
        var top = new DockPanel();
        var size = Ui.Text(Size(item.Bytes));
        SetDock(size, Dock.Right);
        top.Children.Add(size);
        top.Children.Add(new TextBlock { Text = name.Length > 0 ? name : item.Path, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) });

        var bar = new Grid { Height = 3, Margin = new Thickness(0, 3, 0, 0) };
        var share = Math.Clamp(item.Bytes / (double)Math.Max(categoryBytes, 1), 0.002, 1);
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share + 0.0001, GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(1.5) };
        track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(color) });

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(bar);
        var face = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 5), Child = stack, Background = Brushes.Transparent };
        var row = new Button
        {
            Content = face, Template = GallerySheet.BareButton(), Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = item.Path, Margin = new Thickness(-6, 0, -6, 0),
        };
        row.MouseEnter += (_, _) => face.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        row.MouseLeave += (_, _) => face.Background = Brushes.Transparent;
        row.Click += (_, _) => InstalledApps.ShowInFolder(item.Path);
        var menu = new ContextMenu();
        menu.Items.Add(Ui.Item("Abrir en el Explorador", Glyphs.Folder, () => InstalledApps.ShowInFolder(item.Path)));
        menu.Items.Add(Ui.Item("Copiar la ruta", Glyphs.Document, () =>
        {
            try { Clipboard.SetText(item.Path); }
            catch (COMException) { } // the clipboard is busy in another app
        }));
        row.ContextMenu = menu;
        return row;
    }

    // ── Recycle bin ──

    private void EmptyRecycleBin()
    {
        var hwnd = Window.GetWindow(this) is { } w ? new WindowInteropHelper(w).Handle : IntPtr.Zero;
        // Windows asks for confirmation itself; a cancel or an already empty bin returns an error.
        if (SHEmptyRecycleBinW(hwnd, null, 0) != 0) return;
        if (_c.Settings["scan"]?["drives"] is JsonArray drives)
            foreach (var d in drives.OfType<JsonObject>())
                if (d["parts"] is JsonObject parts && parts[StorageUsage.Trash] is JsonValue v && v.TryGetValue<long>(out var trash))
                {
                    parts.Remove(StorageUsage.Trash);
                    d["free"] = (d["free"]?.GetValue<long>() ?? 0) + trash;
                    (d["items"] as JsonObject)?.Remove(StorageUsage.Trash);
                }
        _c.Save();
        Render();
        _c.Toast("Papelera vaciada.");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? rootPath, uint flags);

    // ── Colours ──

    // Validated categorical palette (dataviz reference), fixed order; folders of the user's own
    // categories share a slate, with their names in the legend so colour never carries identity alone.
    private static readonly string[] Light = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"];
    private static readonly string[] Dark = ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"];

    private Color ColorOf(string category)
    {
        var dark = _c.Services.Theme.Current.IsDark;
        var i = StorageUsage.Categories.ToList().IndexOf(category);
        var hex = category == StorageUsage.Other ? "#a3a39c"
            : category == StorageUsage.Hidden ? (dark ? "#7a7a74" : "#6a6a65")
            : i >= 0 ? (dark ? Dark : Light)[i]
            : "#6f8197";
        return (Color)ColorConverter.ConvertFromString(hex);
    }

    // ── Saved result ──

    /// <summary>The new measurement, with the baseline it is compared to: the old measurement once it is 12 h old, else the old one's baseline.</summary>
    private static JsonObject ToJson(IReadOnlyList<DriveUsage> drives, JsonNode? old)
    {
        var now = DateTimeOffset.Now;
        var oldAt = Date(old?["at"]);
        var oldDrives = (old?["drives"] as JsonArray)?.OfType<JsonObject>().ToDictionary(d => d["name"]?.GetValue<string>() ?? "") ?? [];
        var arr = new JsonArray();
        foreach (var d in drives)
        {
            var parts = new JsonObject();
            foreach (var (category, bytes) in d.Parts) parts[category] = bytes;
            var items = new JsonObject();
            foreach (var (category, list) in d.Items)
                items[category] = new JsonArray(list.Select(i => (JsonNode)new JsonArray(i.Path, i.Bytes)).ToArray());
            JsonNode? baseline = null;
            if (oldDrives.TryGetValue(d.Name, out var o))
                baseline = oldAt is { } at && now - at >= TimeSpan.FromHours(12)
                    ? new JsonObject { ["at"] = at.ToString("o"), ["used"] = (o["total"]?.GetValue<long>() ?? 0) - (o["free"]?.GetValue<long>() ?? 0), ["parts"] = o["parts"]?.DeepClone() }
                    : o["baseline"]?.DeepClone();
            arr.Add(new JsonObject
            {
                ["name"] = d.Name, ["label"] = d.Label, ["total"] = d.Total, ["free"] = d.Free,
                ["parts"] = parts, ["items"] = items, ["baseline"] = baseline,
            });
        }
        return new JsonObject { ["at"] = now.ToString("o"), ["drives"] = arr };
    }

    private static IReadOnlyList<DriveUsage> FromJson(JsonNode? scan)
    {
        var list = new List<DriveUsage>();
        if (scan?["drives"] is not JsonArray arr) return list;
        foreach (var d in arr.OfType<JsonObject>())
        {
            try
            {
                var parts = (d["parts"] as JsonObject)?.Select(kv => (kv.Key, kv.Value?.GetValue<long>() ?? 0)).ToList() ?? [];
                var items = (d["items"] as JsonObject)?.ToDictionary(kv => kv.Key,
                    kv => (IReadOnlyList<StorageItem>)((kv.Value as JsonArray)?.OfType<JsonArray>()
                        .Select(i => new StorageItem(i[0]?.GetValue<string>() ?? "", i[1]?.GetValue<long>() ?? 0)).ToList() ?? []))
                    ?? new Dictionary<string, IReadOnlyList<StorageItem>>();
                DriveBaseline? baseline = d["baseline"] is JsonObject b && Date(b["at"]) is { } at
                    ? new DriveBaseline(at, b["used"]?.GetValue<long>() ?? 0,
                        (b["parts"] as JsonObject)?.ToDictionary(kv => kv.Key, kv => kv.Value?.GetValue<long>() ?? 0) ?? [])
                    : null;
                list.Add(new DriveUsage(d["name"]?.GetValue<string>() ?? "?", d["label"]?.GetValue<string>() ?? "",
                    d["total"]?.GetValue<long>() ?? 0, d["free"]?.GetValue<long>() ?? 0, parts, items, baseline));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException) { Log.Warn("Medición guardada ilegible", ex); }
        }
        return list;
    }

    public void Dispose()
    {
        StorageScanner.Progress -= OnProgress;
        _c.Services.Clock.Minute -= OnMinute;
        _c.Services.Governor.PausedChanged -= OnPaused;
    }
}

/// <summary>Walks every fixed drive, one walk at a time for every panel, on four low-priority threads.</summary>
internal static class StorageScanner
{
    public static Task<IReadOnlyList<DriveUsage>>? Running { get; private set; }

    /// <summary>While true (a game or video at full screen, the session locked) the walk waits, so it does not compete for the disk.</summary>
    public static volatile bool Hold;

    /// <summary>"Midiendo… 12 de 80 carpetas", raised on the UI thread.</summary>
    public static event Action<string>? Progress;

    public static Task<IReadOnlyList<DriveUsage>> Start(string extra)
    {
        if (Running is { } r) return r;
        var ui = new Progress<string>(text => Progress?.Invoke(text)); // created here: reports come back on the UI thread
        var done = new TaskCompletionSource<IReadOnlyList<DriveUsage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(Measure(extra, ui)); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "SackSpace almacenamiento" };
        Running = done.Task;
        done.Task.ContinueWith(_ => Running = null, TaskScheduler.FromCurrentSynchronizationContext());
        thread.Start();
        return done.Task;
    }

    private sealed record Result(string Drive, StoragePlanItem Item, long Bytes, IReadOnlyList<StorageItem> Items);

    private static IReadOnlyList<DriveUsage> Measure(string extra, IProgress<string> progress)
    {
        var drives = DriveInfo.GetDrives().Where(Ready).ToList();
        var plan = StorageUsage.Plan(Roots(drives, extra))
            .Select(item => (Item: item, Path: Resolve(item.Path)))
            .Select(x => (x.Item, x.Path, Drive: Path.GetPathRoot(x.Path)?.TrimEnd('\\').ToUpperInvariant() ?? ""))
            .ToList();
        var results = new System.Collections.Concurrent.ConcurrentBag<Result>();
        var done = 0;
        var watch = Stopwatch.StartNew();
        // Four low-priority workers share the folders (SSDs answer several reads at once).
        var queue = new System.Collections.Concurrent.ConcurrentQueue<(StoragePlanItem Item, string Path, string Drive)>(plan);
        var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
        {
            while (queue.TryDequeue(out var next))
            {
                var (item, path, drive) = next;
                var started = watch.Elapsed;
                var (bytes, items) = Size(path, item);
                if (watch.Elapsed - started > TimeSpan.FromSeconds(10))
                    Log.Info($"Almacenamiento: '{path}' tardó {(watch.Elapsed - started).TotalSeconds:0} s");
                results.Add(new Result(drive, item, bytes, items));
                progress.Report($"Midiendo… {Interlocked.Increment(ref done)} de {plan.Count} carpetas");
            }
        }) { IsBackground = true, Priority = ThreadPriority.Lowest }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        Log.Info($"Almacenamiento medido en {watch.Elapsed.TotalSeconds:0} s ({plan.Count} carpetas)");

        return drives.Select(d =>
        {
            var name = d.Name.TrimEnd('\\').ToUpperInvariant();
            var mine = results.Where(r => r.Drive == name).ToList();
            var parts = StorageUsage.Summarize(d.TotalSize - d.TotalFreeSpace, mine.Select(r => (r.Item.Category, r.Bytes)));
            var items = mine.GroupBy(r => r.Item.Category).ToDictionary(g => g.Key,
                g => (IReadOnlyList<StorageItem>)g.SelectMany(r => r.Items).OrderByDescending(i => i.Bytes).Take(12).ToList());
            return new DriveUsage(name, d.VolumeLabel, d.TotalSize, d.TotalFreeSpace, parts, items, null);
        }).ToList();
    }

    private static bool Ready(DriveInfo d)
    {
        try { return d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Real folders inside a folder: junctions ("Documents and Settings", "Application Data") would count things twice.</summary>
    private static IEnumerable<DirectoryInfo> Folders(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Generic folders first (every top-level folder, every folder of the profile), then the known ones, the user's last: in Plan the later wins.</summary>
    private static IEnumerable<StorageRoot> Roots(IReadOnlyList<DriveInfo> drives, string extra)
    {
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var local = F(Environment.SpecialFolder.LocalApplicationData);
        var roaming = F(Environment.SpecialFolder.ApplicationData);
        var profile = F(Environment.SpecialFolder.UserProfile);

        foreach (var d in drives)
            foreach (var dir in Folders(d.Name))
                if (dir.Name is not ("$Recycle.Bin" or "System Volume Information"))
                    yield return new(StorageUsage.Classify(dir.Name) ?? StorageUsage.Other, dir.FullName, Expand: false);
        foreach (var dir in Folders(profile))
            yield return new(StorageUsage.Classify(dir.Name) ?? StorageUsage.Other, dir.FullName, Expand: false);

        yield return new(StorageUsage.System, F(Environment.SpecialFolder.Windows));
        foreach (var d in drives)
        {
            foreach (var file in new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys" })
                yield return new(StorageUsage.System, Path.Combine(d.Name, file), Expand: false);
            yield return new(StorageUsage.Trash, Path.Combine(d.Name, "$Recycle.Bin"), Expand: false);
            yield return new(StorageUsage.Games, Path.Combine(d.Name, "XboxGames"));
        }
        yield return new(StorageUsage.Programs, F(Environment.SpecialFolder.ProgramFiles));
        yield return new(StorageUsage.Programs, F(Environment.SpecialFolder.ProgramFilesX86));
        yield return new(StorageUsage.Programs, F(Environment.SpecialFolder.CommonApplicationData));
        yield return new(StorageUsage.AppData, roaming);
        yield return new(StorageUsage.AppData, local);
        yield return new(StorageUsage.Programs, Path.Combine(local, "Programs"));
        foreach (var name in new[] { "Pictures", "Videos" })
            if (ShellNames.KnownFolder(name) is { } p) yield return new(StorageUsage.Media, p);
        foreach (var name in new[] { "Documents", "Downloads", "Desktop", "Music" })
            if (ShellNames.KnownFolder(name) is { } p) yield return new(StorageUsage.Files, p);

        // Developer tools that hide in AppData: Docker and WSL disks, SDKs, package caches.
        foreach (var dev in new[] { "Docker", "Android", "JetBrains", "npm-cache", "pnpm", "pnpm-cache", "Yarn" })
            yield return new(StorageUsage.Dev, Path.Combine(local, dev));
        yield return new(StorageUsage.Dev, Path.Combine(roaming, "npm"));
        foreach (var distro in Folders(Path.Combine(local, "Packages")).Where(p => p.Name.StartsWith("CanonicalGroupLimited.", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Debian", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("openSUSE", StringComparison.OrdinalIgnoreCase)))
            yield return new(StorageUsage.Dev, distro.FullName, Expand: false);
        yield return new(StorageUsage.Games, Path.Combine(local, @"Google\Play Games"));

        foreach (var lib in GameLibraries.SteamLibraries() ?? [])
            yield return new(StorageUsage.Games, Path.Combine(lib, "steamapps"));
        foreach (var (app, folder) in GameLibraries.EpicInstalls() ?? [])
            yield return new(app.StartsWith("UE_", StringComparison.OrdinalIgnoreCase) ? StorageUsage.Programs : StorageUsage.Games, folder, Expand: false); // UE_5.3 is the engine

        foreach (var own in AiUsageParser.ParseAccounts(extra))
            yield return new(StorageUsage.Categories.FirstOrDefault(c => c.Equals(own.Name, StringComparison.CurrentCultureIgnoreCase)) ?? own.Name, own.Folder);
    }

    /// <summary>A root that is itself a junction is measured where it points (and counted on that drive).</summary>
    private static string Resolve(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            return dir.Exists && dir.LinkTarget is not null ? dir.ResolveLinkTarget(true)?.FullName ?? path : path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return path; }
    }

    // OneDrive "online only" files have a size but take nothing on the disk.
    private const FileAttributes CloudOnly = FileAttributes.Offline | (FileAttributes)0x400000 /* RECALL_ON_DATA_ACCESS */ | (FileAttributes)0x40000 /* RECALL_ON_OPEN */;

    private static long OnDisk(string path, long fallback)
    {
        var low = GetCompressedFileSizeW(path, out var high);
        return low == uint.MaxValue && Marshal.GetLastWin32Error() != 0 ? fallback : ((long)high << 32) | low;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string path, out uint high);

    /// <summary>The bytes under a root (minus nested roots) and the entries its breakdown lists.</summary>
    /// <summary>The bytes a folder takes on the disk, without a breakdown.</summary>
    public static long Measure(string path) => Size(path, new StoragePlanItem("", path, [], Expand: false)).Bytes;

    private static (long Bytes, IReadOnlyList<StorageItem> Items) Size(string path, StoragePlanItem item)
    {
        try
        {
            if (File.Exists(path))
            {
                var length = new FileInfo(path).Length;
                return (length, [new StorageItem(path, length)]);
            }
            if (!Directory.Exists(path)) return (0, []);
            var skip = item.Skip.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var children = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var grandchildren = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
            var rootLength = path.TrimEnd('\\').Length;
            string? lastDir = null;
            string first = "", second = "";
            var atRoot = true;

            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            var files = new FileSystemEnumerable<long>(path, (ref FileSystemEntry e) =>
            {
                // Sparse and compressed files (an emulator's 512 GB userdata.img) take far less than their length.
                var length = (e.Attributes & (FileAttributes.SparseFile | FileAttributes.Compressed)) != 0 ? OnDisk(e.ToFullPath(), e.Length) : e.Length;
                if (!item.Expand) return length;
                // Files come grouped by folder: the folder's first two levels under the root are worked out once per folder.
                if (lastDir is null || !e.Directory.SequenceEqual(lastDir))
                {
                    lastDir = e.Directory.ToString();
                    var rel = lastDir.Length > rootLength ? lastDir[(rootLength + 1)..] : "";
                    atRoot = rel.Length == 0;
                    var slash = rel.IndexOf('\\');
                    first = slash < 0 ? rel : rel[..slash];
                    var rest = slash < 0 ? "" : rel[(slash + 1)..];
                    var slash2 = rest.IndexOf('\\');
                    second = slash2 < 0 ? rest : rest[..slash2];
                }
                var key = atRoot ? e.FileName.ToString() : first;
                children[key] = children.GetValueOrDefault(key) + length;
                if (!atRoot)
                {
                    if (!grandchildren.TryGetValue(first, out var inner)) grandchildren[first] = inner = new(StringComparer.OrdinalIgnoreCase);
                    inner[second] = inner.GetValueOrDefault(second) + length; // "" = files right inside the child
                }
                return length;
            }, options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && (e.Attributes & CloudOnly) == 0,
                ShouldRecursePredicate = (ref FileSystemEntry e) =>
                {
                    while (Hold) Thread.Sleep(1000);
                    var full = e.ToFullPath();
                    if (skip.Contains(full)) return false;
                    // Junctions and symlinks (loops, data on another drive) are skipped; OneDrive folders are reparse points too but hold real files.
                    return (e.Attributes & FileAttributes.ReparsePoint) == 0 || new DirectoryInfo(full).LinkTarget is null;
                },
            };
            long sum = 0;
            foreach (var length in files) sum += length;
            if (!item.Expand) return (sum, sum > 0 ? [new StorageItem(path, sum)] : []);
            var inner = grandchildren.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, long>)kv.Value, StringComparer.OrdinalIgnoreCase);
            return (sum, StorageUsage.TopItems(path, sum, children, inner, 12));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se pudo medir '{path}'", e);
            return (0, []);
        }
    }
}
