using System.Windows.Threading;
using Microsoft.Win32;
using SackSpace.Interop;

namespace SackSpace.Services;

/// <summary>
/// Decides when the widgets may rest: while a game or a video runs full screen (if the
/// user wants that) and while the session is locked. Clocks and meters stop; the
/// panels stay where they are.
/// </summary>
public sealed class ActivityGovernor
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
    private bool _locked, _fullscreen, _paused;

    public bool PauseInFullscreen { get; set; } = true;
    public bool Paused => _paused;
    public event Action<bool>? PausedChanged;

    public ActivityGovernor()
    {
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
                _locked = true;
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
                _locked = false;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(Update);
        };
    }

    private void Check()
    {
        _fullscreen = Native.SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
        Update();
    }

    private void Update()
    {
        var paused = _locked || (PauseInFullscreen && _fullscreen);
        if (paused == _paused) return;
        _paused = paused;
        PausedChanged?.Invoke(paused);
    }
}
