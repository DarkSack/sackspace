using SackSpace.Interop;
using SackSpace.Shell;
using SackSpace.Theming;

using SackSpace.Core.Widgets;

namespace SackSpace.Services;

/// <summary>The app's shared services, built once at startup.</summary>
public sealed class AppServices
{
    public static AppServices Current { get; set; } = null!;

    public required string DataFolder { get; init; }
    public required ConfigSession Session { get; init; }
    public required ThemeService Theme { get; init; }
    public required WallpaperService Wallpaper { get; init; }
    public required ClockTicker Clock { get; init; }
    public required SystemSampler System { get; init; }
    public required MediaService Media { get; init; }
    public required ActivityGovernor Governor { get; init; }
    public required DesktopIconService Icons { get; init; }
    public required TimerService Timers { get; init; }
    /// <summary>The first-run setup (used by "Empezar de cero").</summary>
    public required Func<Core.Model.AppConfig> CreateDefault { get; init; }
    public HostManager Hosts { get; set; } = null!;
    /// <summary>Null in --snapshot and --bench runs.</summary>
    public UpdateService? Updates { get; set; }
    public HotkeyService? Hotkeys { get; set; }
    /// <summary>Counts play time. Null in --snapshot runs.</summary>
    public PlayTracker? Play { get; set; }
    /// <summary>The user's files for the search; null when turned off or in --snapshot runs.</summary>
    public FileIndex? Files { get; set; }
    /// <summary>Plugin folders found at startup (see PluginLoader).</summary>
    public IReadOnlyList<SackSpace.Widgets.PluginInfo> Plugins { get; set; } = [];
    /// <summary>Actions whose shortcut could not be registered (taken by another app).</summary>
    public IReadOnlyList<string> FailedHotkeys { get; set; } = [];

    private TwitchApi? _twitch;
    private ClipboardWatcher? _clipboard;

    /// <summary>Clipboard history; starts listening the first time a panel asks.</summary>
    public ClipboardWatcher Clipboard => _clipboard ??= new ClipboardWatcher();

    /// <summary>The signed-in Twitch account (shared by every Twitch panel).</summary>
    public TwitchApi Twitch => _twitch ??= new TwitchApi(DataFolder);

    private Task<IReadOnlyList<InstalledApp>>? _apps;

    /// <summary>The Start menu's apps, read once on a background STA thread.</summary>
    public Task<IReadOnlyList<InstalledApp>> AppsAsync() => _apps ??= Sta.Run(InstalledApps.List);

    public void RefreshApps() => _apps = null;

    /// <summary>Recomputes colours (accent from the workspace or the wallpaper) and the glass blur.</summary>
    public void ApplyTheme(bool animate)
    {
        Wallpaper.CustomImage = Session.Config.Appearance.CustomWallpaper;
        var primary = Hosts?.Monitors.FirstOrDefault(m => m.IsPrimary);
        var dominant = primary is null ? null : Wallpaper.Get(primary).Dominant;
        Theme.Apply(Session.Config, dominant, animate);
        Wallpaper.BlurRadius = Theme.Current.BlurRadius;
        Wallpaper.BlurByMonitor = Session.Config.Appearance.BlurByMonitor;
    }
}

/// <summary>Runs work on a fresh STA thread (shell COM objects need one).</summary>
internal static class Sta
{
    public static Task<T> Run<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "SackSpace STA" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return done.Task;
    }
}
