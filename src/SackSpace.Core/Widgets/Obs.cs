using System.Buffers;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SackSpace.Core.Widgets;

public sealed class ObsException(string message) : Exception(message);

/// <summary>OBS's own WebSocket settings, read from its config so the user types nothing.</summary>
public sealed record ObsServer(int Port, string? Password)
{
    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json");

    /// <exception cref="ObsException">OBS is not installed or its server is off.</exception>
    public static ObsServer Read(string path)
    {
        if (!File.Exists(path)) throw new ObsException("No encontré OBS en este equipo.");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var r = doc.RootElement;
        if (!(r.TryGetProperty("server_enabled", out var on) && on.ValueKind == JsonValueKind.True))
            throw new ObsException("Activa el servidor en OBS: Herramientas → Ajustes del servidor WebSocket.");
        var port = r.TryGetProperty("server_port", out var p) && p.TryGetInt32(out var n) ? n : 4455;
        var auth = r.TryGetProperty("auth_required", out var a) && a.ValueKind == JsonValueKind.True;
        return new ObsServer(port, auth && r.TryGetProperty("server_password", out var pw) ? pw.GetString() : null);
    }
}

/// <summary>Just enough of obs-websocket 5: identify, then one request at a time. Same client as SackMix.</summary>
public sealed class ObsClient : IDisposable
{
    private readonly ClientWebSocket _ws = new();
    private int _next;

    public static async Task<ObsClient> ConnectAsync(Uri uri, string? password, CancellationToken ct)
    {
        var obs = new ObsClient();
        try
        {
            try { await obs._ws.ConnectAsync(uri, ct); }
            catch (WebSocketException) { throw new ObsException("OBS no está abierto."); }

            var hello = (await obs.ReceiveAsync(ct)).GetProperty("d");
            string? auth = null;
            if (hello.TryGetProperty("authentication", out var a))
            {
                if (password is null) throw new ObsException("OBS pide contraseña y no está en su configuración.");
                auth = Auth(password, a.GetProperty("salt").GetString()!, a.GetProperty("challenge").GetString()!);
            }
            await obs.SendAsync(new JsonObject
            {
                ["op"] = 1,
                ["d"] = new JsonObject { ["rpcVersion"] = 1, ["authentication"] = auth, ["eventSubscriptions"] = 0 },
            }, ct);
            if ((await obs.ReceiveAsync(ct)).GetProperty("op").GetInt32() != 2) throw new ObsException("OBS no aceptó la conexión.");
            return obs;
        }
        catch
        {
            obs.Dispose();
            throw;
        }
    }

    public static string Auth(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    /// <returns>The responseData (default when there is none).</returns>
    /// <exception cref="ObsException">OBS refused the request.</exception>
    public async Task<JsonElement> RequestAsync(string type, JsonObject? data, CancellationToken ct)
    {
        var id = (++_next).ToString(CultureInfo.InvariantCulture);
        var d = new JsonObject { ["requestType"] = type, ["requestId"] = id };
        if (data is not null) d["requestData"] = data;
        await SendAsync(new JsonObject { ["op"] = 6, ["d"] = d }, ct);
        while (true)
        {
            var msg = await ReceiveAsync(ct);
            if (msg.GetProperty("op").GetInt32() != 7) continue;
            var r = msg.GetProperty("d");
            if (r.GetProperty("requestId").GetString() != id) continue;
            var status = r.GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean())
                throw new ObsException(status.TryGetProperty("comment", out var c) ? c.GetString() ?? type : type);
            return r.TryGetProperty("responseData", out var response) ? response : default;
        }
    }

    private Task SendAsync(JsonObject message, CancellationToken ct) =>
        _ws.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, ct);

    private async Task<JsonElement> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        while (true)
        {
            var r = await _ws.ReceiveAsync(buffer.GetMemory(4096), ct);
            if (r.MessageType == WebSocketMessageType.Close)
                throw new ObsException(_ws.CloseStatus == (WebSocketCloseStatus)4009 // AuthenticationFailed
                    ? Loc.T("OBS rechazó la contraseña.")
                    : Loc.T("OBS cerró la conexión ({0}).", _ws.CloseStatusDescription));
            buffer.Advance(r.Count);
            if (!r.EndOfMessage) continue;
            using var doc = JsonDocument.Parse(buffer.WrittenMemory);
            return doc.RootElement.Clone();
        }
    }

    public void Dispose() => _ws.Dispose();
}

/// <summary>Numbers the OBS widget shows, worked out from what obs-websocket reports.</summary>
public static class ObsNumbers
{
    /// <summary>Upload rate in megabits per second between two readings of outputBytes; null on the first reading or after a restart.</summary>
    public static double? Megabits(long previousBytes, DateTime previousAt, long bytes, DateTime at)
    {
        var seconds = (at - previousAt).TotalSeconds;
        if (seconds <= 0 || bytes < previousBytes) return null;
        return (bytes - previousBytes) * 8 / seconds / 1_000_000;
    }

    /// <summary>Share of frames OBS had to drop, in percent.</summary>
    public static double Dropped(long skipped, long total) => total <= 0 ? 0 : skipped * 100.0 / total;

    /// <summary>"01:23:45.678" → "1:23:45"; "00:04:05.000" → "4:05".</summary>
    public static string Clock(string? timecode)
    {
        if (string.IsNullOrEmpty(timecode)) return "";
        var t = timecode.Split('.')[0];
        if (!TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var span)) return t;
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span:ss}";
    }
}
