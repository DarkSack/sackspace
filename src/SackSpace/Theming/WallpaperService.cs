using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SackSpace.Core.Themes;
using SackSpace.Interop;

namespace SackSpace.Theming;

/// <summary>A monitor's wallpaper, small and blurred: what the glass panels show through. Source says where it came from.</summary>
public sealed record WallpaperImage(BitmapSource Blurred, Native.RECT Bounds, Argb? Dominant, string Source);

/// <summary>
/// Glass without a live backdrop blur: the panels sit on the desktop, so what is behind
/// them is the wallpaper. Each monitor's wallpaper is drawn once at 480 px wide, blurred,
/// and every panel paints its own slice of it. Cheap (one small bitmap per monitor).
/// With Wallpaper Engine or Lively the picture is that wallpaper's still preview: close in
/// colour, not frame-exact (clear glass shows the moving one).
/// </summary>
public sealed class WallpaperService
{
    private const int TargetWidth = 480;
    private readonly Dictionary<string, WallpaperImage> _cache = new();
    private double _blurRadius = 40;
    private string? _customImage;
    // Wallpaper Engine's config.json and Lively's data folder, when those apps run; and a fingerprint of both.
    private (string? Engine, string? Lively, string Stamp) _live = (null, null, "");
    // Windows sends several "desktop changed" notices for one change (and a display change brings more):
    // one repaint after the last of them instead of one per notice.
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(400) };

    /// <summary>The wallpaper or the blur changed: panels must repaint their glass.</summary>
    public event Action? Changed;

    public WallpaperService()
    {
        _settle.Tick += (_, _) => { _settle.Stop(); Invalidate(); };
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            // The new picture is written a moment after the notification.
            if (e.Category == UserPreferenceCategory.Desktop)
                Application.Current?.Dispatcher.BeginInvoke(() => { _settle.Stop(); _settle.Start(); });
        };
        // Neither app tells Windows when its wallpaper changes, so look every few seconds (a few file stamps).
        _live = FindLive();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        poll.Tick += (_, _) =>
        {
            var now = FindLive();
            if (now.Stamp == _live.Stamp) return;
            _live = now;
            Invalidate();
        };
        poll.Start();
    }

    /// <summary>A picture to use behind the glass on every screen, or null for each screen's own wallpaper.</summary>
    public string? CustomImage
    {
        get => _customImage;
        set
        {
            if (value == _customImage) return;
            _customImage = value;
            Invalidate();
        }
    }

    public double BlurRadius
    {
        get => _blurRadius;
        set
        {
            if (Math.Abs(value - _blurRadius) < 0.5) return;
            _blurRadius = value;
            Invalidate();
        }
    }

    public void Invalidate()
    {
        _cache.Clear();
        Changed?.Invoke();
    }

    public WallpaperImage Get(MonitorInfo monitor)
    {
        if (_cache.TryGetValue(monitor.Key, out var cached) && cached.Bounds.Equals(monitor.Bounds)) return cached;
        WallpaperImage image;
        try
        {
            var (source, label) = Pick(monitor);
            image = Render(source, label, monitor.Bounds, _blurRadius, monitor.Scale);
            Log.Info($"Cristal de {monitor.DeviceName}: {label}");
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo dibujar el fondo de pantalla", ex);
            image = Render(new WallpaperSource(null, WallpaperPosition.Fill, 0x1E1A16), "Color liso", monitor.Bounds, 0, monitor.Scale);
        }
        _cache[monitor.Key] = image;
        return image;
    }

    /// <summary>What this screen shows: the user's picture, else Wallpaper Engine's or Lively's, else Windows'.</summary>
    private (WallpaperSource Source, string Label) Pick(MonitorInfo monitor)
    {
        static WallpaperSource Still(string path) => new(path, WallpaperPosition.Fill, 0);
        if (_customImage is not null && File.Exists(_customImage)) return (Still(_customImage), "Imagen propia");
        if (_live.Engine is { } config && LiveWallpapers.WallpaperEngine(config, monitor.Key, monitor.IsPrimary) is { } engine)
            return (Still(engine.Picture), "Wallpaper Engine · " + engine.Title);
        if (_live.Lively is { } folder && LiveWallpapers.Lively(folder, monitor.Key) is { } lively)
            return (Still(lively.Picture), "Lively · " + lively.Title);
        return (DesktopWallpaper.For(monitor.Bounds), "Fondo de Windows");
    }

    private static (string? Engine, string? Lively, string Stamp) FindLive()
    {
        string? engine = null, lively = null;
        // One pass over the process list (GetProcessesByName walks all of it on every call).
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses()) { running.Add(p.ProcessName); p.Dispose(); }
        if (running.Contains("wallpaper64") || running.Contains("wallpaper32"))
        {
            var exe = Registry.GetValue(@"HKEY_CURRENT_USER\Software\WallpaperEngine", "installPath", null) as string;
            var config = exe is null ? null : Path.Combine(Path.GetDirectoryName(exe) ?? "", "config.json");
            if (File.Exists(config)) engine = config;
        }
        if (running.Contains("Lively"))
        {
            // The Store build keeps its data in the package's redirected AppData.
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var packages = Path.Combine(local, "Packages");
            lively = new[] { Path.Combine(local, "Lively Wallpaper") }
                .Concat(Directory.Exists(packages)
                    ? Directory.EnumerateDirectories(packages, "*LivelyWallpaper*").Select(p => Path.Combine(p, "LocalCache", "Local", "Lively Wallpaper"))
                    : [])
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "WallpaperLayout.json")));
        }
        var stamp = $"{engine}|{Stamp(engine)}|{lively}|{Stamp(lively is null ? null : Path.Combine(lively, "WallpaperLayout.json"))}";
        return (engine, lively, stamp);

        static long Stamp(string? path) => path is not null && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
    }

    private static WallpaperImage Render(WallpaperSource source, string label, Native.RECT bounds, double blurDip, double scale)
    {
        int mw = Math.Max(1, bounds.Width), mh = Math.Max(1, bounds.Height);
        var k = TargetWidth / (double)mw;
        int w = TargetWidth, h = Math.Max(1, (int)Math.Round(mh * k));
        var bg = Color.FromRgb((byte)(source.BackgroundColorRef & 0xFF), (byte)((source.BackgroundColorRef >> 8) & 0xFF), (byte)((source.BackgroundColorRef >> 16) & 0xFF));

        var (image, naturalW, naturalH) = Load(source.Path);
        var sharp = DrawSharp(image, naturalW, naturalH, bg, source.Position, w, h, mw, mh, k);

        var pixels = new byte[w * h * 4];
        sharp.CopyPixels(pixels, w * 4, 0);
        var dominant = ColorMath.Dominant(pixels);

        var radius = Math.Clamp(blurDip * scale * k, 0, 60);
        var blurred = radius < 0.5 ? sharp : Blur(sharp, radius);
        return new WallpaperImage(blurred, bounds, dominant, label);
    }

    private static (BitmapSource? Image, int NaturalWidth, int NaturalHeight) Load(string? path)
    {
        if (path is null || !File.Exists(path)) return (null, 0, 0);
        try
        {
            // The real size decides how Center and Tile look; the decode itself stays small.
            int naturalW, naturalH;
            using (var s = File.OpenRead(path))
            {
                var frame = BitmapFrame.Create(s, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                naturalW = frame.PixelWidth;
                naturalH = frame.PixelHeight;
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = Math.Min(naturalW, 1280);
            bmp.EndInit();
            bmp.Freeze();
            return (bmp, naturalW, naturalH);
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo leer el fondo '{path}'", ex);
            return (null, 0, 0);
        }
    }

    private static BitmapSource DrawSharp(BitmapSource? image, int iw, int ih, Color bg, WallpaperPosition position,
        int w, int h, int mw, int mh, double k)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, w, h));
            if (image is not null && iw > 0 && ih > 0)
            {
                switch (position)
                {
                    case WallpaperPosition.Tile:
                        var tile = new ImageBrush(image)
                        {
                            TileMode = TileMode.Tile,
                            ViewportUnits = BrushMappingMode.Absolute,
                            Viewport = new Rect(0, 0, iw * k, ih * k),
                        };
                        dc.DrawRectangle(tile, null, new Rect(0, 0, w, h));
                        break;
                    case WallpaperPosition.Stretch:
                        dc.DrawImage(image, new Rect(0, 0, w, h));
                        break;
                    case WallpaperPosition.Center:
                        dc.DrawImage(image, Centered(iw * k, ih * k, w, h));
                        break;
                    case WallpaperPosition.Fit:
                        var fit = Math.Min(mw / (double)iw, mh / (double)ih);
                        dc.DrawImage(image, Centered(iw * fit * k, ih * fit * k, w, h));
                        break;
                    default: // Fill, and Span (shortcut: drawn per monitor like Fill, close enough behind blur)
                        var fill = Math.Max(mw / (double)iw, mh / (double)ih);
                        dc.DrawImage(image, Centered(iw * fill * k, ih * fill * k, w, h));
                        break;
                }
            }
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static Rect Centered(double width, double height, int w, int h) => new((w - width) / 2, (h - height) / 2, width, height);

    /// <summary>
    /// Blur that does not darken the edges: the picture is first drawn on a bigger canvas
    /// whose margin repeats its own colours, then blurred, then cropped back.
    /// </summary>
    private static BitmapSource Blur(BitmapSource sharp, double radius)
    {
        int w = sharp.PixelWidth, h = sharp.PixelHeight;
        var pad = (int)Math.Ceiling(radius * 2);
        int pw = w + 2 * pad, ph = h + 2 * pad;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(sharp, new Rect(0, 0, pw, ph));
            dc.DrawImage(sharp, new Rect(pad, pad, w, h));
        }
        var padded = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        padded.Render(dv);

        var img = new Image
        {
            Source = padded,
            Width = pw,
            Height = ph,
            Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        };
        img.Measure(new Size(pw, ph));
        img.Arrange(new Rect(0, 0, pw, ph));
        var blurred = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        blurred.Render(img);

        var cropped = new CroppedBitmap(blurred, new Int32Rect(pad, pad, w, h));
        cropped.Freeze();
        return cropped;
    }
}
