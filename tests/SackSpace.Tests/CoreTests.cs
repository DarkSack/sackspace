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
    [Theory]
    [InlineData("1100", "11:00")]
    [InlineData("930", "09:30")]
    [InlineData("9", "09:00")]
    [InlineData("9:05", "09:05")]
    [InlineData(" 23:59 ", "23:59")]
    [InlineData("2400", null)]
    [InlineData("1260", null)]
    [InlineData("12345", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    public void Typed_times_are_tidied(string typed, string? expected) => Assert.Equal(expected, AutoSwitch.NormalizeTime(typed));

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

    [Theory]
    [InlineData("10:00-19:00 Trabajo", "Trabajo")]
    [InlineData("Turno 10:00-19:00", "Turno")]
    [InlineData("10:00-20:00 Trabajo, comida 16:00-17:00", "Trabajo")]
    [InlineData("09:00-18:00 comida 14:00-15:00", "")]
    [InlineData("22:00-06:00", "")]
    public void A_shift_takes_its_name_from_the_words_around_the_hours(string text, string title) =>
        Assert.Equal(title, ShiftSchedule.Parse(text)!.Title);

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

    [Fact]
    public void Activities_keep_their_end_and_overlapping_ones_share_the_width()
    {
        var s = new JsonObject();
        ScheduleEvent.WriteAll(s, [new("j", "Junta", TimeSpan.FromHours(12), null, "mon", TimeSpan.FromHours(15))]);
        var junta = ScheduleEvent.ReadAll(s)[0];
        Assert.Equal(TimeSpan.FromHours(15), junta.End);
        Assert.Equal("12:00–15:00  Junta", junta.Label);
        Assert.Equal((720, 900), junta.Span());
        Assert.Equal((600, 660), new ScheduleEvent("x", "Sin fin", TimeSpan.FromHours(10), null, "mon").Span());

        // 12–15 and 13–14 overlap (two lanes), 14–16 reuses lane 1, 17–18 stands alone.
        var lanes = ScheduleEvent.Lanes([(720, 900), (780, 840), (840, 960), (1020, 1080)]);
        Assert.Equal([(0, 2), (1, 2), (1, 2), (0, 1)], lanes);
    }
}

public class AiUsageTests
{
    [Fact]
    public void Accounts_take_a_name_or_the_folder_name_and_expand_variables()
    {
        var accounts = AiUsageParser.ParseAccounts(@"Principal=%HOME%\.claude; D:\Otra\.claude ;; Trabajo = ""E:\x""",
            s => s.Replace("%HOME%", @"C:\Users\Sack"));
        Assert.Equal(
            [new AiAccount("Principal", @"C:\Users\Sack\.claude"), new AiAccount(".claude", @"D:\Otra\.claude"), new AiAccount("Trabajo", @"E:\x")],
            accounts);
        Assert.Empty(AiUsageParser.ParseAccounts("  "));
    }

    [Fact]
    public void Claude_reads_both_windows_and_a_missing_one_stays_empty()
    {
        var json = JsonNode.Parse("""{"five_hour":{"utilization":35.0,"resets_at":"2026-11-05T00:49:59.62-06:00"},"seven_day":{"utilization":81,"resets_at":null},"seven_day_opus":null}""")!;
        var r = AiUsageParser.ParseClaude(json, "max", DateTimeOffset.UnixEpoch);
        Assert.Equal(35, r.FiveHours!.Percent);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 6, 49, 59, 620, TimeSpan.Zero), r.FiveHours.ResetsAt);
        Assert.Equal(new UsageWindow(81, null), r.Week);
        Assert.Equal("max", r.Plan);
        Assert.Null(AiUsageParser.ParseClaude(JsonNode.Parse("{}")!, null, DateTimeOffset.UnixEpoch).FiveHours);
    }

    [Fact]
    public void Codex_takes_the_newest_line_with_limits_and_skips_cut_or_empty_ones()
    {
        string[] lines =
        [
            """{"timestamp":"2026-11-04T10:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":50,"window_minutes":300,"resets_at":1793871219}}}}""",
            """{"timestamp":"2026-11-04T11:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"secondary":{"used_percent":98.0,"window_minutes":10080,"resets_at":1793999395},"primary":{"used_percent":1.0,"window_minutes":300,"resets_at":1793871219},"plan_type":"plus"}}}""",
            """{"timestamp":"2026-11-04T12:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":null}}""",
            "\"rate_limits\":{\"primary\":{\"used_pe", // a cut first line of a tail read
            """{"type":"response_item"}""",
        ];
        var r = AiUsageParser.ParseCodexLog(lines, DateTimeOffset.UnixEpoch)!;
        Assert.Equal(new UsageWindow(1, DateTimeOffset.FromUnixTimeSeconds(1793871219)), r.FiveHours);
        Assert.Equal(98, r.Week!.Percent);
        Assert.Equal("plus", r.Plan);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero), r.ReadAt);
        Assert.Null(AiUsageParser.ParseCodexLog(["""{"type":"session_meta"}"""], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Claude_Desktop_history_takes_the_newest_sample_with_numbers()
    {
        var json = JsonNode.Parse("""{"version":2,"samples":[{"t":1793058262787,"org":"a","u":{"fh":100,"sd":4}},{"t":1793236801324,"org":"a","u":{"fh":0,"sd":91,"xu":1}},{"t":1793300000000,"org":"a","u":{}}]}""")!;
        var r = AiUsageParser.ParseClaudeDesktopHistory(json)!;
        Assert.Equal(new UsageWindow(0, null), r.FiveHours);
        Assert.Equal(new UsageWindow(91, null), r.Week);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793236801324), r.ReadAt);
        Assert.Null(AiUsageParser.ParseClaudeDesktopHistory(JsonNode.Parse("""{"samples":[]}""")!));
    }

    [Fact]
    public void A_window_whose_reset_has_passed_reads_as_empty()
    {
        var w = new UsageWindow(90, new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(w, w.At(new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new UsageWindow(0, null), w.At(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)));
    }
}

public class ShortcutPresenceTests
{
    private static InstallFacts Facts(string[]? steam = null, string[]? epic = null, string[]? menu = null, string[]? files = null, bool driveReady = true) => new(
        menu?.ToHashSet(StringComparer.OrdinalIgnoreCase),
        steam?.ToHashSet(),
        epic?.ToHashSet(StringComparer.OrdinalIgnoreCase),
        p => files?.Contains(p, StringComparer.OrdinalIgnoreCase) == true,
        _ => driveReady);

    [Fact]
    public void Steam_games_are_checked_against_the_manifests_not_the_start_menu()
    {
        const string path = @"shell:AppsFolder\steam://rungameid/2357570";
        var menu = new[] { "steam://rungameid/2357570" }; // Steam leaves this behind on uninstall
        Assert.Equal(Presence.Gone, ShortcutPresence.Check(path, Facts(steam: ["289070"], menu: menu)));
        Assert.Equal(Presence.Here, ShortcutPresence.Check(path, Facts(steam: ["2357570"])));
        Assert.Equal(Presence.Unknown, ShortcutPresence.Check(path, Facts(steam: null)));
        Assert.Equal("440", ShortcutPresence.SteamGameId("steam://run/440"));
    }

    [Fact]
    public void Epic_links_use_the_app_name()
    {
        Assert.Equal("Potoo", ShortcutPresence.EpicAppName("com.epicgames.launcher://apps/Potoo?action=launch&silent=true"));
        Assert.Equal("Fortnite", ShortcutPresence.EpicAppName("com.epicgames.launcher://apps/fn%3A4fe75bbc%3AFortnite?action=launch"));
        var path = "com.epicgames.launcher://apps/Heather?action=launch&silent=true";
        Assert.Equal(Presence.Gone, ShortcutPresence.Check(path, Facts(epic: ["Potoo"])));
        Assert.Equal(Presence.Unknown, ShortcutPresence.Check(path, Facts()));
    }

    [Fact]
    public void Start_menu_apps_files_and_unplugged_drives()
    {
        Assert.Equal(Presence.Here, ShortcutPresence.Check(@"shell:AppsFolder\Microsoft.VisualStudioCode", Facts(menu: ["Microsoft.VisualStudioCode"])));
        Assert.Equal(Presence.Gone, ShortcutPresence.Check(@"shell:AppsFolder\Kiro", Facts(menu: ["Microsoft.VisualStudioCode"])));
        Assert.Equal(Presence.Unknown, ShortcutPresence.Check(@"shell:AppsFolder\Kiro", Facts(menu: []))); // the list could not be read
        Assert.Equal(Presence.Here, ShortcutPresence.Check(@"D:\JUEGOS\PokeMMO\PokeMMO.exe", Facts(files: [@"D:\JUEGOS\PokeMMO\PokeMMO.exe"])));
        Assert.Equal(Presence.Gone, ShortcutPresence.Check(@"D:\JUEGOS\Hytale\hytale.exe", Facts()));
        Assert.Equal(Presence.Unknown, ShortcutPresence.Check(@"F:\Juego\juego.exe", Facts(driveReady: false)));
        Assert.Equal(Presence.Unknown, ShortcutPresence.Check("shell:Downloads", Facts()));
        Assert.Equal(Presence.Unknown, ShortcutPresence.Check("battlenet://Pro", Facts()));
    }
}

public class GameCatalogTests
{
    private const string Acf = """
        "AppState"
        {
        	"appid"		"289070"
        	"name"		"Sid Meier's Civilization VI"
        	"installdir"		"Sid Meier's Civilization VI"
        	"LastPlayed"		"1791589367"
        	"SizeOnDisk"		"27533885031"
        	"InstalledDepots"
        	{
        		"289071"
        		{
        			"size"		"19546523624"
        		}
        	}
        }
        """;

    private static InstalledGame Game(string key, string name, long bytes = 1, DateTimeOffset? played = null) => new(key, name, "x", bytes, played, "", null);

    [Fact]
    public void Steam_manifest_gives_name_size_last_played_and_the_start_menu_link()
    {
        var g = GameCatalog.ParseSteam(Acf, @"E:\SteamLibrary", link => link == "steam://rungameid/289070")!;
        Assert.Equal("steam:289070", g.Key);
        Assert.Equal("Sid Meier's Civilization VI", g.Name);
        Assert.Equal(27533885031, g.Bytes); // the depot's own "size" further down is not the game's
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791589367), g.LastPlayed);
        Assert.Equal(@"shell:AppsFolder\steam://rungameid/289070", g.LaunchPath);
        Assert.Equal(@"E:\SteamLibrary\steamapps\common\Sid Meier's Civilization VI", g.Folder);
        Assert.Equal("steam://rungameid/289070", GameCatalog.ParseSteam(Acf, "E:", _ => false)!.LaunchPath);
        Assert.Equal("steam:289070", GameCatalog.KeyOf(g.LaunchPath));
    }

    [Fact]
    public void Epic_engines_and_add_ons_are_not_games()
    {
        var game = JsonNode.Parse("""{"AppName":"Potoo","DisplayName":"Overcooked! 2","MainGameAppName":"Potoo","AppCategories":["public","games","applications"],"InstallSize":5000}""")!;
        var dlc = JsonNode.Parse("""{"AppName":"94bc","DisplayName":"Contenido de LEGO Fortnite","MainGameAppName":"Fortnite","AppCategories":["addons"]}""")!;
        var engine = JsonNode.Parse("""{"AppName":"UE_5.3","DisplayName":"Unreal Engine","MainGameAppName":"","AppCategories":["engines/ue5","engines"]}""")!;
        Assert.Equal("epic:potoo", GameCatalog.ParseEpic(game)!.Key);
        Assert.Equal("epic:potoo", GameCatalog.KeyOf("com.epicgames.launcher://apps/Potoo?action=launch&silent=true"));
        Assert.Null(GameCatalog.ParseEpic(dlc));
        Assert.Null(GameCatalog.ParseEpic(engine));
    }

    [Fact]
    public void A_group_adds_only_games_installed_after_it_first_looked()
    {
        var installed = new[] { Game("steam:1", "Palworld"), Game("steam:2", "Wallpaper Engine"), Game("steam:3", "Hades") };
        var (first, seen) = GameCatalog.NewGames(installed, null, new HashSet<string>());
        Assert.Empty(first); // a curated group stays as it is
        Assert.Equal(3, seen.Count);

        var later = installed.Append(Game("steam:4", "Hollow Knight")).Append(Game("steam:5", "Some Game Dedicated Server")).ToList();
        var (add, seen2) = GameCatalog.NewGames(later, seen, new HashSet<string>());
        Assert.Equal(["Hollow Knight"], add.Select(g => g.Name));
        Assert.Equal(5, seen2.Count);
        Assert.Empty(GameCatalog.NewGames(later, seen2, new HashSet<string>()).Add); // removed by hand later: never back
    }

    [Fact]
    public void Forgotten_games_are_big_old_and_were_played_at_least_once()
    {
        var now = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
        var games = new[]
        {
            Game("steam:1", "Conan Exiles", 60, now.AddDays(-200)),
            Game("steam:2", "Marvel Rivals", 100, now.AddDays(-2)),
            Game("steam:3", "Recién instalado", 90),
            Game("steam:4", "Pequeño", 1, now.AddDays(-400)),
            Game("steam:5", "Grande", 80, now.AddDays(-100)),
        };
        Assert.Equal(["Grande", "Conan Exiles"], GameCatalog.Forgotten(games, now, TimeSpan.FromDays(90), 10).Select(g => g.Name));
    }
}

public class StorageUsageTests
{
    [Fact]
    public void Nested_roots_are_skipped_by_their_parent_and_later_duplicates_win()
    {
        var plan = StorageUsage.Plan(
        [
            new(StorageUsage.Programs, @"C:\Program Files (x86)"),
            new(StorageUsage.Games, @"C:\Program Files (x86)\Steam\steamapps\"),
            new(StorageUsage.Games, @"D:\JUEGOS"),
            new("Desarrollo", @"d:\juegos"),
            new(StorageUsage.Programs, @"C:\Program Files"), // a prefix of "Program Files (x86)" but not its parent
        ]);
        Assert.Equal(4, plan.Count);
        var x86 = plan.Single(p => p.Path == @"C:\Program Files (x86)");
        Assert.Equal([@"C:\Program Files (x86)\Steam\steamapps"], x86.Skip);
        Assert.Empty(plan.Single(p => p.Path == @"C:\Program Files").Skip);
        Assert.Equal("Desarrollo", plan.Single(p => p.Path.Equals(@"D:\JUEGOS", StringComparison.OrdinalIgnoreCase)).Category);
    }

    [Fact]
    public void Summary_adds_up_categories_and_leaves_the_rest_hidden()
    {
        const long gb = 1024L * 1024 * 1024;
        var parts = StorageUsage.Summarize(10 * gb, [(StorageUsage.Games, 3 * gb), (StorageUsage.System, gb), (StorageUsage.Games, 2 * gb), (StorageUsage.Trash, 0)]);
        Assert.Equal([(StorageUsage.Games, 5 * gb), (StorageUsage.System, gb), (StorageUsage.Hidden, 4 * gb)], parts);
        Assert.DoesNotContain(StorageUsage.Summarize(gb, [(StorageUsage.System, 2 * gb)]), p => p.Category == StorageUsage.Hidden); // hard links can pass the used space
        Assert.DoesNotContain(StorageUsage.Summarize(gb + 1000, [(StorageUsage.System, gb)]), p => p.Category == StorageUsage.Hidden); // crumbs are not worth a segment
    }

    [Fact]
    public void Folder_names_suggest_a_category()
    {
        Assert.Equal(StorageUsage.Games, StorageUsage.Classify("JUEGOS"));
        Assert.Equal(StorageUsage.Dev, StorageUsage.Classify(".gradle"));
        Assert.Equal(StorageUsage.Dev, StorageUsage.Classify("Dev"));
        Assert.Equal(StorageUsage.Media, StorageUsage.Classify("Grabaciones"));
        Assert.Equal(StorageUsage.Games, StorageUsage.Classify("Emulacion"));
        Assert.Equal(StorageUsage.Dev, StorageUsage.Classify("VulkanSDK"));
        Assert.Equal(StorageUsage.Dev, StorageUsage.Classify(".claude"));
        Assert.Null(StorageUsage.Classify("VoiceStudio"));
    }

    [Fact]
    public void A_child_holding_most_of_the_root_is_opened_one_level()
    {
        var children = new Dictionary<string, long> { ["common"] = 900, ["workshop"] = 80, ["shadercache"] = 20 };
        var inner = new Dictionary<string, IReadOnlyDictionary<string, long>>
        {
            ["common"] = new Dictionary<string, long> { ["Marvel Rivals"] = 600, ["Palworld"] = 290, [""] = 10 },
        };
        var items = StorageUsage.TopItems(@"D:\SteamLibrary\steamapps", 1000, children, inner);
        Assert.Equal(@"D:\SteamLibrary\steamapps\common\Marvel Rivals", items[0].Path);
        Assert.Equal([600L, 290L, 80L, 20L, 10L], items.Select(i => i.Bytes));
        Assert.Equal(@"D:\SteamLibrary\steamapps\common", items[^1].Path); // loose files of common

        var spread = StorageUsage.TopItems(@"C:\Windows", 100, new Dictionary<string, long> { ["System32"] = 50, ["WinSxS"] = 50 }, inner, take: 1);
        Assert.Single(spread);
    }
}

public class ObsNumbersTests
{
    [Fact]
    public void Bitrate_dropped_frames_and_clock()
    {
        var t0 = new DateTime(2026, 11, 6, 12, 0, 0);
        Assert.Equal(6.0, ObsNumbers.Megabits(0, t0, 1_500_000, t0.AddSeconds(2)));
        Assert.Null(ObsNumbers.Megabits(5_000, t0, 10, t0.AddSeconds(2))); // the stream restarted
        Assert.Equal(0.5, ObsNumbers.Dropped(5, 1000));
        Assert.Equal(0, ObsNumbers.Dropped(5, 0));
        Assert.Equal("1:23:45", ObsNumbers.Clock("01:23:45.678"));
        Assert.Equal("4:05", ObsNumbers.Clock("00:04:05.000"));
        Assert.Equal("", ObsNumbers.Clock(null));
    }

    [Fact]
    public void Auth_matches_the_obs_websocket_example()
    {
        // obs-websocket 5 protocol docs: password "supersecretpassword", salt and challenge from its example.
        Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=",
            ObsClient.Auth("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="));
    }
}

public class UpdatesTests
{
    private static JsonNode Release(string tag, bool pre = false, string url = "https://github.com/DarkSack/sackspace/releases/download/v0.4.0/SackSpace-Setup-0.4.0.exe") =>
        JsonNode.Parse($$"""
            {"tag_name":"{{tag}}","draft":false,"prerelease":{{(pre ? "true" : "false")}},"html_url":"https://github.com/DarkSack/sackspace/releases/tag/{{tag}}",
             "assets":[{"name":"notas.txt","browser_download_url":"https://github.com/DarkSack/sackspace/x"},
                       {"name":"SackSpace-Setup-0.4.0.exe","browser_download_url":"{{url}}","digest":"sha256:ABCDEF"}]}
            """)!;

    [Fact]
    public void Only_a_newer_full_release_with_its_installer_is_offered()
    {
        var u = Updates.Newer(Release("v0.4.0"), new Version(0, 3, 0, 0))!;
        Assert.Equal(new Version(0, 4, 0), u.Version);
        Assert.Equal("SackSpace-Setup-0.4.0.exe", u.InstallerName);
        Assert.Equal("abcdef", u.Sha256);
        Assert.Null(Updates.Newer(Release("v0.4.0"), new Version(0, 4, 0, 0))); // the same, with the build number the exe carries
        Assert.Null(Updates.Newer(Release("v0.3.9"), new Version(0, 4, 0)));
        Assert.Null(Updates.Newer(Release("v0.5.0", pre: true), new Version(0, 4, 0)));
        Assert.Null(Updates.Newer(Release("v0.5.0", url: "https://evil.example/SackSpace-Setup-0.5.0.exe"), new Version(0, 4, 0)));
        Assert.Null(Updates.ParseTag("nightly"));
    }
}

public class PlayTimeTests
{
    [Fact]
    public void Adds_per_day_and_sums_periods()
    {
        var log = new JsonObject();
        var thu = new DateTime(2026, 11, 5, 21, 0, 0);
        PlayTime.Add(log, "steam:730", "CS2", thu, TimeSpan.FromMinutes(30));
        PlayTime.Add(log, "steam:730", "CS2", thu, TimeSpan.FromMinutes(30));
        PlayTime.Add(log, "epic:fortnite", "Fortnite", thu.AddDays(1), TimeSpan.FromHours(2));
        PlayTime.Add(log, "epic:fortnite", "Fortnite", new DateTime(2026, 10, 30), TimeSpan.FromHours(1));
        PlayTime.Add(log, "x", "nada", thu, TimeSpan.Zero);

        var week = PlayTime.Totals(log, PlayTime.WeekStart(thu));
        Assert.Equal(new DateTime(2026, 11, 2), PlayTime.WeekStart(thu)); // Monday
        Assert.Equal(new DateTime(2026, 11, 2), PlayTime.WeekStart(new DateTime(2026, 11, 8))); // Sunday belongs to that week
        Assert.Equal(["Fortnite", "CS2"], week.Select(t => t.Name));
        Assert.Equal(TimeSpan.FromHours(2), week[0].Time);
        Assert.Equal(TimeSpan.FromHours(3), PlayTime.Totals(log, null)[0].Time);
        Assert.Equal("Fortnite", Assert.Single(PlayTime.Totals(log, thu.Date.AddDays(1))).Name); // from Friday on
        Assert.Null(log["x"]);
    }

    [Fact]
    public void Trim_keeps_the_total()
    {
        var log = new JsonObject();
        var now = new DateTime(2026, 11, 6);
        PlayTime.Add(log, "k", "Juego", now.AddDays(-PlayTime.KeepDays - 10), TimeSpan.FromHours(5));
        PlayTime.Add(log, "k", "Juego", now, TimeSpan.FromHours(1));
        PlayTime.Trim(log, now);
        Assert.Single(log["k"]!["days"]!.AsObject());
        Assert.Equal(TimeSpan.FromHours(6), PlayTime.Totals(log, null)[0].Time);
        Assert.Equal(TimeSpan.FromHours(1), PlayTime.Totals(log, now.AddDays(-30))[0].Time);
    }

    [Fact]
    public void Formats_hours_and_minutes()
    {
        Assert.Equal("menos de 1 min", PlayTime.Format(TimeSpan.FromSeconds(30)));
        Assert.Equal("45 min", PlayTime.Format(TimeSpan.FromMinutes(45)));
        Assert.Equal("2 h 05 min", PlayTime.Format(TimeSpan.FromMinutes(125)));
    }
}

public class TwitchTests
{
    [Fact]
    public void Combines_live_channel_and_new_followers()
    {
        var user = JsonNode.Parse("""{"id":"1","login":"sackitogamer","display_name":"SackitoGamer"}""")!;
        var channel = JsonNode.Parse("""{"title":"viejo","game_name":"Just Chatting"}""")!;
        var stream = JsonNode.Parse("""{"title":"Fortnite con subs","game_name":"Fortnite","viewer_count":42,"started_at":"2026-11-06T20:00:00Z"}""")!;
        var followers = JsonNode.Parse("""{"total":1234,"data":[{"user_name":"Ana","followed_at":"2026-11-06T20:30:00Z"},{"user_name":"Beto","followed_at":"2026-11-06T10:00:00Z"}]}""")!;
        var ch = Twitch.Combine(user, channel, stream, followers);
        Assert.True(ch.Live);
        Assert.Equal(("Fortnite con subs", "Fortnite", 42, 1234), (ch.Title, ch.Category, ch.Viewers, ch.Followers));
        Assert.Equal(1, ch.NewFollowers(DateTimeOffset.Parse("2026-11-06T21:00:00Z"))); // only Ana came during the stream

        var offline = Twitch.Combine(user, channel, null, followers);
        Assert.False(offline.Live);
        Assert.Equal("viejo", offline.Title);
        Assert.Equal(2, offline.NewFollowers(DateTimeOffset.Parse("2026-11-06T21:00:00Z"))); // last 24 h
    }

    [Fact]
    public void Picks_exact_category_else_first()
    {
        var search = JsonNode.Parse("""{"data":[{"id":"1","name":"Fortnite Festival"},{"id":"2","name":"Fortnite"}]}""");
        Assert.Equal(("2", "Fortnite"), Twitch.PickCategory(search, " fortnite "));
        Assert.Equal(("1", "Fortnite Festival"), Twitch.PickCategory(search, "fort"));
        Assert.Null(Twitch.PickCategory(JsonNode.Parse("""{"data":[]}"""), "x"));
    }

    [Fact]
    public void Parses_device_flow()
    {
        var now = DateTimeOffset.Parse("2026-11-06T00:00:00Z");
        var code = Twitch.ParseDevice(JsonNode.Parse("""{"device_code":"d","user_code":"ABCD1234","verification_uri":"https://www.twitch.tv/activate?device-code=ABCD1234","expires_in":1800,"interval":5}""")!, now);
        Assert.Equal(("ABCD1234", TimeSpan.FromSeconds(5), now.AddMinutes(30)), (code.UserCode, code.Interval, code.ExpiresAt));
        Assert.True(Twitch.IsPending(JsonNode.Parse("""{"status":400,"message":"authorization_pending"}""")));
        Assert.False(Twitch.IsPending(JsonNode.Parse("""{"status":400,"message":"invalid device code"}""")));
    }
}

public class ClipHistoryTests
{
    [Fact]
    public void Newest_first_without_repeats_and_capped()
    {
        var list = new List<ClipEntry>();
        var t = DateTime.Now;
        ClipHistory.Add(list, new("uno", false, t), 3);
        ClipHistory.Add(list, new("dos", false, t), 3);
        ClipHistory.Add(list, new("uno", false, t), 3); // moves up
        ClipHistory.Add(list, new("uno", true, t), 3);  // a file named "uno" is another thing
        ClipHistory.Add(list, new("   ", false, t), 3);
        ClipHistory.Add(list, new(new string('x', ClipHistory.MaxTextLength + 1), false, t), 3);
        Assert.Equal([("uno", true), ("uno", false), ("dos", false)], list.Select(e => (e.Text, e.Files)));
        ClipHistory.Add(list, new("tres", false, t), 3);
        Assert.Equal(3, list.Count);
        Assert.Equal("a b…", new ClipEntry("a\n\n b   cdef", false, t).Preview(4));
    }
}

public class FileSearchTests
{
    private static FileMatch? S(string file, string query, bool similar = true, string folder = @"c:\users\sack\documents")
    {
        var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
        return FileSearch.Score(FileSearch.Normalize(file), FileSearch.Normalize(folder), ext, FileSearch.Parse(query), similar);
    }

    [Fact]
    public void Normalizes_accents_case_and_separators()
    {
        Assert.Equal("cancion final 2 mp3", FileSearch.Normalize("Canción_Final (2).MP3"));
        Assert.Equal("informe v2", FileSearch.Normalize("--Informe--v2--"));
    }

    [Fact]
    public void Exact_matches_rank_by_where_the_word_is()
    {
        var start = S("Factura enero.pdf", "factura")!.Value;
        var word = S("Enero factura.pdf", "factura")!.Value;
        var inside = S("Prefactura.pdf", "factura")!.Value;
        Assert.False(start.Similar);
        Assert.True(start.Score > word.Score && word.Score > inside.Score);
        Assert.NotNull(S("Canción.mp3", "cancion"));
        Assert.Null(S("Canción.mp3", "perro"));
    }

    [Fact]
    public void Typos_and_missing_letters_match_as_similar()
    {
        Assert.True(S("Presupuesto 2026.xlsx", "presupusto")!.Value.Similar); // missing letter
        Assert.True(S("Presupuesto 2026.xlsx", "prseupuesto")!.Value.Similar); // swapped letters
        Assert.True(S("Facturas.pdf", "factur")!.Value.Score > 0);
        Assert.Null(S("Presupuesto 2026.xlsx", "presupusto", similar: false)); // exact only
        Assert.Null(S("Presupuesto.xlsx", "zzz"));
    }

    [Fact]
    public void Filters_and_phrases()
    {
        Assert.NotNull(S("notas reunion.docx", "notas ext:docx"));
        Assert.Null(S("notas reunion.docx", "notas *.pdf"));
        Assert.NotNull(S("Mi informe final.docx", "\"informe final\""));
        Assert.Null(S("Informe y final.docx", "\"informe final\""));
        Assert.NotNull(S("a.txt", "en:documents"));
        Assert.Null(S("a.txt", "en:dev"));
        Assert.NotNull(S("leeme.txt", "sack")); // the word is in the folder path
        var q = FileSearch.Parse("ext:PDF,.docx en:Mis_Cosas hola");
        Assert.Equal(["pdf", "docx"], q.Extensions.Order().Reverse());
        Assert.Equal(["mis cosas"], q.InFolders);
        Assert.Equal(["hola"], q.Words);
        Assert.Equal(1, FileSearch.Distance("ab", "ba"));
    }
}

public class FileSearchFilterTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0);

    [Fact]
    public void Parses_kind_size_and_date()
    {
        var q = FileSearch.Parse("tipo:fotos tam:>10mb fecha:semana viaje", Now);
        Assert.Contains("jpg", q.Extensions);
        Assert.Equal(10L << 20, q.MinBytes);
        Assert.Equal(Now.Date.AddDays(-7), q.From);
        Assert.Equal(["viaje"], q.Words);
        Assert.True(q.Passes(20L << 20, Now));
        Assert.False(q.Passes(1L << 20, Now));
        Assert.False(q.Passes(20L << 20, Now.AddDays(-30)));

        var year = FileSearch.Parse("fecha:2025 size:<1.5gb", Now);
        Assert.Equal((new DateTime(2025, 1, 1), new DateTime(2026, 1, 1)), (year.From!.Value, year.Until!.Value));
        Assert.Equal((long)(1.5 * (1L << 30)), year.MaxBytes);
        Assert.False(year.IsEmpty);
        Assert.Equal(["fecha", "rara"], FileSearch.Parse("fecha:rara", Now).Words); // not a filter: plain words
    }

    [Fact]
    public void Build_output_ranks_after_documents_and_far_letters_do_not_match()
    {
        static FileMatch? S(string file, string query) => FileSearch.Score(FileSearch.Normalize(file), @"c:\x",
            Path.GetExtension(file).TrimStart('.').ToLowerInvariant(), FileSearch.Parse(query), true, FileSearch.Mask(FileSearch.Normalize(file)));
        Assert.True(S("Informe.pdf", "informe")!.Value.Score > S("Informe.class", "informe")!.Value.Score);
        Assert.Null(S("Cancellation.java", "cancion"));
        Assert.Null(S("plasma_cannon.png", "cancion"));
        Assert.NotNull(S("Presupuesto.xlsx", "prsupusto"));
    }
}

public class LocTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void TablesLoadAndKeepPlaceholders(string code)
    {
        var table = Loc.Read(code);
        Assert.NotNull(table);
        Assert.NotEmpty(table);
        foreach (var (spanish, text) in table)
        {
            // A translation with other placeholders would throw (or lose a value) in string.Format.
            var args = Enumerable.Range(0, 4).Select(i => (object)i).ToArray();
            Assert.False(string.IsNullOrWhiteSpace(text), spanish);
            var expected = System.Text.RegularExpressions.Regex.Matches(spanish, @"\{\d").Select(m => m.Value).Order();
            var actual = System.Text.RegularExpressions.Regex.Matches(text, @"\{\d").Select(m => m.Value).Order();
            Assert.Equal(expected, actual);
            _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
        }
    }

    [Fact]
    public void ResolvePicksWindowsLanguageOrEnglish()
    {
        Assert.Equal("pt", Loc.Resolve("auto", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Equal("es", Loc.Resolve("auto", System.Globalization.CultureInfo.GetCultureInfo("es-MX")));
        Assert.Equal("en", Loc.Resolve("auto", System.Globalization.CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("en", Loc.Resolve("en", System.Globalization.CultureInfo.GetCultureInfo("es-ES")));
    }
}
