using System.Windows;
using System.Windows.Controls;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>Big time that grows with the panel, and the date under it.</summary>
public sealed class ClockWidget : Grid, IDisposable
{
    private readonly WidgetContext _c;
    private readonly bool _seconds, _h24;
    private readonly TextBlock _time = new() { FontSize = 72, FontWeight = FontWeights.Light };
    private readonly TextBlock _suffix = new() { FontSize = 26, Margin = new Thickness(8, 0, 0, 12), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _date = new() { FontSize = 15, Margin = new Thickness(2, 2, 0, 0) };

    public ClockWidget(WidgetContext c)
    {
        _c = c;
        _seconds = c.Settings.GetBool("seconds", false);
        _h24 = c.Settings.GetBool("format24", true);

        _time.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _suffix.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _date.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(_time);
        line.Children.Add(_suffix);
        Children.Add(new Viewbox { Child = line, HorizontalAlignment = HorizontalAlignment.Left, StretchDirection = StretchDirection.Both });
        if (c.Settings.GetBool("date", true))
        {
            SetRow(_date, 1);
            Children.Add(_date);
        }

        if (_seconds) c.Services.Clock.Second += Tick;
        else c.Services.Clock.Minute += Tick;
        Tick(DateTime.Now);
    }

    private void Tick(DateTime now)
    {
        _time.Text = now.ToString(_h24 ? "HH:mm" : "h:mm", Ui.Culture);
        _suffix.Text = (_seconds ? now.ToString("ss") : "") + (_h24 ? "" : (_seconds ? " " : "") + now.ToString("tt", Ui.Culture));
        _suffix.Visibility = _suffix.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _date.Text = Ui.Capitalize(now.ToString("dddd, d 'de' MMMM", Ui.Culture));
    }

    public void Dispose()
    {
        _c.Services.Clock.Second -= Tick;
        _c.Services.Clock.Minute -= Tick;
    }
}
