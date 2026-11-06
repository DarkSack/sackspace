using Loc = SackSpace.Core.Loc;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SackSpace.Core.Widgets;

namespace SackSpace.Services;

/// <summary>
/// The signed-in Twitch account: device-code sign-in, token refresh and Helix calls.
/// The tokens live in twitch.dat in the data folder, encrypted for this Windows user (DPAPI).
/// </summary>
public sealed class TwitchApi(string dataFolder)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _file = Path.Combine(dataFolder, "twitch.dat");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string ClientId, TwitchToken Token)? _session;
    private string? _userId;

    public bool SignedIn => Load() is not null;

    /// <summary>Starts the sign-in; the user types the code at the returned address.</summary>
    public async Task<TwitchDeviceCode> StartSignInAsync(string clientId, CancellationToken ct)
    {
        var json = await PostFormAsync(Twitch.DeviceUrl, new() { ["client_id"] = clientId, ["scopes"] = Twitch.Scopes }, ct);
        return Twitch.ParseDevice(json, DateTimeOffset.UtcNow);
    }

    /// <summary>Waits until the user approves (or the code expires) and keeps the tokens.</summary>
    public async Task FinishSignInAsync(string clientId, TwitchDeviceCode code, CancellationToken ct)
    {
        while (DateTimeOffset.UtcNow < code.ExpiresAt)
        {
            await Task.Delay(code.Interval, ct);
            var (status, json) = await SendFormAsync(Twitch.TokenUrl, new()
            {
                ["client_id"] = clientId,
                ["scopes"] = Twitch.Scopes,
                ["device_code"] = code.DeviceCode,
                ["grant_type"] = Twitch.DeviceGrant,
            }, ct);
            if (status == HttpStatusCode.OK && json is not null)
            {
                Save(clientId, Twitch.ParseToken(json, DateTimeOffset.UtcNow));
                return;
            }
            if (!Twitch.IsPending(json)) throw new InvalidOperationException(json?["message"]?.GetValue<string>() ?? $"Twitch: {(int)status}");
        }
        throw new TimeoutException("El código caducó.");
    }

    public void SignOut()
    {
        _session = null;
        _userId = null;
        File.Delete(_file);
    }

    public async Task<TwitchChannel> ChannelAsync(CancellationToken ct)
    {
        var user = (await GetAsync("users", ct))["data"]![0]!;
        _userId = user["id"]!.GetValue<string>();
        var channel = (await GetAsync($"channels?broadcaster_id={_userId}", ct))["data"]?[0];
        var stream = (await GetAsync($"streams?user_id={_userId}", ct))["data"]?.AsArray().FirstOrDefault();
        var followers = await GetAsync($"channels/followers?broadcaster_id={_userId}&first=20", ct);
        return Twitch.Combine(user, channel, stream, followers);
    }

    /// <summary>Changes the title and/or category; returns the category name Twitch picked.</summary>
    public async Task<string?> UpdateAsync(string? title, string? category, CancellationToken ct)
    {
        if (_userId is null) await ChannelAsync(ct);
        var body = new JsonObject();
        if (title is not null) body["title"] = title;
        string? picked = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            var found = Twitch.PickCategory(await GetAsync("search/categories?first=8&query=" + Uri.EscapeDataString(category), ct), category)
                ?? throw new InvalidOperationException($"No hay ninguna categoría «{category}».");
            body["game_id"] = found.Id;
            picked = found.Name;
        }
        if (body.Count == 0) return null;
        using var req = await RequestAsync(HttpMethod.Patch, $"channels?broadcaster_id={_userId}", ct);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Twitch no aceptó el cambio ({(int)res.StatusCode}).");
        return picked;
    }

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        using var req = await RequestAsync(HttpMethod.Get, path, ct);
        using var res = await Http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException("Twitch cerró la sesión: vuelve a conectar.");
        res.EnsureSuccessStatusCode();
        return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var (clientId, token) = await FreshAsync(ct);
        var req = new HttpRequestMessage(method, Twitch.Helix + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Access);
        req.Headers.Add("Client-Id", clientId);
        return req;
    }

    // Refreshes the access token a few minutes before it runs out (public client: no secret).
    private async Task<(string ClientId, TwitchToken Token)> FreshAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var s = Load() ?? throw new UnauthorizedAccessException("Twitch no está conectado.");
            if (s.Token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5)) return s;
            var (status, json) = await SendFormAsync(Twitch.TokenUrl, new()
            {
                ["client_id"] = s.ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = s.Token.Refresh,
            }, ct);
            if (status != HttpStatusCode.OK || json is null)
            {
                SignOut();
                throw new UnauthorizedAccessException("Twitch cerró la sesión: vuelve a conectar.");
            }
            var token = Twitch.ParseToken(json, DateTimeOffset.UtcNow);
            if (token.Refresh.Length == 0) token = token with { Refresh = s.Token.Refresh };
            Save(s.ClientId, token);
            return _session!.Value;
        }
        finally { _gate.Release(); }
    }

    private (string ClientId, TwitchToken Token)? Load()
    {
        if (_session is not null || !File.Exists(_file)) return _session;
        try
        {
            var json = JsonNode.Parse(ProtectedData.Unprotect(File.ReadAllBytes(_file), null, DataProtectionScope.CurrentUser))!;
            _session = (json["client"]!.GetValue<string>(), new TwitchToken(
                json["access"]!.GetValue<string>(), json["refresh"]!.GetValue<string>(), json["expires"]!.GetValue<DateTimeOffset>()));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or System.Text.Json.JsonException or NullReferenceException)
        {
            Log.Warn("No se pudo leer la sesión de Twitch", ex);
        }
        return _session;
    }

    private void Save(string clientId, TwitchToken token)
    {
        _session = (clientId, token);
        var json = new JsonObject { ["client"] = clientId, ["access"] = token.Access, ["refresh"] = token.Refresh, ["expires"] = token.ExpiresAt };
        File.WriteAllBytes(_file, ProtectedData.Protect(Encoding.UTF8.GetBytes(json.ToJsonString()), null, DataProtectionScope.CurrentUser));
    }

    private static async Task<JsonNode> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        var (status, json) = await SendFormAsync(url, form, ct);
        if (status != HttpStatusCode.OK || json is null)
            throw new HttpRequestException(json?["message"]?.GetValue<string>() ?? $"Twitch: {(int)status}");
        return json;
    }

    private static async Task<(HttpStatusCode, JsonNode?)> SendFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var res = await Http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        JsonNode? json = null;
        try { json = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { }
        return (res.StatusCode, json);
    }
}
