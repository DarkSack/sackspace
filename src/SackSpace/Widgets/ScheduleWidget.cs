using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>
/// Today's shift with what comes next (enter, break, leave), today's activities and,
/// unfolded, the week with today highlighted. Folding shrinks the panel; unfolding
/// gives back the height it had. Redrawn once a minute.
/// </summary>
public sealed class ScheduleWidget : DockPanel, IDisposable
{
    private const double FoldedBase = 175, EventRow = 22;

    private readonly WidgetContext _c;
    private readonly TextBlock _phase = new() { FontSize = 13 };
    private readonly TextBlock _range = new() { FontSize = 30, FontWeight = FontWeights.Light };
    private readonly TextBlock _detail;
    private readonly StackPanel _today = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _week = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly ScrollViewer _weekScroll;
    private readonly Button _fold;

    public ScheduleWidget(WidgetContext c)
    {
        _c = c;
        if (ShiftSchedule.DropPast(c.Settings, DateTime.Today)) c.Save();

        _fold = Ui.IconButton(Glyphs.ChevronUp, "", ToggleFold, 28);
        var header = Ui.Header(Glyphs.Work, "Horario",
            Ui.IconButton(Glyphs.Add, "Añadir una actividad", () => c.Host.OpenSheet(new AddActivitySheet(c, Refresh)), 28), _fold);
        SetDock(header, Dock.Top);
        Children.Add(header);

        _phase.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _range.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _detail = Ui.Text("", wrap: true);
        var now = new StackPanel();
        now.Children.Add(_phase);
        now.Children.Add(_range);
        now.Children.Add(_detail);
        now.Children.Add(_today);
        SetDock(now, Dock.Top);
        Children.Add(now);
        _weekScroll = new ScrollViewer { Content = _week, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(_weekScroll);

        c.Services.Clock.Minute += Refresh;
        Refresh();
    }

    private bool Expanded => _c.Settings.GetBool("expanded", true);

    private void ToggleFold()
    {
        var s = _c.Settings;
        if (Expanded)
        {
            s["expandedHeight"] = _c.Panel.Height;
            s["expanded"] = false;
            _c.Resize(FoldedBase + EventRow * ShiftSchedule.EventsOn(s, DateTime.Today).Count);
        }
        else
        {
            s["expanded"] = true;
            _c.Resize(s.GetDouble("expandedHeight", 420));
        }
        _c.Save();
        Refresh();
    }

    private void Refresh(DateTime _) => Refresh();

    private void Refresh()
    {
        var now = DateTime.Now;
        var s = _c.Settings;
        var expanded = Expanded;
        _fold.Content = expanded ? Glyphs.ChevronUp : Glyphs.ChevronDown;
        _fold.ToolTip = expanded ? "Retraer: solo hoy" : "Extender: toda la semana";
        _weekScroll.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        if (ShiftSchedule.Week.All(d => string.IsNullOrWhiteSpace(s.GetString(d.Key, ""))))
        {
            _phase.Text = "Sin horario";
            _range.Visibility = Visibility.Collapsed;
            _detail.Text = "Escribe tu turno de cada día en las opciones del widget (clic derecho › Opciones), por ejemplo «09:00-18:00 comida 14:00-15:00» o «Descanso». Las actividades se añaden con el +.";
        }
        else
        {
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
        }

        _today.Children.Clear();
        foreach (var e in ShiftSchedule.EventsOn(s, now))
        {
            var past = e.Time is { } t && t < now.TimeOfDay;
            _today.Children.Add(EventLine(e, past ? "TextTertiaryBrush" : "TextBrush", 13));
        }
        if (expanded) DrawWeek(now);
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

    /// <summary>This week, Monday first; today on an accent background with its activities.</summary>
    private void DrawWeek(DateTime now)
    {
        _week.Children.Clear();
        var monday = ShiftSchedule.WeekStart(now);
        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            var (_, key, name) = ShiftSchedule.Week[i];
            var text = _c.Settings.GetString(key, "").Trim();
            var shift = ShiftSchedule.Parse(text);
            var isToday = date == now.Date;
            var brush = isToday ? "AccentBrush" : shift is null ? "TextTertiaryBrush" : "TextSecondaryBrush";

            var line = new DockPanel();
            var label = new TextBlock { Text = $"{name[..3]} {date.Day}", Width = 56, FontSize = 13, FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal };
            var value = new TextBlock
            {
                Text = shift is not null ? ShiftSchedule.Range(shift) + BreakText(shift) : text.Length > 0 ? text : "Descanso",
                FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, brush);
            value.SetResourceReference(TextBlock.ForegroundProperty, brush);
            SetDock(label, Dock.Left);
            line.Children.Add(label);
            line.Children.Add(value);

            var day = new StackPanel();
            day.Children.Add(line);
            // Today's activities are already listed above.
            if (!isToday)
                foreach (var e in ShiftSchedule.EventsOn(_c.Settings, date))
                {
                    var ev = EventLine(e, "TextSecondaryBrush", 12);
                    ev.Margin = new Thickness(56, 1, 0, 0);
                    day.Children.Add(ev);
                }

            var row = new Border { Child = day, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(-8, 0, -8, 2), CornerRadius = new CornerRadius(8) };
            if (isToday) row.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            _week.Children.Add(row);
        }
    }

    /// <summary>An activity; right click to remove it.</summary>
    private FrameworkElement EventLine(ScheduleEvent e, string brush, double size)
    {
        var t = new TextBlock
        {
            Text = (e.Weekly is not null ? "↻ " : "• ") + e.Label, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = e.Weekly is not null ? "Cada semana. Clic derecho para quitarla." : "Clic derecho para quitarla.",
            Background = System.Windows.Media.Brushes.Transparent,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        // Its own menu wins over the panel's for a right click on this line.
        t.ContextMenu = new ContextMenu();
        t.ContextMenu.Items.Add(Ui.Item("Quitar «" + e.Text + "»", Glyphs.Delete, () =>
        {
            ScheduleEvent.WriteAll(_c.Settings, ScheduleEvent.ReadAll(_c.Settings).Where(x => x.Id != e.Id));
            _c.Save();
            Refresh();
        }));
        return t;
    }

    public void Dispose() => _c.Services.Clock.Minute -= Refresh;
}

/// <summary>"Nueva actividad": text, optional time, the day (this week or every week).</summary>
public sealed class AddActivitySheet : SheetBase
{
    public AddActivitySheet(WidgetContext c, Action added) : base(c.Host, Glyphs.Add, "Nueva actividad", 420)
    {
        var text = new TextBox { Style = Ui.Style("FieldTextBox"), MaxLength = 60, Margin = new Thickness(0, 0, 0, 12) };
        var time = new TextBox { Style = Ui.Style("FieldTextBox"), MaxLength = 5, Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        Body.Children.Add(Label("Qué"));
        Body.Children.Add(Ui.WithPlaceholder(text, "Junta, coaching, entregar reporte…"));
        Body.Children.Add(Label("Hora (opcional)"));
        Body.Children.Add(Ui.WithPlaceholder(time, "10:30"));

        // Days from today on, so "Mar" is the next Tuesday, not last week's.
        Body.Children.Add(Label("Día"));
        var today = DateTime.Today;
        var picked = today;
        var days = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        for (var i = 0; i < 7; i++)
        {
            var date = today.AddDays(i);
            var name = i == 0 ? "Hoy" : i == 1 ? "Mañana" : Ui.Capitalize(date.ToString("ddd d", Ui.Culture).Replace(".", ""));
            var chip = Ui.Pill(name, () => { }, accent: i == 0);
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Click += (_, _) =>
            {
                picked = date;
                foreach (var b in days.Children.OfType<Button>()) b.Style = Ui.Style(b == chip ? "AccentPillButton" : "PillButton");
            };
            days.Children.Add(chip);
        }
        Body.Children.Add(days);
        var weekly = new CheckBox { Content = "Repetir cada semana", Style = Ui.Style("ToggleSwitch"), FontSize = 14, Margin = new Thickness(0, 0, 0, 12) };
        Body.Children.Add(weekly);

        var error = Ui.Text("", wrap: true);
        error.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x7A, 0x7A));
        Body.Children.Add(error);

        void Add()
        {
            if (text.Text.Trim().Length == 0) { error.Text = "Escribe qué es la actividad."; return; }
            TimeSpan? at = null;
            if (time.Text.Trim().Length > 0)
            {
                if (!AutoSwitch.TryParseTime(time.Text, out var t)) { error.Text = "La hora va así: 10:30."; return; }
                at = t;
            }
            var e = new ScheduleEvent(Guid.NewGuid().ToString("N")[..8], text.Text.Trim(), at,
                weekly.IsChecked == true ? null : picked, weekly.IsChecked == true ? ShiftSchedule.Key(picked.DayOfWeek) : null);
            ScheduleEvent.WriteAll(c.Settings, [.. ScheduleEvent.ReadAll(c.Settings), e]);
            c.Save();
            added();
            Host.CloseSheet();
        }
        text.KeyDown += (_, k) => { if (k.Key == Key.Enter) Add(); };
        time.KeyDown += (_, k) => { if (k.Key == Key.Enter) Add(); };
        var ok = Ui.Pill("Añadir", Add, accent: true, glyph: Glyphs.Add);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        Body.Children.Add(ok);
        Loaded += (_, _) => text.Focus();
    }

    private static TextBlock Label(string s) => new() { Text = s, FontSize = 13, Margin = new Thickness(0, 4, 0, 8) };
}
