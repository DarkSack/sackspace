using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using SackSpace.Core;
using SackSpace.Core.Widgets;
using SackSpace.Interop;

namespace SackSpace.Services;

/// <summary>
/// Counts play time: every 15 s, if the window in front belongs to a Steam or Epic game
/// (its install folder), or to any program running exclusive full screen (Direct3D),
/// those seconds go to that game. Keeps running while the widgets rest in full screen,
/// which is exactly when people play. Costs one foreground lookup per tick.
/// </summary>
public sealed class PlayTracker
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);
    private readonly AppServices _s;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = Tick };
    private IReadOnlyList<(string Folder, string Key, string Name)> _games = [];
    private DateTime _gamesRead = DateTime.MinValue;
    private bool _reading;

    /// <summary>The game being played right now (key, name), or null.</summary>
    public (string Key, string Name)? Current { get; private set; }

    /// <summary>Raised after time was added (once per tick while playing) or play stopped.</summary>
    public event Action? Changed;

    public PlayTracker(AppServices services)
    {
        _s = services;
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        PlayTime.Trim(_s.Session.Config.PlayTime, DateTime.Today);
    }

    private void OnTick()
    {
        if (DateTime.UtcNow - _gamesRead > TimeSpan.FromMinutes(30)) ReadGames();
        var playing = Find();
        var was = Current;
        Current = playing;
        if (playing is { } g)
        {
            PlayTime.Add(_s.Session.Config.PlayTime, g.Key, g.Name, DateTime.Now, Tick);
            _s.Session.MarkDirty();
        }
        if (playing is not null || was is not null) Changed?.Invoke();
    }

    private (string Key, string Name)? Find()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return null;
        var path = Native.ProcessPath(pid);
        if (path is null) return null;
        foreach (var (folder, key, name) in _games)
            if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return (key, name);
        // Not a Steam or Epic game: exclusive full screen still means a game (Minecraft, Riot, emulators…).
        if (Native.SHQueryUserNotificationState(out var state) != 0 || state != 3) return null;
        return ("exe:" + Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), ProgramName(path));
    }

    private void ReadGames()
    {
        if (_reading) return;
        _reading = true;
        _gamesRead = DateTime.UtcNow;
        Task.Run(() => GameLibraries.InstalledGames(new HashSet<string>())
                .Where(g => !GameCatalog.IsTool(g) && Directory.Exists(g.Folder))
                .Select(g => (g.Folder.TrimEnd('\\') + "\\", g.Key, g.Name))
                .ToList())
            .ContinueWith(t =>
            {
                _reading = false;
                if (t.IsCompletedSuccessfully) _games = t.Result;
                else Log.Warn("No se pudo leer la lista de juegos para el tiempo de juego", t.Exception);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static string ProgramName(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description)) return description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Path.GetFileNameWithoutExtension(path);
    }
}
