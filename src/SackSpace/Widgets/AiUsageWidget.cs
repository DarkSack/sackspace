using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>Why an account shows no fresh numbers, in words for the user.</summary>
public sealed class UsageUnavailable(string message) : Exception(message);

/// <summary>
/// Where the numbers come from. Claude, in a Claude Code folder: api.anthropic.com/api/oauth/usage
/// with the token kept there (read only: refreshing it here would rotate the token Claude Code
/// also holds); in a Claude Desktop data folder: the plan-usage-history.json the app writes.
/// ChatGPT: the rate_limits Codex writes into its session logs. Only the first goes online.
/// Reads are shared by every panel with the same folder.
/// </summary>
public static class AiUsageService
{
    public static readonly TimeSpan ClaudeMaxAge = TimeSpan.FromMinutes(5);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Dictionary<string, (Task<AiUsageReport> Task, DateTimeOffset At)> Claude = new(StringComparer.OrdinalIgnoreCase);

    static AiUsageService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("SackSpace/0.2");

    /// <summary>At most one request per folder every five minutes, failed or not.</summary>
    public static Task<AiUsageReport> GetClaude(string folder)
    {
        if (Claude.TryGetValue(folder, out var cached) && (!cached.Task.IsCompleted || DateTimeOffset.UtcNow - cached.At < ClaudeMaxAge))
            return cached.Task;
        var task = FetchClaude(folder);
        Claude[folder] = (task, DateTimeOffset.UtcNow);
        return task;
    }

    private static async Task<AiUsageReport> FetchClaude(string folder)
    {
        var file = Path.Combine(folder, ".credentials.json");
        if (!File.Exists(file))
        {
            // A Claude Desktop data folder: what the app itself saved, without going online.
            var history = Path.Combine(folder, "plan-usage-history.json");
            if (!File.Exists(history)) throw new UsageUnavailable("Ni Claude Code ni Claude Desktop tienen datos en esta carpeta.");
            await using var fs = new FileStream(history, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return (await JsonNode.ParseAsync(fs)) is { } saved && AiUsageParser.ParseClaudeDesktopHistory(saved) is { } report ? report
                : throw new UsageUnavailable("Claude Desktop aún no ha guardado tu uso. Ábrelo con esta cuenta.");
        }
        var oauth = JsonNode.Parse(await File.ReadAllTextAsync(file))?["claudeAiOauth"];
        var token = oauth?["accessToken"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token)) throw new UsageUnavailable("No hay sesión de Claude Code en esta carpeta.");
        if (oauth?["expiresAt"] is JsonValue e && e.TryGetValue<long>(out var ms) && DateTimeOffset.FromUnixTimeMilliseconds(ms) <= DateTimeOffset.UtcNow)
            throw new UsageUnavailable("La sesión caducó. Abre Claude Code con esta cuenta y se renueva sola.");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        using var response = await Http.SendAsync(request);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UsageUnavailable("Anthropic no aceptó la sesión. Abre Claude Code con esta cuenta.");
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? throw new InvalidOperationException("Respuesta vacía.");
        return AiUsageParser.ParseClaude(json, oauth?["subscriptionType"]?.GetValue<string>(), DateTimeOffset.UtcNow);
    }

    /// <summary>The newest limits in the Codex logs of this folder. A log without a reply yet has none, so a few are tried.</summary>
    public static Task<AiUsageReport> GetChatGpt(string folder) => Task.Run(() =>
    {
        var sessions = Path.Combine(folder, "sessions");
        if (!Directory.Exists(sessions)) throw new UsageUnavailable("No hay sesiones de Codex en esta carpeta.");
        foreach (var f in new DirectoryInfo(sessions).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).Take(10))
            if (AiUsageParser.ParseCodexLog(TailLines(f.FullName), f.LastWriteTimeUtc) is { } report) return report;
        throw new UsageUnavailable("Codex aún no ha guardado tus límites. Úsalo una vez con esta cuenta.");
    });

    // shortcut: only the last 512 KB of each log is read; a huge final tool output can hide the limits and an older log is used instead.
    private static List<string> TailLines(string path, int bytes = 512 * 1024)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, fs.Length - bytes);
        fs.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        var lines = reader.ReadToEnd().Split('\n').ToList();
        if (start > 0 && lines.Count > 0) lines.RemoveAt(0); // started mid-line
        return lines;
    }
}

/// <summary>Each account's 5-hour and weekly limits as bars, with when they start again. Looked at again every minute.</summary>
public sealed class AiUsageWidget : DockPanel, IDisposable
{
    private readonly WidgetContext _c;
    private readonly StackPanel _list = new();
    private readonly List<(string Title, Func<Task<AiUsageReport>> Read)> _accounts = [];
    private readonly Dictionary<string, AiUsageReport> _last = new();
    private bool _loading;

    public AiUsageWidget(WidgetContext c)
    {
        _c = c;
        var header = Ui.Header(Glyphs.Gauge, "Uso de IA");
        SetDock(header, Dock.Top);
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var settings = BuiltInWidgets.Find(BuiltInWidgets.Usage)!.Settings;
        string Setting(string key) => c.Settings.Value(settings.First(s => s.Key == key));
        foreach (var a in AiUsageParser.ParseAccounts(Setting("claude")))
            _accounts.Add(("Claude · " + a.Name, () => AiUsageService.GetClaude(a.Folder)));
        foreach (var a in AiUsageParser.ParseAccounts(Setting("chatgpt")))
            _accounts.Add(("ChatGPT · " + a.Name, () => AiUsageService.GetChatGpt(a.Folder)));

        if (_accounts.Count == 0)
        {
            _list.Children.Add(Ui.Text("Añade tus cuentas en las opciones del widget (el engranaje en modo edición, o clic derecho › Opciones).", wrap: true));
            return;
        }
        c.Services.Clock.Minute += OnMinute;
        _ = Load();
    }

    private void OnMinute(DateTime now) => _ = Load();

    private async Task Load()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var rows = new List<FrameworkElement>();
            foreach (var (title, read) in _accounts)
            {
                try
                {
                    _last[title] = await read();
                    rows.Add(Row(title, _last[title], null));
                }
                catch (Exception ex)
                {
                    if (ex is not UsageUnavailable) Log.Warn($"Uso de IA de '{title}'", ex);
                    // Keep the last numbers on screen, with the reason they are not new.
                    rows.Add(Row(title, _last.GetValueOrDefault(title), ex is UsageUnavailable ? ex.Message : "Sin conexión. Lo intento de nuevo en un rato."));
                }
            }
            _list.Children.Clear();
            foreach (var r in rows) _list.Children.Add(r);
        }
        finally { _loading = false; }
    }

    private static FrameworkElement Row(string title, AiUsageReport? r, string? problem)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var top = new DockPanel();
        var info = Ui.Text(r is null ? "" : Info(r));
        SetDock(info, Dock.Right);
        top.Children.Add(info);
        top.Children.Add(new TextBlock { Text = title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        box.Children.Add(top);
        if (r is not null)
        {
            var now = DateTimeOffset.Now;
            box.Children.Add(Limit("5 horas", r.FiveHours?.At(now), now));
            box.Children.Add(Limit("Semana", r.Week?.At(now), now));
        }
        if (problem is not null)
        {
            var why = Ui.Text(problem, wrap: true);
            why.Margin = new Thickness(0, 6, 0, 0);
            box.Children.Add(why);
        }
        return box;
    }

    /// <summary>The plan, and how old the numbers are when they are not fresh.</summary>
    private static string Info(AiUsageReport r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(r.Plan)) parts.Add(Ui.Capitalize(r.Plan));
        var age = DateTimeOffset.UtcNow - r.ReadAt;
        if (age > TimeSpan.FromMinutes(10))
            parts.Add("hace " + (age.TotalHours < 1 ? $"{age.TotalMinutes:0} min" : age.TotalDays < 1 ? $"{age.TotalHours:0} h" : $"{age.TotalDays:0} d"));
        return string.Join(" · ", parts);
    }

    private static FrameworkElement Limit(string label, UsageWindow? w, DateTimeOffset now)
    {
        var row = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var top = new DockPanel();
        var right = Ui.Text(w is null ? "sin dato" : w.ResetsAt is { } at ? $"{w.Percent:0} % · reinicia {When(at, now)}" : $"{w.Percent:0} %");
        SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(Ui.Text(label));
        row.Children.Add(top);
        row.Children.Add(Ui.Bar((w?.Percent ?? 0) / 100));
        return row;
    }

    private static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime().DateTime;
        var today = now.LocalDateTime.Date;
        return local.Date == today ? local.ToString("HH:mm", Ui.Culture)
            : local.Date == today.AddDays(1) ? "mañana " + local.ToString("HH:mm", Ui.Culture)
            : local.ToString("ddd HH:mm", Ui.Culture).Replace(".", "");
    }

    public void Dispose() => _c.Services.Clock.Minute -= OnMinute;
}
