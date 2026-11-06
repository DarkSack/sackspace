using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SackSpace.Core.Widgets;

/// <summary>
/// An installed game, read from its launcher's own files. Key: "steam:289070" or "epic:Potoo".
/// Uninstall: a link that opens the launcher's own uninstall dialog, when it has one.
/// </summary>
public sealed record InstalledGame(string Key, string Name, string LaunchPath, long Bytes, DateTimeOffset? LastPlayed, string Folder, string? Uninstall);

/// <summary>Reads Steam and Epic manifests and decides what an "Accesos" group adds on its own.</summary>
public static partial class GameCatalog
{
    public static string SteamKey(string appId) => "steam:" + appId;
    public static string EpicKey(string appName) => "epic:" + appName.ToLowerInvariant();

    /// <summary>The key of the game a shortcut opens, or null when it is not a Steam or Epic link.</summary>
    public static string? KeyOf(string path) =>
        ShortcutPresence.SteamGameId(path) is { } steam ? SteamKey(steam)
        : ShortcutPresence.EpicAppName(path) is { } epic ? EpicKey(epic)
        : null;

    /// <summary>
    /// One appmanifest_*.acf. Never-played games have LastPlayed 0. The launch path is the Start menu
    /// entry Steam made when there is one (it has the game's icon), else the bare steam:// link.
    /// </summary>
    public static InstalledGame? ParseSteam(string acf, string libraryFolder, Func<string, bool> inStartMenu)
    {
        var fields = AcfField().Matches(acf).GroupBy(m => m.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Groups[2].Value.Replace(@"\\", @"\"), StringComparer.OrdinalIgnoreCase);
        if (!fields.TryGetValue("appid", out var id) || !fields.TryGetValue("name", out var name)) return null;
        var link = "steam://rungameid/" + id;
        var played = fields.TryGetValue("LastPlayed", out var lp) && long.TryParse(lp, out var secs) && secs > 0
            ? DateTimeOffset.FromUnixTimeSeconds(secs) : (DateTimeOffset?)null;
        long.TryParse(fields.GetValueOrDefault("SizeOnDisk"), out var size);
        var folder = Path.Combine(libraryFolder, "steamapps", "common", fields.GetValueOrDefault("installdir") ?? "");
        return new InstalledGame(SteamKey(id), name, inStartMenu(link) ? @"shell:AppsFolder\" + link : link, size, played, folder, "steam://uninstall/" + id);
    }

    /// <summary>One Epic *.item manifest; engines (Unreal) and add-ons of another game are not games of their own.</summary>
    public static InstalledGame? ParseEpic(JsonNode manifest)
    {
        var app = manifest["AppName"]?.GetValue<string>();
        var name = manifest["DisplayName"]?.GetValue<string>();
        if (string.IsNullOrEmpty(app) || string.IsNullOrEmpty(name)) return null;
        var main = manifest["MainGameAppName"]?.GetValue<string>() ?? "";
        var categories = (manifest["AppCategories"] as JsonArray)?.Select(c => c?.GetValue<string>() ?? "").ToList() ?? [];
        if (main.Length > 0 && !main.Equals(app, StringComparison.OrdinalIgnoreCase)) return null;
        if (!categories.Contains("games") || app.StartsWith("UE_", StringComparison.OrdinalIgnoreCase)) return null;
        var size = manifest["InstallSize"] is JsonValue v && v.TryGetValue<long>(out var s) ? s : 0;
        return new InstalledGame(EpicKey(app), name, $"com.epicgames.launcher://apps/{app}?action=launch&silent=true", size, null,
            manifest["InstallLocation"]?.GetValue<string>() ?? "", null);
    }

    private static readonly string[] ToolWords =
    [
        "redistributable", "dedicated server", "sdk", "mod tools", "soundtrack", "proton", "steam linux runtime", "steamvr",
        "wallpaper engine", "vtube studio", "vtuber maker", "vroid", "benchmark", "editor", "tool", "demo",
    ];

    /// <summary>Launcher entries that are tools, servers or runtimes rather than something to play.</summary>
    public static bool IsTool(InstalledGame game)
    {
        var n = game.Name.ToLowerInvariant();
        return game.Key == SteamKey("228980") || ToolWords.Any(n.Contains);
    }

    /// <summary>
    /// What a group with "add new games" adds now: games installed since it last looked, minus tools and
    /// minus what it already has. The first look only remembers what is there, so a curated group stays as it is,
    /// and a game removed by hand is never added back (it was already seen).
    /// </summary>
    public static (IReadOnlyList<InstalledGame> Add, IReadOnlySet<string> Seen) NewGames(
        IReadOnlyList<InstalledGame> installed, IReadOnlySet<string>? seen, IReadOnlySet<string> inGroup)
    {
        var all = new HashSet<string>(seen ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var g in installed) all.Add(g.Key);
        if (seen is null) return ([], all);
        var add = installed.Where(g => !seen.Contains(g.Key) && !inGroup.Contains(g.Key) && !IsTool(g)).ToList();
        return (add, all);
    }

    /// <summary>
    /// Games not opened for this long, biggest first. Only Steam records when a game was last played;
    /// one never played has no date and is left out (it may have just been installed).
    /// </summary>
    public static IReadOnlyList<InstalledGame> Forgotten(IReadOnlyList<InstalledGame> installed, DateTimeOffset now, TimeSpan unusedFor, long minBytes) =>
        installed.Where(g => !IsTool(g) && g.Bytes >= minBytes && g.LastPlayed is { } played && now - played >= unusedFor)
            .OrderByDescending(g => g.Bytes)
            .ToList();

    [GeneratedRegex("^\\s*\"([^\"]+)\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Multiline)]
    private static partial Regex AcfField();
}
