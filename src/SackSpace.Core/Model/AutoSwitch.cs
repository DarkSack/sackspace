using System.Globalization;

namespace SackSpace.Core.Model;

/// <summary>Which workspace should come up by itself: by the clock, or by the app in front.</summary>
public static class AutoSwitch
{
    public static bool TryParseTime(string? text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text?.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);

    public static bool InSchedule(Workspace ws, DateTime now)
    {
        if (!TryParseTime(ws.ScheduleFrom, out var from) || !TryParseTime(ws.ScheduleTo, out var to) || from == to) return false;
        var t = now.TimeOfDay;
        var inside = from < to ? t >= from && t < to : t >= from || t < to;
        if (!inside) return false;
        // Overnight (22:00–02:00): after midnight the slot still belongs to the day it started.
        var day = from > to && t < to ? now.AddDays(-1).DayOfWeek : now.DayOfWeek;
        return ws.ScheduleDays is not { Count: > 0 } days || days.Contains(day);
    }

    public static Workspace? Scheduled(IEnumerable<Workspace> list, DateTime now) => list.FirstOrDefault(w => InSchedule(w, now));

    /// <summary>
    /// Every workspace that claims this program, in list order. A rule is a program name
    /// ("steam") or a folder ("D:\SteamLibrary"): anything running from inside it. A rule
    /// starting with "!" leaves that program or folder out ("!wallpaper64").
    /// </summary>
    public static List<Workspace> ForApp(IEnumerable<Workspace> list, string program, string? fullPath = null)
    {
        var name = Normalize(program);
        var path = fullPath?.Replace('/', '\\');
        if (name.Length == 0) return [];
        bool Hit(string rule) => IsFolder(rule)
            ? path is not null && path.StartsWith(rule.TrimEnd('\\', '/').Replace('/', '\\') + "\\", StringComparison.OrdinalIgnoreCase)
            : Normalize(rule) == name;
        return list.Where(w => w.Apps is { } rules
            && rules.Any(r => !r.StartsWith('!') && Hit(r))
            && !rules.Any(r => r.StartsWith('!') && Hit(r[1..]))).ToList();
    }

    public static bool IsFolder(string rule) => rule.Contains('\\') || rule.Contains('/');

    /// <summary>"Steam.exe", "C:\x\steam.exe" and " steam " are all "steam".</summary>
    public static string Normalize(string program)
    {
        var name = Path.GetFileName(program.Trim());
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToLowerInvariant();
    }

    /// <summary>What the user typed ("steam, discord.exe, D:\Juegos") as a clean list; null when empty.</summary>
    public static List<string>? ParseApps(string text)
    {
        var list = text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.StartsWith('!') ? "!" + Clean(r[1..]) : Clean(r))
            .Where(n => n.TrimStart('!').Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list.Count == 0 ? null : list;

        static string Clean(string r) => IsFolder(r) ? r.Trim().TrimEnd('\\', '/') : Normalize(r);
    }
}
