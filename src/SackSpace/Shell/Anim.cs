using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SackSpace.Shell;

/// <summary>
/// The app's motion, in one place: short, eased, and off entirely when the user (or
/// Windows' "Animation effects" setting) says so.
/// </summary>
public static class Anim
{
    private static bool _userEnabled = true;

    public static bool Enabled => _userEnabled && SystemParameters.ClientAreaAnimation;

    public static void Configure(bool userEnabled) => _userEnabled = userEnabled;

    public static readonly IEasingFunction Ease = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });
    /// <summary>A little overshoot, like Android's springs.</summary>
    public static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 });

    private static IEasingFunction Frozen(EasingFunctionBase e) { e.Freeze(); return e; }

    public static Duration Ms(int ms) => new(TimeSpan.FromMilliseconds(Enabled ? ms : 0));

    public static void Fade(UIElement e, double to, int ms = 200, Action? done = null)
    {
        if (!Enabled)
        {
            e.BeginAnimation(UIElement.OpacityProperty, null);
            e.Opacity = to;
            done?.Invoke();
            return;
        }
        var a = new DoubleAnimation(to, Ms(ms)) { EasingFunction = Ease };
        if (done is not null) a.Completed += (_, _) => done();
        e.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// <summary>Slides in from an offset while fading in.</summary>
    public static void SlideIn(FrameworkElement e, double fromX, double fromY, int ms = 280)
    {
        var t = Translate(e);
        if (!Enabled) { e.Opacity = 1; t.X = t.Y = 0; return; }
        e.Opacity = 0;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, Ms(ms)) { EasingFunction = Ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, Ms(ms)) { EasingFunction = Ease });
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(ms * 3 / 4)) { EasingFunction = Ease });
    }

    public static void SlideOut(FrameworkElement e, double toX, double toY, int ms = 220, Action? done = null)
    {
        if (!Enabled) { done?.Invoke(); return; }
        var t = Translate(e);
        var fade = new DoubleAnimation(0, Ms(ms)) { EasingFunction = Ease };
        if (done is not null) fade.Completed += (_, _) => done();
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(toX, Ms(ms)) { EasingFunction = Ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(toY, Ms(ms)) { EasingFunction = Ease });
        e.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Grows from 92 % with a soft overshoot: how a new widget lands.</summary>
    public static void Pop(FrameworkElement e, int ms = 320)
    {
        var s = Scale(e);
        if (!Enabled) { e.Opacity = 1; s.ScaleX = s.ScaleY = 1; return; }
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(ms / 2)));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, Ms(ms)) { EasingFunction = Spring });
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, Ms(ms)) { EasingFunction = Spring });
    }

    public static void ScaleTo(FrameworkElement e, double to, int ms = 160)
    {
        var s = Scale(e);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, Ms(ms)) { EasingFunction = Ease });
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, Ms(ms)) { EasingFunction = Ease });
    }

    /// <summary>The element's TranslateTransform, added inside a TransformGroup if needed.</summary>
    public static TranslateTransform Translate(FrameworkElement e) => Part<TranslateTransform>(e);

    public static ScaleTransform Scale(FrameworkElement e)
    {
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        return Part<ScaleTransform>(e);
    }

    private static T Part<T>(FrameworkElement e) where T : Transform, new()
    {
        if (e.RenderTransform is TransformGroup g)
        {
            var found = g.Children.OfType<T>().FirstOrDefault();
            if (found is not null) return found;
            var made = new T();
            g.Children.Add(made);
            return made;
        }
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform());
        group.Children.Add(new TranslateTransform());
        e.RenderTransform = group;
        return group.Children.OfType<T>().First();
    }
}
