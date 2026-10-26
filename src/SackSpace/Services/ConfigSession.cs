using System.Windows.Threading;
using SackSpace.Core.Model;
using SackSpace.Core.Storage;

namespace SackSpace.Services;

[Flags]
public enum ChangeKind
{
    None = 0,
    /// <summary>Another workspace became active.</summary>
    Workspace = 1,
    /// <summary>Panels were added, removed, hidden or moved to another monitor.</summary>
    Layout = 2,
    Appearance = 4,
    General = 8,
    Hotkeys = 16,
    /// <summary>Workspaces were added, renamed, recoloured or reordered.</summary>
    WorkspaceList = 32,
    /// <summary>The whole config was replaced (import, reset).</summary>
    All = 63,
}

/// <summary>
/// The live config and its saving. Every change marks it dirty; it is written 600 ms
/// after the last change (typing a note does not hammer the disk) and on exit.
/// UI thread only.
/// </summary>
public sealed class ConfigSession
{
    private readonly DispatcherTimer _timer;
    private bool _dirty;

    public ConfigStore Store { get; }
    public AppConfig Config { get; private set; }

    /// <summary>Raised after something the UI shows changed.</summary>
    public event Action<ChangeKind>? Changed;

    public ConfigSession(ConfigStore store, AppConfig config)
    {
        Store = store;
        Config = config;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _timer.Tick += (_, _) => Flush();
    }

    /// <summary>Data changed (a note, a task, a panel position): just save it soon.</summary>
    public void MarkDirty()
    {
        _dirty = true;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Something the UI shows changed: save soon and tell everyone.</summary>
    public void Notify(ChangeKind kind)
    {
        MarkDirty();
        Changed?.Invoke(kind);
    }

    public void Flush()
    {
        _timer.Stop();
        if (!_dirty) return;
        try
        {
            Store.Save(Config);
            _dirty = false;
        }
        catch (Exception ex)
        {
            // Kept dirty: the next change or the exit tries again.
            Log.Error("No se pudo guardar la configuración", ex);
        }
    }

    public void Replace(AppConfig config)
    {
        Config = config;
        _dirty = true;
        Flush();
        Changed?.Invoke(ChangeKind.All);
    }
}
