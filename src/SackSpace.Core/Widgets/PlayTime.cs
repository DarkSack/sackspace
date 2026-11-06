using System.Globalization;
using System.Text.Json.Nodes;

namespace SackSpace.Core.Widgets;

/// <summary>A game's time in one period.</summary>
public sealed record PlayTotal(string Key, string Name, TimeSpan Time);

/// <summary>
/// Time spent per game, kept as seconds per day:
/// { "steam:730": { "name": "Counter-Strike 2", "days": { "2026-11-06": 5400 } } }.
/// Weeks start on Monday. Days older than <see cref="KeepDays"/> fold into "older" so
/// the file stays small but the total stays right.
/// </summary>
public static class PlayTime
{
    public const int KeepDays = 400;
    private const string DayFormat = "yyyy-MM-dd";

    public static string Day(DateTime date) => date.ToString(DayFormat, CultureInfo.InvariantCulture);

    public static void Add(JsonObject log, string key, string name, DateTime when, TimeSpan time)
    {
        if (time <= TimeSpan.Zero) return;
        if (log[key] is not JsonObject game) log[key] = game = new JsonObject();
        game["name"] = name;
        if (game["days"] is not JsonObject days) game["days"] = days = new JsonObject();
        var day = Day(when);
        days[day] = Seconds(days[day]) + Math.Round(time.TotalSeconds);
    }

    /// <summary>Folds days older than KeepDays into each game's "older" counter.</summary>
    public static void Trim(JsonObject log, DateTime today)
    {
        var cutoff = Day(today.Date.AddDays(-KeepDays));
        foreach (var (_, node) in log)
        {
            if (node is not JsonObject { } game || game["days"] is not JsonObject days) continue;
            foreach (var old in days.Where(d => string.CompareOrdinal(d.Key, cutoff) < 0).Select(d => d.Key).ToList())
            {
                game["older"] = Seconds(game["older"]) + Seconds(days[old]);
                days.Remove(old);
            }
        }
    }

    /// <summary>Each game's time from <paramref name="from"/> (a date, inclusive) on; null = all time. Biggest first, zero left out.</summary>
    public static List<PlayTotal> Totals(JsonObject log, DateTime? from)
    {
        var start = from is { } f ? Day(f.Date) : null;
        var list = new List<PlayTotal>();
        foreach (var (key, node) in log)
        {
            if (node is not JsonObject game) continue;
            double seconds = start is null ? Seconds(game["older"]) : 0;
            if (game["days"] is JsonObject days)
                seconds += days.Where(d => start is null || string.CompareOrdinal(d.Key, start) >= 0).Sum(d => Seconds(d.Value));
            if (seconds > 0)
                list.Add(new PlayTotal(key, game["name"]?.GetValue<string>() ?? key, TimeSpan.FromSeconds(seconds)));
        }
        return list.OrderByDescending(t => t.Time).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static DateTime WeekStart(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static DateTime MonthStart(DateTime date) => new(date.Year, date.Month, 1);

    /// <summary>"2 h 05 min", "45 min", "menos de 1 min".</summary>
    public static string Format(TimeSpan t)
    {
        var minutes = (int)t.TotalMinutes;
        if (minutes < 1) return Loc.T("menos de 1 min");
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
    }

    private static double Seconds(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) ? d : node is JsonValue i && i.TryGetValue<long>(out var l) ? l : 0;
}
