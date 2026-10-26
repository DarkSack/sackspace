using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace SackSpace.Shell;

/// <summary>
/// A panel's drop shadow, drawn once per size into a bitmap. A live DropShadowEffect is
/// recomputed in software on every frame its panel moves or fades (the app renders in
/// software), which made every animation crawl; a bitmap costs nothing to redraw.
/// The shadow spills outside the element's bounds by Pad.
/// </summary>
public sealed class BakedShadow : FrameworkElement
{
    private const double Pad = 40;
    private static readonly Dictionary<(int W, int H, int R, int Dpi), ImageSource> Cache = new();

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(BakedShadow), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public BakedShadow()
    {
        IsHitTestVisible = false;
        // Baked at the drag strength; at rest it shows at 62 % (≈ the old 0.26 opacity).
        Opacity = 0.62;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 8 || h < 8) return;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        dc.DrawImage(Get(w, h, Radius, scale), new Rect(-Pad, -Pad, w + 2 * Pad, h + 2 * Pad));
    }

    private static ImageSource Get(double w, double h, double radius, double scale)
    {
        var key = ((int)Math.Round(w), (int)Math.Round(h), (int)Math.Round(radius), (int)Math.Round(scale * 96));
        if (Cache.TryGetValue(key, out var cached)) return cached;
        if (Cache.Count > 64) Cache.Clear(); // shortcut: a resize drag bakes one per size; dropping all is fine at this size

        var shape = new Border
        {
            Width = Math.Max(1, key.Item1 - 4), Height = Math.Max(1, key.Item2 - 4), CornerRadius = new CornerRadius(radius),
            Background = Brushes.Black, Margin = new Thickness(Pad + 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 5, Direction = 270, Opacity = 0.42, RenderingBias = RenderingBias.Quality },
        };
        var size = new Size(key.Item1 + 2 * Pad, key.Item2 + 2 * Pad);
        var root = new Grid { Width = size.Width, Height = size.Height };
        root.Children.Add(shape);
        root.Measure(size);
        root.Arrange(new Rect(size));
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(root);
        bmp.Freeze();
        return Cache[key] = bmp;
    }
}
