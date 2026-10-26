namespace SackSpace.Core;

/// <summary>A rectangle in DIPs. Core keeps its own so it does not depend on WPF.</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool IntersectsWith(Rect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public Rect Inflate(double by) => new(X - by, Y - by, Width + 2 * by, Height + 2 * by);

    public Rect WithPosition(double x, double y) => this with { X = x, Y = y };
}
