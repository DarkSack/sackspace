using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using SackSpace.Core;
using SackSpace.Core.Model;
using SackSpace.Interop;
using SackSpace.Services;
using SackSpace.Settings;

namespace SackSpace.Shell;

/// <summary>
/// One DesktopHost per monitor, and the state they share: which workspace is shown,
/// whether the panels are raised over the windows, and edit mode.
/// </summary>
public sealed class HostManager
{
    private readonly AppServices _services;
    private readonly List<DesktopHost> _hosts = new();
    private readonly DispatcherTimer _displayDebounce = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private SettingsWindow? _settings;
    private bool _show = true;

    /// <summary>Monitors that have a host, primary first.</summary>
    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = [];
    public IReadOnlyList<DesktopHost> Hosts => _hosts;
    public IEnumerable<HostView> Views => _hosts.Select(h => h.View);
    public HostView? PrimaryView => _hosts.FirstOrDefault()?.View;

    public bool IsEditing { get; private set; }
    public bool IsRaised { get; private set; }
    /// <summary>When the panels last went down because another app took the focus.</summary>
    public DateTime LastAutoLower { get; private set; }

    /// <summary>Raised, edit mode or the active workspace changed.</summary>
    public event Action? StateChanged;

    private AppConfig Config => _services.Session.Config;

    public HostManager(AppServices services)
    {
        _services = services;
        _displayDebounce.Tick += (_, _) => { _displayDebounce.Stop(); OnDisplaysChanged(); };
        // Monitors plugged, unplugged, resized, rescaled; the taskbar moved.
        SystemEvents.DisplaySettingsChanged += (_, _) => Restart(_displayDebounce);
        SystemEvents.UserPreferenceChanged += (_, e) => { if (e.Category == UserPreferenceCategory.Desktop) Restart(_displayDebounce); };
    }

    private static void Restart(DispatcherTimer t) =>
        Application.Current?.Dispatcher.BeginInvoke(() => { t.Stop(); t.Start(); });

    /// <param name="show">False for --snapshot: the views are built and laid out but no window appears.</param>
    public void Build(bool show = true)
    {
        _show = show;
        var all = Interop.Monitors.All();
        Monitors = Config.General.UseAllMonitors ? all : all.Take(1).ToList();
        foreach (var m in Monitors)
        {
            var host = new DesktopHost(this, _services, m);
            _hosts.Add(host);
            if (show) host.Show();
            else
            {
                host.View.Measure(host.View.AreaSize);
                host.View.Arrange(new System.Windows.Rect(host.View.AreaSize));
            }
        }
        UpdateBar();
        var ws = Config.ActiveWorkspace;
        foreach (var v in Views)
        {
            v.UpdateWallpaper();
            if (ws is not null) v.ShowWorkspace(ws, 0, animate: show);
        }
        Log.Info($"Escritorio listo en {Monitors.Count} pantalla(s)");
    }

    public void CloseAll()
    {
        IsRaised = IsEditing = false;
        foreach (var h in _hosts)
        {
            h.View.DisposeAll();
            h.AllowClose = true;
            h.Close();
        }
        _hosts.Clear();
    }

    /// <summary>New monitors or work areas: rebuilt only if something really changed.</summary>
    private void OnDisplaysChanged()
    {
        var all = Interop.Monitors.All();
        var wanted = Config.General.UseAllMonitors ? all : all.Take(1).ToList();
        if (wanted.SequenceEqual(Monitors)) return;
        Rebuild();
    }

    public void Rebuild()
    {
        Log.Info("Las pantallas cambiaron: se rehace el escritorio");
        CloseAll();
        Build(_show);
        _services.ApplyTheme(false);
        StateChanged?.Invoke();
    }

    public void OnExplorerRestarted()
    {
        foreach (var h in _hosts)
        {
            h.AttachToDesktop();
            h.Place();
        }
    }

    public void OnWallpaperChanged()
    {
        foreach (var v in Views) v.UpdateWallpaper();
    }

    public void ReloadIcons()
    {
        foreach (var v in Views) v.ReloadIcons();
    }

    // ── Bar ──────────────────────────────────────────────────

    /// <summary>The workspace bar lives on the primary monitor, if the user wants it.</summary>
    public void UpdateBar()
    {
        var primary = PrimaryView;
        foreach (var v in Views)
        {
            if (v == primary && Config.General.ShowWorkspaceBar)
            {
                if (v.Bar is null) v.SetBar(new WorkspaceBar(this, _services, v));
                else v.Bar.Rebuild();
            }
            else v.SetBar(null);
        }
    }

    // ── Modes ────────────────────────────────────────────────

    public void SetEditing(bool editing) => ApplyMode(raised: editing, editing: editing);

    /// <summary>Lowering also ends edit mode.</summary>
    public void SetRaised(bool raised) => ApplyMode(raised, editing: raised && IsEditing);

    public void ToggleRaised() => SetRaised(!IsRaised);

    private void ApplyMode(bool raised, bool editing)
    {
        if (raised == IsRaised && editing == IsEditing) return;
        var raiseChanged = raised != IsRaised;
        IsRaised = raised;
        IsEditing = editing;
        foreach (var h in _hosts)
        {
            if (raiseChanged) h.SetRaised(raised);
            h.View.SetMode(raised, editing);
        }
        if (raised && raiseChanged) HostUnderMouse()?.FocusHost();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Windows will not keep a window owned by the desktop above the others, so raised
    /// panels go down as soon as another app takes the focus (like Windows' own widgets).
    /// </summary>
    public void OnHostDeactivated()
    {
        if (!IsRaised) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
            if (!IsRaised || pid == Environment.ProcessId) return; // another host, a dialog, a menu
            LastAutoLower = DateTime.UtcNow;
            SetRaised(false);
        }, DispatcherPriority.Background);
    }

    private DesktopHost? HostUnderMouse()
    {
        var p = System.Windows.Forms.Cursor.Position;
        return _hosts.FirstOrDefault(h => p.X >= h.Monitor.Bounds.Left && p.X < h.Monitor.Bounds.Right
                                          && p.Y >= h.Monitor.Bounds.Top && p.Y < h.Monitor.Bounds.Bottom)
               ?? _hosts.FirstOrDefault();
    }

    // ── Workspaces ───────────────────────────────────────────

    public void SwitchTo(string id)
    {
        var list = Config.Workspaces;
        var from = list.FindIndex(w => w.Id == Config.ActiveWorkspace?.Id);
        var to = list.FindIndex(w => w.Id == id);
        if (to < 0 || to == from) return;
        Config.ActiveWorkspaceId = id;
        _services.Session.MarkDirty();
        _services.ApplyTheme(animate: true);
        var direction = Math.Sign(to - from);
        foreach (var v in Views) v.ShowWorkspace(list[to], direction, animate: _show);
        PrimaryView?.Bar?.Update();
        if (!Config.General.ShowWorkspaceBar) PrimaryView?.ShowSnackbar(list[to].Name);
        StateChanged?.Invoke();
    }

    public void SwitchToNumber(int number)
    {
        if (number >= 1 && number <= Config.Workspaces.Count) SwitchTo(Config.Workspaces[number - 1].Id);
    }

    public void Next() => Step(+1);
    public void Previous() => Step(-1);

    private void Step(int delta)
    {
        var list = Config.Workspaces;
        if (list.Count < 2) return;
        var i = list.FindIndex(w => w.Id == Config.ActiveWorkspace?.Id);
        SwitchTo(list[((i + delta) % list.Count + list.Count) % list.Count].Id);
    }

    /// <summary>After the workspace list or the whole config changed.</summary>
    public void ReloadAll()
    {
        var ws = Config.ActiveWorkspace;
        if (ws is not null && Config.ActiveWorkspaceId != ws.Id) Config.ActiveWorkspaceId = ws.Id;
        UpdateBar();
        foreach (var v in Views) v.ReloadPanels();
        StateChanged?.Invoke();
    }

    // ── Panels ───────────────────────────────────────────────

    public void MovePanel(PanelState panel, MonitorInfo target)
    {
        var view = Views.FirstOrDefault(v => v.Monitor.Key == target.Key);
        if (view is null) return;
        var spot = LayoutMath.FindFreeSpot(panel.Width, panel.Height, view.Panels.Select(p => p.Bounds), view.LayoutBounds);
        panel.Monitor = target.Key;
        panel.Bounds = spot;
        panel.Z = view.NextZ();
        _services.Session.MarkDirty();
        foreach (var v in Views) v.ReloadPanels();
    }

    // ── Settings window ──────────────────────────────────────

    public void OpenSettings(string? page = null)
    {
        SetRaised(false);
        if (_settings is null)
        {
            _settings = new SettingsWindow(_services);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        if (page is not null) _settings.ShowPage(page);
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    public void CloseSettings() => _settings?.Close();
}
