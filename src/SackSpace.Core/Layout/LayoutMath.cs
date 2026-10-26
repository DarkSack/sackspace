namespace SackSpace.Core;

/// <summary>
/// Where panels go: snapping while dragging or resizing, keeping them on screen,
/// and finding room for a new one. Pure math so it is tested without a window.
/// </summary>
public static class LayoutMath
{
    public const double Grid = 8;
    /// <summary>An edge closer than this to another edge sticks to it.</summary>
    public const double SnapDistance = 10;
    /// <summary>Space between panels that snap side by side.</summary>
    public const double Gap = 12;
    public const double Margin = 24;

    public static double SnapToGrid(double value, double grid = Grid) => Math.Round(value / grid) * grid;

    /// <summary>
    /// The panel moved to <paramref name="r"/>: its edges stick to the edges of the other
    /// panels (aligned or a gap apart) and of the area; if nothing is close, to the grid.
    /// </summary>
    public static Rect SnapMove(Rect r, IEnumerable<Rect> others, Rect bounds)
    {
        var list = others.ToList();
        var x = Snap(r.X, r.Width, XTargets(list, bounds), out var snappedX) ? snappedX : SnapToGrid(r.X);
        var y = Snap(r.Y, r.Height, YTargets(list, bounds), out var snappedY) ? snappedY : SnapToGrid(r.Y);
        return Clamp(r.WithPosition(x, y), bounds);
    }

    /// <summary>The panel's bottom-right corner moved: the right and bottom edges snap, the size stays above the minimum.</summary>
    public static Rect SnapResize(Rect r, IEnumerable<Rect> others, Rect bounds, double minWidth, double minHeight)
    {
        var list = others.ToList();
        var right = Nearest(r.Right, XTargets(list, bounds).End, out var snappedRight) ? snappedRight : SnapToGrid(r.Right);
        var bottom = Nearest(r.Bottom, YTargets(list, bounds).End, out var snappedBottom) ? snappedBottom : SnapToGrid(r.Bottom);
        right = Math.Min(right, bounds.Right);
        bottom = Math.Min(bottom, bounds.Bottom);
        var width = Math.Max(minWidth, right - r.X);
        var height = Math.Max(minHeight, bottom - r.Y);
        return Clamp(r with { Width = width, Height = height }, bounds);
    }

    /// <summary>Keeps the whole panel inside the area, shrinking it only if it is bigger than the area.</summary>
    public static Rect Clamp(Rect r, Rect bounds)
    {
        var w = Math.Min(r.Width, bounds.Width);
        var h = Math.Min(r.Height, bounds.Height);
        var x = Math.Clamp(r.X, bounds.X, bounds.Right - w);
        var y = Math.Clamp(r.Y, bounds.Y, bounds.Bottom - h);
        return new Rect(x, y, w, h);
    }

    /// <summary>
    /// First free place for a panel of this size, filling columns from the right edge
    /// (the desktop icons live on the left). Falls back to a spot near the centre.
    /// </summary>
    public static Rect FindFreeSpot(double width, double height, IEnumerable<Rect> occupied, Rect bounds)
    {
        var taken = occupied.Select(o => o.Inflate(Gap - 0.5)).ToList();
        var step = Grid * 2;
        var inner = new Rect(bounds.X + Margin, bounds.Y + Margin, bounds.Width - 2 * Margin, bounds.Height - 2 * Margin);
        for (var x = inner.Right - width; x >= inner.X; x -= step)
        {
            for (var y = inner.Y; y + height <= inner.Bottom; y += step)
            {
                var candidate = new Rect(SnapToGrid(x), SnapToGrid(y), width, height);
                if (candidate.Right > inner.Right) candidate = candidate with { X = candidate.X - Grid };
                if (!taken.Any(t => t.IntersectsWith(candidate))) return candidate;
            }
        }
        var centre = new Rect(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        return Clamp(centre, bounds);
    }

    /// <summary>Tidies panels into free places, keeping their order and sizes.</summary>
    public static IReadOnlyList<Rect> Arrange(IReadOnlyList<Rect> panels, Rect bounds)
    {
        var placed = new List<Rect>();
        foreach (var p in panels)
        {
            var size = Clamp(p, bounds);
            placed.Add(FindFreeSpot(size.Width, size.Height, placed, bounds));
        }
        return placed;
    }

    public static bool Overlaps(IReadOnlyList<Rect> rects)
    {
        for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
                if (rects[i].IntersectsWith(rects[j])) return true;
        return false;
    }

    // Where a left/top edge may stick, and where a right/bottom edge may stick.
    private static (List<double> Start, List<double> End) XTargets(List<Rect> others, Rect b) =>
        (others.SelectMany(o => new[] { o.X, o.Right + Gap }).Append(b.X + Margin).Append(b.X).ToList(),
         others.SelectMany(o => new[] { o.Right, o.X - Gap }).Append(b.Right - Margin).Append(b.Right).ToList());

    private static (List<double> Start, List<double> End) YTargets(List<Rect> others, Rect b) =>
        (others.SelectMany(o => new[] { o.Y, o.Bottom + Gap }).Append(b.Y + Margin).Append(b.Y).ToList(),
         others.SelectMany(o => new[] { o.Bottom, o.Y - Gap }).Append(b.Bottom - Margin).Append(b.Bottom).ToList());

    private static bool Snap(double start, double size, (List<double> Start, List<double> End) targets, out double snappedStart)
    {
        var best = double.MaxValue;
        snappedStart = start;
        foreach (var t in targets.Start)
            if (Math.Abs(start - t) < best) { best = Math.Abs(start - t); snappedStart = t; }
        foreach (var t in targets.End)
            if (Math.Abs(start + size - t) < best) { best = Math.Abs(start + size - t); snappedStart = t - size; }
        return best <= SnapDistance;
    }

    private static bool Nearest(double edge, List<double> targets, out double snapped)
    {
        var best = double.MaxValue;
        snapped = edge;
        foreach (var t in targets)
            if (Math.Abs(edge - t) < best) { best = Math.Abs(edge - t); snapped = t; }
        return best <= SnapDistance;
    }
}
