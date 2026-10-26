using System.Text.Json;
using System.Text.Json.Nodes;

namespace SackSpace.Core.Themes;

/// <summary>
/// Wallpaper Engine and Lively draw their own window behind the desktop icons, so Windows
/// still reports its old picture. These read each app's own files to find what a monitor
/// shows (monitor key = its device interface path) and a still picture of it to blur.
/// </summary>
public static class LiveWallpapers
{
    public sealed record Found(string Picture, string Title);

    private static readonly string[] PictureTypes = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    /// <summary>From Wallpaper Engine's config.json (next to wallpaper64.exe).</summary>
    public static Found? WallpaperEngine(string configPath, string monitorKey, bool isPrimary)
    {
        // The settings live under the Windows user name; the other root keys start with "?".
        var general = (Read(configPath) as JsonObject)?
            .Where(p => !p.Key.StartsWith('?')).Select(p => p.Value?["general"]).FirstOrDefault(g => g is not null);
        var config = general?["wallpaperconfig"];
        if (config?["selectedwallpapers"] is not JsonObject selected || selected.Count == 0) return null;

        JsonNode? entry;
        // shortcut: "stretch" and "clone" layouts show the first wallpaper on every monitor; crop it per monitor if anyone asks.
        if (Int(config["layout"]) is not (null or 0)) entry = selected.First().Value;
        else
        {
            // Keyed by device path in some versions; by "MonitorN" plus a device-path map in others.
            var slashed = monitorKey.Replace('\\', '/');
            entry = selected.FirstOrDefault(p => Same(p.Key, monitorKey) || Same(p.Key, slashed)).Value;
            if (entry is null)
            {
                var location = Int(general!["user"]?["monitormap"]?[slashed]?["location"]) ?? (isPrimary ? 0 : null);
                entry = location is { } n ? selected[$"Monitor{n}"] : null;
            }
        }
        // A playlist has no "file"; the wallpaper it shows right now is not written down.
        if (Str(entry?["file"]) is not { } file) return null;

        var folder = Path.GetDirectoryName(file);
        if (folder is null) return null;
        var project = Read(Path.Combine(folder, "project.json"));
        return Picture(folder, Str(project?["title"]), Str(project?["preview"]), anyType: true);
    }

    /// <summary>From Lively's data folder (WallpaperLayout.json, Settings.json).</summary>
    public static Found? Lively(string dataFolder, string monitorKey)
    {
        if (Read(Path.Combine(dataFolder, "WallpaperLayout.json")) is not JsonArray layout || layout.Count == 0) return null;
        var arrangement = Read(Path.Combine(dataFolder, "Settings.json"))?["WallpaperArrangement"];
        // 0 = a wallpaper per screen; span and duplicate keep one entry for all of them.
        var perScreen = arrangement is null || Int(arrangement) == 0 || Same(Str(arrangement), "per");
        var entry = perScreen
            ? layout.FirstOrDefault(e => Same(Str((e?["LivelyScreen"] ?? e?["Display"])?["DeviceId"]), monitorKey))
            : layout[0];
        if (Str(entry?["LivelyInfoPath"]) is not { } folder) return null;

        var info = Read(Path.Combine(folder, "LivelyInfo.json"));
        if (info is null) return null;
        var title = Str(info["Title"]);
        // A picture wallpaper is its own best still; anything else has a thumbnail.
        return Picture(folder, title, Str(info["FileName"]), anyType: false)
            ?? Picture(folder, title, Str(info["Thumbnail"]), anyType: false)
            ?? Picture(folder, title, Str(info["Preview"]), anyType: false);
    }

    private static Found? Picture(string folder, string? title, string? name, bool anyType)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!anyType && !PictureTypes.Contains(Path.GetExtension(name).ToLowerInvariant())) return null;
        var path = Path.Combine(folder, name); // an absolute name wins
        return File.Exists(path) ? new Found(path, string.IsNullOrWhiteSpace(title) ? Path.GetFileName(folder) : title.Trim()) : null;
    }

    /// <summary>Lenient JSON read that does not fight the app for the file while it writes.</summary>
    private static JsonNode? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonNode.Parse(stream,
                new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
    private static bool Same(string? a, string? b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
