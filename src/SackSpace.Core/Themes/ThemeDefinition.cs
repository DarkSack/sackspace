using System.Text.Json;
using System.Text.Json.Serialization;
using SackSpace.Core.Model;

namespace SackSpace.Core.Themes;

/// <summary>
/// A theme: the glass, text and accent colours plus blur and corners. Built-in ones
/// live in <see cref="BuiltInThemes"/>; more can be dropped as JSON files in the
/// themes folder (same property names, see docs/TEMAS.md).
/// </summary>
public sealed class ThemeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDark { get; set; } = true;
    public string Accent { get; set; } = "#7C9CFF";
    /// <summary>Colour laid over the blurred wallpaper inside each panel.</summary>
    public string GlassTint { get; set; } = "#141824";
    /// <summary>How strongly the tint covers the wallpaper, 0–1.</summary>
    public double GlassOpacity { get; set; } = 0.45;
    public double BlurRadius { get; set; } = 40;
    public double CornerRadius { get; set; } = 22;
    public string Foreground { get; set; } = "#F4F6FB";
    public string SecondaryForeground { get; set; } = "#AEB6C8";
    /// <summary>Panel outline; usually white or black at low opacity.</summary>
    public string Border { get; set; } = "#33FFFFFF";
    /// <summary>Shade behind the panels when they are brought to the front.</summary>
    public string Scrim { get; set; } = "#66000000";
    public string FontFamily { get; set; } = "Segoe UI Variable Display, Segoe UI";

    /// <summary>Problems that keep the theme from loading; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id) || !Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            errors.Add("id vacío o con caracteres no válidos (letras, números, '-' o '_').");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("name vacío.");
        foreach (var (name, value) in new[]
                 {
                     ("accent", Accent), ("glassTint", GlassTint), ("foreground", Foreground),
                     ("secondaryForeground", SecondaryForeground), ("border", Border), ("scrim", Scrim),
                 })
            if (!Argb.TryParse(value, out _)) errors.Add($"{name}: color no válido '{value}'.");
        if (GlassOpacity is < 0 or > 1) errors.Add("glassOpacity debe ir de 0 a 1.");
        if (BlurRadius is < 0 or > 120) errors.Add("blurRadius debe ir de 0 a 120.");
        if (CornerRadius is < 0 or > 48) errors.Add("cornerRadius debe ir de 0 a 48.");
        return errors;
    }
}

public static class BuiltInThemes
{
    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        new() { Id = "cristal-oscuro", Name = "Cristal oscuro" },
        new()
        {
            Id = "cristal-claro", Name = "Cristal claro", IsDark = false, Accent = "#3D5AFE",
            GlassTint = "#F5F7FC", GlassOpacity = 0.55, Foreground = "#141826", SecondaryForeground = "#4A5266",
            Border = "#66FFFFFF", Scrim = "#40FFFFFF",
        },
        new()
        {
            Id = "medianoche", Name = "Medianoche", Accent = "#8AB4FF",
            GlassTint = "#05070D", GlassOpacity = 0.7, BlurRadius = 60, Border = "#22FFFFFF",
            Foreground = "#EEF2FF", SecondaryForeground = "#98A2BC", Scrim = "#99000000",
        },
        new()
        {
            Id = "aurora", Name = "Aurora", Accent = "#5EEAD4",
            GlassTint = "#0E2A33", GlassOpacity = 0.4, BlurRadius = 50, CornerRadius = 26, Border = "#3D9CF5E6",
            Foreground = "#ECFEFF", SecondaryForeground = "#A5D8DD",
        },
        new()
        {
            Id = "grafito", Name = "Grafito", Accent = "#FFB74D",
            GlassTint = "#1E1E1E", GlassOpacity = 0.6, BlurRadius = 30, CornerRadius = 14, Border = "#26FFFFFF",
            Foreground = "#F5F5F5", SecondaryForeground = "#B0B0B0",
        },
    ];
}

/// <summary>The built-in themes plus any valid JSON theme in a folder.</summary>
public sealed class ThemeCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public IReadOnlyList<ThemeDefinition> Themes { get; }

    /// <summary>"file: problem" for each theme file that was skipped.</summary>
    public IReadOnlyList<string> Errors { get; }

    public ThemeCatalog(string? folder)
    {
        var themes = BuiltInThemes.All.ToList();
        var errors = new List<string>();
        if (folder is not null && Directory.Exists(folder))
        {
            foreach (var file in Directory.GetFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                try
                {
                    var theme = JsonSerializer.Deserialize<ThemeDefinition>(File.ReadAllText(file), Options);
                    var problems = theme?.Validate() ?? ["vacío."];
                    if (problems.Count > 0) { errors.Add($"{name}: {problems[0]}"); continue; }
                    if (themes.Any(t => t.Id.Equals(theme!.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"{name}: ya hay un tema con el id '{theme!.Id}'.");
                        continue;
                    }
                    themes.Add(theme!);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{name}: {ex.Message}");
                }
            }
        }
        Themes = themes;
        Errors = errors;
    }

    public ThemeDefinition Get(string? id) =>
        Themes.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
}

/// <summary>A theme with the user's overrides and the accent already decided.</summary>
public sealed record ResolvedTheme(
    ThemeDefinition Base,
    Argb Accent,
    Argb GlassTint,
    double GlassOpacity,
    double BlurRadius,
    double CornerRadius,
    Argb Foreground,
    Argb SecondaryForeground,
    Argb Border,
    Argb Scrim)
{
    public bool IsDark => Base.IsDark;

    public static ResolvedTheme Resolve(ThemeDefinition theme, AppearanceSettings settings, string? workspaceAccent, Argb? wallpaperColor)
    {
        var accent = settings.AccentMode switch
        {
            AccentMode.Wallpaper when wallpaperColor is { } w => ColorMath.AsAccent(w, theme.IsDark),
            AccentMode.Custom when Argb.TryParse(settings.CustomAccent, out var c) => c,
            AccentMode.Workspace when Argb.TryParse(workspaceAccent, out var ws) => ws,
            _ => Argb.Parse(theme.Accent),
        };
        return new ResolvedTheme(
            theme,
            accent,
            Argb.Parse(theme.GlassTint),
            Math.Clamp(settings.GlassOpacity ?? theme.GlassOpacity, 0, 1),
            Math.Clamp(settings.BlurRadius ?? theme.BlurRadius, 0, 120),
            Math.Clamp(settings.CornerRadius ?? theme.CornerRadius, 0, 48),
            Argb.Parse(theme.Foreground),
            Argb.Parse(theme.SecondaryForeground),
            Argb.Parse(theme.Border),
            Argb.Parse(theme.Scrim));
    }
}
