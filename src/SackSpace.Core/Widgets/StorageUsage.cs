namespace SackSpace.Core.Widgets;

/// <summary>
/// A folder (or a single file, like pagefile.sys) whose size goes to a category. Expand: its
/// biggest subfolders are what the breakdown lists (a Steam library lists its games); otherwise
/// the folder itself is one entry (a top-level folder of a drive).
/// </summary>
public sealed record StorageRoot(string Category, string Path, bool Expand = true);

/// <summary>A root to measure, minus the other roots inside it (those are counted on their own).</summary>
public sealed record StoragePlanItem(string Category, string Path, IReadOnlyList<string> Skip, bool Expand = true);

/// <summary>An entry of the breakdown: a folder or a file and the bytes it takes.</summary>
public sealed record StorageItem(string Path, long Bytes);

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
    public const string Dev = "Desarrollo";
    public const string Trash = "Papelera";
    public const string Other = "Sin clasificar";
    /// <summary>Used space no folder accounts for: restore points, NTFS metadata, folders that need admin.</summary>
    public const string Hidden = "Oculto o sin acceso";

    /// <summary>The built-in categories in their fixed colour order; any other name is a folder of the user's.</summary>
    public static IReadOnlyList<string> Categories { get; } = [Games, System, Programs, AppData, Media, Files, Dev, Trash];

    /// <summary>
    /// Same folder twice: the later one wins (generic folders go first, known ones after, the user's own last).
    /// Each root skips the roots nested inside it.
    /// </summary>
    public static IReadOnlyList<StoragePlanItem> Plan(IEnumerable<StorageRoot> roots)
    {
        var byPath = new Dictionary<string, StorageRoot>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in roots)
        {
            if (string.IsNullOrWhiteSpace(r.Path)) continue;
            string full;
            try { full = Normalize(r.Path); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            byPath[full] = r;
        }
        return byPath.Select(kv => new StoragePlanItem(kv.Value.Category, kv.Key,
                byPath.Keys.Where(other => IsInside(other, kv.Key)).ToList(), kv.Value.Expand))
            .ToList();
    }

    /// <summary>
    /// Per category, biggest first; whatever the drive uses beyond what was measured is
    /// "Oculto o sin acceso". Hard links (WinSxS) can make the sum pass the used space: then nothing is left over.
    /// </summary>
    public static IReadOnlyList<(string Category, long Bytes)> Summarize(long used, IEnumerable<(string Category, long Bytes)> measured)
    {
        var list = measured.Where(m => m.Bytes > 0)
            .GroupBy(m => m.Category)
            .Select(g => (Category: g.Key, Bytes: g.Sum(m => m.Bytes)))
            .OrderByDescending(m => m.Bytes)
            .ToList();
        var rest = used - list.Sum(m => m.Bytes);
        if (rest >= 512L * 1024 * 1024) list.Add((Hidden, rest));
        return list;
    }

    /// <summary>
    /// The entries a measured root shows in the breakdown. A child that holds most of it
    /// (steamapps\common) is replaced by its own children (the games), so the list says something.
    /// </summary>
    public static IReadOnlyList<StorageItem> TopItems(string root, long total, IReadOnlyDictionary<string, long> children,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> grandchildren, int take = 10)
    {
        var items = children.Select(kv => new StorageItem(Path.Combine(root, kv.Key), kv.Value)).OrderByDescending(i => i.Bytes).ToList();
        if (items.Count > 0 && total > 0 && items[0].Bytes > total * 0.7)
        {
            var big = children.MaxBy(kv => kv.Value).Key;
            if (grandchildren.TryGetValue(big, out var inner) && inner.Count >= 2)
            {
                items.RemoveAt(0);
                items.AddRange(inner.Select(kv => new StorageItem(kv.Key.Length == 0 ? Path.Combine(root, big) : Path.Combine(root, big, kv.Key), kv.Value)));
                items = items.OrderByDescending(i => i.Bytes).ToList();
            }
        }
        return items.Where(i => i.Bytes > 0).Take(take).ToList();
    }

    private static readonly (string Category, string[] Names)[] Hints =
    [
        (Games, ["juegos", "games", "game", "steam", "steamlibrary", "epic games", "epicstore", "epicgames", "riot games", "xboxgames",
                 "gog games", "gog galaxy", "ea games", "ubisoft", "battle.net", "hoyoplay", "roblox", "emulators", "emuladores", "roms", "retroarch"]),
        (Dev, ["dev", "desarrollo", "projects", "proyectos", "repos", "source", "src", "code", "github", "workspace", "androidsdk",
               "android", "sdk", "flutter", "jdk", "go", ".gradle", ".m2", ".nuget", ".cargo", ".rustup", ".android", ".npm", ".pnpm-store",
               ".bun", ".deno", ".vscode", ".cursor", ".docker", ".conda", "anaconda3", "miniconda3", ".ollama", ".lmstudio", "docker", "wsl", "vm", "vms", "virtual machines", "hyper-v", "xampp", "wamp64", "laragon"]),
        (Media, ["fotos", "photos", "imágenes", "imagenes", "pictures", "videos", "vídeos", "recordings", "grabaciones", "capturas",
                 "obs", "streaming", "music", "música", "musica", "clips"]),
        (Files, ["descargas", "downloads", "documentos", "documents", "desktop", "escritorio", "backups", "backup", "respaldos", "copias", "onedrive", "google drive", "dropbox"]),
        (AppData, ["appdata"]),
        (Programs, ["programs", "programas", "program files", "apps", "tools", "herramientas", "portable"]),
    ];

    // Parts of a name that give it away ("Emulacion", "VulkanSDK", "XBOX Games").
    private static readonly (string Category, string[] Parts)[] Fragments =
    [
        (Games, ["juego", "game", "emula", "steam", "xbox", "minecraft", "roms"]),
        (Dev, ["sdk"]),
    ];

    /// <summary>A category for a folder that is not a known one, from its name ("D:\JUEGOS", "E:\Dev", "~\.gradle"), or null.</summary>
    public static string? Classify(string folderName)
    {
        var n = folderName.Trim().ToLowerInvariant();
        foreach (var (category, names) in Hints)
            if (names.Contains(n)) return category;
        foreach (var (category, parts) in Fragments)
            if (parts.Any(n.Contains)) return category;
        // Dot folders (~\.claude, ~\.cache) are where command-line and developer tools keep their data.
        return n.StartsWith('.') ? Dev : null;
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
