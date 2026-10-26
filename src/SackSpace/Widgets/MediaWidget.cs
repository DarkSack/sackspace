using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SackSpace.Core.Model;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>What Windows says is playing, with its cover and controls.</summary>
public sealed class MediaWidget : Grid, IDisposable
{
    private readonly WidgetContext _c;
    private readonly Border _art = new() { CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 16, 0), MaxWidth = 150 };
    private readonly TextBlock _artGlyph;
    private readonly TextBlock _title = new() { FontSize = 15 };
    private readonly TextBlock _artist = new();
    private readonly TextBlock _app = new() { FontSize = 11 };
    private readonly Border _progress = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _track = new() { Height = 4, Margin = new Thickness(0, 10, 0, 8) };
    private readonly Button _play, _prev, _next;
    private readonly StackPanel _controls = new() { Orientation = Orientation.Horizontal };
    private bool _ticking;

    public MediaWidget(WidgetContext c)
    {
        _c = c;
        _title.Style = Ui.Style("Title");
        _artist.Style = Ui.Style("Caption");
        _app.Style = Ui.Style("Caption");
        _app.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

        // Square cover as tall as the widget.
        _art.SetBinding(WidthProperty, new Binding(nameof(ActualHeight)) { Source = _art });
        _art.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        _artGlyph = Ui.Glyph(Glyphs.Music, 26, "AccentBrush");
        _art.Child = _artGlyph;

        var trackBg = new Border { CornerRadius = new CornerRadius(2) };
        trackBg.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        _progress.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _track.Children.Add(trackBg);
        _track.Children.Add(_progress);
        _track.SizeChanged += (_, _) => UpdateProgress();

        var media = c.Services.Media;
        _prev = Ui.IconButton(Glyphs.Previous, "Anterior", media.Previous, 34);
        _play = new Button { Style = Ui.Style("AccentIconButton"), Content = Glyphs.Play, ToolTip = "Reproducir o pausar", Margin = new Thickness(6, 0, 6, 0) };
        _play.Click += (_, _) => media.PlayPause();
        _next = Ui.IconButton(Glyphs.Next, "Siguiente", media.Next, 34);
        _controls.Children.Add(_prev);
        _controls.Children.Add(_play);
        _controls.Children.Add(_next);

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());
        Children.Add(_art);
        var info = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var texts = new StackPanel();
        texts.Children.Add(_title);
        texts.Children.Add(_artist);
        texts.Children.Add(_app);
        DockPanel.SetDock(texts, Dock.Top);
        DockPanel.SetDock(_track, Dock.Top);
        info.Children.Add(texts);
        info.Children.Add(_track);
        info.Children.Add(_controls);
        SetColumn(info, 1);
        Children.Add(info);

        media.Changed += Update;
        media.EnsureStarted();
        Update();
    }

    private void Update()
    {
        var s = _c.Services.Media.State;
        if (s is null)
        {
            _title.Text = "No suena nada";
            _artist.Text = "Pon música en Spotify, el navegador o cualquier app.";
            _artist.TextWrapping = TextWrapping.Wrap;
            _app.Text = "";
            _art.Background = null;
            _art.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            _artGlyph.Visibility = Visibility.Visible;
            _controls.Visibility = _track.Visibility = Visibility.Collapsed;
            Tick(false);
            return;
        }
        _title.Text = s.Title.Length > 0 ? s.Title : "Sin título";
        _artist.Text = s.Artist;
        _artist.TextWrapping = TextWrapping.NoWrap;
        _app.Text = s.AppName;
        if (s.Artwork is not null)
        {
            _art.Background = new System.Windows.Media.ImageBrush(s.Artwork) { Stretch = System.Windows.Media.Stretch.UniformToFill };
            _artGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            _art.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            _artGlyph.Visibility = Visibility.Visible;
        }
        _controls.Visibility = Visibility.Visible;
        _play.Content = s.IsPlaying ? Glyphs.Pause : Glyphs.Play;
        _prev.IsEnabled = s.CanPrevious;
        _next.IsEnabled = s.CanNext;
        _track.Visibility = s.Duration > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress();
        Tick(s.IsPlaying && s.Duration > TimeSpan.Zero);
    }

    /// <summary>The bar moves once a second, only while something plays.</summary>
    private void Tick(bool on)
    {
        if (on == _ticking) return;
        _ticking = on;
        if (on) _c.Services.Clock.Second += OnSecond;
        else _c.Services.Clock.Second -= OnSecond;
    }

    private void OnSecond(DateTime _) => UpdateProgress();

    private void UpdateProgress()
    {
        var s = _c.Services.Media.State;
        if (s is null || s.Duration <= TimeSpan.Zero) return;
        _progress.Width = _track.ActualWidth * Math.Clamp(s.CurrentPosition / s.Duration, 0, 1);
    }

    public void Dispose()
    {
        _c.Services.Media.Changed -= Update;
        Tick(false);
    }
}
