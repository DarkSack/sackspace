using System.Text.Json.Nodes;

namespace SackSpace.Core.Widgets;

/// <summary>
/// The timer widget's state, kept in its panel's settings so it keeps running on another
/// workspace and across restarts: "endsAt" (Unix ms) while a countdown runs, "left"
/// (seconds) while paused, "startedAt"/"elapsed" for the stopwatch, "phase" and "rounds"
/// for Pomodoro.
/// </summary>
public static class TimerLogic
{
    public const string Countdown = "Temporizador", Pomodoro = "Pomodoro", Stopwatch = "Cronómetro";

    public static string Mode(JsonObject s) => s.GetString("mode", Countdown);
    public static bool IsStopwatch(JsonObject s) => Mode(s) == Stopwatch;
    public static bool OnBreak(JsonObject s) => Mode(s) == Pomodoro && s.GetString("phase", "work") == "break";
    public static int Rounds(JsonObject s) => (int)s.GetDouble("rounds", 0);

    /// <summary>The full length of the current countdown.</summary>
    public static TimeSpan Length(JsonObject s) => TimeSpan.FromMinutes(Mode(s) switch
    {
        Pomodoro => OnBreak(s) ? s.GetDouble("break", 5) : s.GetDouble("work", 25),
        _ => s.GetDouble("minutes", 10),
    });

    public static bool IsRunning(JsonObject s) => s.ContainsKey(IsStopwatch(s) ? "startedAt" : "endsAt");

    public static DateTime? EndsAt(JsonObject s) =>
        !IsStopwatch(s) && s["endsAt"] is JsonValue v && v.TryGetValue<long>(out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;

    /// <summary>Time left (countdown) or elapsed (stopwatch).</summary>
    public static TimeSpan Shown(JsonObject s, DateTime nowUtc)
    {
        if (IsStopwatch(s))
        {
            var elapsed = TimeSpan.FromSeconds(s.GetDouble("elapsed", 0));
            return s["startedAt"] is JsonValue v && v.TryGetValue<long>(out var ms)
                ? elapsed + (nowUtc - DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime)
                : elapsed;
        }
        if (EndsAt(s) is { } end) return end > nowUtc ? end - nowUtc : TimeSpan.Zero;
        return s.ContainsKey("left") ? TimeSpan.FromSeconds(s.GetDouble("left", 0)) : Length(s);
    }

    public static void Start(JsonObject s, DateTime nowUtc)
    {
        if (IsRunning(s)) return;
        var now = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
        if (IsStopwatch(s)) s["startedAt"] = now;
        else
        {
            s["endsAt"] = now + (long)Shown(s, nowUtc).TotalMilliseconds;
            s.Remove("left");
        }
    }

    public static void Pause(JsonObject s, DateTime nowUtc)
    {
        if (!IsRunning(s)) return;
        var shown = Shown(s, nowUtc).TotalSeconds;
        if (IsStopwatch(s)) { s["elapsed"] = shown; s.Remove("startedAt"); }
        else { s["left"] = shown; s.Remove("endsAt"); }
    }

    /// <summary>Back to the start of the current phase (Pomodoro keeps its rounds).</summary>
    public static void Reset(JsonObject s)
    {
        foreach (var key in new[] { "endsAt", "left", "startedAt", "elapsed" }) s.Remove(key);
    }

    /// <summary>A countdown reached zero: stops it (Pomodoro moves to the next phase) and says what happened.</summary>
    public static string Finish(JsonObject s)
    {
        Reset(s);
        if (Mode(s) != Pomodoro) return "Se acabó el tiempo.";
        if (OnBreak(s))
        {
            s["phase"] = "work";
            return "Fin del descanso: a por el siguiente pomodoro.";
        }
        s["phase"] = "break";
        s["rounds"] = Rounds(s) + 1;
        return $"Pomodoro {Rounds(s)} terminado: toca descansar {s.GetDouble("break", 5):0} min.";
    }

    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
}
