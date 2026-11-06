using System.Windows;
using System.Windows.Controls;
using SackSpace.Core;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>
/// Hours per game today, this week, this month or ever (PlayTracker counts them), the
/// game being played now and the most played of the month.
/// </summary>
public sealed class PlayTimeWidget : DockPanel, IDisposable
{
    private static readonly string[] Periods = ["today", "week", "month", "total"];
    private readonly WidgetContext _c;
    private readonly StackPanel _list = new();
    private readonly TextBlock _footer;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

    public PlayTimeWidget(WidgetContext c)
    {
        _c = c;
        var header = Ui.Header(Glyphs.Game, Loc.T("Tiempo de juego"));
        SetDock(header, Dock.Top);
        Children.Add(header);
        SetDock(_tabs, Dock.Top);
        Children.Add(_tabs);
        _footer = Ui.Text("", wrap: true);
        _footer.Margin = new Thickness(0, 8, 0, 0);
        SetDock(_footer, Dock.Bottom);
        Children.Add(_footer);
        Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (c.Services.Play is { } play) play.Changed += Refresh;
        c.Services.Clock.Minute += OnMinute; // the day or week rolls over
        Refresh();
    }

    private string Period => _c.Settings.GetString("period", "week");

    private void OnMinute(DateTime now)
    {
        if (now.Hour == 0 && now.Minute == 0) Refresh();
    }

    private void Refresh()
    {
        _tabs.Children.Clear();
        foreach (var p in Periods)
        {
            var label = p switch { "today" => Loc.T("Hoy"), "week" => Loc.T("Semana"), "month" => Loc.T("Mes"), _ => Loc.T("Total") };
            var pill = Ui.Pill(label, () => { _c.Settings["period"] = p; _c.Save(); Refresh(); }, accent: p == Period);
            pill.Margin = new Thickness(0, 0, 6, 0);
            _tabs.Children.Add(pill);
        }

        var log = _c.Services.Session.Config.PlayTime;
        var today = DateTime.Today;
        DateTime? from = Period switch
        {
            "today" => today,
            "week" => PlayTime.WeekStart(today),
            "month" => PlayTime.MonthStart(today),
            _ => null,
        };
        var totals = PlayTime.Totals(log, from);
        var shown = (int)_c.Settings.GetDouble("count", 6);
        _list.Children.Clear();
        if (totals.Count == 0)
            _list.Children.Add(Ui.Text(Loc.T("Aún no hay partidas en este periodo. Se cuentan solas mientras el juego está delante."), wrap: true));
        var max = totals.Count > 0 ? totals[0].Time.TotalSeconds : 1;
        foreach (var t in totals.Take(shown)) _list.Children.Add(Row(t, t.Time.TotalSeconds / max));

        var now = _c.Services.Play?.Current;
        var top = PlayTime.Totals(log, PlayTime.MonthStart(today)).FirstOrDefault();
        var lines = new List<string>();
        if (now is { } n) lines.Add(Loc.T("Jugando ahora: {0}", n.Name));
        if (top is not null) lines.Add(Loc.T("Más jugado del mes: {0} ({1})", top.Name, PlayTime.Format(top.Time)));
        lines.Add(Loc.T("Total del periodo: {0}", PlayTime.Format(TimeSpan.FromSeconds(totals.Sum(x => x.Time.TotalSeconds)))));
        _footer.Text = string.Join("\n", lines);
    }

    private static FrameworkElement Row(PlayTotal t, double share)
    {
        var top = new DockPanel();
        var time = Ui.Text(PlayTime.Format(t.Time));
        SetDock(time, Dock.Right);
        top.Children.Add(time);
        top.Children.Add(new TextBlock { Text = t.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(top);
        row.Children.Add(Ui.Bar(share));
        return row;
    }

    public void Dispose()
    {
        if (_c.Services.Play is { } play) play.Changed -= Refresh;
        _c.Services.Clock.Minute -= OnMinute;
    }
}
