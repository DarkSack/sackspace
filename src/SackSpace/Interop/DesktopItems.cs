using Loc = SackSpace.Core.Loc;
using System.IO;
using Microsoft.Win32;

namespace SackSpace.Interop;

/// <summary>One icon of the desktop: a file or folder path, or "::{CLSID}" for Recycle Bin and the like.</summary>
public sealed record DesktopItem(string Path, string Name);

/// <summary>What the Windows desktop shows, and its "Ver › Mostrar iconos del escritorio" switch.</summary>
internal static class DesktopItems
{
    // Settings › Personalización › Temas › Iconos del escritorio: Recycle Bin is on unless turned off, the rest off unless turned on.
    private static readonly (string Clsid, bool OnByDefault)[] SystemIcons =
    [
        ("{645FF040-5081-101B-9F08-00AA002F954E}", true),  // Papelera de reciclaje
        ("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false), // Este equipo
        ("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", false), // Archivos del usuario
        ("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", false), // Red
        ("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", false), // Panel de control
    ];

    private const string HideDesktopIcons = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    /// <summary>The user's desktop folder (maybe in OneDrive) and the one shared by every user.</summary>
    public static IEnumerable<string> Folders =>
        new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }
            .Where(f => f.Length > 0 && Directory.Exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>System icons first, then folders, then files, by name (like "Ordenar por nombre").</summary>
    public static List<DesktopItem> List()
    {
        var items = new List<DesktopItem>();
        foreach (var (clsid, onByDefault) in SystemIcons)
        {
            var hidden = Registry.GetValue(HideDesktopIcons, clsid, null) as int?;
            if (hidden is null ? onByDefault : hidden == 0) items.Add(new DesktopItem("::" + clsid, ShellNames.DisplayName("::" + clsid)));
        }

        var entries = new List<(DesktopItem Item, bool Folder)>();
        foreach (var folder in Folders)
        {
            try
            {
                foreach (var e in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                    if ((e.Attributes & FileAttributes.Hidden) == 0)
                        entries.Add((new DesktopItem(e.FullName, ShellNames.DisplayName(e.FullName)), e is DirectoryInfo));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"No se pudo leer el escritorio '{folder}'", ex);
            }
        }
        items.AddRange(entries.OrderBy(e => !e.Folder).ThenBy(e => e.Item.Name, StringComparer.CurrentCultureIgnoreCase).Select(e => e.Item));
        return items;
    }

    /// <summary>The desktop's own "Mostrar iconos del escritorio"; Explorer remembers it across restarts.</summary>
    public static void ShowWindowsIcons(bool show)
    {
        var view = DefView();
        var list = Native.FindWindowEx(view, IntPtr.Zero, "SysListView32", "FolderView");
        if (view == IntPtr.Zero || list == IntPtr.Zero || Native.IsWindowVisible(list) == show) return;
        const int WM_COMMAND = 0x111, ToggleDesktopIcons = 0x7402;
        Native.SendMessage(view, WM_COMMAND, new IntPtr(ToggleDesktopIcons), IntPtr.Zero);
        Log.Info(show ? "Iconos de Windows: visibles" : "Iconos de Windows: ocultos (los dibuja SackSpace)");
    }

    /// <summary>The window holding the desktop's icon list: under Progman, or under a WorkerW while a wallpaper app or slideshow runs.</summary>
    private static IntPtr DefView()
    {
        var view = Native.FindWindowEx(Native.FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        for (var w = IntPtr.Zero; view == IntPtr.Zero && (w = Native.FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero;)
            view = Native.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        return view;
    }
}
