using System.Diagnostics;
using System.IO;

namespace SackSpace.Interop;

/// <summary>An app from the Start menu's "All apps" list.</summary>
public sealed record InstalledApp(string Name, string ParsingName);

/// <summary>
/// The Start menu's "All apps" (shell:AppsFolder): desktop programs and Store apps
/// alike, with the names Windows shows. Read through the Shell.Application COM object,
/// like SackDesk does.
/// </summary>
internal static class InstalledApps
{
    public static IReadOnlyList<InstalledApp> List()
    {
        var result = new List<InstalledApp>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return result;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            foreach (var item in folder.Items())
            {
                string name = item.Name;
                string path = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                if (IsNoise(name, path)) continue;
                result.Add(new InstalledApp(name, @"shell:AppsFolder\" + path));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo leer la lista de apps", ex);
        }
        return result
            .GroupBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The first app whose name matches (exactly, then by prefix).</summary>
    public static string? Find(IReadOnlyList<InstalledApp> apps, string name) =>
        (apps.FirstOrDefault(a => a.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
         ?? apps.FirstOrDefault(a => a.Name.StartsWith(name + " ", StringComparison.CurrentCultureIgnoreCase)))?.ParsingName;

    // Uninstallers, readmes and web links clutter the list.
    private static bool IsNoise(string name, string path)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("uninstall") || n.Contains("desinstalar") || n.StartsWith("readme") || n.Contains("léame")
               || path.StartsWith("http", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Opens an app, file or folder like a double click in Explorer.</summary>
    public static bool Launch(string path, out string? error)
    {
        error = null;
        try
        {
            var psi = path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::")
                ? new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }
                : new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = SafeDirectory(path) };
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Opens Explorer with the item selected.</summary>
    public static void ShowInFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
            else if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Log.Warn("No se pudo abrir la ubicación", ex); }
    }

    public static bool Exists(string path) =>
        path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::") || File.Exists(path) || Directory.Exists(path);

    private static string SafeDirectory(string path)
    {
        try { return File.Exists(path) ? Path.GetDirectoryName(path) ?? "" : ""; }
        catch (ArgumentException) { return ""; }
    }
}
