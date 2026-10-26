using System.Text.Json;
using System.Text.Json.Serialization;
using SackSpace.Core.Hotkeys;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;

namespace SackSpace.Core.Storage;

public enum LoadOutcome
{
    /// <summary>The file was read as is.</summary>
    Loaded,
    /// <summary>No file yet: first run.</summary>
    Created,
    /// <summary>The file was damaged; the backup was used. The damaged one was kept aside.</summary>
    RestoredFromBackup,
    /// <summary>The file and its backup were damaged; started over. Both were kept aside.</summary>
    Reset,
}

/// <summary>
/// config.json in the data folder. Saving never leaves a half-written file: it writes a
/// temporary file and swaps it in, keeping the previous version as config.json.bak.
/// A file that cannot be read is moved aside, never overwritten.
/// </summary>
public sealed class ConfigStore(string folder)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Folder { get; } = folder;
    public string FilePath => Path.Combine(Folder, "config.json");
    public string BackupPath => FilePath + ".bak";

    public (AppConfig Config, LoadOutcome Outcome) Load(Func<AppConfig> createDefault)
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(FilePath) && !File.Exists(BackupPath))
            return (Normalize(createDefault()), LoadOutcome.Created);

        if (TryRead(FilePath, out var config)) return (Normalize(config!), LoadOutcome.Loaded);
        MoveAside(FilePath);

        if (TryRead(BackupPath, out var backup)) return (Normalize(backup!), LoadOutcome.RestoredFromBackup);
        MoveAside(BackupPath);

        return (Normalize(createDefault()), LoadOutcome.Reset);
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            JsonSerializer.Serialize(stream, config, Json);

        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, BackupPath, ignoreMetadataErrors: true);
        else File.Move(tmp, FilePath);
    }

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, Json);

    /// <summary>For importing: the config in a file, or null if it is not one.</summary>
    public static AppConfig? TryDeserialize(string json)
    {
        try
        {
            var c = JsonSerializer.Deserialize<AppConfig>(json, Json);
            return c is { Workspaces.Count: > 0 } ? Normalize(c) : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool TryRead(string path, out AppConfig? config)
    {
        config = null;
        try
        {
            if (!File.Exists(path)) return false;
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json);
            return config is not null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static void MoveAside(string path)
    {
        if (!File.Exists(path)) return;
        var aside = $"{path}.danado-{DateTime.Now:yyyyMMdd-HHmmss}";
        try { File.Move(path, aside, overwrite: true); } catch (IOException) { }
    }

    /// <summary>Fills what an older or hand-edited file may lack, so the app can rely on it.</summary>
    public static AppConfig Normalize(AppConfig c)
    {
        c.General ??= new GeneralSettings();
        c.Appearance ??= new AppearanceSettings();
        if (string.IsNullOrWhiteSpace(c.Appearance.CustomWallpaper)) c.Appearance.CustomWallpaper = null;
        c.Workspaces ??= [];
        c.Hotkeys ??= [];
        c.Workspaces.RemoveAll(w => w is null);
        if (c.Workspaces.Count == 0) c.Workspaces.Add(new Workspace { Name = "Personal", Icon = Glyphs.Person });

        var ids = new HashSet<string>();
        foreach (var ws in c.Workspaces)
        {
            if (string.IsNullOrWhiteSpace(ws.Id) || !ids.Add(ws.Id)) { ws.Id = Workspace.NewId(); ids.Add(ws.Id); }
            ws.Name = string.IsNullOrWhiteSpace(ws.Name) ? "Espacio" : ws.Name.Trim();
            ws.Panels ??= [];
            ws.Panels.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Widget));
            var panelIds = new HashSet<string>();
            foreach (var p in ws.Panels)
            {
                if (string.IsNullOrWhiteSpace(p.Id) || !panelIds.Add(p.Id)) { p.Id = Workspace.NewId(); panelIds.Add(p.Id); }
                p.Settings ??= [];
                p.Monitor = string.IsNullOrWhiteSpace(p.Monitor) ? MonitorKeys.Primary : p.Monitor;
                if (BuiltInWidgets.Find(p.Widget) is { } d)
                {
                    p.Width = double.IsFinite(p.Width) ? Math.Max(p.Width, d.MinWidth) : d.DefaultWidth;
                    p.Height = double.IsFinite(p.Height) ? Math.Max(p.Height, d.MinHeight) : d.DefaultHeight;
                }
                if (!double.IsFinite(p.X)) p.X = 0;
                if (!double.IsFinite(p.Y)) p.Y = 0;
            }
        }

        if (c.ActiveWorkspaceId is null || c.Workspaces.All(w => w.Id != c.ActiveWorkspaceId))
            c.ActiveWorkspaceId = c.Workspaces[0].Id;

        // New actions get their default; one the user cleared ("") stays off.
        foreach (var (action, gesture) in HotkeyActions.Defaults)
            c.Hotkeys.TryAdd(action, gesture);

        c.SchemaVersion = AppConfig.CurrentSchema;
        return c;
    }
}
