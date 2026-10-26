using System.Windows;
using System.Windows.Controls;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>Today's shift with what comes next (enter, break, leave) and the week below. Redrawn once a minute.</summary>
public sealed class ScheduleWidget : DockPanel, IDisposable
{
    private readonly WidgetContext _c;
    private readonly TextBlock _phase = new() { FontSize = 13 };
    private readonly TextBlock _range = new() { FontSize = 30, FontWeight = FontWeights.Light };
    private readonly TextBlock _detail;
    private readonly StackPanel _week = new() { Margin = new Thickness(0, 12, 0, 0) };

    public ScheduleWidget(WidgetContext c)
    {
        _c = c;
        var header = Ui.Header(Glyphs.Work, "Horario");
        SetDock(header, Dock.Top);
        Children.Add(header);

        _phase.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _range.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _detail = Ui.Text("", wrap: true);
        var today = new StackPanel();
        today.Children.Add(_phase);
        today.Children.Add(_range);
        today.Children.Add(_detail);
        SetDock(today, Dock.Top);
        Children.Add(today);
        if (c.Settings.GetBool("week", true))
            Children.Add(new ScrollViewer { Content = _week, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        c.Services.Clock.Minute += Refresh;
        Refresh(DateTime.Now);
    }

    private void Refresh(DateTime now)
    {
        var s = _c.Settings;
        if (ShiftSchedule.Week.All(d => string.IsNullOrWhiteSpace(s.GetString(d.Key, ""))))
        {
            _phase.Text = "Sin horario";
            _range.Text = "";
            _range.Visibility = Visibility.Collapsed;
            _detail.Text = "Escribe tu turno de cada día en las opciones del widget (clic derecho › Opciones), por ejemplo «09:00-18:00 comida 14:00-15:00» o «Descanso».";
            _week.Children.Clear();
            return;
        }

        var status = ShiftSchedule.Status(s, now);
        string At(DateTime? t) => t?.ToString("HH:mm", Ui.Culture) ?? "";
        string In(DateTime? t) => t is { } v ? ShiftSchedule.Span(v - now) : "";
        _range.Visibility = status.Shift is null ? Visibility.Collapsed : Visibility.Visible;
        _range.Text = status.Shift is { } sh ? ShiftSchedule.Range(sh) : "";
        (_phase.Text, _detail.Text) = status.Phase switch
        {
            ShiftPhase.Before => ("Hoy entras", $"Faltan {In(status.Next)}{BreakText(status.Shift)}"),
            ShiftPhase.Working when status.Shift?.BreakStart is { } && status.Next?.TimeOfDay == status.Shift.BreakStart
                => ("En turno", $"Comida en {In(status.Next)} ({At(status.Next)})"),
            ShiftPhase.Working => ("En turno", $"Sales en {In(status.Next)} ({At(status.Next)})"),
            ShiftPhase.OnBreak => ("En la comida", $"Vuelves a las {At(status.Next)}, en {In(status.Next)}"),
            ShiftPhase.Done => ("Turno terminado", NextShift(now)),
            _ => ("Hoy descansas", NextShift(now)),
        };
        DrawWeek(now);
    }

    private static string BreakText(Shift? s) =>
        s?.BreakStart is { } b && s.BreakEnd is { } e ? $" · comida {b:hh\\:mm}–{e:hh\\:mm}" : "";

    /// <summary>"Mañana: 09:00 – 18:00" or the next working day.</summary>
    private string NextShift(DateTime now)
    {
        for (var i = 1; i <= 7; i++)
        {
            var day = now.Date.AddDays(i);
            if (ShiftSchedule.For(_c.Settings, day.DayOfWeek) is not { } s) continue;
            var when = i == 1 ? "Mañana" : Ui.Capitalize(day.ToString("dddd", Ui.Culture));
            return $"{when}: {ShiftSchedule.Range(s)}";
        }
        return "";
    }

    private void DrawWeek(DateTime now)
    {
        _week.Children.Clear();
        foreach (var (day, key, name) in ShiftSchedule.Week)
        {
            var text = _c.Settings.GetString(key, "").Trim();
            var shift = ShiftSchedule.Parse(text);
            var isToday = day == now.DayOfWeek;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var label = new TextBlock { Text = name[..3], Width = 44, FontSize = 13, FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal };
            var value = new TextBlock
            {
                Text = shift is not null ? ShiftSchedule.Range(shift) + BreakText(shift) : text.Length > 0 ? text : "Descanso",
                FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
            };
            var brush = isToday ? "AccentBrush" : shift is null ? "TextTertiaryBrush" : "TextSecondaryBrush";
            label.SetResourceReference(TextBlock.ForegroundProperty, brush);
            value.SetResourceReference(TextBlock.ForegroundProperty, brush);
            SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(value);
            _week.Children.Add(row);
        }
    }

    public void Dispose() => _c.Services.Clock.Minute -= Refresh;
}
