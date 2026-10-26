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
