using System.Windows.Threading;

namespace SackSpace.Services;

/// <summary>
/// One timer for every clock-like widget, aligned to the wall clock. With nobody
/// showing seconds it wakes once a minute instead of once a second.
/// </summary>
public sealed class ClockTicker
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private DateTime _last = DateTime.Now;
    private bool _paused;
    private Action<DateTime>? _second;

    public event Action<DateTime>? Second
    {
        add { _second += value; Reschedule(); }
        remove { _second -= value; }
    }

    public event Action<DateTime>? Minute;
    public event Action<DateTime>? Day;

    public ClockTicker()
    {
        _timer.Tick += (_, _) => Tick();
        Reschedule();
    }

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            if (value) _timer.Stop();
            else Tick(); // catch up at once after a game or a locked session
        }
    }

    private void Tick()
    {
        var now = DateTime.Now;
        _second?.Invoke(now);
        if (now.Minute != _last.Minute || now.Hour != _last.Hour || now.Date != _last.Date) Minute?.Invoke(now);
        if (now.Date != _last.Date) Day?.Invoke(now);
        _last = now;
        Reschedule();
    }

    private void Reschedule()
    {
        if (_paused) return;
        var now = DateTime.Now;
        var next = _second is not null
            ? now.AddMilliseconds(1000 - now.Millisecond + 5)
            : now.Date.AddHours(now.Hour).AddMinutes(now.Minute + 1).AddMilliseconds(5);
        _timer.Stop();
        _timer.Interval = next - now;
        _timer.Start();
    }
}
