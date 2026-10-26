using System.IO;
using System.Runtime.InteropServices;
using SackSpace.Core.Model;

namespace SackSpace.Interop;

/// <summary>Names as Explorer shows them ("Descargas", not "Downloads") and the user's known folders.</summary>
internal static partial class ShellNames
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    private const uint SHGFI_DISPLAYNAME = 0x200;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>Explorer's name for a file or folder; the plain file name when the shell has none.</summary>
    public static string DisplayName(string path)
    {
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return ShortcutItem.NameFor(path);
        var info = new SHFILEINFO();
        var ok = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_DISPLAYNAME) != IntPtr.Zero;
        var name = ok ? info.szDisplayName : "";
        // Shortcuts keep ".lnk" when extensions are shown; NameFor strips it.
        return string.IsNullOrWhiteSpace(name) || Path.GetExtension(name).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? ShortcutItem.NameFor(path)
            : name;
    }

    private static readonly Dictionary<string, Guid> Known = new()
    {
        ["Documents"] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        ["Downloads"] = new("374DE290-123F-4565-9164-39C4925E467B"),
        ["Pictures"] = new("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        ["Music"] = new("4BD8D571-6D19-48D3-BE97-422220080E43"),
        ["Videos"] = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
        ["Desktop"] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        ["UserProfile"] = new("5E6C858F-0E22-4760-9AFE-EA3317B67173"),
    };

    /// <summary>A known folder's real path (follows OneDrive and other redirections), or null.</summary>
    public static string? KnownFolder(string name)
    {
        if (!Known.TryGetValue(name, out var id)) return null;
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var ptr) != 0) return null;
        try
        {
            var path = Marshal.PtrToStringUni(ptr);
            return Directory.Exists(path) ? path : null;
        }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }
}
