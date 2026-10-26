using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SackSpace.Core.Model;
using SackSpace.Core.Themes;
using SackSpace.Shell;

namespace SackSpace.Theming;

/// <summary>
/// Turns the chosen theme into the brushes every view uses through DynamicResource
/// (AccentBrush, GlassTintBrush, TextBrush…). WPF freezes what sits in the app's
/// dictionaries, so each change puts in a new brush; a fading one is still animating
/// when added, which keeps it from being frozen.
/// </summary>
public sealed class ThemeService
{
    private readonly ResourceDictionary _dict = new();

    public string ThemesFolder { get; }
    public ThemeCatalog Catalog { get; private set; }
    public ResolvedTheme Current { get; private set; } = null!;

    /// <summary>After the theme or its overrides changed (glass, blur, corners).</summary>
    public event Action? Changed;

    public static readonly string[] BrushKeys =
    [
        "AccentBrush", "AccentSoftBrush", "AccentForegroundBrush", "GlassTintBrush", "GlassBorderBrush",
        "TextBrush", "TextSecondaryBrush", "TextTertiaryBrush", "ScrimBrush", "SurfaceBrush",
        "HoverBrush", "PressedBrush", "DividerBrush", "TrackBrush",
    ];

    public ThemeService(string dataFolder)
    {
        ThemesFolder = Path.Combine(dataFolder, "themes");
        Directory.CreateDirectory(ThemesFolder);
        Catalog = new ThemeCatalog(ThemesFolder);
        foreach (var e in Catalog.Errors) Log.Warn("Tema ignorado: " + e);

        foreach (var key in BrushKeys) _dict[key] = Brushes.Transparent;
        // Added last: of the merged dictionaries, the last one wins.
        Application.Current.Resources.MergedDictionaries.Add(_dict);
    }

    public void ReloadCatalog()
    {
        Catalog = new ThemeCatalog(ThemesFolder);
        foreach (var e in Catalog.Errors) Log.Warn("Tema ignorado: " + e);
    }

    public void Apply(AppConfig config, Argb? wallpaperColor, bool animate)
    {
        var theme = Catalog.Get(config.Appearance.ThemeId);
        var t = ResolvedTheme.Resolve(theme, config.Appearance, config.ActiveWorkspace?.Accent, wallpaperColor);
        Current = t;

        var accent = C(t.Accent);
        Set("AccentBrush", accent);
        Set("AccentSoftBrush", Color.FromArgb(t.IsDark ? (byte)56 : (byte)40, accent.R, accent.G, accent.B));
        Set("AccentForegroundBrush", t.Accent.Luminance > 0.45 ? Color.FromRgb(16, 19, 26) : Colors.White);

        var tint = C(t.GlassTint);
        Set("GlassTintBrush", Color.FromArgb((byte)Math.Round(t.GlassOpacity * 255), tint.R, tint.G, tint.B));
        // Menus and sheets need to stay readable over anything: same tint, more opaque.
        Set("SurfaceBrush", Color.FromArgb((byte)Math.Round(Math.Max(t.GlassOpacity, 0.94) * 255), tint.R, tint.G, tint.B));
        Set("GlassBorderBrush", C(t.Border));
        Set("ScrimBrush", C(t.Scrim));

        var fg = C(t.Foreground);
        Set("TextBrush", fg);
        Set("TextSecondaryBrush", C(t.SecondaryForeground));
        Set("TextTertiaryBrush", Color.FromArgb(150, t.SecondaryForeground.R, t.SecondaryForeground.G, t.SecondaryForeground.B));
        Set("HoverBrush", Color.FromArgb(22, fg.R, fg.G, fg.B));
        Set("PressedBrush", Color.FromArgb(38, fg.R, fg.G, fg.B));
        Set("DividerBrush", Color.FromArgb(28, fg.R, fg.G, fg.B));
        Set("TrackBrush", Color.FromArgb(40, fg.R, fg.G, fg.B));

        if (_dict["GlassHighlightBrush"] is not LinearGradientBrush old || old.GradientStops[0].Color.A != (t.IsDark ? 26 : 70))
        {
            var highlight = new LinearGradientBrush(t.IsDark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(70, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.45));
            highlight.Freeze();
            _dict["GlassHighlightBrush"] = highlight;
        }

        Put("PanelCornerRadius", new CornerRadius(t.CornerRadius));
        Put("PanelCornerRadiusValue", t.CornerRadius);
        Put("ControlCornerRadius", new CornerRadius(Math.Min(12, Math.Max(4, t.CornerRadius / 2))));
        Put("AppFont", new FontFamily(theme.FontFamily));
        Changed?.Invoke();
    }

    /// <summary>
    /// Only what really changed is replaced: every new resource makes each element using it
    /// (in every panel, on every screen) look it up and repaint. Switching workspaces changes
    /// the accent and nothing else. Not animated, for the same reason.
    /// </summary>
    private void Set(string key, Color color)
    {
        if (_dict[key] is SolidColorBrush current && current.Color == color) return;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _dict[key] = brush;
    }

    private void Put(string key, object value)
    {
        if (!Equals(_dict[key], value)) _dict[key] = value;
    }

    public static Color C(Argb a) => Color.FromArgb(a.A, a.R, a.G, a.B);
}
