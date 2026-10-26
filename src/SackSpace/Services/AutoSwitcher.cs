using SackSpace.Core.Model;
using SackSpace.Interop;

namespace SackSpace.Services;

/// <summary>
/// Brings a workspace up by itself: when its hours begin, or when one of its programs
/// comes to the front. Only on a change (a new slot, an app of other workspaces), so
/// switching by hand afterwards sticks. A program can belong to several workspaces:
/// if the one shown is among them nothing moves; otherwise the first of them comes up.
/// </summary>
public sealed class AutoSwitcher
{
    private readonly AppServices _s;
    private string? _lastScheduled, _lastApp;

    public AutoSwitcher(AppServices services)
    {
        _s = services;
        _s.Clock.Minute += OnMinute;
        _s.Governor.Foreground += OnForeground;
        OnMinute(DateTime.Now); // starting inside working hours opens the right workspace
    }

    private List<Workspace> Workspaces => _s.Session.Config.Workspaces;

    private void OnMinute(DateTime now)
    {
        var ws = AutoSwitch.Scheduled(Workspaces, now);
        if (ws?.Id == _lastScheduled) return;
        _lastScheduled = ws?.Id;
        if (ws is not null) Switch(ws, "horario");
    }

    private void OnForeground(IntPtr hwnd)
    {
        if (!Workspaces.Any(w => w.Apps is { Count: > 0 })) return;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return;
        var path = Native.ProcessPath(pid);
        if (path is null) return; // gone already, or a protected process
        var matches = AutoSwitch.ForApp(Workspaces, path, path);
        var key = string.Join(",", matches.Select(w => w.Id));
        if (key == _lastApp) return;
        _lastApp = key;
        if (matches.Count == 0 || matches.Any(w => w.Id == _s.Session.Config.ActiveWorkspace?.Id)) return;
        Switch(matches[0], System.IO.Path.GetFileName(path));
    }

    private void Switch(Workspace ws, string why)
    {
        if (ws.Id == _s.Session.Config.ActiveWorkspace?.Id || _s.Hosts.IsEditing) return;
        Log.Info($"Espacio automático: {ws.Name} ({why})");
        _s.Hosts.SwitchTo(ws.Id);
    }
}
