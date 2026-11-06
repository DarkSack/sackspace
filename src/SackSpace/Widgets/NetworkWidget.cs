using Loc = SackSpace.Core.Loc;
using System.Windows.Controls;
using SackSpace.Core.Widgets;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>Download and upload speed now and over the last minute, on one shared scale.</summary>
public sealed class NetworkWidget : Grid, IDisposable
{
    /// <summary>The graphs never zoom in below this, so an idle line stays flat instead of showing noise.</summary>
    private const double FloorBytes = 64 * 1024;

    private readonly WidgetContext _c;
    private readonly Meter _down, _up;

    public NetworkWidget(WidgetContext c)
    {
        _c = c;
        var sampler = c.Services.System;
        _down = new Meter(Loc.T("Bajada"), () => Scaled(sampler.DownHistory));
        _up = new Meter(Loc.T("Subida"), () => Scaled(sampler.UpHistory));
        Meter.Stack(this, [_down, _up]);
        sampler.Sampled += Show;
        sampler.Subscribe(this, Meter.Interval(c), SampleKinds.Network);
        if (sampler.Last is { } last) Show(last);
    }

    private IReadOnlyList<double> Scaled(IReadOnlyList<double> values)
    {
        var s = _c.Services.System;
        var max = Math.Max(FloorBytes, s.DownHistory.Concat(s.UpHistory).DefaultIfEmpty(0).Max());
        return values.Select(v => v / max).ToList();
    }

    private void Show(SystemSample s)
    {
        _down.Set(Speed(s.Down), "");
        _up.Set(Speed(s.Up), "");
    }

    public static string Speed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => string.Format(Ui.Culture, "{0:0.0} MB/s", bytesPerSecond / (1024 * 1024)),
        >= 1024 => string.Format(Ui.Culture, "{0:0} KB/s", bytesPerSecond / 1024),
        _ => string.Format(Ui.Culture, "{0:0} B/s", bytesPerSecond),
    };

    public void Dispose()
    {
        _c.Services.System.Sampled -= Show;
        _c.Services.System.Unsubscribe(this);
    }
}
