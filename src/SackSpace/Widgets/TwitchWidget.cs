using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SackSpace.Core;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>
/// The Twitch channel: live or not, viewers, followers (and the new ones), and the title
/// and category, which can be changed from here. Signs in with a code at twitch.tv/activate;
/// looks again once a minute while on screen.
/// </summary>
public sealed class TwitchWidget : DockPanel, IDisposable
{
    private static readonly SolidColorBrush LiveBrush = new(Color.FromRgb(0xE6, 0x67, 0x67));
    private readonly WidgetContext _c;
    private readonly StackPanel _body = new();
    private readonly CancellationTokenSource _cts = new();
    private TwitchChannel? _last;
    private bool _busy;

    public TwitchWidget(WidgetContext c)
    {
        _c = c;
        var header = Ui.Header(Glyphs.Broadcast, "Twitch", Ui.IconButton(Glyphs.Refresh, Loc.T("Actualizar"), () => _ = RefreshAsync(), 28));
        SetDock(header, Dock.Top);
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        c.Services.Clock.Minute += OnMinute;
        _ = RefreshAsync();
    }

    private TwitchApi Api => _c.Services.Twitch;

    private string ClientId
    {
        get
        {
            var own = _c.Settings.GetString("clientId", "").Trim();
            return own.Length > 0 ? own : Twitch.DefaultClientId;
        }
    }

    private void OnMinute(DateTime now) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy) return;
        if (!Api.SignedIn) { ShowSignIn(); return; }
        _busy = true;
        try
        {
            _last = await Api.ChannelAsync(_cts.Token);
            ShowChannel(_last);
        }
        catch (UnauthorizedAccessException) { ShowSignIn(); }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            Log.Warn("Twitch no respondió", ex);
            if (_last is null) ShowMessage(Loc.T("Twitch no responde ahora. Se intenta de nuevo en un minuto."));
        }
        finally { _busy = false; }
    }

    private void ShowMessage(string text)
    {
        _body.Children.Clear();
        _body.Children.Add(Ui.Text(text, wrap: true));
    }

    private void ShowSignIn()
    {
        _body.Children.Clear();
        _body.Children.Add(Ui.Text(Loc.T("Conecta tu canal para ver si estás en directo, tus espectadores y seguidores, y cambiar el título y la categoría desde aquí."), wrap: true));
        if (ClientId.Length == 0)
        {
            var hint = Ui.Text(Loc.T("Falta el Client ID de una app de Twitch: créala en dev.twitch.tv/console (tipo «Público», URL http://localhost) y pégalo en las opciones de este panel."), wrap: true);
            hint.Margin = new Thickness(0, 8, 0, 0);
            _body.Children.Add(hint);
            return;
        }
        var connect = Ui.Pill(Loc.T("Conectar con Twitch"), () => _ = SignInAsync(), accent: true);
        connect.Margin = new Thickness(0, 12, 0, 0);
        connect.HorizontalAlignment = HorizontalAlignment.Left;
        _body.Children.Add(connect);
    }

    private async Task SignInAsync()
    {
        try
        {
            var code = await Api.StartSignInAsync(ClientId, _cts.Token);
            _body.Children.Clear();
            _body.Children.Add(Ui.Text(Loc.T("En twitch.tv/activate escribe este código:"), wrap: true));
            var big = new TextBlock { Text = code.UserCode, FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) };
            big.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            _body.Children.Add(big);
            var open = Ui.Pill(Loc.T("Abrir twitch.tv/activate"), () => Open(code.VerificationUri), accent: true);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            _body.Children.Add(open);
            Open(code.VerificationUri);
            await Api.FinishSignInAsync(ClientId, code, _cts.Token);
            _c.Toast(Loc.T("Twitch conectado"));
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Warn("No se pudo conectar con Twitch", ex);
            ShowSignIn();
            _c.Toast(Loc.T("No se pudo conectar con Twitch: {0}", ex.Message));
        }
    }

    private void ShowChannel(TwitchChannel ch)
    {
        _body.Children.Clear();
        var status = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        if (ch.Live) dot.Fill = LiveBrush; else dot.SetResourceReference(Shape.FillProperty, "TextTertiaryBrush");
        status.Children.Add(dot);
        var line = ch.Live
            ? Loc.T("En directo · {0}", ch.LiveSince is { } since ? (DateTimeOffset.UtcNow - since).ToString(@"h\:mm\:ss") : "")
            : Loc.T("Sin conexión");
        status.Children.Add(new TextBlock { Text = line, FontSize = 15, FontWeight = FontWeights.SemiBold });
        _body.Children.Add(status);

        var news = ch.NewFollowers(DateTimeOffset.UtcNow);
        var numbers = new List<string>();
        if (ch.Live) numbers.Add(Loc.T("{0} espectadores", ch.Viewers.ToString("N0", Loc.Culture)));
        numbers.Add(Loc.T("{0} seguidores", ch.Followers.ToString("N0", Loc.Culture)));
        if (news > 0) numbers.Add(ch.Live ? Loc.T("+{0} en este directo", news) : Loc.T("+{0} en 24 h", news));
        _body.Children.Add(Ui.Text(string.Join(" · ", numbers), wrap: true));
        if (ch.RecentFollowers.Count > 0)
            _body.Children.Add(Ui.Text(Loc.T("Últimos: {0}", string.Join(", ", ch.RecentFollowers.Take(4).Select(f => f.Name))), wrap: true));

        var title = new TextBox { Style = Ui.Style("FieldTextBox"), Text = ch.Title, MaxLength = 140, Margin = new Thickness(0, 12, 0, 6) };
        var category = new TextBox { Style = Ui.Style("FieldTextBox"), Text = ch.Category, MaxLength = 80, Margin = new Thickness(0, 0, 0, 8) };
        _body.Children.Add(Ui.WithPlaceholder(title, Loc.T("Título del directo")));
        _body.Children.Add(Ui.WithPlaceholder(category, Loc.T("Categoría (p. ej. Fortnite)")));
        var save = Ui.Pill(Loc.T("Guardar en Twitch"), () => _ = SaveAsync(title.Text, category.Text, ch), accent: true);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        var bottom = new DockPanel();
        var signOut = Ui.Pill(Loc.T("Desconectar"), () => { Api.SignOut(); ShowSignIn(); });
        DockPanel.SetDock(signOut, Dock.Right);
        bottom.Children.Add(signOut);
        bottom.Children.Add(save);
        _body.Children.Add(bottom);
    }

    private async Task SaveAsync(string title, string category, TwitchChannel before)
    {
        try
        {
            var picked = await Api.UpdateAsync(
                title.Trim() == before.Title ? null : title.Trim(),
                category.Trim() == before.Category ? null : category.Trim(), _cts.Token);
            _c.Toast(picked is null ? Loc.T("Guardado en Twitch") : Loc.T("Guardado en Twitch · categoría: {0}", picked));
            await RefreshAsync();
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            Log.Warn("No se pudo cambiar el canal de Twitch", ex);
            _c.Toast(ex.Message);
        }
    }

    private static void Open(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    public void Dispose()
    {
        _c.Services.Clock.Minute -= OnMinute;
        _cts.Cancel();
    }
}
