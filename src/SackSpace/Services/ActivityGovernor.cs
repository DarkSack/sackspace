using System.Windows.Threading;
using Microsoft.Win32;
using SackSpace.Interop;

namespace SackSpace.Services;

/// <summary>
/// Decides when the widgets may rest: while a game or a video runs full screen (if the
/// user wants that) and while the session is locked. Clocks and meters stop; the
/// panels stay where they are. Also tells who is interested when another window comes
/// to the front or the windows may have changed (minimized, maximized).
/// </summary>
public sealed class ActivityGovernor
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
    // Kept in a field: the hook calls it for as long as the app runs.
    private readonly Native.WinEventProc _onWinEvent;
    private bool _locked, _fullscreen, _paused;

    public bool PauseInFullscreen { get; set; } = true;
    public bool Paused => _paused;
    public event Action<bool>? PausedChanged;
    /// <summary>Another window came to the front (its handle).</summary>
    public event Action<IntPtr>? Foreground;
    /// <summary>Every few seconds and on window events: time to look at what covers the screens.</summary>
    public event Action? WindowsMayHaveChanged;

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
        _onWinEvent = (_, evt, hwnd, _, _, _, _) =>
        {
            if (evt == Native.EVENT_SYSTEM_FOREGROUND) Foreground?.Invoke(hwnd);
            WindowsMayHaveChanged?.Invoke();
        };
        // Out of context: delivered through this thread's message loop, no DLL injected anywhere.
        Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onWinEvent, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZEEND, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _onWinEvent, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
    }

    private void Check()
    {
        _fullscreen = Native.SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
        Update();
        WindowsMayHaveChanged?.Invoke();
    }

    private void Update()
    {
        var paused = _locked || (PauseInFullscreen && _fullscreen);
        if (paused == _paused) return;
        _paused = paused;
        PausedChanged?.Invoke(paused);
    }
}
