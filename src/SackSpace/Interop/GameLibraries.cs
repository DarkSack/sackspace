using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SackSpace.Core.Widgets;

namespace SackSpace.Interop;

/// <summary>Where Steam, Epic and the Xbox app keep their games, read from their own files.</summary>
internal static partial class GameLibraries
{
    /// <summary>Each Steam library folder (the one holding steamapps), or null without Steam.</summary>
    public static IReadOnlyList<string>? SteamLibraries()
    {
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is not string steam) return null;
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) return [Path.GetFullPath(steam)];
            return VdfPath().Matches(File.ReadAllText(vdf))
                .Select(m => Path.GetFullPath(m.Groups[1].Value.Replace(@"\\", @"\")))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron leer las bibliotecas de Steam", ex);
            return null;
        }
    }

    /// <summary>App ids with an appmanifest in some library; null when Steam or one of its drives is missing.</summary>
    public static IReadOnlySet<string>? SteamGames()
    {
        if (SteamLibraries() is not { } libraries) return null;
        var ids = new HashSet<string>();
        foreach (var lib in libraries)
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) return null; // a drive not plugged in: cannot tell what is installed there
            foreach (var f in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                ids.Add(Path.GetFileNameWithoutExtension(f)["appmanifest_".Length..]);
        }
        return ids;
    }

    private static string EpicManifests => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");

    /// <summary>AppName and install folder of each Epic install; null without the launcher.</summary>
    public static IReadOnlyList<(string AppName, string Folder)>? EpicInstalls()
    {
        if (!Directory.Exists(EpicManifests)) return null;
        var list = new List<(string, string)>();
        foreach (var f in Directory.EnumerateFiles(EpicManifests, "*.item"))
        {
            try
            {
                var j = JsonNode.Parse(File.ReadAllText(f));
                if (j?["AppName"]?.GetValue<string>() is { Length: > 0 } name)
                    list.Add((name, j["InstallLocation"]?.GetValue<string>() ?? ""));
            }
            catch (Exception ex) { Log.Warn($"Manifiesto de Epic ilegible: {f}", ex); }
        }
        return list;
    }

    /// <summary>Steam and Epic games installed now (tools included: callers filter with GameCatalog.IsTool).</summary>
    public static IReadOnlyList<InstalledGame> InstalledGames(IReadOnlySet<string> startMenu)
    {
        var games = new List<InstalledGame>();
        foreach (var lib in SteamLibraries() ?? [])
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var f in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    if (GameCatalog.ParseSteam(File.ReadAllText(f), lib, startMenu.Contains) is { } g) games.Add(g);
                }
                catch (IOException ex) { Log.Warn($"Manifiesto de Steam ilegible: {f}", ex); }
            }
        }
        if (Directory.Exists(EpicManifests))
            foreach (var f in Directory.EnumerateFiles(EpicManifests, "*.item"))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(f)) is { } j && GameCatalog.ParseEpic(j) is { } g) games.Add(g);
                }
                catch (Exception ex) { Log.Warn($"Manifiesto de Epic ilegible: {f}", ex); }
            }
        return games.DistinctBy(g => g.Key).ToList();
    }

    public static IReadOnlySet<string>? EpicApps() =>
        EpicInstalls()?.Select(e => e.AppName).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPath();
}
