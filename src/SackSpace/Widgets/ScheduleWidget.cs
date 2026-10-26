using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>
/// Today's shift with what comes next (enter, break, leave), today's activities and,
/// unfolded, the week as a timetable: a coloured column per day, a row per hour, shifts
/// and activities as blocks, today highlighted. Folding shrinks the panel; unfolding
/// gives back the size it had. Redrawn once a minute.
/// </summary>
public sealed class ScheduleWidget : DockPanel, IDisposable
{
    private const double FoldedBase = 175, EventRow = 22, FoldedWidth = 340;
    // Activities have no length; an hour is room enough for their text.
    private const int EventSpan = 60;

    // One colour per day, Monday first, like a paper timetable.
    private static readonly Color[] DayColors =
    [
        Color.FromRgb(0xF7, 0xA1, 0x3A), Color.FromRgb(0x1E, 0xC1, 0xC6), Color.FromRgb(0xF5, 0xD0, 0x2A), Color.FromRgb(0xEF, 0x5B, 0x5B),
        Color.FromRgb(0xF0, 0x6B, 0xF0), Color.FromRgb(0x8B, 0xC3, 0x4A), Color.FromRgb(0x9C, 0x7C, 0xF4),
    ];
    private static readonly Brush Ink = Frozen(Color.FromRgb(0x1E, 0x1E, 0x1E));

    private readonly WidgetContext _c;
    private readonly TextBlock _phase = new() { FontSize = 13 };
    private readonly TextBlock _range = new() { FontSize = 30, FontWeight = FontWeights.Light };
    private readonly TextBlock _detail;
    private readonly StackPanel _today = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly Grid _week = new() { Margin = new Thickness(0, 10, 0, 0) };
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
        Children.Add(_week);

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
            s["expandedWidth"] = _c.Panel.Width;
            s["expanded"] = false;
            _c.Resize(FoldedBase + EventRow * ShiftSchedule.EventsOn(s, DateTime.Today).Count, FoldedWidth);
        }
        else
        {
            s["expanded"] = true;
            // The timetable needs room: seven columns.
            _c.Resize(s.GetDouble("expandedHeight", 520), Math.Max(640, s.GetDouble("expandedWidth", 760)));
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
        _week.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        // The timetable shows today's activities itself.
        _today.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;

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

    /// <summary>
    /// This week as a timetable, Monday first: a coloured header per day, activities without
    /// a time under it, then a quarter-hour grid from the earliest to the latest thing in the week.
    /// Rows are star-sized, so the panel's height sets the scale and nothing scrolls.
    /// </summary>
    private void DrawWeek(DateTime now)
    {
        _week.Children.Clear();
        _week.RowDefinitions.Clear();
        _week.ColumnDefinitions.Clear();
        var s = _c.Settings;
        var monday = ShiftSchedule.WeekStart(now);

        // Shift pieces in minutes of their column; an overnight shift goes on in the next column
        // (Offset 1440: its minutes count from the shift's own day).
        var pieces = new List<(int Day, int From, int To, int Offset, Shift Shift)>();
        for (var i = 0; i < 7; i++)
        {
            if (ShiftSchedule.For(s, ShiftSchedule.Week[i].Day) is not { } sh) continue;
            var from = (int)sh.Start.TotalMinutes;
            var to = from + (int)sh.Length.TotalMinutes;
            pieces.Add((i, from, Math.Min(to, 1440), 0, sh));
            if (to > 1440) pieces.Add(((i + 1) % 7, 0, to - 1440, 1440, sh));
        }
        var events = Enumerable.Range(0, 7).Select(i => ShiftSchedule.EventsOn(s, monday.AddDays(i))).ToList();
        var times = pieces.SelectMany(p => new[] { p.From, p.To })
            .Concat(events.SelectMany(d => d).Where(e => e.Time is not null)
                .SelectMany(e => new[] { (int)e.Time!.Value.TotalMinutes, Math.Min(1440, (int)e.Time.Value.TotalMinutes + EventSpan) }))
            .ToList();
        var first = times.Count > 0 ? times.Min() / 60 : 8;
        var last = times.Count > 0 ? (times.Max() + 59) / 60 : 18;
        if (last - first < 8) last = Math.Min(24, first + 8);
        if (last - first < 8) first = last - 8;
        var hours = last - first;
        int Row(int minutes) => 2 + (Math.Clamp(minutes, first * 60, last * 60) - first * 60) / 15;

        _week.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        for (var i = 0; i < 7; i++) _week.ColumnDefinitions.Add(new ColumnDefinition());
        _week.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _week.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var q = 0; q < hours * 4; q++) _week.RowDefinitions.Add(new RowDefinition());

        void Put(UIElement e, int row, int col, int rowSpan = 1, int colSpan = 1, int z = 0)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            Grid.SetRowSpan(e, Math.Max(1, rowSpan));
            Grid.SetColumnSpan(e, colSpan);
            Panel.SetZIndex(e, z);
            _week.Children.Add(e);
        }

        // Today's column, behind everything.
        var todayCol = 1 + (int)(now.Date - monday).TotalDays;
        var todayBack = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(1, 0, 1, 0) };
        todayBack.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        Put(todayBack, 0, todayCol, 2 + hours * 4, z: -1);

        // Hour lines and labels; a label every second hour when the day is long.
        var step = hours > 14 ? 2 : 1;
        for (var h = first; h < last; h++)
        {
            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), IsHitTestVisible = false };
            line.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
            Put(line, Row(h * 60), 1, 4, 7, z: -1);
            if ((h - first) % step != 0) continue;
            var label = new TextBlock { Text = $"{h:00}:00", FontSize = 10, Margin = new Thickness(0, -6, 5, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            Put(label, Row(h * 60), 0, 4);
        }

        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            var color = DayColors[i];
            var isToday = date == now.Date;

            var head = new Border
            {
                Background = Frozen(color), CornerRadius = new CornerRadius(7), Margin = new Thickness(2, 0, 2, 4), Padding = new Thickness(2, 3, 2, 3),
                ToolTip = Ui.Capitalize(date.ToString("dddd d 'de' MMMM", Ui.Culture)),
                Child = new TextBlock
                {
                    Text = $"{(isToday ? "Hoy" : ShiftSchedule.Week[i].Name[..3])} {date.Day}", Foreground = Ink, FontSize = 12,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            if (isToday)
            {
                head.BorderThickness = new Thickness(2);
                head.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
            }
            Put(head, 0, i + 1);

            var untimed = new StackPanel { Margin = new Thickness(3, 0, 3, 2) };
            foreach (var e in events[i].Where(e => e.Time is null)) untimed.Children.Add(EventLine(e, "TextBrush", 11));
            Put(untimed, 1, i + 1);

            foreach (var e in events[i].Where(e => e.Time is not null))
            {
                var at = (int)e.Time!.Value.TotalMinutes;
                var block = new Border
                {
                    Background = Frozen(color), CornerRadius = new CornerRadius(4), Margin = new Thickness(5, 1, 3, 1), Padding = new Thickness(3, 0, 3, 0),
                    ToolTip = e.Label + (e.Weekly is not null ? " · cada semana" : "") + "\nClic derecho para quitarla.",
                    ContextMenu = RemoveMenu(e),
                    Child = new TextBlock { Text = e.Label, Foreground = Ink, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
                };
                Put(block, Row(at), i + 1, Row(at + EventSpan) - Row(at), z: 2);
            }
        }

        foreach (var (day, from, to, offset, sh) in pieces)
        {
            var color = DayColors[day];
            var range = new TextBlock { Text = ShiftSchedule.Range(sh).Replace(" ", ""), FontSize = 10, Margin = new Thickness(4, 2, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            range.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var block = new Border
            {
                Background = Frozen(Color.FromArgb(0x48, color.R, color.G, color.B)), BorderBrush = Frozen(color), BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6), Margin = new Thickness(2, 1, 2, 1), IsHitTestVisible = false, Child = range,
            };
            Put(block, Row(from), day + 1, Row(to) - Row(from));

            if (sh.BreakStart is not { } bs || sh.BreakEnd is not { } be) continue;
            var bFrom = (int)bs.TotalMinutes + (bs < sh.Start ? 1440 : 0) - offset;
            var bTo = bFrom + (int)(be > bs ? be - bs : be + TimeSpan.FromDays(1) - bs).TotalMinutes;
            (bFrom, bTo) = (Math.Max(bFrom, from), Math.Min(bTo, to));
            if (bTo <= bFrom) continue;
            var pause = new Border
            {
                Background = Frozen(Color.FromArgb(0x60, 0, 0, 0)), CornerRadius = new CornerRadius(4), Margin = new Thickness(4, 0, 4, 0), IsHitTestVisible = false,
                Child = new TextBlock { Text = "comida", FontSize = 9.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Put(pause, Row(bFrom), day + 1, Row(bTo) - Row(bFrom), z: 1);
        }

        // Now, a line across today's column.
        var minute = (int)now.TimeOfDay.TotalMinutes;
        if (minute >= first * 60 && minute < last * 60)
        {
            var mark = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, 0, 0), IsHitTestVisible = false };
            mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            Put(mark, Row(minute), todayCol, z: 3);
        }
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
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
        t.ContextMenu = RemoveMenu(e);
        return t;
    }

    /// <summary>Its own menu wins over the panel's for a right click on the activity.</summary>
    private ContextMenu RemoveMenu(ScheduleEvent e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Ui.Item("Quitar «" + e.Text + "»", Glyphs.Delete, () =>
        {
            ScheduleEvent.WriteAll(_c.Settings, ScheduleEvent.ReadAll(_c.Settings).Where(x => x.Id != e.Id));
            _c.Save();
            Refresh();
        }));
        return menu;
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
