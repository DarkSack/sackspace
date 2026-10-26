using System.Runtime.InteropServices;

namespace SackSpace.Interop;

public enum WallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

/// <summary>The wallpaper of one monitor: file (may be empty for a solid colour), fit and background colour.</summary>
public sealed record WallpaperSource(string? Path, WallpaperPosition Position, uint BackgroundColorRef);

/// <summary>
/// IDesktopWallpaper (Windows 8+): unlike SPI_GETDESKWALLPAPER it knows each monitor's
/// own picture. Only the read methods are used; the vtable order must match the SDK.
/// </summary>
internal static class DesktopWallpaper
{
    public static WallpaperSource For(Native.RECT monitorBounds)
    {
        try
        {
            var wp = (IDesktopWallpaper)new DesktopWallpaperClass();
            try
            {
                var position = wp.GetPosition();
                var color = wp.GetBackgroundColor();
                var count = wp.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    var id = wp.GetMonitorDevicePathAt(i);
                    var r = wp.GetMonitorRECT(id);
                    if (r.Left == monitorBounds.Left && r.Top == monitorBounds.Top && r.Right == monitorBounds.Right && r.Bottom == monitorBounds.Bottom)
                        return new WallpaperSource(NullIfEmpty(wp.GetWallpaper(id)), position, color);
                }
                return new WallpaperSource(NullIfEmpty(wp.GetWallpaper(null)), position, color);
            }
            finally { Marshal.ReleaseComObject(wp); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            return new WallpaperSource(null, WallpaperPosition.Fill, 0x000000);
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass;

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        Native.RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(WallpaperPosition position);
        WallpaperPosition GetPosition();
    }
}
