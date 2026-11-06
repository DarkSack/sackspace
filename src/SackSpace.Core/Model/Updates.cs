using System.Text.Json.Nodes;

namespace SackSpace.Core.Model;

/// <summary>A newer SackSpace on GitHub: its version, its page and the installer with its SHA-256.</summary>
public sealed record UpdateInfo(Version Version, string Page, string InstallerUrl, string InstallerName, string? Sha256);

/// <summary>Reads GitHub's "latest release" answer and decides whether it is newer than what runs.</summary>
public static class Updates
{
    public const string LatestUrl = "https://api.github.com/repos/DarkSack/sackspace/releases/latest";

    /// <summary>"v0.4.0" or "0.4.0" → 0.4.0; anything else (a draft name) → null.</summary>
    public static Version? ParseTag(string? tag) =>
        tag is { Length: > 0 } && Version.TryParse(tag.TrimStart('v', 'V'), out var v) ? v : null;

    /// <summary>
    /// The release when it is newer than <paramref name="running"/> and has a SackSpace-Setup-*.exe;
    /// drafts and pre-releases are not offered.
    /// </summary>
    public static UpdateInfo? Newer(JsonNode release, Version running)
    {
        if (release["draft"]?.GetValue<bool>() == true || release["prerelease"]?.GetValue<bool>() == true) return null;
        if (ParseTag(release["tag_name"]?.GetValue<string>()) is not { } version) return null;
        if (Normalize(version) <= Normalize(running)) return null;
        var asset = (release["assets"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(a =>
            a["name"]?.GetValue<string>() is { } n && n.StartsWith("SackSpace-Setup-", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (asset?["browser_download_url"]?.GetValue<string>() is not { } url || !url.StartsWith("https://github.com/DarkSack/sackspace/", StringComparison.Ordinal)) return null;
        var digest = asset["digest"]?.GetValue<string>();
        var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
        return new UpdateInfo(version, release["html_url"]?.GetValue<string>() ?? "https://github.com/DarkSack/sackspace/releases/latest",
            url, asset["name"]!.GetValue<string>(), sha);
    }

    // 0.4 and 0.4.0 are the same version; Version says 0.4 < 0.4.0.
    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
