using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>A month grid with today marked; arrows or the wheel change month, the title goes back to today.</summary>
public sealed class CalendarWidget : Grid, IDisposable
{
    private readonly WidgetContext _c;
    private readonly bool _weeks;
    private readonly TextBlock _title = new() { FontSize = 15, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Volver a hoy" };
    private readonly UniformGrid _names, _days;
    private DateTime _month;

    public CalendarWidget(WidgetContext c)
    {
        _c = c;
        _weeks = c.Settings.GetBool("weekNumbers", false);
        _month = FirstOfMonth(DateTime.Today);
        var columns = _weeks ? 8 : 7;
        _names = new UniformGrid { Columns = columns, Margin = new Thickness(0, 4, 0, 4) };
        _days = new UniformGrid { Columns = columns, Rows = 6 };

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        _title.Style = Ui.Style("Title");
        _title.MouseLeftButtonUp += (_, _) => Go(FirstOfMonth(DateTime.Today));
        var header = Ui.Header(Glyphs.Calendar, "",
            Ui.IconButton(Glyphs.ChevronLeft, "Mes anterior", () => Go(_month.AddMonths(-1)), 28),
            Ui.IconButton(Glyphs.ChevronRight, "Mes siguiente", () => Go(_month.AddMonths(1)), 28));
        header.Children.RemoveAt(header.Children.Count - 1);
        header.Tag = null; // month navigation, not a title
        header.Children.Add(_title);
        header.Margin = new Thickness(0, 0, 0, 4);
        Children.Add(header);
        SetRow(_names, 1);
        Children.Add(_names);
        SetRow(_days, 2);
        Children.Add(_days);

        BuildNames();
        Render();
        MouseWheel += (_, e) => { e.Handled = true; Go(_month.AddMonths(e.Delta < 0 ? 1 : -1)); };
        c.Services.Clock.Day += OnDay;
    }

    private static DateTime FirstOfMonth(DateTime d) => new(d.Year, d.Month, 1);

    private void Go(DateTime month)
    {
        _month = month;
        Render();
    }

    private void OnDay(DateTime _) => Render();

    private void BuildNames()
    {
        var fmt = Ui.Culture.DateTimeFormat;
        if (_weeks) _names.Children.Add(Cell("S", "TextTertiaryBrush", 11));
        for (var i = 0; i < 7; i++)
        {
            var day = (int)(fmt.FirstDayOfWeek + i) % 7;
            _names.Children.Add(Cell(Ui.Capitalize(fmt.ShortestDayNames[day]), "TextTertiaryBrush", 11));
        }
    }

    private void Render()
    {
        _title.Text = Ui.Capitalize(_month.ToString("MMMM yyyy", Ui.Culture));
        _days.Children.Clear();
        var first = Ui.Culture.DateTimeFormat.FirstDayOfWeek;
        var start = _month.AddDays(-(((int)_month.DayOfWeek - (int)first + 7) % 7));
        var today = DateTime.Today;
        for (var row = 0; row < 6; row++)
        {
            if (_weeks) _days.Children.Add(Cell(ISOWeek.GetWeekOfYear(start.AddDays(row * 7 + 3)).ToString(), "TextTertiaryBrush", 11));
            for (var col = 0; col < 7; col++)
            {
                var day = start.AddDays(row * 7 + col);
                if (day == today)
                {
                    var g = new Grid();
                    var dot = new Ellipse { Width = 28, Height = 28 };
                    dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
                    g.Children.Add(dot);
                    var t = Cell(day.Day.ToString(), "AccentForegroundBrush", 13);
                    t.FontWeight = FontWeights.Bold;
                    g.Children.Add(t);
                    _days.Children.Add(g);
                }
                else _days.Children.Add(Cell(day.Day.ToString(), day.Month == _month.Month ? "TextBrush" : "TextTertiaryBrush", 13));
            }
        }
    }

    private static TextBlock Cell(string text, string brush, double size)
    {
        var t = new TextBlock { Text = text, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public void Dispose() => _c.Services.Clock.Day -= OnDay;
}
