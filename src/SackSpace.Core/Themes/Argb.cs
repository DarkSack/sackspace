using System.Globalization;

namespace SackSpace.Core.Themes;

/// <summary>A colour with alpha. Hex as "#RRGGBB" or "#AARRGGBB", like WPF.</summary>
public readonly record struct Argb(byte A, byte R, byte G, byte B)
{
    public static Argb Parse(string hex) =>
        TryParse(hex, out var c) ? c : throw new FormatException($"Color no válido: '{hex}'");

    public static bool TryParse(string? hex, out Argb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        if (s.Length is not (6 or 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return false;
        color = s.Length == 6
            ? new Argb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : new Argb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public string ToHex(bool withAlpha = false) =>
        withAlpha || A != 255 ? $"#{A:X2}{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}";

    public Argb WithAlpha(double opacity) => this with { A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255) };

    /// <summary>Relative luminance (sRGB), 0 = black, 1 = white.</summary>
    public double Luminance
    {
        get
        {
            static double Lin(byte c) { var v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Lin(R) + 0.7152 * Lin(G) + 0.0722 * Lin(B);
        }
    }

    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Argb FromHsl(double h, double s, double l, byte a = 255)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-9) { var v = (byte)Math.Round(l * 255); return new Argb(a, v, v, v); }
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        return new Argb(a,
            (byte)Math.Round(Hue(p, q, h + 1.0 / 3) * 255),
            (byte)Math.Round(Hue(p, q, h) * 255),
            (byte)Math.Round(Hue(p, q, h - 1.0 / 3) * 255));
    }
}

public static class ColorMath
{
    /// <summary>
    /// The wallpaper's main colour, for the "from the wallpaper" accent (Android's
    /// Material You does the same). Hues of colourful, mid-bright pixels are counted;
    /// the busiest hue wins. Null when the picture is grey.
    /// </summary>
    /// <param name="bgra">Pixels as B, G, R, A bytes (WPF's Bgra32 / Pbgra32).</param>
    public static Argb? Dominant(ReadOnlySpan<byte> bgra)
    {
        const int Buckets = 36;
        Span<double> weight = stackalloc double[Buckets];
        Span<double> sumR = stackalloc double[Buckets];
        Span<double> sumG = stackalloc double[Buckets];
        Span<double> sumB = stackalloc double[Buckets];

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var c = new Argb(255, bgra[i + 2], bgra[i + 1], bgra[i]);
            var (h, s, l) = c.ToHsl();
            if (s < 0.25 || l < 0.12 || l > 0.9) continue;
            var w = s * (1 - Math.Abs(l - 0.5) * 2);
            var bucket = (int)(h / 10) % Buckets;
            weight[bucket] += w;
            sumR[bucket] += c.R * w;
            sumG[bucket] += c.G * w;
            sumB[bucket] += c.B * w;
        }

        var best = -1;
        var bestWeight = 0.0;
        for (var b = 0; b < Buckets; b++)
        {
            // Neighbouring buckets count too, so one hue split across a boundary still wins.
            var w = weight[b] + 0.5 * (weight[(b + 1) % Buckets] + weight[(b + Buckets - 1) % Buckets]);
            if (weight[b] > 0 && w > bestWeight) { bestWeight = w; best = b; }
        }
        if (best < 0) return null;
        return new Argb(255,
            (byte)(sumR[best] / weight[best]),
            (byte)(sumG[best] / weight[best]),
            (byte)(sumB[best] / weight[best]));
    }

    /// <summary>A colour made readable as an accent: bright and saturated enough on a dark theme, deeper on a light one.</summary>
    public static Argb AsAccent(Argb c, bool dark)
    {
        var (h, s, l) = c.ToHsl();
        s = Math.Max(s, 0.55);
        l = dark ? Math.Clamp(l, 0.62, 0.74) : Math.Clamp(l, 0.38, 0.48);
        return Argb.FromHsl(h, s, l);
    }

    public static Argb Mix(Argb a, Argb b, double t) => new(
        (byte)Math.Round(a.A + (b.A - a.A) * t),
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
