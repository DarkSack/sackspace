using System.IO;
using System.Windows;
using System.Windows.Threading;
using SackSpace.Core.Model;
using SackSpace.Interop;

namespace SackSpace.Services;

/// <summary>
/// Desktop icons per workspace. While on, Windows' own icons are hidden with the desktop's
/// "Mostrar iconos del escritorio" switch and SackSpace draws the ones each workspace
/// shows; off, or when SackSpace quits, Windows' come back. No file is moved or changed:
/// the desktop is in OneDrive for many people, and hidden files can be set to show.
/// </summary>
public sealed class DesktopIconService(ConfigSession session)
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private DispatcherTimer? _reload;

    public bool Enabled { get; private set; }
    public IReadOnlyList<DesktopItem> Items { get; private set; } = [];

    /// <summary>The icons or the choice of each workspace changed: redraw them.</summary>
    public event Action? Changed;

    /// <param name="touchWindows">False for snapshots: Windows' icons stay as they are.</param>
    public void Enable(bool on, bool touchWindows = true)
    {
        if (on == Enabled) return;
        Enabled = on;
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        if (touchWindows) DesktopItems.ShowWindowsIcons(!on);
        if (on) Watch();
        Reload();
    }

    public void Reload()
    {
        Items = Enabled ? DesktopItems.List() : [];
        Changed?.Invoke();
    }

    /// <summary>Saves a change to a workspace's choice and redraws.</summary>
    public void Choose(Workspace ws, string path, bool show)
    {
        DesktopIconLists.Set(ws, path, show, Items.Select(i => i.Path));
        session.MarkDirty();
        Changed?.Invoke();
    }

    public void ChooseAll(Workspace ws, bool show)
    {
        ws.DesktopIcons = show ? null : [];
        session.MarkDirty();
        Changed?.Invoke();
    }

    private void Watch()
    {
        foreach (var folder in DesktopItems.Folders)
        {
            try
            {
                var w = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes };
                w.Created += (_, e) => OnUi(() => DesktopIconLists.Created(session.Config, e.FullPath));
                w.Renamed += (_, e) => OnUi(() => DesktopIconLists.Renamed(session.Config, e.OldFullPath, e.FullPath));
                w.Deleted += (_, _) => OnUi(null);
                w.Changed += (_, _) => OnUi(null); // shown or hidden
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                Log.Warn($"No se puede vigilar el escritorio '{folder}'", ex);
            }
        }
    }

    /// <summary>Watcher events come on pool threads and in bursts: apply on the UI thread, redraw once.</summary>
    private void OnUi(Func<bool>? change) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        if (!Enabled) return;
        if (change?.Invoke() == true) session.MarkDirty();
        if (_reload is null)
        {
            _reload = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _reload.Tick += (_, _) => { _reload.Stop(); Reload(); };
        }
        _reload.Stop();
        _reload.Start();
    });
}
