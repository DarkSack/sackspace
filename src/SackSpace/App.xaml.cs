using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SackSpace.Core.Defaults;
using SackSpace.Core.Hotkeys;
using SackSpace.Core.Model;
using SackSpace.Core.Storage;
using SackSpace.Core.Widgets;
using SackSpace.Interop;
using SackSpace.Services;
using SackSpace.Shell;
using SackSpace.Theming;
using SackSpace.Widgets;

namespace SackSpace;

/// <summary>
/// Startup and the wiring between services. Arguments:
/// --data DIR (another data folder, for tests), --snapshot DIR (render every workspace
/// to PNG and exit), --autostart (started by Windows).
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _wake;
    private AppServices? _s;
    private TrayIcon? _tray;
    private GeneralSettings _lastGeneral = new();
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? Arg(string name)
        {
            var i = Array.IndexOf(e.Args, name);
            return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null;
        }
        var data = Path.GetFullPath(Arg("--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SackSpace"));
        var snapshot = Arg("--snapshot");
        var bench = e.Args.Contains("--bench");
        Directory.CreateDirectory(data);
        Log.Init(data);

        // One SackSpace per data folder; launching it again opens the settings of the running one.
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToLowerInvariant())))[..16];
        _mutex = new Mutex(true, "SackSpace-" + id, out var first);
        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, "SackSpace-wake-" + id);
        if (!first)
        {
            _wake.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ev) => { Log.Error("Error no controlado", ev.Exception); ev.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Error("Error fatal", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log.Warn("Tarea sin observar", ev.Exception); ev.SetObserved(); };

        // Transparent full-screen windows are copied back to memory on every frame anyway, so
        // the GPU path costs the same CPU and ~4× the memory (measured: 340 MB vs 80 MB).
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        Anim.LimitFrameRate();

        var store = new ConfigStore(data);
        var (config, outcome) = store.Load(CreateDefault);
        Log.Info($"SackSpace {typeof(App).Assembly.GetName().Version?.ToString(3)} · configuración: {outcome} · datos en {data}");
        Anim.Configure(config.General.Animations && snapshot is null);
        WidgetRegistry.RegisterBuiltIns();

        var session = new ConfigSession(store, config);
        var s = new AppServices
        {
            DataFolder = data,
            Session = session,
            Icons = new DesktopIconService(session),
            Theme = new ThemeService(data),
            Wallpaper = new WallpaperService(),
            Clock = new ClockTicker(),
            System = new SystemSampler(),
            Media = new MediaService(),
            Governor = new ActivityGovernor { PauseInFullscreen = config.General.PauseInFullscreen },
            Timers = new TimerService(session),
            CreateDefault = CreateDefault,
        };
        AppServices.Current = _s = s;
        s.Hosts = new HostManager(s);
        s.ApplyTheme(false); // brushes exist before the first window
        s.Icons.Enable(config.General.DesktopIcons, touchWindows: snapshot is null);
        s.Hosts.Build(show: snapshot is null);
        s.Icons.Changed += s.Hosts.ReloadIcons;
        s.ApplyTheme(false); // again, now that the wallpaper colour is known

        s.Governor.PausedChanged += paused => { s.Clock.Paused = paused; s.System.Paused = paused; };
        s.Governor.WindowsMayHaveChanged += s.Hosts.UpdateResting;
        s.Wallpaper.Changed += () => { s.Hosts.OnWallpaperChanged(); s.ApplyTheme(true); };
        s.Session.Changed += OnConfigChanged;
        // A new or recovered config goes to disk right away, not at the first change.
        if (outcome != LoadOutcome.Loaded && snapshot is null) s.Session.MarkDirty();
        _lastGeneral = Copy(config.General);

        if (snapshot is not null)
        {
            _ = Snapshot(Path.GetFullPath(snapshot));
            return;
        }

        s.Hotkeys = new HotkeyService();
        s.Hotkeys.Pressed += OnHotkey;
        RegisterHotkeys();
        if (config.General.StartWithWindows) Autostart.Apply(true); // keeps the path right after an update
        _tray = new TrayIcon(s, Quit);
        s.Timers.Notify = _tray.Notify;
        _ = new AutoSwitcher(s);
        if (bench) _ = Bench();
        ListenForSecondLaunch();
        SessionEnding += (_, _) => s.Session.Flush();

        // One line for problem reports: how heavy SackSpace is once settled.
        var memoryCheck = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        memoryCheck.Tick += (_, _) =>
        {
            memoryCheck.Stop();
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            Log.Info($"Memoria a los 20 s: gestionada {GC.GetGCMemoryInfo().HeapSizeBytes >> 20} MB, privada {me.PrivateMemorySize64 >> 20} MB");
        };
        memoryCheck.Start();

        var primary = s.Hosts.PrimaryView;
        switch (outcome)
        {
            case LoadOutcome.RestoredFromBackup:
                primary?.ShowSnackbar("La configuración estaba dañada: se recuperó la copia anterior.");
                break;
            case LoadOutcome.Reset:
                primary?.ShowSnackbar("La configuración estaba dañada y sin copia: se empezó de cero. El archivo dañado se guardó aparte.");
                break;
            case LoadOutcome.Created:
                primary?.ShowSnackbar("SackSpace ya está en tu escritorio. Mantén pulsado un panel para moverlo.");
                break;
        }
    }

    /// <summary>First run: four workspaces laid out for the primary monitor, with the apps this PC has.</summary>
    private static AppConfig CreateDefault()
    {
        var primary = Interop.Monitors.All().FirstOrDefault();
        double w = 1280, h = 720;
        if (primary is not null)
        {
            w = primary.WorkArea.Width / primary.Scale;
            h = primary.WorkArea.Height / primary.Scale;
        }
        IReadOnlyList<InstalledApp> apps = [];
        try { apps = Sta.Run(InstalledApps.List).GetAwaiter().GetResult(); }
        catch (Exception ex) { Log.Warn("No se pudo leer el menú Inicio", ex); }

        var config = DefaultConfig.Create(w, h, name => InstalledApps.Find(apps, name), ShellNames.KnownFolder);
        // Names as the Start menu and Explorer show them ("Descargas", "Visual Studio Code").
        foreach (var panel in config.Workspaces.SelectMany(ws => ws.Panels).Where(p => p.Widget == BuiltInWidgets.Shortcuts))
        {
            var items = ShortcutItem.ReadAll(panel.Settings).Select(i => i with
            {
                Name = i.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                    ? apps.FirstOrDefault(a => a.ParsingName == i.Path)?.Name ?? i.Name
                    : ShellNames.DisplayName(i.Path),
            });
            ShortcutItem.WriteAll(panel.Settings, items.ToList());
        }
        return config;
    }

    private void OnConfigChanged(ChangeKind kind)
    {
        var s = _s!;
        var config = s.Session.Config;
        if (kind == ChangeKind.All)
        {
            // Imported or reset: everything again.
            Anim.Configure(config.General.Animations);
            s.Governor.PauseInFullscreen = config.General.PauseInFullscreen;
            if (config.General.StartWithWindows != _lastGeneral.StartWithWindows) Autostart.Apply(config.General.StartWithWindows);
            _lastGeneral = Copy(config.General);
            s.Icons.Enable(config.General.DesktopIcons);
            s.Hosts.Rebuild();
            RegisterHotkeys();
            return;
        }
        if (kind.HasFlag(ChangeKind.Appearance) || kind.HasFlag(ChangeKind.Workspace)) s.ApplyTheme(animate: true);
        if (kind.HasFlag(ChangeKind.Appearance)) s.Hosts.OnWallpaperChanged(); // clear or frosted glass
        if (kind.HasFlag(ChangeKind.General))
        {
            var g = config.General;
            Anim.Configure(g.Animations);
            s.Governor.PauseInFullscreen = g.PauseInFullscreen;
            if (g.StartWithWindows != _lastGeneral.StartWithWindows) Autostart.Apply(g.StartWithWindows);
            if (g.DesktopIcons != _lastGeneral.DesktopIcons) s.Icons.Enable(g.DesktopIcons);
            if (g.UseAllMonitors != _lastGeneral.UseAllMonitors) s.Hosts.Rebuild();
            else if (g.ShowWorkspaceBar != _lastGeneral.ShowWorkspaceBar) s.Hosts.UpdateBar();
            if (g.RestBehindWindows != _lastGeneral.RestBehindWindows) s.Hosts.UpdateResting();
            _lastGeneral = Copy(g);
        }
        if (kind.HasFlag(ChangeKind.Workspace) || kind.HasFlag(ChangeKind.Layout)) s.Hosts.ReloadAll();
        else if (kind.HasFlag(ChangeKind.WorkspaceList) || kind.HasFlag(ChangeKind.Hotkeys)) s.Hosts.UpdateBar();
        if (kind.HasFlag(ChangeKind.Hotkeys)) RegisterHotkeys();
    }

    private static GeneralSettings Copy(GeneralSettings g) => new()
    {
        StartWithWindows = g.StartWithWindows,
        UseAllMonitors = g.UseAllMonitors,
        ShowWorkspaceBar = g.ShowWorkspaceBar,
        Animations = g.Animations,
        PauseInFullscreen = g.PauseInFullscreen,
        DesktopIcons = g.DesktopIcons,
        LockLayout = g.LockLayout,
        RestBehindWindows = g.RestBehindWindows,
    };

    private void RegisterHotkeys()
    {
        var s = _s!;
        if (s.Hotkeys is null) return;
        s.FailedHotkeys = s.Hotkeys.Register(s.Session.Config.Hotkeys);
        if (s.FailedHotkeys.Count > 0) Log.Warn("Atajos ocupados por otra app: " + string.Join(", ", s.FailedHotkeys));
    }

    private void OnHotkey(string action)
    {
        var hosts = _s!.Hosts;
        switch (action)
        {
            case HotkeyActions.Peek: hosts.ToggleRaised(); break;
            case HotkeyActions.Edit: hosts.SetEditing(!hosts.IsEditing); break;
            case HotkeyActions.Next: hosts.Next(); break;
            case HotkeyActions.Previous: hosts.Previous(); break;
            case HotkeyActions.Settings: hosts.OpenSettings(); break;
            default:
                if (action.StartsWith("workspace", StringComparison.Ordinal) && int.TryParse(action["workspace".Length..], out var n))
                    hosts.SwitchToNumber(n);
                break;
        }
    }

    private void ListenForSecondLaunch()
    {
        var wake = _wake!;
        new Thread(() =>
        {
            while (wake.WaitOne())
                Dispatcher.BeginInvoke(() => _s?.Hosts.OpenSettings());
        }) { IsBackground = true, Name = "SackSpace wake" }.Start();
    }

    public void Quit()
    {
        if (_exiting) return;
        _exiting = true;
        _s?.Session.Flush();
        _tray?.Dispose();
        _s?.Hotkeys?.Dispose();
        _s?.Hosts.CloseSettings();
        _s?.Icons.Enable(false); // Windows' own desktop icons come back
        _s?.Hosts.CloseAll();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _s?.Session.Flush();
        _tray?.Dispose();
        base.OnExit(e);
    }

    // ── --bench: CPU spent by the animations, for comparing builds (use with --data) ──

    private async Task Bench()
    {
        var hosts = _s!.Hosts;
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        TimeSpan Cpu() { me.Refresh(); return me.TotalProcessorTime; }
        await Task.Delay(4000);
        var ids = _s.Session.Config.Workspaces.Select(w => w.Id).ToList();
        var t0 = Cpu();
        for (var i = 0; i < 12; i++)
        {
            var id = ids[(i + 1) % ids.Count];
            hosts.SwitchTo(id);
            await Task.Delay(800);
        }
        var t1 = Cpu();
        for (var i = 0; i < 6; i++) { hosts.SetRaised(true); await Task.Delay(600); hosts.SetRaised(false); await Task.Delay(600); }
        var t2 = Cpu();
        await Task.Delay(6000);
        var t3 = Cpu();
        // One tiny change (like hovering a button): what a single redraw of the window costs.
        var bar = hosts.PrimaryView?.Bar;
        for (var i = 0; i < 20 && bar is not null; i++) { bar.Opacity = i % 2 == 0 ? 0.99 : 1; await Task.Delay(150); }
        var t4 = Cpu();
        Log.Info($"Banco: cambio de espacio {(t1 - t0).TotalMilliseconds / 12:0} ms de CPU, subir y bajar {(t2 - t1).TotalMilliseconds / 6:0} ms, reposo {(t3 - t2).TotalMilliseconds / 6:0} ms/s, un cambio pequeño {((t4 - t3).TotalMilliseconds - 3000 * (t3 - t2).TotalMilliseconds / 6000) / 20:0} ms");
        Quit();
    }

    // ── --snapshot: every workspace, edit mode and the gallery, as PNGs ──

    private async Task Snapshot(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var hosts = _s!.Hosts;
            await Task.Delay(1500);
            hosts.SetRaised(true);
            var n = 1;
            foreach (var ws in _s.Session.Config.Workspaces.ToList())
            {
                hosts.SwitchTo(ws.Id);
                await Task.Delay(1500); // icons and artwork load in the background
                SaveViews(dir, $"{n++}-{Slug(ws.Name)}");
            }
            hosts.SetEditing(true);
            await Task.Delay(400);
            SaveViews(dir, $"{n++}-edicion");
            hosts.PrimaryView?.OpenGallery();
            await Task.Delay(400);
            SaveViews(dir, $"{n}-galeria");
            if (_s.Icons.Enabled && hosts.PrimaryView is { CurrentWorkspace: { } current } primary)
            {
                primary.OpenSheet(new DesktopIconsSheet(primary, current));
                await Task.Delay(800);
                SaveViews(dir, $"{n + 1}-iconos");
            }
            if (hosts.PrimaryView is { } view && view.Panels.FirstOrDefault() is { } panel)
            {
                view.OpenWidgetSettings(panel);
                await Task.Delay(400);
                SaveViews(dir, $"{n + 2}-opciones");
            }
            // The settings pages, drawn without opening the window (dark background: Mica only exists on screen).
            var settings = new Settings.SettingsWindow(_s);
            foreach (var page in new[] { "general", "espacios", "apariencia" })
            {
                settings.ShowPage(page);
                var root = (FrameworkElement)settings.Content;
                var size = new Size(1000, 1500);
                root.Measure(size);
                root.Arrange(new Rect(size));
                root.UpdateLayout();
                var bmp = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 26, 34)), null, new Rect(size));
                bmp.Render(bg);
                bmp.Render(root);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                using var file = File.Create(Path.Combine(dir, $"ajustes-{page}.png"));
                png.Save(file);
            }
            settings.Close();
            Log.Info("Capturas guardadas en " + dir);
        }
        catch (Exception ex)
        {
            Log.Error("Falló --snapshot", ex);
        }
        Quit();
    }

    private void SaveViews(string dir, string name)
    {
        var i = 0;
        foreach (var v in _s!.Hosts.Views)
        {
            i++;
            var size = v.AreaSize;
            v.Measure(size);
            v.Arrange(new Rect(size));
            v.UpdateLayout();
            var scale = v.Monitor.Scale;
            var bmp = new RenderTargetBitmap((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(v);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(Path.Combine(dir, $"{name}-pantalla{i}.png"));
            png.Save(file);
        }
    }

    private static string Slug(string s) =>
        new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
