namespace SackSpace.Core.Widgets;

/// <summary>Whether a shortcut still points at something: here, gone for sure, or impossible to tell now.</summary>
public enum Presence { Here, Gone, Unknown }

/// <summary>
/// What is installed right now. A null set means it could not be read (Steam not installed,
/// a library on a drive that is not plugged in, the Start menu unreadable): its links stay Unknown.
/// </summary>
public sealed record InstallFacts(
    IReadOnlySet<string>? StartMenu,
    IReadOnlySet<string>? SteamGames,
    IReadOnlySet<string>? EpicApps,
    Func<string, bool> PathExists,
    Func<string, bool> DriveReady);

/// <summary>
/// Decides if a shortcut's target is gone. Steam leaves its Start menu entry behind when a game
/// is uninstalled, so steam:// links are checked against the libraries' appmanifest files and
/// Epic links against the launcher's manifests. Never says Gone for a drive that is not there.
/// </summary>
public static class ShortcutPresence
{
    private const string AppsFolder = @"shell:AppsFolder\";

    public static Presence Check(string path, InstallFacts facts)
    {
        if (SteamGameId(path) is { } steam)
            return facts.SteamGames is null ? Presence.Unknown : facts.SteamGames.Contains(steam) ? Presence.Here : Presence.Gone;
        if (EpicAppName(path) is { } epic)
            return facts.EpicApps is null ? Presence.Unknown : facts.EpicApps.Contains(epic) ? Presence.Here : Presence.Gone;
        if (path.StartsWith(AppsFolder, StringComparison.OrdinalIgnoreCase))
            return facts.StartMenu is null || facts.StartMenu.Count == 0 ? Presence.Unknown
                : facts.StartMenu.Contains(path[AppsFolder.Length..]) ? Presence.Here : Presence.Gone;
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::") || path.Contains("://"))
            return Presence.Unknown; // a known folder, a CLSID or another app's link: nothing to check against
        if (facts.PathExists(path)) return Presence.Here;
        var root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) || !facts.DriveReady(root) ? Presence.Unknown : Presence.Gone;
    }

    /// <summary>"steam://rungameid/289070" or "steam://run/289070", also inside a Start menu path.</summary>
    public static string? SteamGameId(string path)
    {
        foreach (var prefix in new[] { "steam://rungameid/", "steam://run/" })
        {
            var i = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var digits = new string(path[(i + prefix.Length)..].TakeWhile(char.IsAsciiDigit).ToArray());
            if (digits.Length > 0) return digits;
        }
        return null;
    }

    /// <summary>"com.epicgames.launcher://apps/Potoo?action=launch" → Potoo; "apps/ns%3Aid%3AName" → Name.</summary>
    public static string? EpicAppName(string path)
    {
        const string prefix = "com.epicgames.launcher://apps/";
        var i = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var rest = path[(i + prefix.Length)..];
        var end = rest.IndexOfAny(['?', '/']);
        var id = Uri.UnescapeDataString(end < 0 ? rest : rest[..end]);
        var name = id[(id.LastIndexOf(':') + 1)..];
        return name.Length > 0 ? name : null;
    }
}
