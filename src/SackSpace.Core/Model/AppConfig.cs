using System.Text.Json.Nodes;

namespace SackSpace.Core.Model;

/// <summary>
/// Everything the user sets up, saved as one JSON file (see ConfigStore).
/// Coordinates are DIPs relative to the top-left of a monitor's work area.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Bumped when the shape changes in a way old files must be migrated from.</summary>
    public int SchemaVersion { get; set; } = CurrentSchema;

    public const int CurrentSchema = 1;

    public GeneralSettings General { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public List<Workspace> Workspaces { get; set; } = new();
    public string? ActiveWorkspaceId { get; set; }

    /// <summary>Action id (see HotkeyActions) → gesture such as "Ctrl+Alt+Space". Empty = off.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public Workspace? ActiveWorkspace =>
        Workspaces.FirstOrDefault(w => w.Id == ActiveWorkspaceId) ?? Workspaces.FirstOrDefault();
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    /// <summary>Off: only the primary monitor gets panels.</summary>
    public bool UseAllMonitors { get; set; } = true;
    public bool ShowWorkspaceBar { get; set; } = true;
    public bool Animations { get; set; } = true;
    /// <summary>Stop the clocks and meters while a game or video runs full screen.</summary>
    public bool PauseInFullscreen { get; set; } = true;
    /// <summary>SackSpace draws the desktop icons (Windows' own are hidden) and each workspace shows its own.</summary>
    public bool DesktopIcons { get; set; }
}

public enum AccentMode
{
    /// <summary>Each workspace has its own colour.</summary>
    Workspace,
    /// <summary>Taken from the wallpaper, like Android's Material You.</summary>
    Wallpaper,
    /// <summary>The theme's own accent.</summary>
    Theme,
    Custom,
}

public sealed class AppearanceSettings
{
    public string ThemeId { get; set; } = "cristal-oscuro";
    public AccentMode AccentMode { get; set; } = AccentMode.Workspace;
    public string CustomAccent { get; set; } = "#7C9CFF";
    /// <summary>Overrides of the theme; null = the theme's value.</summary>
    public double? GlassOpacity { get; set; }
    public double? BlurRadius { get; set; }
    public double? CornerRadius { get; set; }
    /// <summary>Picture blurred behind the glass instead of each screen's wallpaper; null = automatic.</summary>
    public string? CustomWallpaper { get; set; }
    /// <summary>The panels let the live desktop show through, unblurred, instead of a blurred copy.</summary>
    public bool ClearGlass { get; set; }
}

public sealed class Workspace
{
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = "Espacio";
    /// <summary>Segoe Fluent Icons glyph, as text.</summary>
    public string Icon { get; set; } = Glyphs.Home;
    public string Accent { get; set; } = "#7C9CFF";
    public List<PanelState> Panels { get; set; } = new();
    /// <summary>Desktop icons shown here (paths, or "::{CLSID}" for Recycle Bin and the like); null = all of them.</summary>
    public List<string>? DesktopIcons { get; set; }

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}

/// <summary>One widget placed on a workspace.</summary>
public sealed class PanelState
{
    public string Id { get; set; } = Workspace.NewId();
    /// <summary>Widget id (see BuiltInWidgets).</summary>
    public string Widget { get; set; } = "";
    /// <summary>Stable monitor key; "primary" or an unknown key lands on the primary monitor.</summary>
    public string Monitor { get; set; } = MonitorKeys.Primary;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Hidden { get; set; }
    /// <summary>Stacking order: the last panel moved stays on top.</summary>
    public int Z { get; set; }
    /// <summary>The widget's own data (note text, tasks, shortcuts, options).</summary>
    public JsonObject Settings { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public Rect Bounds
    {
        get => new(X, Y, Width, Height);
        set { X = value.X; Y = value.Y; Width = value.Width; Height = value.Height; }
    }
}

public static class MonitorKeys
{
    public const string Primary = "primary";
}
