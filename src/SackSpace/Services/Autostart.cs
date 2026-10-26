using System.IO;
using Microsoft.Win32;

namespace SackSpace.Services;

/// <summary>Start with Windows through the user's Run key (no admin, shows in Task Manager › Startup).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SackSpace";

    /// <summary>A `dotnet run` build has dotnet.exe as its process: nothing sensible to register.</summary>
    public static bool Available =>
        Environment.ProcessPath is { } p && !Path.GetFileName(p).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);

    public static void Apply(bool enabled)
    {
        if (!Available) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --autostart");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo cambiar el inicio con Windows", ex);
        }
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }
}
