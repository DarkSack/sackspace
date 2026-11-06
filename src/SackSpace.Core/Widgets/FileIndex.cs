using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace SackSpace.Core.Widgets;

/// <summary>A file the search found.</summary>
public sealed record FoundFile(string Name, string Folder, bool Similar, long Bytes, DateTime Modified)
{
    public string Path => System.IO.Path.Combine(Folder, Name);
}

/// <summary>
/// The list of the user's files for the search. Built in the background into files.idx in the
/// data folder (every local disk, without the system, programs, caches, dot-folders, dependency
/// folders and installed games), loaded into memory only while someone searches (dropped two
/// minutes after), and kept current between builds by watching the disks: new and renamed
/// files are found at once, deleted ones are left out.
/// Searching runs on every core over names already normalized, with one letter mask per file
/// so similar matches skip almost every name before the costly comparison.
/// </summary>
public sealed class FileIndex : IDisposable
{
    private const int Magic = 0x58495353; // "SSIX"
    private const int Version = 3; // bump when Normalize changes: names are stored normalized
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);
    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Windows.old", "$Recycle.Bin", "$WinREAgent", "System Volume Information", "Recovery", "PerfLogs",
        "ProgramData", "Program Files", "Program Files (x86)", "WindowsApps", "AppData",
        "node_modules", "site-packages", "__pycache__", "obj", "steamapps",
    };

    private readonly string _file;
    private readonly Func<bool> _paused;
    private readonly Func<IEnumerable<string>> _gameFolders;
    private readonly Action<string, Exception?> _log;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, byte> _fresh = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _gone = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private readonly object _lock = new();
    private HashSet<string> _skipFolders = new(StringComparer.OrdinalIgnoreCase);
    private Loaded? _loaded;
    private Task<Loaded?>? _loading;
    private Task? _building;
    private DateTime _lastUse;

    /// <param name="paused">True while a full-screen game runs: no build then.</param>
    /// <param name="gameFolders">Install folders of games, left out.</param>
    /// <param name="log">Where problems and build times go.</param>
    /// <param name="watch">Follow changes on the disks (off in tests).</param>
    public FileIndex(string dataFolder, Func<bool> paused, Func<IEnumerable<string>> gameFolders, Action<string, Exception?> log, bool watch = true)
    {
        _file = Path.Combine(dataFolder, "files.idx");
        _paused = paused;
        _gameFolders = gameFolders;
        _log = log;
        // Every 10 minutes: drop the loaded list if unused, rebuild if old (never during a full-screen game).
        _timer = new Timer(_ => Housekeeping(), null, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(10));
        if (watch) Watch();
    }

    /// <summary>The disks to list; tests point it at a folder.</summary>
    public IReadOnlyList<string>? Roots { get; init; }

    public bool Building => _building is { IsCompleted: false };
    public bool Exists => File.Exists(_file);
    public DateTime? BuiltAt => Exists ? File.GetLastWriteTime(_file) : null;

    /// <summary>Starts a build now unless one is running.</summary>
    public Task BuildAsync()
    {
        lock (_lock)
        {
            if (_building is { IsCompleted: false }) return _building;
            _building = Task.Factory.StartNew(Build, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return _building;
        }
    }

    /// <summary>Starts reading the list now (the search box just opened), so the first keystroke does not wait.</summary>
    public void Preload()
    {
        _lastUse = DateTime.UtcNow;
        _ = LoadAsync();
    }

    /// <summary>The best matches: exact ones first, then similar ones, at most <paramref name="max"/>.</summary>
    public async Task<IReadOnlyList<FoundFile>> SearchAsync(string text, bool allowSimilar, int max, CancellationToken ct)
    {
        var q = FileSearch.Parse(text);
        if (q.IsEmpty) return [];
        _lastUse = DateTime.UtcNow;
        var data = await LoadAsync();
        ct.ThrowIfCancellationRequested();
        return await Task.Run(() => Search(data, q, allowSimilar, max, ct), ct);
    }

    private Task<Loaded?> LoadAsync()
    {
        lock (_lock)
        {
            if (_loaded is not null) return Task.FromResult<Loaded?>(_loaded);
            if (_loading is { IsCompleted: false }) return _loading;
            _loading = Task.Run(async () =>
            {
                if (!Exists) await BuildAsync();
                var data = Read();
                lock (_lock) _loaded = data;
                return data;
            });
            return _loading;
        }
    }

    // Exact matches outrank similar ones; among equals, recently changed files come first.
    private static int Rank(FileMatch m) => m.Score + (m.Similar ? 0 : 10_000);

    private List<FoundFile> Search(Loaded? data, FileQuery q, bool allowSimilar, int max, CancellationToken ct)
    {
        var keep = max * 2;
        var recentTicks = DateTime.UtcNow.AddDays(-30).Ticks;
        var found = new ConcurrentBag<List<(int Rank, int Index, string? Path)>>();
        if (data is not null)
        {
            // One slice per core; each keeps its own best few and the slices are merged at the end.
            var slices = Math.Max(1, Math.Min(Environment.ProcessorCount, data.Count / 20_000));
            Parallel.For(0, slices, new ParallelOptions { CancellationToken = ct }, s =>
            {
                var best = new List<(int Rank, int Index, string? Path)>();
                int from = (int)((long)data.Count * s / slices), to = (int)((long)data.Count * (s + 1) / slices);
                for (var i = from; i < to; i++)
                {
                    if ((i & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
                    if (q.HasFilters && !q.Passes(data.Bytes[i], data.ModifiedAt(i))) continue;
                    var name = data.Name(i);
                    var space = name.LastIndexOf(' ');
                    if (FileSearch.Score(name, data.DirNorm[data.Dir[i]], space < 0 ? default : name[(space + 1)..], q, allowSimilar, data.Masks[i]) is not { } m) continue;
                    best.Add((Rank(m) + (data.Modified[i] >= recentTicks ? 3 : 0), i, null));
                    if (best.Count > keep * 2) Trim(best, keep);
                }
                found.Add(best);
            });
        }
        var fresh = new List<(int Rank, int Index, string? Path)>();
        foreach (var path in _fresh.Keys)
        {
            var name = Path.GetFileName(path);
            if (FileSearch.Score(FileSearch.Normalize(name), FileSearch.Normalize(Path.GetDirectoryName(path) ?? ""),
                    Path.GetExtension(name).TrimStart('.'), q, allowSimilar) is { } m)
                fresh.Add((Rank(m) + 3, -1, path));
        }
        var all = found.SelectMany(b => b).Concat(fresh).ToList();
        Trim(all, keep);

        var result = new List<FoundFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rank, i, p) in all)
        {
            var path = p ?? data!.Path(i);
            if (path is null || _gone.ContainsKey(path) || !seen.Add(path)) continue;
            var (bytes, modified) = i >= 0 ? (data!.Bytes[i], data.ModifiedAt(i)) : Facts(path);
            if (p is not null && q.HasFilters && !q.Passes(bytes, modified)) continue;
            result.Add(new FoundFile(Path.GetFileName(path), Path.GetDirectoryName(path) ?? "", rank < 10_000, bytes, modified));
            if (result.Count == max) break;
        }
        return result;

        static (long, DateTime) Facts(string path)
        {
            try { var f = new FileInfo(path); return (f.Length, f.LastWriteTime); }
            catch (IOException) { return (0, DateTime.MinValue); }
        }
    }

    private static void Trim(List<(int Rank, int Index, string? Path)> list, int keep)
    {
        list.Sort((a, b) => b.Rank.CompareTo(a.Rank));
        if (list.Count > keep) list.RemoveRange(keep, list.Count - keep);
    }

    // ── Building ──

    private IEnumerable<string> RootFolders() => Roots ??
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && Ready(d)).Select(d => d.RootDirectory.FullName);

    private static bool Ready(DriveInfo d)
    {
        try { return d.IsReady; } catch (IOException) { return false; }
    }

    private bool Skip(string name, string fullPath) =>
        name.StartsWith('.') || SkipNames.Contains(name) || _skipFolders.Contains(fullPath);

    private void Build()
    {
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        var started = DateTime.UtcNow;
        try
        {
            // Installed games are huge piles of data files nobody searches by name.
            _skipFolders = _gameFolders().Select(f => f.TrimEnd('\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tmp = _file + ".tmp";
            var dirs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                // Files are written as they are found (memory stays flat); their folders go at the end.
                w.Write(Magic);
                w.Write(Version);
                w.Write(0L); // where the folder table starts, filled in at the end
                w.Write(0); // file count, idem
                foreach (var root in RootFolders())
                {
                    var e = new FileSystemEnumerable<(string Dir, string Name, long Bytes, long Modified)?>(root,
                        (ref FileSystemEntry x) => x.IsDirectory ? null : (x.Directory.ToString(), x.FileName.ToString(), x.Length, x.LastWriteTimeUtc.UtcTicks),
                        new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden })
                    {
                        ShouldRecursePredicate = (ref FileSystemEntry x) => !Skip(x.FileName.ToString(), x.ToFullPath()),
                    };
                    foreach (var entry in e)
                    {
                        if (entry is not { } f) continue;
                        if (!dirs.TryGetValue(f.Dir, out var id)) dirs[f.Dir] = id = dirs.Count;
                        w.Write(id);
                        w.Write(f.Name);
                        w.Write(FileSearch.Normalize(f.Name));
                        w.Write(f.Bytes);
                        w.Write(f.Modified);
                        count++;
                    }
                }
                var table = w.BaseStream.Position;
                w.Write(dirs.Count);
                foreach (var (d, _) in dirs.OrderBy(d => d.Value)) { w.Write(d); w.Write(FileSearch.Normalize(d)); }
                w.Seek(8, SeekOrigin.Begin);
                w.Write(table);
                w.Write(count);
            }
            File.Move(tmp, _file, overwrite: true);
            // Whatever was created after the walk started stays in the fresh list; the rest is in the file now.
            foreach (var p in _fresh.Keys) if (File.Exists(p) && File.GetCreationTimeUtc(p) < started) _fresh.TryRemove(p, out _);
            _gone.Clear();
            lock (_lock) { _loaded?.Dispose(); _loaded = null; } // the next search reads the new list
            _log($"Índice de archivos: {count:N0} archivos en {dirs.Count:N0} carpetas, {(DateTime.UtcNow - started).TotalSeconds:0} s", null);
        }
        catch (Exception ex) { _log("No se pudo crear el índice de archivos", ex); }
    }

    // ── Reading: normalized names as one block of text; original names read back from the file for the results ──

    private Loaded? Read()
    {
        if (!Exists) return null;
        FileStream? stream = null;
        try
        {
            stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16);
            var r = new BinaryReader(stream);
            if (r.ReadInt32() != Magic || r.ReadInt32() != Version) { stream.Dispose(); _ = BuildAsync(); return null; }
            var table = r.ReadInt64();
            var count = r.ReadInt32();
            var dir = new int[count];
            var nameAt = new long[count];
            var bytes = new long[count];
            var modified = new long[count];
            var masks = new ulong[count];
            var starts = new int[count + 1];
            var chars = new System.Text.StringBuilder(count * 24);
            for (var i = 0; i < count; i++)
            {
                dir[i] = r.ReadInt32();
                nameAt[i] = stream.Position;
                stream.Seek(Read7BitLength(r), SeekOrigin.Current); // the original name stays on disk
                starts[i] = chars.Length;
                var norm = r.ReadString();
                chars.Append(norm);
                masks[i] = FileSearch.Mask(norm);
                bytes[i] = r.ReadInt64();
                modified[i] = r.ReadInt64();
            }
            starts[count] = chars.Length;
            stream.Position = table;
            var dirCount = r.ReadInt32();
            var dirPath = new string[dirCount];
            var dirNorm = new string[dirCount];
            for (var i = 0; i < dirCount; i++) { dirPath[i] = r.ReadString(); dirNorm[i] = r.ReadString(); }
            return new Loaded(stream, dirPath, dirNorm, dir, nameAt, starts, chars.ToString(), masks, bytes, modified);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException or FormatException)
        {
            stream?.Dispose();
            _log("Índice de archivos ilegible: se rehace", ex);
            _ = BuildAsync();
            return null;
        }
    }

    // BinaryWriter.Write(string) puts the UTF-8 byte count first as a 7-bit encoded number.
    private static int Read7BitLength(BinaryReader r) => r.Read7BitEncodedInt();

    private sealed class Loaded(FileStream file, string[] dirPath, string[] dirNorm, int[] dir, long[] nameAt, int[] starts, string names, ulong[] masks, long[] bytes, long[] modified) : IDisposable
    {
        private readonly BinaryReader _reader = new(file);
        public string[] DirNorm { get; } = dirNorm;
        public int[] Dir { get; } = dir;
        public ulong[] Masks { get; } = masks;
        public long[] Bytes { get; } = bytes;
        public long[] Modified { get; } = modified;
        public int Count => Dir.Length;

        public ReadOnlySpan<char> Name(int i) => names.AsSpan(starts[i], starts[i + 1] - starts[i]);

        public DateTime ModifiedAt(int i) => new DateTime(Modified[i], DateTimeKind.Utc).ToLocalTime();

        public string? Path(int i)
        {
            lock (_reader)
            {
                try
                {
                    file.Position = nameAt[i];
                    return System.IO.Path.Combine(dirPath[Dir[i]], _reader.ReadString());
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException) { return null; }
            }
        }

        public void Dispose() => file.Dispose();
    }

    // ── Keeping current ──

    private void Watch()
    {
        foreach (var root in RootFolders())
        {
            try
            {
                var w = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName,
                    InternalBufferSize = 64 * 1024,
                };
                w.Created += (_, e) => Fresh(e.FullPath);
                w.Renamed += (_, e) => { Gone(e.OldFullPath); Fresh(e.FullPath); };
                w.Deleted += (_, e) => Gone(e.FullPath);
                w.Error += (_, e) => _log($"Vigilancia de {root} desbordada", e.GetException());
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                _log($"No se puede vigilar {root}", ex);
            }
        }
    }

    private bool Interesting(string path)
    {
        foreach (var part in path.Split('\\'))
            if (part.Length > 0 && (part.StartsWith('.') || SkipNames.Contains(part))) return false;
        return !_skipFolders.Any(f => path.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase));
    }

    private void Fresh(string path)
    {
        if (_fresh.Count > 20_000 || !Interesting(path)) return; // a flood: the next build catches up
        _gone.TryRemove(path, out _);
        _fresh[path] = 0;
    }

    private void Gone(string path)
    {
        if (_fresh.TryRemove(path, out _) || !Interesting(path) || _gone.Count > 50_000) return;
        _gone[path] = 0;
    }

    private void Housekeeping()
    {
        lock (_lock)
        {
            if (_loaded is not null && DateTime.UtcNow - _lastUse > TimeSpan.FromMinutes(2))
            {
                _loaded.Dispose();
                _loaded = null;
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
            }
        }
        if (_paused() || Building) return;
        if (!Exists || DateTime.Now - File.GetLastWriteTime(_file) > MaxAge) _ = BuildAsync();
    }

    /// <summary>Turned off: the list of names leaves the disk too.</summary>
    public static void Delete(string dataFolder)
    {
        try { File.Delete(Path.Combine(dataFolder, "files.idx")); }
        catch (IOException) { } // in use: overwritten by the next build if search is turned on again
    }

    public void Dispose()
    {
        _timer.Dispose();
        foreach (var w in _watchers) w.Dispose();
        _loaded?.Dispose();
    }
}
