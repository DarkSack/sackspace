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

    public static Workspace? ForApp(IEnumerable<Workspace> list, string program)
    {
        var name = Normalize(program);
        return name.Length == 0 ? null : list.FirstOrDefault(w => w.Apps?.Any(a => Normalize(a) == name) == true);
    }

    /// <summary>"Steam.exe", "C:\x\steam.exe" and " steam " are all "steam".</summary>
    public static string Normalize(string program)
    {
        var name = Path.GetFileName(program.Trim());
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToLowerInvariant();
    }

    /// <summary>What the user typed ("steam, discord.exe") as a clean list; null when empty.</summary>
    public static List<string>? ParseApps(string text)
    {
        var list = text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).Where(n => n.Length > 0).Distinct().ToList();
        return list.Count == 0 ? null : list;
    }
}
