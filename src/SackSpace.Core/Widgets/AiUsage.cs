using System.Globalization;
using System.Text.Json.Nodes;

namespace SackSpace.Core.Widgets;

/// <summary>One usage limit: the share used (0-100) and when it starts again.</summary>
public sealed record UsageWindow(double Percent, DateTimeOffset? ResetsAt)
{
    /// <summary>Once the reset time has passed the window is empty again, even before the next read.</summary>
    public UsageWindow At(DateTimeOffset now) => ResetsAt is { } r && r <= now ? new(0, null) : this;
}

/// <summary>An account's two limits as last read, with the plan when the source says it.</summary>
public sealed record AiUsageReport(UsageWindow? FiveHours, UsageWindow? Week, string? Plan, DateTimeOffset ReadAt);

/// <summary>An account the widget shows: a name and the folder that holds its sign-in or sessions.</summary>
public sealed record AiAccount(string Name, string Folder);

/// <summary>
/// Reads the usage limits of Claude (the JSON of api.anthropic.com/api/oauth/usage) and of
/// ChatGPT (the rate_limits Codex writes into its session logs), and the accounts setting.
/// </summary>
public static class AiUsageParser
{
    /// <summary>"Principal=C:\x; Trabajo=D:\y" (a bare folder is named after itself). Variables like %USERPROFILE% are expanded.</summary>
    public static IReadOnlyList<AiAccount> ParseAccounts(string text, Func<string, string>? expand = null)
    {
        expand ??= Environment.ExpandEnvironmentVariables;
        var list = new List<AiAccount>();
        foreach (var raw in text.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = raw.IndexOf('=');
            var folder = expand((eq > 0 ? raw[(eq + 1)..] : raw).Trim().Trim('"'));
            if (folder.Length == 0) continue;
            var name = eq > 0 ? raw[..eq].Trim() : Path.GetFileName(folder.TrimEnd('\\', '/'));
            list.Add(new AiAccount(name.Length > 0 ? name : folder, folder));
        }
        return list;
    }

    /// <summary>Claude: five_hour and seven_day, each with utilization (percent) and resets_at (ISO 8601).</summary>
    public static AiUsageReport ParseClaude(JsonNode json, string? plan, DateTimeOffset readAt) =>
        new(ClaudeWindow(json["five_hour"]), ClaudeWindow(json["seven_day"]), plan, readAt);

    /// <summary>
    /// Claude Desktop's plan-usage-history.json: samples {t: unix ms, u: {fh: 5-hour %, sd: 7-day %}}
    /// taken every 15 minutes while the app is open. No reset times; the newest sample with numbers wins.
    /// </summary>
    public static AiUsageReport? ParseClaudeDesktopHistory(JsonNode json)
    {
        if (json["samples"] is not JsonArray samples) return null;
        for (var i = samples.Count - 1; i >= 0; i--)
        {
            if (samples[i] is not JsonObject s || s["u"] is not JsonObject u || s["t"] is not JsonValue t || !t.TryGetValue<double>(out var ms)) continue;
            UsageWindow? Window(string key) => u[key] is JsonValue v && v.TryGetValue<double>(out var p) ? new UsageWindow(p, null) : null;
            var (five, week) = (Window("fh"), Window("sd"));
            if (five is not null || week is not null) return new AiUsageReport(five, week, null, DateTimeOffset.FromUnixTimeMilliseconds((long)ms));
        }
        return null;
    }

    private static UsageWindow? ClaudeWindow(JsonNode? w) =>
        w?["utilization"] is JsonValue v && v.TryGetValue<double>(out var percent)
            ? new UsageWindow(percent,
                w["resets_at"] is JsonValue r && r.TryGetValue<string>(out var at)
                && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out var reset) ? reset : null)
            : null;

    /// <summary>
    /// The newest line of a Codex session log that carries rate_limits, or null. Each window
    /// says its length, so a window of a day or more is the week and a shorter one the 5 hours.
    /// </summary>
    public static AiUsageReport? ParseCodexLog(IReadOnlyList<string> lines, DateTimeOffset fallbackReadAt)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!lines[i].Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;
            JsonNode? line;
            try { line = JsonNode.Parse(lines[i]); }
            catch (System.Text.Json.JsonException) { continue; } // the first line of a tail read can be cut
            if ((line?["payload"]?["rate_limits"] ?? line?["rate_limits"]) is not JsonObject limits) continue;

            UsageWindow? five = null, week = null;
            foreach (var key in new[] { "primary", "secondary" })
            {
                if (limits[key] is not JsonObject w || !(w["used_percent"] is JsonValue p && p.TryGetValue<double>(out var percent))) continue;
                var minutes = w["window_minutes"] is JsonValue m && m.TryGetValue<double>(out var mins) ? mins : key == "primary" ? 300 : 10080;
                DateTimeOffset? reset = w["resets_at"] is JsonValue r && r.TryGetValue<double>(out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds((long)unix) : null;
                if (minutes >= 1440) week = new UsageWindow(percent, reset);
                else five = new UsageWindow(percent, reset);
            }
            if (five is null && week is null) continue;

            var readAt = line?["timestamp"] is JsonValue t && t.TryGetValue<string>(out var ts)
                && DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp) ? stamp : fallbackReadAt;
            var plan = limits["plan_type"] is JsonValue pt && pt.TryGetValue<string>(out var planType) ? planType : null;
            return new AiUsageReport(five, week, plan, readAt);
        }
        return null;
    }
}
