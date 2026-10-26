using System.Windows.Threading;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;

namespace SackSpace.Services;

/// <summary>
/// Ends the timer widgets' countdowns even while their workspace is not shown: one timer
/// aimed at the nearest end (checked at least every 30 s, so sleep or a clock change
/// cannot make it miss), a Windows notification, and the panel's state moved on.
/// </summary>
public sealed class TimerService
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);
    private readonly ConfigSession _session;
    private readonly DispatcherTimer _timer = new();

    /// <summary>(title, text): a notification next to the clock.</summary>
    public Action<string, string>? Notify { get; set; }

    /// <summary>A panel's countdown ended; its widget redraws.</summary>
    public event Action<PanelState>? Finished;

    public TimerService(ConfigSession session)
    {
        _session = session;
        _timer.Tick += (_, _) => Check();
        Reschedule();
    }

    private IEnumerable<PanelState> Timers =>
        _session.Config.Workspaces.SelectMany(w => w.Panels).Where(p => p.Widget == BuiltInWidgets.Timer);

    /// <summary>After a timer started, paused or was reset.</summary>
    public void Reschedule()
    {
        _timer.Stop();
        var next = Timers.Select(p => TimerLogic.EndsAt(p.Settings)).Where(e => e is not null).Min();
        if (next is not { } end) return;
        var wait = end - DateTime.UtcNow;
        _timer.Interval = wait < TimeSpan.Zero ? TimeSpan.Zero : wait > MaxWait ? MaxWait : wait + TimeSpan.FromMilliseconds(50);
        _timer.Start();
    }

    private void Check()
    {
        var now = DateTime.UtcNow;
        foreach (var p in Timers.Where(p => TimerLogic.EndsAt(p.Settings) <= now).ToList())
        {
            var text = TimerLogic.Finish(p.Settings);
            Notify?.Invoke(TimerLogic.Mode(p.Settings), text);
            _session.MarkDirty();
            Finished?.Invoke(p);
        }
        Reschedule();
    }
}
