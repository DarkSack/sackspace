using System.Text.Json.Nodes;

namespace SackSpace.Core.Model;

/// <summary>An app, file or folder in an "Accesos" widget. Stored in the panel's settings under "items".</summary>
public sealed record ShortcutItem(string Name, string Path)
{
    public static List<ShortcutItem> ReadAll(JsonObject settings) =>
        settings["items"] is JsonArray items
            ? items.OfType<JsonObject>()
                .Select(o => new ShortcutItem(
                    o["name"]?.GetValue<string>() ?? "",
                    o["path"]?.GetValue<string>() ?? ""))
                .Where(i => i.Path.Length > 0)
                .ToList()
            : [];

    public static void WriteAll(JsonObject settings, IEnumerable<ShortcutItem> items) =>
        settings["items"] = new JsonArray(items
            .Select(i => (JsonNode)new JsonObject { ["name"] = i.Name, ["path"] = i.Path })
            .ToArray());

    /// <summary>Display name for a path: the file name without .lnk/.exe/.url, or the folder name.</summary>
    public static string NameFor(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = System.IO.Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name)) return trimmed; // a drive root such as "D:\"
        var ext = System.IO.Path.GetExtension(name);
        return ext.ToLowerInvariant() is ".lnk" or ".exe" or ".url" or ".appref-ms" ? name[..^ext.Length] : name;
    }
}

/// <summary>A to-do in a "Tareas" widget, stored under "items".</summary>
public sealed record TaskItem(string Text, bool Done)
{
    public static List<TaskItem> ReadAll(JsonObject settings) =>
        settings["items"] is JsonArray items
            ? items.OfType<JsonObject>()
                .Select(o => new TaskItem(o["text"]?.GetValue<string>() ?? "", o["done"]?.GetValue<bool>() ?? false))
                .Where(t => t.Text.Length > 0)
                .ToList()
            : [];

    public static void WriteAll(JsonObject settings, IEnumerable<TaskItem> items) =>
        settings["items"] = new JsonArray(items
            .Select(t => (JsonNode)new JsonObject { ["text"] = t.Text, ["done"] = t.Done })
            .ToArray());
}
