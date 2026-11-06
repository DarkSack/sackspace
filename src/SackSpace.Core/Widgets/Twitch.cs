using System.Text.Json.Nodes;

namespace SackSpace.Core.Widgets;

/// <summary>What the Twitch widget shows about the channel.</summary>
public sealed record TwitchChannel(
    string Login,
    string DisplayName,
    string Title,
    string Category,
    bool Live,
    int Viewers,
    DateTimeOffset? LiveSince,
    int Followers,
    IReadOnlyList<(string Name, DateTimeOffset At)> RecentFollowers)
{
    /// <summary>Followers who came during this stream, or in the last 24 h when offline.</summary>
    public int NewFollowers(DateTimeOffset now) =>
        RecentFollowers.Count(f => f.At >= (LiveSince ?? now.AddDays(-1)));
}

/// <summary>A login code the user types at twitch.tv/activate.</summary>
public sealed record TwitchDeviceCode(string DeviceCode, string UserCode, string VerificationUri, TimeSpan Interval, DateTimeOffset ExpiresAt);

public sealed record TwitchToken(string Access, string Refresh, DateTimeOffset ExpiresAt);

/// <summary>
/// Twitch's Helix API, the parts that are pure data. Sign-in is OAuth's device code flow
/// for a public client: no secret ships in the app, the user approves on twitch.tv.
/// </summary>
public static class Twitch
{
    /// <summary>
    /// The SackSpace app registered on dev.twitch.tv (client type "Public"). Empty until it
    /// is registered; the widget then asks for a Client ID of the user's own app.
    /// </summary>
    public const string DefaultClientId = "";

    /// <summary>Change the title and category; read who follows.</summary>
    public const string Scopes = "channel:manage:broadcast moderator:read:followers";

    public const string DeviceUrl = "https://id.twitch.tv/oauth2/device";
    public const string TokenUrl = "https://id.twitch.tv/oauth2/token";
    public const string Helix = "https://api.twitch.tv/helix/";
    public const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    public static TwitchDeviceCode ParseDevice(JsonNode json, DateTimeOffset now) => new(
        Str(json, "device_code"), Str(json, "user_code"), Str(json, "verification_uri"),
        TimeSpan.FromSeconds(Math.Max(1, Num(json, "interval", 5))), now.AddSeconds(Num(json, "expires_in", 1800)));

    public static TwitchToken ParseToken(JsonNode json, DateTimeOffset now) => new(
        Str(json, "access_token"), Str(json, "refresh_token"), now.AddSeconds(Num(json, "expires_in", 3600)));

    /// <summary>Twitch answers 400 with this while the user has not approved yet.</summary>
    public static bool IsPending(JsonNode? json) => json?["message"]?.GetValue<string>() is "authorization_pending" or "slow_down";

    /// <summary>Builds the view from /users, /channels, /streams and /channels/followers (each one's "data" or the whole answer).</summary>
    public static TwitchChannel Combine(JsonNode user, JsonNode? channel, JsonNode? stream, JsonNode? followers)
    {
        var live = stream is not null;
        DateTimeOffset? since = live && DateTimeOffset.TryParse(stream!["started_at"]?.GetValue<string>(), out var s) ? s : null;
        var recent = followers?["data"]?.AsArray()
            .Select(f => (Name: f?["user_name"]?.GetValue<string>() ?? "", At: DateTimeOffset.TryParse(f?["followed_at"]?.GetValue<string>(), out var at) ? at : DateTimeOffset.MinValue))
            .Where(f => f.Name.Length > 0).ToList() ?? [];
        return new TwitchChannel(
            Str(user, "login"), Str(user, "display_name"),
            Str(stream ?? channel, "title"), Str(stream ?? channel, "game_name"),
            live, (int)Num(stream, "viewer_count", 0), since,
            (int)Num(followers, "total", 0), recent);
    }

    /// <summary>The category to set: the one named exactly like the text, else the first suggestion.</summary>
    public static (string Id, string Name)? PickCategory(JsonNode? search, string text)
    {
        var list = search?["data"]?.AsArray()
            .Select(c => (Id: c?["id"]?.GetValue<string>() ?? "", Name: c?["name"]?.GetValue<string>() ?? ""))
            .Where(c => c.Id.Length > 0).ToList() ?? [];
        if (list.Count == 0) return null;
        return list.FirstOrDefault(c => string.Equals(c.Name, text.Trim(), StringComparison.OrdinalIgnoreCase)) is { Id.Length: > 0 } exact
            ? exact : list[0];
    }

    private static string Str(JsonNode? json, string key) =>
        json?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static double Num(JsonNode? json, string key, double fallback) =>
        json?[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : fallback;
}
