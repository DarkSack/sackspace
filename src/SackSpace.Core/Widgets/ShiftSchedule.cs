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
}
