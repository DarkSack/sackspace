using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SackSpace.Core.Widgets;

/// <summary>One day of the work schedule: off, or a shift with an optional break.</summary>
public sealed record Shift(TimeSpan Start, TimeSpan End, TimeSpan? BreakStart, TimeSpan? BreakEnd, string Text)
{
    public bool Overnight => End <= Start;
    public TimeSpan Length => Overnight ? End + TimeSpan.FromDays(1) - Start : End - Start;
}

/// <summary>Where "now" falls in the schedule, for the widget's status line.</summary>
public enum ShiftPhase { Off, Before, Working, OnBreak, Done }

public sealed record ShiftStatus(ShiftPhase Phase, Shift? Shift, DateTime? Next);

/// <summary>
/// The "Horario" widget: what the user typed for each weekday ("09:00-18:00 comida
/// 14:00-15:00", "22:00-06:00", "Descanso"). The first range is the shift, a second one
/// its break; anything without a range is a day off. Stored under "mon"…"sun".
/// </summary>
public static partial class ShiftSchedule
{
    public static readonly (DayOfWeek Day, string Key, string Name)[] Week =
    [
        (DayOfWeek.Monday, "mon", "Lunes"), (DayOfWeek.Tuesday, "tue", "Martes"), (DayOfWeek.Wednesday, "wed", "Miércoles"),
        (DayOfWeek.Thursday, "thu", "Jueves"), (DayOfWeek.Friday, "fri", "Viernes"), (DayOfWeek.Saturday, "sat", "Sábado"),
        (DayOfWeek.Sunday, "sun", "Domingo"),
    ];

    [GeneratedRegex(@"(\d{1,2})[:.](\d{2})\s*(?:-|–|a|al|hasta)\s*(\d{1,2})[:.](\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex Range();

    public static string Key(DayOfWeek day) => Week.First(w => w.Day == day).Key;

    /// <summary>null = a day off (empty, "Descanso", "Libre"…).</summary>
    public static Shift? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var ranges = Range().Matches(text).Select(ToRange).Where(r => r is not null).Select(r => r!.Value).ToList();
        if (ranges.Count == 0) return null;
        var (start, end) = ranges[0];
        if (start == end) return null;
        return new Shift(start, end, ranges.Count > 1 ? ranges[1].Start : null, ranges.Count > 1 ? ranges[1].End : null, text.Trim());
    }

    private static (TimeSpan Start, TimeSpan End)? ToRange(Match m)
    {
        static TimeSpan? T(string h, string mm) =>
            int.Parse(h, CultureInfo.InvariantCulture) is var hh and >= 0 and <= 24 && int.Parse(mm, CultureInfo.InvariantCulture) is var mi and < 60
                ? new TimeSpan(hh % 24, mi, 0) : null;
        var a = T(m.Groups[1].Value, m.Groups[2].Value);
        var b = T(m.Groups[3].Value, m.Groups[4].Value);
        return a is { } s && b is { } e ? (s, e) : null;
    }

    public static Shift? For(JsonObject settings, DayOfWeek day) => Parse(settings.GetString(Key(day), ""));

    /// <summary>
    /// The shift that matters now: yesterday's if it runs past midnight and has not ended,
    /// else today's. Next is when the phase changes (start, break, end).
    /// </summary>
    public static ShiftStatus Status(JsonObject settings, DateTime now)
    {
        var yesterday = For(settings, now.AddDays(-1).DayOfWeek);
        if (yesterday is { Overnight: true } && now.TimeOfDay < yesterday.End)
            return Inside(yesterday, now.Date.AddDays(-1), now);

        var today = For(settings, now.DayOfWeek);
        if (today is null) return new ShiftStatus(ShiftPhase.Off, null, null);
        var start = now.Date + today.Start;
        if (now < start) return new ShiftStatus(ShiftPhase.Before, today, start);
        return Inside(today, now.Date, now);
    }

    private static ShiftStatus Inside(Shift s, DateTime day, DateTime now)
    {
        var end = day + s.Start + s.Length;
        if (now >= end) return new ShiftStatus(ShiftPhase.Done, s, null);
        if (s.BreakStart is { } bs && s.BreakEnd is { } be)
        {
            // The break sits inside the shift, possibly after midnight.
            var breakStart = day + bs + (bs < s.Start ? TimeSpan.FromDays(1) : TimeSpan.Zero);
            var breakEnd = breakStart + (be > bs ? be - bs : be + TimeSpan.FromDays(1) - bs);
            if (now >= breakStart && now < breakEnd) return new ShiftStatus(ShiftPhase.OnBreak, s, breakEnd);
            if (now < breakStart) return new ShiftStatus(ShiftPhase.Working, s, breakStart);
        }
        return new ShiftStatus(ShiftPhase.Working, s, end);
    }

    /// <summary>"2 h 15 min", "45 min", "menos de 1 min".</summary>
    public static string Span(TimeSpan t)
    {
        var minutes = (int)Math.Ceiling(t.TotalMinutes);
        if (minutes < 1) return "menos de 1 min";
        return minutes >= 60 ? (minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min") : $"{minutes} min";
    }

    public static string Range(Shift s) => $"{s.Start:hh\\:mm} – {s.End:hh\\:mm}";

    // ── Activities ───────────────────────────────────────────

    /// <summary>The Monday of the week a date falls in.</summary>
    public static DateTime WeekStart(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>A day's activities, timed ones first in order, then the rest as added.</summary>
    public static List<ScheduleEvent> EventsOn(JsonObject settings, DateTime date) =>
        ScheduleEvent.ReadAll(settings).Where(e => e.On(date))
            .OrderBy(e => e.Time is null).ThenBy(e => e.Time).ToList();

    /// <summary>One-off activities of days already gone are dropped; true if any was.</summary>
    public static bool DropPast(JsonObject settings, DateTime today)
    {
        var all = ScheduleEvent.ReadAll(settings);
        var kept = all.Where(e => e.Date is not { } d || d >= today.Date).ToList();
        if (kept.Count == all.Count) return false;
        ScheduleEvent.WriteAll(settings, kept);
        return true;
    }
}

/// <summary>
/// An activity in the schedule: on one date, or every week on a weekday ("mon"…"sun").
/// Time is optional, and so is End (only with a time, after it, same day).
/// Stored in the panel's settings under "events".
/// </summary>
public sealed record ScheduleEvent(string Id, string Text, TimeSpan? Time, DateTime? Date, string? Weekly, TimeSpan? End = null)
{
    public bool On(DateTime date) => Date is { } d ? d.Date == date.Date : Weekly == ShiftSchedule.Key(date.DayOfWeek);

    public string Label => Time is { } t ? (End is { } e ? $"{t:hh\\:mm}–{e:hh\\:mm}  {Text}" : $"{t:hh\\:mm}  {Text}") : Text;

    /// <summary>Minutes of the day it covers on the timetable; without an end, an hour.</summary>
    public (int From, int To) Span(int defaultLength = 60)
    {
        var from = (int)(Time ?? TimeSpan.Zero).TotalMinutes;
        return (from, End is { } e && e > Time ? (int)e.TotalMinutes : Math.Min(1439, from + defaultLength));
    }

    /// <summary>
    /// Side-by-side lanes for blocks that overlap: each gets a lane and how many lanes its
    /// group of overlapping blocks needs. Results in input order.
    /// </summary>
    public static (int Lane, int Lanes)[] Lanes(IReadOnlyList<(int From, int To)> blocks)
    {
        var result = new (int Lane, int Lanes)[blocks.Count];
        var group = new List<int>();
        var laneEnds = new List<int>();
        var groupEnd = int.MinValue;
        void Close()
        {
            foreach (var g in group) result[g].Lanes = laneEnds.Count;
            group.Clear();
            laneEnds.Clear();
        }
        foreach (var i in Enumerable.Range(0, blocks.Count).OrderBy(i => blocks[i].From).ThenBy(i => blocks[i].To))
        {
            var (from, to) = blocks[i];
            if (from >= groupEnd) Close();
            var lane = laneEnds.FindIndex(end => end <= from);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(to); }
            else laneEnds[lane] = to;
            result[i].Lane = lane;
            group.Add(i);
            groupEnd = Math.Max(from >= groupEnd ? to : groupEnd, to);
        }
        Close();
        return result;
    }

    public static List<ScheduleEvent> ReadAll(JsonObject settings) =>
        settings["events"] is JsonArray items
            ? items.OfType<JsonObject>().Select(o => new ScheduleEvent(
                    o.GetString("id", Guid.NewGuid().ToString("N")[..8]),
                    o.GetString("text", ""),
                    ParseTime(o.GetString("time", "")),
                    DateTime.TryParseExact(o.GetString("date", ""), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                    o["weekly"] is null ? null : o.GetString("weekly", ""),
                ParseTime(o.GetString("end", ""))))
                .Where(e => e.Text.Length > 0 && (e.Date is not null || e.Weekly is not null))
                .ToList()
            : [];

    public static void WriteAll(JsonObject settings, IEnumerable<ScheduleEvent> events) =>
        settings["events"] = new JsonArray(events.Select(e =>
        {
            var o = new JsonObject { ["id"] = e.Id, ["text"] = e.Text };
            if (e.Time is { } t) o["time"] = t.ToString(@"hh\:mm");
            if (e.Time is not null && e.End is { } end) o["end"] = end.ToString(@"hh\:mm");
            if (e.Date is { } d) o["date"] = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (e.Weekly is { } w) o["weekly"] = w;
            return (JsonNode)o;
        }).ToArray());

    private static TimeSpan? ParseTime(string text) => Model.AutoSwitch.TryParseTime(text, out var t) ? t : null;
}
