using Loc = SackSpace.Core.Loc;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>
/// OBS at a glance: live and recording with their time, upload rate and dropped frames, the
/// scene on air, and buttons to go live, record, save the replay and change scene. Talks to
/// obs-websocket on this PC with the port and password OBS keeps in its own settings.
/// </summary>
public sealed class ObsWidget : DockPanel, IDisposable
{
    private static readonly SolidColorBrush Live = new(Color.FromRgb(0xE6, 0x67, 0x67));
    private static readonly HttpClient SackClips = new() { Timeout = TimeSpan.FromSeconds(35) }; // its clip waits for OBS (up to 30 s)

    private readonly WidgetContext _c;
    private readonly TextBlock _stream = new() { FontSize = 15, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _streamInfo = Ui.Text("");
    private readonly TextBlock _record = new() { FontSize = 13.5 };
    private readonly WrapPanel _buttons = new() { Margin = new Thickness(-2, 10, 0, 0) };
    private readonly WrapPanel _scenes = new() { Margin = new Thickness(-2, 8, 0, 0) };
    private readonly TextBlock _error = Ui.Text("", wrap: true);
    private readonly Ellipse _dot = new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ObsClient? _obs;
    private DateTime _retryAt;
    private (long Bytes, DateTime At)? _lastBytes;
    private int _tick;
    private List<string> _sceneNames = [];
    private bool _disposed;

    public ObsWidget(WidgetContext c)
    {
        _c = c;
        var header = Ui.Header(Glyphs.Broadcast, "OBS");
        SetDock(header, Dock.Top);
        Children.Add(header);

        var body = new StackPanel();
        var live = new StackPanel { Orientation = Orientation.Horizontal };
        live.Children.Add(_dot);
        live.Children.Add(_stream);
        body.Children.Add(live);
        body.Children.Add(_streamInfo);
        _record.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(_record);
        body.Children.Add(_buttons);
        if (c.Settings.GetBool("scenes", true)) body.Children.Add(_scenes);
        body.Children.Add(_error);
        Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        ShowOffline(Loc.T("Conectando con OBS…"));
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        c.Services.Governor.PausedChanged += OnPaused;
        Poll();
    }

    // A full-screen game or video hides the desktop: no need to ask OBS every 2 s meanwhile.
    private void OnPaused(bool paused)
    {
        if (paused) _timer.Stop();
        else { _timer.Start(); Poll(); }
    }

    private async Task<JsonElement> Ask(string type, JsonObject? data = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (_obs is null)
            {
                var server = ObsServer.Read(ObsServer.DefaultConfigPath);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                _obs = await ObsClient.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}"), server.Password, cts.Token);
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _obs.RequestAsync(type, data, timeout.Token);
        }
        catch (Exception e) when (!IsRefusal(e)) // the connection broke or never opened: start again next time
        {
            _obs?.Dispose();
            _obs = null;
            throw;
        }
        finally { _gate.Release(); }
    }

    // OBS answered but said no (no replay buffer set up, for one): the connection is fine.
    private static bool IsRefusal(Exception e) => e is ObsException && !e.Message.StartsWith("OBS ", StringComparison.Ordinal);

    private async void Poll()
    {
        if (_disposed || DateTime.UtcNow < _retryAt) return;
        try
        {
            var stream = await Ask("GetStreamStatus");
            var record = await Ask("GetRecordStatus");
            var scene = await Ask("GetCurrentProgramScene");
            if (_tick++ % 5 == 0 || _sceneNames.Count == 0)
            {
                var list = await Ask("GetSceneList");
                // OBS lists scenes bottom-up; the panel shows them in the order of OBS's dock.
                _sceneNames = list.GetProperty("scenes").EnumerateArray().Select(s => s.GetProperty("sceneName").GetString() ?? "").Reverse().ToList();
            }
            bool? replay = null;
            try { replay = (await Ask("GetReplayBufferStatus")).GetProperty("outputActive").GetBoolean(); }
            catch (ObsException e) when (IsRefusal(e)) { } // no replay buffer in this profile
            Show(stream, record, scene.GetProperty("currentProgramSceneName").GetString() ?? "", replay);
        }
        catch (Exception ex)
        {
            _retryAt = DateTime.UtcNow.AddSeconds(10);
            _lastBytes = null;
            ShowOffline(ex is ObsException ? ex.Message : Loc.T("No se pudo hablar con OBS."));
            if (ex is not ObsException) Log.Warn("OBS", ex);
        }
    }

    private void ShowOffline(string why)
    {
        _dot.Fill = Ui.Brush("TrackBrush");
        _stream.Text = Loc.T("Sin conexión");
        _streamInfo.Text = "";
        _record.Text = "";
        _buttons.Children.Clear();
        _scenes.Children.Clear();
        _error.Text = why;
        _error.Margin = new Thickness(0, 4, 0, 0);
        Collapse();
    }

    private void Show(JsonElement stream, JsonElement record, string scene, bool? replay)
    {
        _error.Text = "";
        var live = stream.GetProperty("outputActive").GetBoolean();
        _dot.Fill = live ? Live : Ui.Brush("TrackBrush");
        _stream.Text = live ? "En directo  " + ObsNumbers.Clock(stream.GetProperty("outputTimecode").GetString()) : Loc.T("Sin directo");
        if (live)
        {
            var bytes = stream.GetProperty("outputBytes").GetInt64();
            var now = DateTime.UtcNow;
            var rate = _lastBytes is { } last ? ObsNumbers.Megabits(last.Bytes, last.At, bytes, now) : null;
            _lastBytes = (bytes, now);
            var dropped = ObsNumbers.Dropped(stream.GetProperty("outputSkippedFrames").GetInt64(), stream.GetProperty("outputTotalFrames").GetInt64());
            _streamInfo.Text = (rate is { } r ? string.Format(Ui.Culture, "{0:0.0} Mb/s · ", r) : "")
                               + string.Format(Ui.Culture, Loc.T("{0:0.##} % de cuadros perdidos"), dropped)
                               + (dropped >= 1 ? Loc.T(" (la conexión no da abasto)") : "");
        }
        else
        {
            _lastBytes = null;
            _streamInfo.Text = "";
        }

        var recording = record.GetProperty("outputActive").GetBoolean();
        var paused = recording && record.TryGetProperty("outputPaused", out var p) && p.GetBoolean();
        _record.Text = recording ? Loc.T(paused ? "Grabación en pausa" : "Grabando") + "  " + ObsNumbers.Clock(record.GetProperty("outputTimecode").GetString()) : Loc.T("Sin grabar");
        Collapse();

        _buttons.Children.Clear();
        _buttons.Children.Add(Button(live ? Loc.T("Terminar directo") : Loc.T("Iniciar directo"), Glyphs.Broadcast, () => ToggleStream(live), accent: !live));
        _buttons.Children.Add(Button(recording ? Loc.T("Detener grabación") : Loc.T("Grabar"), Glyphs.Record, () => Send("ToggleRecord")));
        if (replay == true) _buttons.Children.Add(Button(Loc.T("Guardar repetición"), Glyphs.Clock, () => Send("SaveReplayBuffer", Loc.T("Repetición guardada."))));
        if (live || replay == true) _buttons.Children.Add(Button("Clip", Glyphs.Star, () => SaveClip(replay == true)));

        _scenes.Children.Clear();
        foreach (var name in _sceneNames)
        {
            var on = name == scene;
            var b = Button(name, null, () => Send("SetCurrentProgramScene", null, new JsonObject { ["sceneName"] = name }), accent: on);
            b.ToolTip = on ? Loc.T("En el aire") : Loc.T("Cambiar a esta escena");
            _scenes.Children.Add(b);
        }
    }

    // Empty lines take no room (offline, nothing recording).
    private void Collapse()
    {
        foreach (var t in new[] { _streamInfo, _record, _error })
            t.Visibility = t.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Button Button(string text, string? glyph, Action click, bool accent = false)
    {
        var b = Ui.Pill(text, click, accent, glyph);
        b.Margin = new Thickness(2, 0, 6, 6);
        return b;
    }

    private void ToggleStream(bool live)
    {
        if (_c.Settings.GetBool("confirmStream", true))
        {
            var answer = MessageBox.Show(Window.GetWindow(this), live ? Loc.T("¿Terminar el directo?") : Loc.T("¿Iniciar el directo?"), "SackSpace · OBS",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }
        Send("ToggleStream");
    }

    private async void Send(string request, string? done = null, JsonObject? data = null)
    {
        try
        {
            await Ask(request, data);
            if (done is not null) _c.Toast(done);
            _tick = 0;
            Poll();
        }
        catch (Exception ex)
        {
            _c.Toast("OBS: " + ex.Message);
        }
    }

    // Same as !clip in the chat: SackClips saves OBS's replay and lists it in its panel. Without
    // SackClips open, the replay is saved straight from OBS so the moment is not lost.
    private async void SaveClip(bool replay)
    {
        var url = _c.Settings.GetString("sackclips", "http://127.0.0.1:28790").TrimEnd('/') + "/api/clip";
        try
        {
            using var res = await SackClips.PostAsync(url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            var ok = res.IsSuccessStatusCode && JsonNode.Parse(await res.Content.ReadAsStringAsync())?["ok"]?.GetValue<bool>() == true;
            _c.Toast(ok ? Loc.T("Clip guardado en SackClips.") : Loc.T("SackClips no pudo guardar el clip: mira su panel."));
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Info($"SackClips no responde en {url}: {ex.Message}");
        }
        if (replay) Send("SaveReplayBuffer", Loc.T("SackClips no está abierto: repetición guardada en OBS."));
        else _c.Toast(Loc.T("Abre el panel de SackClips o activa la repetición de OBS para guardar clips."));
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _c.Services.Governor.PausedChanged -= OnPaused;
        _obs?.Dispose();
    }
}
