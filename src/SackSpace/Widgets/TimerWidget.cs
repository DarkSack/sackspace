using Loc = SackSpace.Core.Loc;
using System.Windows;
using System.Windows.Controls;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>
/// Countdown, Pomodoro or stopwatch. The state lives in the panel (TimerLogic), so it
/// keeps going on another workspace; TimerService ends it and notifies.
/// </summary>
public sealed class TimerWidget : Grid, IDisposable
{
    private readonly WidgetContext _c;
    private readonly TextBlock _time = new() { FontSize = 64, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _caption;
    private readonly Button _play;
    private bool _ticking;

    public TimerWidget(WidgetContext c)
    {
        _c = c;
        var s = c.Settings;
        // Another mode or other minutes in the options: start clean.
        var setup = $"{TimerLogic.Mode(s)}|{s.GetDouble("minutes", 10)}|{s.GetDouble("work", 25)}|{s.GetDouble("break", 5)}";
        if (s.GetString("setup", "") != setup)
        {
            TimerLogic.Reset(s);
            s.Remove("phase");
            s.Remove("rounds");
            s["setup"] = setup;
            c.Services.Timers.Reschedule();
        }

        _time.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _caption = Ui.Text("");
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        _play = Ui.IconButton(Glyphs.Play, Loc.T("Empezar"), Toggle, 40);
        _play.Style = Ui.Style("AccentIconButton");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_play);
        var reset = Ui.IconButton(Glyphs.Refresh, Loc.T("Reiniciar"), Reset, 40);
        reset.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(reset);

        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(new Viewbox { Child = _time, StretchDirection = StretchDirection.Both });
        SetRow(_caption, 1);
        Children.Add(_caption);
        SetRow(buttons, 2);
        Children.Add(buttons);

        c.Services.Timers.Finished += OnFinished;
        Refresh();
    }

    private void Toggle()
    {
        var s = _c.Settings;
        if (TimerLogic.IsRunning(s)) TimerLogic.Pause(s, DateTime.UtcNow);
        else TimerLogic.Start(s, DateTime.UtcNow);
        Changed();
    }

    private void Reset()
    {
        TimerLogic.Reset(_c.Settings);
        Changed();
    }

    private void Changed()
    {
        _c.Save();
        _c.Services.Timers.Reschedule();
        Refresh();
    }

    private void OnFinished(PanelState p)
    {
        if (p == _c.Panel) Refresh();
    }

    private void Refresh()
    {
        var s = _c.Settings;
        var running = TimerLogic.IsRunning(s);
        _time.Text = TimerLogic.Format(TimerLogic.Shown(s, DateTime.UtcNow));
        _play.Content = running ? Glyphs.Pause : Glyphs.Play;
        _play.ToolTip = running ? Loc.T("Pausar") : Loc.T("Empezar");
        _caption.Text = TimerLogic.Mode(s) switch
        {
            TimerLogic.Pomodoro => (TimerLogic.OnBreak(s) ? Loc.T("Descanso") : Loc.T("Concentración")) + (TimerLogic.Rounds(s) > 0 ? Loc.T(" · {0} hechos", TimerLogic.Rounds(s)) : ""),
            TimerLogic.Stopwatch => Loc.T("Cronómetro"),
            _ => running ? Loc.T("Termina a las ") + TimerLogic.EndsAt(s)!.Value.ToLocalTime().ToString("HH:mm", Ui.Culture) : Loc.T("Temporizador"),
        };
        // Ticks every second only while running and on screen.
        if (running == _ticking) return;
        _ticking = running;
        if (running) _c.Services.Clock.Second += Tick;
        else _c.Services.Clock.Second -= Tick;
    }

    private void Tick(DateTime _) => _time.Text = TimerLogic.Format(TimerLogic.Shown(_c.Settings, DateTime.UtcNow));

    public void Dispose()
    {
        _c.Services.Timers.Finished -= OnFinished;
        _c.Services.Clock.Second -= Tick;
    }
}
