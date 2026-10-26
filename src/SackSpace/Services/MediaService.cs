using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace SackSpace.Services;

public sealed record MediaState(
    string Title,
    string Artist,
    string AppName,
    bool IsPlaying,
    bool CanPrevious,
    bool CanNext,
    ImageSource? Artwork,
    TimeSpan Position,
    TimeSpan Duration,
    DateTime PositionAt)
{
    /// <summary>Players report the position now and then; this estimates it now.</summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            var p = IsPlaying ? Position + (DateTime.UtcNow - PositionAt) : Position;
            return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
        }
    }
}

/// <summary>
/// What is playing in Windows (Spotify, browsers, any app on the system media
/// controls). Event-driven: nothing runs while the music does not change. Started
/// by the first player widget.
/// </summary>
public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _trackKey = "";
    private ImageSource? _artwork;
    private int _refreshVersion;
    private bool _starting;

    public MediaState? State { get; private set; }
    public event Action? Changed;

    public async void EnsureStarted()
    {
        if (_manager is not null || _starting) return;
        _starting = true;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => OnUi(() => Attach(_manager.GetCurrentSession()));
            Attach(_manager.GetCurrentSession());
        }
        catch (Exception ex)
        {
            Log.Warn("Los controles multimedia de Windows no están disponibles", ex);
        }
        finally { _starting = false; }
    }

    public async void PlayPause() => await Try(s => s.TryTogglePlayPauseAsync().AsTask());
    public async void Next() => await Try(s => s.TrySkipNextAsync().AsTask());
    public async void Previous() => await Try(s => s.TrySkipPreviousAsync().AsTask());

    private async Task Try(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        if (_session is null) return;
        try { await action(_session); }
        catch (Exception ex) { Log.Warn("El reproductor no aceptó la orden", ex); }
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
            _session.TimelinePropertiesChanged -= OnSessionChanged;
        }
        _session = session;
        if (session is not null)
        {
            session.MediaPropertiesChanged += OnSessionChanged;
            session.PlaybackInfoChanged += OnSessionChanged;
            session.TimelinePropertiesChanged += OnSessionChanged;
        }
        _ = RefreshAsync();
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => OnUi(() => _ = RefreshAsync());

    private async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        var session = _session;
        if (session is null)
        {
            State = null;
            Changed?.Invoke();
            return;
        }
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _refreshVersion) return;
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            var key = $"{session.SourceAppUserModelId}|{props?.Title}|{props?.Artist}";
            if (key != _trackKey)
            {
                _trackKey = key;
                _artwork = null;
                _ = LoadArtworkAsync(session, key);
            }

            State = new MediaState(
                props?.Title ?? "",
                props?.Artist ?? "",
                FriendlyAppName(session.SourceAppUserModelId),
                playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                playback?.Controls.IsPreviousEnabled ?? false,
                playback?.Controls.IsNextEnabled ?? false,
                _artwork,
                timeline?.Position ?? TimeSpan.Zero,
                timeline is null ? TimeSpan.Zero : timeline.EndTime - timeline.StartTime,
                timeline?.LastUpdatedTime.UtcDateTime ?? DateTime.UtcNow);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo leer lo que se está reproduciendo", ex);
        }
    }

    /// <summary>Players often publish the title first and the cover a moment later: retried for a few seconds.</summary>
    private async Task LoadArtworkAsync(GlobalSystemMediaTransportControlsSession session, string key)
    {
        for (var attempt = 0; attempt < 5 && key == _trackKey; attempt++)
        {
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (props?.Thumbnail is { } thumb)
                {
                    using var stream = (await thumb.OpenReadAsync()).AsStreamForRead();
                    var copy = new MemoryStream();
                    await stream.CopyToAsync(copy);
                    copy.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = copy;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 192;
                    bmp.EndInit();
                    bmp.Freeze();
                    if (key != _trackKey) return;
                    _artwork = bmp;
                    if (State is not null) State = State with { Artwork = bmp };
                    Changed?.Invoke();
                    return;
                }
            }
            catch (Exception ex) when (attempt == 4)
            {
                Log.Warn("No se pudo leer la carátula", ex);
            }
            catch { }
            await Task.Delay(1000);
        }
    }

    /// <summary>"Spotify.exe" or "SpotifyAB.SpotifyMusic_…!Spotify" → "Spotify".</summary>
    public static string FriendlyAppName(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "";
        var name = aumid.Contains('!') ? aumid[(aumid.LastIndexOf('!') + 1)..] : aumid;
        name = Path.GetFileNameWithoutExtension(name.Replace('/', '\\').Split('\\')[^1]);
        return name.ToLowerInvariant() switch
        {
            "chrome" => "Chrome",
            "msedge" or "microsoftedge" => "Edge",
            "brave" => "Brave",
            "firefox" => "Firefox",
            "opera" => "Opera",
            "spotify" => "Spotify",
            "app" => "Apple Music",
            "microsoft.zunemusic" or "music.ui" or "microsoft.media.player" => "Reproductor multimedia",
            _ => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : aumid,
        };
    }

    private static void OnUi(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);
}
