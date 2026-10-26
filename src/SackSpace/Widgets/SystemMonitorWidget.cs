using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SackSpace.Core.Widgets;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>CPU and memory: the number now and a one-minute graph. Samples only while shown.</summary>
public sealed class SystemMonitorWidget : Grid, IDisposable
{
    private readonly WidgetContext _c;
    private readonly Meter _cpu, _ram;

    public SystemMonitorWidget(WidgetContext c)
    {
        _c = c;
        var sampler = c.Services.System;
        _cpu = new Meter("Procesador", () => sampler.CpuHistory);
        _ram = new Meter("Memoria", () => sampler.RamHistory);
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(_cpu);
        SetRow(_ram, 2);
        Children.Add(_ram);

        var seconds = c.Settings.GetString("interval", "2 s") switch { "1 s" => 1, "5 s" => 5, _ => 2 };
        sampler.Sampled += Show;
        sampler.Subscribe(this, TimeSpan.FromSeconds(seconds));
        if (sampler.Last is { } last) Show(last);
    }

    private void Show(SystemSample s)
    {
        _cpu.Set($"{s.Cpu * 100:0} %", "");
        const double gb = 1024d * 1024 * 1024;
        _ram.Set($"{s.Ram * 100:0} %", string.Format(Ui.Culture, "{0:0.0} de {1:0} GB", s.UsedBytes / gb, s.TotalBytes / gb));
    }

    public void Dispose()
    {
        _c.Services.System.Sampled -= Show;
        _c.Services.System.Unsubscribe(this);
    }

    private sealed class Meter : Grid
    {
        private readonly TextBlock _value = new() { FontSize = 26, FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _detail = new() { Style = Ui.Style("Caption"), FontSize = 11 };
        private readonly Sparkline _graph;

        public Meter(string name, Func<IReadOnlyList<double>> values)
        {
            _graph = new Sparkline(values) { Margin = new Thickness(14, 4, 0, 4) };
            _value.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            _value.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            ColumnDefinitions.Add(new ColumnDefinition());
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Ui.Text(name));
            text.Children.Add(_value);
            text.Children.Add(_detail);
            Children.Add(text);
            SetColumn(_graph, 1);
            Children.Add(_graph);
        }

        public void Set(string value, string detail)
        {
            _value.Text = value;
            _detail.Text = detail;
            _detail.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _graph.InvalidateVisual();
        }
    }
}

/// <summary>A 0–1 line with a soft fill, newest value on the right.</summary>
public sealed class Sparkline(Func<IReadOnlyList<double>> values) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var v = values();
        double w = ActualWidth, h = ActualHeight;
        if (v.Count < 2 || w < 4 || h < 4) return;
        var step = w / (SystemSampler.HistoryLength - 1);
        var x0 = w - (v.Count - 1) * step;
        Point P(int i) => new(x0 + i * step, h - 1 - Math.Clamp(v[i], 0, 1) * (h - 2));

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(P(0), false, false);
            for (var i = 1; i < v.Count; i++) g.LineTo(P(i), true, true);
        }
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(x0, h), true, true);
            for (var i = 0; i < v.Count; i++) g.LineTo(P(i), true, true);
            g.LineTo(new Point(w, h), true, true);
        }

        var accent = (SolidColorBrush)FindResource("AccentBrush");
        var c = accent.Color;
        var fill = new LinearGradientBrush(Color.FromArgb(90, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90);
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(accent, 2) { LineJoin = PenLineJoin.Round }, line);
    }
}
