using System.Text.Json.Nodes;
using SackSpace.Core;
using SackSpace.Core.Defaults;
using SackSpace.Core.Hotkeys;
using SackSpace.Core.Model;
using SackSpace.Core.Storage;
using SackSpace.Core.Themes;
using SackSpace.Core.Widgets;

namespace SackSpace.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sackspace-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static AppConfig Defaults() => DefaultConfig.Create(1536, 816, _ => null, _ => null);

    [Fact]
    public void First_run_creates_the_defaults_and_a_save_round_trips()
    {
        var store = new ConfigStore(_dir);
        var (config, outcome) = store.Load(Defaults);
        Assert.Equal(LoadOutcome.Created, outcome);

        config.Workspaces[1].Name = "Oficina";
        config.Workspaces[0].Panels[0].Settings["text"] = "hola";
        config.Appearance.AccentMode = AccentMode.Wallpaper;
        store.Save(config);

        var (again, outcome2) = store.Load(Defaults);
        Assert.Equal(LoadOutcome.Loaded, outcome2);
        Assert.Equal("Oficina", again.Workspaces[1].Name);
        Assert.Equal("hola", again.Workspaces[0].Panels[0].Settings["text"]!.GetValue<string>());
        Assert.Equal(AccentMode.Wallpaper, again.Appearance.AccentMode);
        Assert.Contains("\"accentMode\": \"wallpaper\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Saving_twice_keeps_the_previous_version_as_backup()
    {
        var store = new ConfigStore(_dir);
        var (config, _) = store.Load(Defaults);
        config.Workspaces[0].Name = "Uno";
        store.Save(config);
        config.Workspaces[0].Name = "Dos";
        store.Save(config);

        Assert.Contains("\"Dos\"", File.ReadAllText(store.FilePath));
        Assert.Contains("\"Uno\"", File.ReadAllText(store.BackupPath));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void A_damaged_file_is_kept_aside_and_the_backup_is_used()
    {
        var store = new ConfigStore(_dir);
        var (config, _) = store.Load(Defaults);
        config.Workspaces[0].Name = "Bueno";
        store.Save(config);
        store.Save(config); // now the backup holds "Bueno" too
        File.WriteAllText(store.FilePath, "{ esto no es json");

        var (loaded, outcome) = store.Load(Defaults);
        Assert.Equal(LoadOutcome.RestoredFromBackup, outcome);
        Assert.Equal("Bueno", loaded.Workspaces[0].Name);
        Assert.Single(Directory.GetFiles(_dir, "config.json.danado-*"));
    }

    [Fact]
    public void Both_damaged_resets_without_deleting_anything()
    {
        Directory.CreateDirectory(_dir);
        var store = new ConfigStore(_dir);
        File.WriteAllText(store.FilePath, "roto");
        File.WriteAllText(store.BackupPath, "también roto");

        var (config, outcome) = store.Load(Defaults);
        Assert.Equal(LoadOutcome.Reset, outcome);
        Assert.Equal(4, config.Workspaces.Count);
        Assert.Equal(2, Directory.GetFiles(_dir, "*.danado-*").Length);
    }

    [Fact]
    public void Normalize_repairs_a_hand_edited_file()
    {
        var json = """
            {
              "workspaces": [
                { "id": "a", "name": " ", "panels": [ { "widget": "clock", "width": 10, "height": 10 }, { "widget": "" } ] },
                { "id": "a", "name": "Dos" }
              ],
              "activeWorkspaceId": "nope",
              "hotkeys": { "peek": "" }
            }
            """;
        var c = ConfigStore.TryDeserialize(json)!;
        Assert.NotEqual(c.Workspaces[0].Id, c.Workspaces[1].Id);
        Assert.Equal("Espacio", c.Workspaces[0].Name);
        Assert.Single(c.Workspaces[0].Panels);
        Assert.Equal(BuiltInWidgets.Find("clock")!.MinWidth, c.Workspaces[0].Panels[0].Width);
        Assert.Equal(c.Workspaces[0].Id, c.ActiveWorkspaceId);
        Assert.Equal("", c.Hotkeys[HotkeyActions.Peek]);           // the user turned it off: stays off
        Assert.Equal("Ctrl+Alt+Shift+E", c.Hotkeys[HotkeyActions.Edit]); // missing: default
    }

    [Fact]
    public void Import_rejects_files_that_are_not_a_config() =>
        Assert.Null(ConfigStore.TryDeserialize("{ \"foo\": 1 }"));
}

public class DefaultConfigTests
{
    [Theory]
    [InlineData(1536, 816)]
    [InlineData(1920, 1032)]
    [InlineData(1280, 760)]
    [InlineData(1024, 700)]
    public void Every_workspace_fits_the_screen_without_overlaps(double w, double h)
    {
        var c = DefaultConfig.Create(w, h, _ => null, _ => null);
        Assert.Equal(["Personal", "Trabajo", "Gaming", "Desarrollo"], c.Workspaces.Select(x => x.Name));
        foreach (var ws in c.Workspaces)
        {
            var rects = ws.Panels.Select(p => p.Bounds).ToList();
            Assert.All(rects, r => Assert.True(r.X >= 0 && r.Y >= 0 && r.Right <= w && r.Bottom <= h, $"{ws.Name}: {r} fuera de {w}x{h}"));
            Assert.False(LayoutMath.Overlaps(rects), $"{ws.Name} tiene paneles encimados en {w}x{h}");
        }
    }

    [Fact]
    public void Shortcuts_only_list_what_exists()
    {
        var c = DefaultConfig.Create(1536, 816,
            app => app == "Steam" ? @"C:\Start\Steam.lnk" : null,
            folder => folder == "Documents" ? @"C:\Users\x\Documents" : null);

        var games = c.Workspaces.Single(w => w.Name == "Gaming").Panels.Single(p => p.Widget == BuiltInWidgets.Shortcuts);
        Assert.Equal([new ShortcutItem("Steam", @"C:\Start\Steam.lnk")], ShortcutItem.ReadAll(games.Settings));

        var folders = c.Workspaces[0].Panels.Single(p => p.Widget == BuiltInWidgets.Shortcuts);
        Assert.Equal(["Documents"], ShortcutItem.ReadAll(folders.Settings).Select(i => i.Name));
    }

    [Fact]
    public void Every_default_hotkey_parses() =>
        Assert.All(HotkeyActions.Defaults.Values, g => Assert.True(HotkeyGesture.TryParse(g, out _), g));
}

public class LayoutMathTests
{
    private static readonly Rect Area = new(0, 0, 1000, 800);

    [Fact]
    public void Moving_near_another_panel_snaps_a_gap_away()
    {
        var other = new Rect(100, 100, 200, 200);
        // Dropped 5 DIPs right of "other + gap": sticks to the gap.
        var moved = LayoutMath.SnapMove(new Rect(other.Right + LayoutMath.Gap + 5, 104, 100, 100), [other], Area);
        Assert.Equal(other.Right + LayoutMath.Gap, moved.X);
        Assert.Equal(other.Y, moved.Y); // top edges aligned
    }

    [Fact]
    public void Far_from_everything_it_snaps_to_the_grid()
    {
        var moved = LayoutMath.SnapMove(new Rect(403, 301, 100, 100), [], Area);
        Assert.Equal(0, moved.X % LayoutMath.Grid);
        Assert.Equal(0, moved.Y % LayoutMath.Grid);
    }

    [Fact]
    public void A_panel_dragged_off_screen_comes_back_inside()
    {
        var moved = LayoutMath.SnapMove(new Rect(950, -40, 200, 100), [], Area);
        Assert.True(moved.X >= 0 && moved.Right <= Area.Right && moved.Y >= 0);
    }

    [Fact]
    public void Resizing_respects_the_minimum_and_the_area()
    {
        var small = LayoutMath.SnapResize(new Rect(100, 100, 20, 20), [], Area, 150, 120);
        Assert.Equal((150.0, 120.0), (small.Width, small.Height));
        var big = LayoutMath.SnapResize(new Rect(900, 700, 400, 400), [], Area, 150, 120);
        Assert.True(big.Right <= Area.Right && big.Bottom <= Area.Bottom);
    }

    [Fact]
    public void Free_spots_fill_from_the_right_and_never_overlap()
    {
        var placed = new List<Rect>();
        for (var i = 0; i < 6; i++) placed.Add(LayoutMath.FindFreeSpot(300, 200, placed, Area));
        Assert.False(LayoutMath.Overlaps(placed));
        Assert.True(placed[0].Right >= Area.Right - LayoutMath.Margin - LayoutMath.Grid);
        Assert.All(placed, r => Assert.True(r.X >= 0 && r.Right <= Area.Right && r.Bottom <= Area.Bottom));
    }

    [Fact]
    public void Arrange_tidies_overlapping_panels()
    {
        var messy = Enumerable.Repeat(new Rect(10, 10, 250, 180), 5).ToList();
        Assert.False(LayoutMath.Overlaps(LayoutMath.Arrange(messy, Area)));
    }
}

public class ThemeTests
{
    [Theory]
    [InlineData("#FF5C8A", 255, 255, 92, 138)]
    [InlineData("80102030", 128, 16, 32, 48)]
    public void Hex_colours_parse(string hex, byte a, byte r, byte g, byte b) =>
        Assert.Equal(new Argb(a, r, g, b), Argb.Parse(hex));

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Bad_hex_is_rejected(string hex) => Assert.False(Argb.TryParse(hex, out _));

    [Fact]
    public void Hsl_round_trips()
    {
        var c = new Argb(255, 79, 209, 197);
        var (h, s, l) = c.ToHsl();
        var back = Argb.FromHsl(h, s, l);
        Assert.InRange(Math.Abs(back.R - c.R) + Math.Abs(back.G - c.G) + Math.Abs(back.B - c.B), 0, 3);
    }

    [Fact]
    public void The_dominant_colour_is_the_colourful_majority()
    {
        // 70 % orange, 20 % grey, 10 % blue (BGRA).
        var pixels = new List<byte>();
        for (var i = 0; i < 70; i++) pixels.AddRange(new byte[] { 30, 140, 240, 255 });
        for (var i = 0; i < 20; i++) pixels.AddRange(new byte[] { 128, 128, 128, 255 });
        for (var i = 0; i < 10; i++) pixels.AddRange(new byte[] { 230, 80, 40, 255 });
        var c = ColorMath.Dominant(pixels.ToArray())!.Value;
        var (h, _, _) = c.ToHsl();
        Assert.InRange(h, 20, 40); // orange
    }

    [Fact]
    public void A_grey_picture_has_no_dominant_colour() =>
        Assert.Null(ColorMath.Dominant(Enumerable.Repeat(new byte[] { 90, 90, 90, 255 }, 50).SelectMany(x => x).ToArray()));

    [Fact]
    public void Accent_follows_the_chosen_mode()
    {
        var theme = BuiltInThemes.All[0];
        var wallpaper = new Argb(255, 20, 60, 200);
        Assert.Equal(Argb.Parse("#4FD1C5"), ResolvedTheme.Resolve(theme, new AppearanceSettings(), "#4FD1C5", wallpaper).Accent);
        Assert.Equal(Argb.Parse(theme.Accent),
            ResolvedTheme.Resolve(theme, new AppearanceSettings { AccentMode = AccentMode.Theme }, "#4FD1C5", wallpaper).Accent);
        var fromWallpaper = ResolvedTheme.Resolve(theme, new AppearanceSettings { AccentMode = AccentMode.Wallpaper }, null, wallpaper).Accent;
        Assert.InRange(fromWallpaper.ToHsl().L, 0.6, 0.76); // brightened for a dark theme
        // No wallpaper colour yet: falls back to the theme.
        Assert.Equal(Argb.Parse(theme.Accent),
            ResolvedTheme.Resolve(theme, new AppearanceSettings { AccentMode = AccentMode.Wallpaper }, null, null).Accent);
    }

    [Fact]
    public void Theme_files_load_and_bad_ones_are_reported()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sackspace-themes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "rosa.json"), """{ "id": "rosa", "name": "Rosa", "accent": "#FF77AA", /* comentario */ }""");
            File.WriteAllText(Path.Combine(dir, "malo.json"), """{ "id": "malo", "name": "Malo", "accent": "rojo" }""");
            File.WriteAllText(Path.Combine(dir, "dup.json"), """{ "id": "grafito", "name": "Otro grafito" }""");

            var catalog = new ThemeCatalog(dir);
            Assert.Equal("Rosa", catalog.Get("rosa").Name);
            Assert.Equal(BuiltInThemes.All.Count + 1, catalog.Themes.Count);
            Assert.Equal(2, catalog.Errors.Count);
            Assert.Equal(BuiltInThemes.All[0].Id, catalog.Get("no-existe").Id);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Built_in_themes_are_valid() =>
        Assert.All(BuiltInThemes.All, t => Assert.Empty(t.Validate()));
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", "Ctrl+Alt+Space")]
    [InlineData("alt + ctrl + e", "Ctrl+Alt+E")]
    [InlineData("Ctrl+Alt+3", "Ctrl+Alt+3")]
    [InlineData("Win+Shift+Left", "Shift+Win+Left")]
    [InlineData("F9", "F9")]
    public void Gestures_parse_and_format(string text, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(expected, g.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("E")]
    [InlineData("Ctrl+Ctrl+E")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Patata+E")]
    public void Unsafe_or_broken_gestures_are_rejected(string text) =>
        Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void Digits_use_wpf_key_names() =>
        Assert.Equal("D3", (HotkeyGesture.TryParse("Ctrl+3", out var g) ? g : default).Key);

    [Fact]
    public void Defaults_never_take_altgr_characters()
    {
        // Ctrl+Alt = AltGr on Spanish layouts: digits give | @ # ~ € ¬, E gives €, Q gives @.
        foreach (var text in HotkeyActions.Defaults.Values)
        {
            Assert.True(HotkeyGesture.TryParse(text, out var g), text);
            if (g.Modifiers != (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) continue;
            Assert.False(g.Key is "E" or "Q" || (g.Key.Length == 2 && g.Key[0] == 'D' && char.IsAsciiDigit(g.Key[1])), text);
        }
    }
}

public class WidgetSettingsTests
{
    [Fact]
    public void Values_fall_back_to_the_declared_default()
    {
        var clock = BuiltInWidgets.Find(BuiltInWidgets.Clock)!;
        var settings = new JsonObject { ["seconds"] = true };
        Assert.Equal("true", settings.Value(clock.Settings.Single(s => s.Key == "seconds")));
        Assert.Equal("true", settings.Value(clock.Settings.Single(s => s.Key == "format24")));
    }

    [Fact]
    public void Shortcut_names_drop_link_extensions()
    {
        Assert.Equal("Steam", ShortcutItem.NameFor(@"C:\x\Steam.lnk"));
        Assert.Equal("Proyectos", ShortcutItem.NameFor(@"E:\Proyectos\"));
        Assert.Equal("informe.pdf", ShortcutItem.NameFor(@"C:\informe.pdf"));
        Assert.Equal(@"D:", ShortcutItem.NameFor(@"D:\"));
    }

    [Fact]
    public void Every_widget_has_sane_sizes() =>
        Assert.All(BuiltInWidgets.All, d =>
        {
            Assert.True(d.MinWidth <= d.DefaultWidth && d.MinHeight <= d.DefaultHeight, d.Id);
            Assert.Equal(d.Settings.Count, d.Settings.Select(s => s.Key).Distinct().Count());
        });
}

public class LiveWallpaperTests : IDisposable
{
    private const string Main = @"\\?\DISPLAY#LEN62A4#7&2beae17&0&UID260#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string Left = @"\\?\DISPLAY#MTC03D7#7&2beae17&0&UID264#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string Tablet = @"\\?\DISPLAY#Default_Monitor#1&1efbcd86&1&UID256#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sackspace-live-" + Guid.NewGuid().ToString("N"));

    public LiveWallpaperTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>A JSON string literal, quotes and escapes included.</summary>
    private static string J(string s) => System.Text.Json.JsonSerializer.Serialize(s);

    private static string Forward(string s) => s.Replace('\\', '/');

    [Fact]
    public void Wallpaper_engine_maps_each_monitor_through_its_monitor_map()
    {
        Write(Path.Combine("a", "project.json"), """{ "title": "Goblin", "preview": "preview.gif" }""");
        Write(Path.Combine("a", "preview.gif"), "x");
        Write(Path.Combine("b", "project.json"), """{ "title": "Danganronpa", "preview": "preview.jpg" }""");
        Write(Path.Combine("b", "preview.jpg"), "x");
        var config = Write("config.json", $$"""
            { "?installdirectory": "E:/x", "Sack": { "general": {
              "user": { "monitordetection": "managed", "monitormap": {
                {{J(Forward(Main))}}: { "location": 0 },
                {{J(Forward(Left))}}: { "location": 1 },
                {{J(Forward(Tablet))}}: { "location": 2 } } },
              "wallpaperconfig": { "layout": 0, "selectedwallpapers": {
                "Monitor0": { "file": {{J(Forward(Path.Combine(_dir, "a", "scene.pkg")))}} },
                "Monitor1": { "file": {{J(Forward(Path.Combine(_dir, "b", "scene.pkg")))}} } } } } } }
            """);

        Assert.Equal("Goblin", LiveWallpapers.WallpaperEngine(config, Main, isPrimary: true)?.Title);
        Assert.Equal(Forward(Path.Combine(_dir, "b", "preview.jpg")), Forward(LiveWallpapers.WallpaperEngine(config, Left, isPrimary: false)!.Picture));
        Assert.Null(LiveWallpapers.WallpaperEngine(config, Tablet, isPrimary: false)); // no wallpaper there
        Assert.Equal("Goblin", LiveWallpapers.WallpaperEngine(config, "DISPLAY#NEW", isPrimary: true)?.Title); // unknown primary → Monitor0
        Assert.Null(LiveWallpapers.WallpaperEngine(Path.Combine(_dir, "missing.json"), Main, true));
    }

    [Fact]
    public void Lively_prefers_a_picture_wallpaper_then_its_thumbnail()
    {
        Write(Path.Combine("pic", "LivelyInfo.json"), """{ "Title": "Playa", "FileName": "beach.png", "Thumbnail": "t.jpg", "Type": 7 }""");
        Write(Path.Combine("pic", "beach.png"), "x");
        Write(Path.Combine("web", "LivelyInfo.json"), """{ "Title": "Lluvia", "FileName": "index.html", "Thumbnail": "lively_t.jpg" }""");
        Write(Path.Combine("web", "lively_t.jpg"), "x");
        Write("WallpaperLayout.json", $$"""
            [ { "LivelyScreen": { "DeviceId": {{J(Main)}} }, "LivelyInfoPath": {{J(Path.Combine(_dir, "pic"))}} },
              { "LivelyScreen": { "DeviceId": {{J(Left)}} }, "LivelyInfoPath": {{J(Path.Combine(_dir, "web"))}} } ]
            """);

        Assert.Equal(Path.Combine(_dir, "pic", "beach.png"), LiveWallpapers.Lively(_dir, Main)?.Picture);
        Assert.Equal("Lluvia", LiveWallpapers.Lively(_dir, Left)?.Title);
        Assert.Null(LiveWallpapers.Lively(_dir, Tablet));

        Write("Settings.json", """{ "WallpaperArrangement": 2 }"""); // duplicate: the first entry everywhere
        Assert.Equal("Playa", LiveWallpapers.Lively(_dir, Tablet)?.Title);
    }
}

public class DesktopIconListTests
{
    [Fact]
    public void Each_workspace_keeps_its_own_icons_and_new_ones_land_where_they_were_made()
    {
        var personal = new Workspace { Name = "Personal" };
        var work = new Workspace { Name = "Trabajo" };
        var config = new AppConfig { Workspaces = [personal, work], ActiveWorkspaceId = work.Id };
        string[] all = ["d/Juego.url", "d/Postman.lnk", "::{645FF040-5081-101B-9F08-00AA002F954E}"];

        Assert.True(DesktopIconLists.Shows(personal, "d/Juego.url")); // no list yet: everything

        DesktopIconLists.Set(work, "d/juego.URL", show: false, all); // case does not matter
        Assert.Equal(["d/Postman.lnk", "::{645FF040-5081-101B-9F08-00AA002F954E}"], work.DesktopIcons);
        Assert.Null(personal.DesktopIcons);

        Assert.True(DesktopIconLists.Created(config, "d/Nuevo.txt"));      // active workspace has a list
        Assert.False(DesktopIconLists.Created(config, "d/Nuevo.txt"));     // only once
        Assert.True(DesktopIconLists.Shows(work, "d/Nuevo.txt"));

        Assert.True(DesktopIconLists.Renamed(config, "d/Postman.lnk", "d/Postman 2.lnk"));
        Assert.True(DesktopIconLists.Shows(work, "d/Postman 2.lnk"));
        Assert.False(DesktopIconLists.Shows(work, "d/Postman.lnk"));
    }
}

public class AutoSwitchTests
{
    [Fact]
    public void Schedules_cover_working_hours_overnight_slots_and_chosen_days()
    {
        var work = new Workspace { Name = "Trabajo", ScheduleFrom = "9:00", ScheduleTo = "18:00", ScheduleDays = [DayOfWeek.Monday, DayOfWeek.Friday] };
        var night = new Workspace { Name = "Gaming", ScheduleFrom = "22:00", ScheduleTo = "02:00", ScheduleDays = [DayOfWeek.Friday] };
        var monday = new DateTime(2026, 10, 26); // a Monday

        Assert.True(AutoSwitch.InSchedule(work, monday.AddHours(9)));
        Assert.False(AutoSwitch.InSchedule(work, monday.AddHours(18))); // the end is not included
        Assert.False(AutoSwitch.InSchedule(work, monday.AddDays(1).AddHours(10))); // Tuesday is not chosen

        var friday = monday.AddDays(4);
        Assert.True(AutoSwitch.InSchedule(night, friday.AddHours(23)));
        Assert.True(AutoSwitch.InSchedule(night, friday.AddDays(1).AddHours(1))); // Saturday 01:00 still belongs to Friday
        Assert.False(AutoSwitch.InSchedule(night, friday.AddHours(1)));          // Friday 01:00 belongs to Thursday
        Assert.Same(night, AutoSwitch.Scheduled([work, night], friday.AddHours(23)));

        Assert.False(AutoSwitch.InSchedule(new Workspace { ScheduleFrom = "25:00", ScheduleTo = "10:00" }, monday.AddHours(9)));
        Assert.False(AutoSwitch.InSchedule(new Workspace(), monday));
    }

    [Fact]
    public void Apps_match_by_program_name_whatever_was_typed()
    {
        Assert.Equal(["steam", "discord", @"D:\Juegos"], AutoSwitch.ParseApps(" Steam.exe, discord ; steam\nD:\\Juegos\\"));
        Assert.Null(AutoSwitch.ParseApps(" , "));
        var gaming = new Workspace { Apps = ["steam", @"D:\SteamLibrary"] };
        var streaming = new Workspace { Apps = ["discord", "steam"] };
        Assert.Equal([gaming], AutoSwitch.ForApp([new Workspace(), gaming], @"C:\Program Files\Steam\STEAM.EXE"));
        Assert.Equal([gaming, streaming], AutoSwitch.ForApp([gaming, streaming], "steam")); // one program, several workspaces
        Assert.Empty(AutoSwitch.ForApp([gaming], "code"));
        Assert.Empty(AutoSwitch.ForApp([gaming], ""));

        // A folder rule takes anything running inside it, and only inside it.
        Assert.Equal([gaming], AutoSwitch.ForApp([gaming], "DMC5", @"d:\steamlibrary\steamapps\common\DMC5\DMC5.exe"));
        Assert.Empty(AutoSwitch.ForApp([gaming], "x", @"D:\SteamLibraryOld\x.exe"));
        Assert.Empty(AutoSwitch.ForApp([gaming], "DMC5")); // no path known

        // "!" leaves things out of a broader rule.
        Assert.Equal(["!wallpaper64", @"!D:\SteamLibrary\steamapps\common\VTube Studio"], AutoSwitch.ParseApps(@"!Wallpaper64.exe, !D:\SteamLibrary\steamapps\common\VTube Studio\"));
        var picky = new Workspace { Apps = [@"D:\SteamLibrary", "!wallpaper64", @"!D:\SteamLibrary\steamapps\common\VTube Studio"] };
        Assert.Empty(AutoSwitch.ForApp([picky], "wallpaper64", @"D:\SteamLibrary\steamapps\common\wallpaper_engine\wallpaper64.exe"));
        Assert.Empty(AutoSwitch.ForApp([picky], "VTube Studio", @"D:\SteamLibrary\steamapps\common\VTube Studio\VTube Studio.exe"));
        Assert.Equal([picky], AutoSwitch.ForApp([picky], "DMC5", @"D:\SteamLibrary\steamapps\common\DMC5\DMC5.exe"));
    }
}

public class TimerLogicTests
{
    private static readonly DateTime T0 = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_countdown_runs_pauses_resumes_and_finishes()
    {
        var s = new JsonObject { ["minutes"] = 10 };
        Assert.Equal(TimeSpan.FromMinutes(10), TimerLogic.Shown(s, T0));
        TimerLogic.Start(s, T0);
        Assert.True(TimerLogic.IsRunning(s));
        Assert.Equal(T0.AddMinutes(10), TimerLogic.EndsAt(s));

        TimerLogic.Pause(s, T0.AddMinutes(4));
        Assert.False(TimerLogic.IsRunning(s));
        Assert.Equal(TimeSpan.FromMinutes(6), TimerLogic.Shown(s, T0.AddHours(1))); // paused: time stands still

        TimerLogic.Start(s, T0.AddMinutes(30));
        Assert.Equal(T0.AddMinutes(36), TimerLogic.EndsAt(s));
        Assert.Equal(TimeSpan.Zero, TimerLogic.Shown(s, T0.AddMinutes(40)));
        Assert.Equal("Se acabó el tiempo.", TimerLogic.Finish(s));
        Assert.False(TimerLogic.IsRunning(s));
        Assert.Equal(TimeSpan.FromMinutes(10), TimerLogic.Shown(s, T0));
    }

    [Fact]
    public void Pomodoro_alternates_work_and_breaks_and_counts_rounds()
    {
        var s = new JsonObject { ["mode"] = TimerLogic.Pomodoro, ["work"] = 25, ["break"] = 5 };
        Assert.Equal(TimeSpan.FromMinutes(25), TimerLogic.Length(s));
        TimerLogic.Start(s, T0);
        Assert.Contains("descansar 5 min", TimerLogic.Finish(s));
        Assert.True(TimerLogic.OnBreak(s));
        Assert.Equal(1, TimerLogic.Rounds(s));
        Assert.Equal(TimeSpan.FromMinutes(5), TimerLogic.Shown(s, T0));
        TimerLogic.Finish(s);
        Assert.False(TimerLogic.OnBreak(s));
        Assert.Equal(1, TimerLogic.Rounds(s));
    }

    [Fact]
    public void The_stopwatch_adds_up_across_pauses()
    {
        var s = new JsonObject { ["mode"] = TimerLogic.Stopwatch };
        TimerLogic.Start(s, T0);
        Assert.Null(TimerLogic.EndsAt(s)); // never "finishes"
        TimerLogic.Pause(s, T0.AddSeconds(90));
        TimerLogic.Start(s, T0.AddHours(1));
        Assert.Equal(TimeSpan.FromSeconds(100), TimerLogic.Shown(s, T0.AddHours(1).AddSeconds(10)));
        Assert.Equal("01:40", TimerLogic.Format(TimeSpan.FromSeconds(100)));
        Assert.Equal("1:00:05", TimerLogic.Format(TimeSpan.FromSeconds(3605)));
    }
}

public class ShiftScheduleTests
{
    private static readonly DateTime Monday = new(2026, 10, 26);

    [Fact]
    public void Days_parse_as_a_shift_with_its_break_or_as_a_day_off()
    {
        var s = ShiftSchedule.Parse("9:00-18:00 comida 14:00 a 15:00");
        Assert.Equal(TimeSpan.FromHours(9), s!.Start);
        Assert.Equal(TimeSpan.FromHours(18), s.End);
        Assert.Equal(TimeSpan.FromHours(14), s.BreakStart);
        Assert.Equal(TimeSpan.FromHours(15), s.BreakEnd);
        Assert.Equal(TimeSpan.FromHours(9), s.Length);

        Assert.Null(ShiftSchedule.Parse("Descanso"));
        Assert.Null(ShiftSchedule.Parse(""));
        Assert.Null(ShiftSchedule.Parse("10:00-10:00"));
        Assert.True(ShiftSchedule.Parse("22:00-06:00")!.Overnight);
        Assert.Equal(TimeSpan.FromHours(8), ShiftSchedule.Parse("22.00 - 6.00")!.Length);
    }

    [Fact]
    public void The_status_follows_the_day_through_its_break()
    {
        var week = new JsonObject { ["mon"] = "09:00-18:00 comida 14:00-15:00", ["tue"] = "Descanso" };
        var before = ShiftSchedule.Status(week, Monday.AddHours(7));
        Assert.Equal(ShiftPhase.Before, before.Phase);
        Assert.Equal(Monday.AddHours(9), before.Next);

        var working = ShiftSchedule.Status(week, Monday.AddHours(10));
        Assert.Equal(ShiftPhase.Working, working.Phase);
        Assert.Equal(Monday.AddHours(14), working.Next); // next stop: the break

        var lunch = ShiftSchedule.Status(week, Monday.AddHours(14.5));
        Assert.Equal(ShiftPhase.OnBreak, lunch.Phase);
        Assert.Equal(Monday.AddHours(15), lunch.Next);

        Assert.Equal(Monday.AddHours(18), ShiftSchedule.Status(week, Monday.AddHours(16)).Next);
        Assert.Equal(ShiftPhase.Done, ShiftSchedule.Status(week, Monday.AddHours(19)).Phase);
        Assert.Equal(ShiftPhase.Off, ShiftSchedule.Status(week, Monday.AddDays(1).AddHours(10)).Phase);
    }

    [Fact]
    public void A_night_shift_still_counts_after_midnight()
    {
        var week = new JsonObject { ["sun"] = "22:00-06:00 cena 02:00-02:30" };
        var sundayNight = Monday.AddDays(6).AddHours(23);
        Assert.Equal(ShiftPhase.Working, ShiftSchedule.Status(week, sundayNight).Phase);

        var mondayEarly = Monday.AddDays(7).AddHours(2.25); // the next Monday, 02:15
        var s = ShiftSchedule.Status(week, mondayEarly);
        Assert.Equal(ShiftPhase.OnBreak, s.Phase);
        Assert.Equal(Monday.AddDays(7).AddHours(2.5), s.Next);
        Assert.Equal(ShiftPhase.Off, ShiftSchedule.Status(week, Monday.AddDays(7).AddHours(7)).Phase);
        Assert.Equal("2 h 15 min", ShiftSchedule.Span(TimeSpan.FromMinutes(134.2)));
    }
}

public class ScheduleEventTests
{
    [Fact]
    public void Activities_come_weekly_or_once_and_old_ones_go_away()
    {
        var monday = new DateTime(2026, 10, 26);
        var s = new JsonObject();
        ScheduleEvent.WriteAll(s,
        [
            new("a", "Coaching", TimeSpan.FromHours(16.5), null, "mon"),
            new("b", "Junta", TimeSpan.FromHours(10), monday, null),
            new("c", "Llevar laptop", null, monday, null),
            new("d", "Dentista", null, monday.AddDays(-3), null),
        ]);

        Assert.Equal(["Junta", "Coaching", "Llevar laptop"], ShiftSchedule.EventsOn(s, monday).Select(e => e.Text));
        Assert.Equal(["Coaching"], ShiftSchedule.EventsOn(s, monday.AddDays(7)).Select(e => e.Text)); // weekly repeats
        Assert.Equal("16:30  Coaching", ShiftSchedule.EventsOn(s, monday.AddDays(7))[0].Label);

        Assert.True(ShiftSchedule.DropPast(s, monday));
        Assert.DoesNotContain(ScheduleEvent.ReadAll(s), e => e.Text == "Dentista");
        Assert.False(ShiftSchedule.DropPast(s, monday));
        Assert.Equal(new DateTime(2026, 10, 26), ShiftSchedule.WeekStart(new DateTime(2026, 11, 1))); // Sunday → its Monday
    }
}
