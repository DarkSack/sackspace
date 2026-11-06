namespace SackSpace.Core.Widgets;

/// <summary>A folder (or a single file, like pagefile.sys) whose size goes to a category.</summary>
public sealed record StorageRoot(string Category, string Path);

/// <summary>A root to measure, minus the other roots inside it (those are counted on their own).</summary>
public sealed record StoragePlanItem(string Category, string Path, IReadOnlyList<string> Skip);

/// <summary>
/// Turns the known folders into what the storage widget measures, so nothing is counted
/// twice (a Steam library inside Program Files is Juegos, not Programas), and adds up the result.
/// </summary>
public static class StorageUsage
{
    public const string Games = "Juegos";
    public const string System = "Sistema";
    public const string Programs = "Programas";
    public const string AppData = "Datos de apps";
    public const string Media = "Fotos y videos";
    public const string Files = "Archivos";
    public const string Trash = "Papelera";
    public const string Other = "Sin clasificar";

    /// <summary>The built-in categories in their fixed colour order; any other name is a folder of the user's.</summary>
    public static IReadOnlyList<string> Categories { get; } = [Games, System, Programs, AppData, Media, Files, Trash];

    /// <summary>
    /// Same folder twice: the later one wins (the user's own folders come last).
    /// Each root skips the roots nested inside it.
    /// </summary>
    public static IReadOnlyList<StoragePlanItem> Plan(IEnumerable<StorageRoot> roots)
    {
        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in roots)
        {
            if (string.IsNullOrWhiteSpace(r.Path)) continue;
            string full;
            try { full = Normalize(r.Path); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            byPath[full] = r.Category;
        }
        return byPath.Select(kv => new StoragePlanItem(kv.Value, kv.Key,
                byPath.Keys.Where(other => IsInside(other, kv.Key)).ToList()))
            .ToList();
    }

    /// <summary>
    /// Per category, biggest first; whatever the drive uses beyond what was measured is
    /// "Sin clasificar". Hard links (WinSxS) can make the sum pass the used space: then nothing is left over.
    /// </summary>
    public static IReadOnlyList<(string Category, long Bytes)> Summarize(long used, IEnumerable<(string Category, long Bytes)> measured)
    {
        var list = measured.Where(m => m.Bytes > 0)
            .GroupBy(m => m.Category)
            .Select(g => (Category: g.Key, Bytes: g.Sum(m => m.Bytes)))
            .OrderByDescending(m => m.Bytes)
            .ToList();
        var rest = used - list.Sum(m => m.Bytes);
        if (rest > 0) list.Add((Other, rest));
        return list;
    }

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0) ? full.TrimEnd('\\', '/') : full;
    }

    public static bool IsInside(string path, string folder) =>
        path.Length > folder.Length && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && (folder.EndsWith('\\') || path[folder.Length] == '\\');
}
