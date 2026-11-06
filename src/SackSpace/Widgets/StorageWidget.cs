using System.IO;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Interop;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>One drive's measured space: what each category takes of it.</summary>
public sealed record DriveUsage(string Name, string Label, long Total, long Free, IReadOnlyList<(string Category, long Bytes)> Parts);

/// <summary>
/// What takes the space on each fixed drive. The known folders (Windows, Program Files,
/// AppData, the user's folders, the Steam/Epic/Xbox libraries, the recycle bins and the
/// user's own folders) are walked on a low-priority thread; the rest is "Sin clasificar".
/// The result is kept in the panel's settings, so it is measured again only once a day
/// or with the refresh button. Nothing leaves the PC.
/// </summary>
public sealed class StorageWidget : DockPanel, IDisposable
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private const double Gb = 1024d * 1024 * 1024;

    private readonly WidgetContext _c;
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = Ui.Text("");

    public StorageWidget(WidgetContext c)
    {
        _c = c;
        var refresh = Ui.IconButton(Glyphs.Refresh, "Medir de nuevo", () => Scan(), 28);
        var header = Ui.Header(Glyphs.Storage, "Almacenamiento", refresh);
        SetDock(header, Dock.Top);
        Children.Add(header);
        _status.Margin = new Thickness(0, -4, 0, 10);
        SetDock(_status, Dock.Top);
        Children.Add(_status);
        Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        StorageScanner.Progress += OnProgress;
        c.Services.Clock.Minute += OnMinute;
        Render();
        if (StorageScanner.Running is { } running) Wait(running);
        else if (ReadAt() is not { } at || DateTimeOffset.Now - at > MaxAge) Scan();
        else ShowAge();
    }

    private void OnMinute(DateTime _)
    {
        if (StorageScanner.Running is null && ReadAt() is { } at && DateTimeOffset.Now - at > MaxAge) Scan();
        else if (StorageScanner.Running is null) ShowAge();
    }

    private void OnProgress(string text) => _status.Text = text;

    private void Scan() => Wait(StorageScanner.Start(_c.Settings.GetString("extra", "")));

    private async void Wait(Task<IReadOnlyList<DriveUsage>> scan)
    {
        _status.Text = "Midiendo…";
        try
        {
            var drives = await scan;
            _c.Settings["scan"] = ToJson(drives);
            _c.Save();
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo medir el almacenamiento", ex);
            _c.Toast("No se pudo medir el almacenamiento: " + ex.Message);
        }
        Render();
        ShowAge();
    }

    private DateTimeOffset? ReadAt() =>
        _c.Settings["scan"]?["at"] is JsonValue v && v.TryGetValue<string>(out var s) && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null;

    private void ShowAge()
    {
        if (ReadAt() is not { } at) { _status.Text = "Aún sin medir."; return; }
        var ago = DateTimeOffset.Now - at;
        _status.Text = ago.TotalMinutes < 1 ? "Medido ahora mismo"
            : ago.TotalHours < 1 ? $"Medido hace {(int)ago.TotalMinutes} min"
            : $"Medido hace {(int)ago.TotalHours} h";
    }

    private void Render()
    {
        _list.Children.Clear();
        var drives = FromJson(_c.Settings["scan"]);
        foreach (var d in drives) _list.Children.Add(Drive(d));
        if (drives.Count == 0) _list.Children.Add(Ui.Text("La primera medición tarda un minuto o dos.", wrap: true));
    }

    private FrameworkElement Drive(DriveUsage d)
    {
        var top = new DockPanel();
        var used = Ui.Text(string.Format(Ui.Culture, "{0:0} de {1:0} GB usados", (d.Total - d.Free) / Gb, d.Total / Gb));
        SetDock(used, Dock.Right);
        top.Children.Add(used);
        top.Children.Add(new TextBlock { Text = d.Label.Length > 0 ? $"{d.Name}  {d.Label}" : d.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        row.Children.Add(top);
        row.Children.Add(StackedBar(d));
        var legend = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (category, bytes) in d.Parts.Where(p => p.Bytes / Gb >= 0.5)) legend.Children.Add(Chip(category, bytes));
        row.Children.Add(legend);
        return row;
    }

    /// <summary>One segment per category, then the free space as the bare track; 2 px gaps between segments.</summary>
    private Grid StackedBar(DriveUsage d)
    {
        var bar = new Grid { Height = 8, Margin = new Thickness(0, 6, 0, 0) };
        var track = new Border { CornerRadius = new CornerRadius(4) };
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
                CornerRadius = new CornerRadius(i == 0 ? 4 : 0, 0, 0, i == 0 ? 4 : 0),
                Margin = new Thickness(0, 0, 2, 0),
                ToolTip = string.Format(Ui.Culture, "{0}: {1:0.#} GB", category, bytes / Gb),
            };
            Grid.SetColumn(seg, i);
            segments.Children.Add(seg);
        }
        segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(d.Free, 0) / total + 0.0001, GridUnitType.Star) });
        return bar;
    }

    private FrameworkElement Chip(string category, long bytes)
    {
        var swatch = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(ColorOf(category)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 4) };
        chip.Children.Add(swatch);
        chip.Children.Add(Ui.Text(string.Format(Ui.Culture, bytes / Gb >= 10 ? "{0} {1:0} GB" : "{0} {1:0.#} GB", category, bytes / Gb)));
        return chip;
    }

    // Validated categorical palette (dataviz reference), fixed order; the user's own folders share violet,
    // with their names in the legend so colour never carries identity alone.
    private static readonly string[] Light = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#e34948"];
    private static readonly string[] Dark = ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#e66767"];

    private Color ColorOf(string category)
    {
        var dark = _c.Services.Theme.Current.IsDark;
        var i = StorageUsage.Categories.ToList().IndexOf(category);
        var hex = category == StorageUsage.Other ? "#8c8c86"
            : i >= 0 ? (dark ? Dark : Light)[i]
            : dark ? "#9085e9" : "#6250d6";
        return (Color)ColorConverter.ConvertFromString(hex);
    }

    private static JsonObject ToJson(IReadOnlyList<DriveUsage> drives)
    {
        var arr = new JsonArray();
        foreach (var d in drives)
        {
            var parts = new JsonObject();
            foreach (var (category, bytes) in d.Parts) parts[category] = bytes;
            arr.Add(new JsonObject { ["name"] = d.Name, ["label"] = d.Label, ["total"] = d.Total, ["free"] = d.Free, ["parts"] = parts });
        }
        return new JsonObject { ["at"] = DateTimeOffset.Now.ToString("o"), ["drives"] = arr };
    }

    private static IReadOnlyList<DriveUsage> FromJson(JsonNode? scan)
    {
        var list = new List<DriveUsage>();
        if (scan?["drives"] is not JsonArray arr) return list;
        foreach (var d in arr.OfType<JsonObject>())
        {
            var parts = (d["parts"] as JsonObject)?.Select(kv => (kv.Key, kv.Value?.GetValue<long>() ?? 0)).ToList() ?? [];
            list.Add(new DriveUsage(d["name"]?.GetValue<string>() ?? "?", d["label"]?.GetValue<string>() ?? "",
                d["total"]?.GetValue<long>() ?? 0, d["free"]?.GetValue<long>() ?? 0, parts));
        }
        return list;
    }

    public void Dispose()
    {
        StorageScanner.Progress -= OnProgress;
        _c.Services.Clock.Minute -= OnMinute;
    }
}

/// <summary>Walks the known folders once at a time for every panel, on a low-priority thread.</summary>
internal static class StorageScanner
{
    public static Task<IReadOnlyList<DriveUsage>>? Running { get; private set; }

    /// <summary>"Midiendo C:\Windows (3 de 30)…", raised on the UI thread.</summary>
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

    private static IReadOnlyList<DriveUsage> Measure(string extra, IProgress<string> progress)
    {
        var drives = DriveInfo.GetDrives().Where(Ready).ToList();
        var plan = StorageUsage.Plan(Roots(drives, extra))
            .Select(item => (Item: item, Path: Resolve(item.Path)))
            .Select(x => (x.Item, x.Path, Drive: Path.GetPathRoot(x.Path)?.TrimEnd('\\').ToUpperInvariant() ?? ""))
            .ToList();
        var measured = new System.Collections.Concurrent.ConcurrentBag<(string Drive, string Category, long Bytes)>();
        var done = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // One thread per drive: each disk is read on its own, a drive's folders one after another.
        var threads = plan.GroupBy(x => x.Drive).Select(group => new Thread(() =>
        {
            foreach (var (item, path, drive) in group)
            {
                measured.Add((drive, item.Category, Size(path, item.Skip)));
                progress.Report($"Midiendo… {Interlocked.Increment(ref done)} de {plan.Count} carpetas");
            }
        }) { IsBackground = true, Priority = ThreadPriority.Lowest }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        Log.Info($"Almacenamiento medido en {watch.Elapsed.TotalSeconds:0} s ({plan.Count} carpetas)");
        return drives.Select(d =>
        {
            var name = d.Name.TrimEnd('\\').ToUpperInvariant();
            var parts = StorageUsage.Summarize(d.TotalSize - d.TotalFreeSpace,
                measured.Where(m => m.Drive == name).Select(m => (m.Category, m.Bytes)));
            return new DriveUsage(name, d.VolumeLabel, d.TotalSize, d.TotalFreeSpace, parts);
        }).ToList();
    }

    private static bool Ready(DriveInfo d)
    {
        try { return d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static IEnumerable<StorageRoot> Roots(IReadOnlyList<DriveInfo> drives, string extra)
    {
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var local = F(Environment.SpecialFolder.LocalApplicationData);

        yield return new(StorageUsage.System, F(Environment.SpecialFolder.Windows));
        foreach (var d in drives)
        {
            foreach (var file in new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys" })
                yield return new(StorageUsage.System, Path.Combine(d.Name, file));
            yield return new(StorageUsage.Trash, Path.Combine(d.Name, "$Recycle.Bin"));
            yield return new(StorageUsage.Games, Path.Combine(d.Name, "XboxGames"));
        }
        yield return new(StorageUsage.Programs, F(Environment.SpecialFolder.ProgramFiles));
        yield return new(StorageUsage.Programs, F(Environment.SpecialFolder.ProgramFilesX86));
        yield return new(StorageUsage.Programs, F(Environment.SpecialFolder.CommonApplicationData));
        yield return new(StorageUsage.AppData, F(Environment.SpecialFolder.ApplicationData));
        yield return new(StorageUsage.AppData, local);
        yield return new(StorageUsage.Programs, Path.Combine(local, "Programs"));
        foreach (var name in new[] { "Pictures", "Videos" })
            if (ShellNames.KnownFolder(name) is { } p) yield return new(StorageUsage.Media, p);
        foreach (var name in new[] { "Documents", "Downloads", "Desktop", "Music" })
            if (ShellNames.KnownFolder(name) is { } p) yield return new(StorageUsage.Files, p);

        foreach (var lib in GameLibraries.SteamLibraries() ?? [])
            yield return new(StorageUsage.Games, Path.Combine(lib, "steamapps"));
        foreach (var (app, folder) in GameLibraries.EpicInstalls() ?? [])
            yield return new(app.StartsWith("UE_", StringComparison.OrdinalIgnoreCase) ? StorageUsage.Programs : StorageUsage.Games, folder); // UE_5.3 is the engine

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

    private static long Size(string path, IReadOnlyList<string> skip)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (!Directory.Exists(path)) return 0;
            var skipSet = skip.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            // Sparse and compressed files (an emulator's 512 GB userdata.img) take far less than their length.
            var files = new FileSystemEnumerable<long>(path, (ref FileSystemEntry e) =>
                (e.Attributes & (FileAttributes.SparseFile | FileAttributes.Compressed)) != 0 ? OnDisk(e.ToFullPath(), e.Length) : e.Length, options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && (e.Attributes & CloudOnly) == 0,
                ShouldRecursePredicate = (ref FileSystemEntry e) =>
                {
                    var full = e.ToFullPath();
                    if (skipSet.Contains(full)) return false;
                    // Junctions and symlinks (loops, data on another drive) are skipped; OneDrive folders are reparse points too but hold real files.
                    return (e.Attributes & FileAttributes.ReparsePoint) == 0 || new DirectoryInfo(full).LinkTarget is null;
                },
            };
            long sum = 0;
            foreach (var length in files) sum += length;
            return sum;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se pudo medir '{path}'", e);
            return 0;
        }
    }
}
