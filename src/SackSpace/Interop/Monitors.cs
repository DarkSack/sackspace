using SackSpace.Core.Model;

namespace SackSpace.Interop;

/// <summary>A monitor as Windows reports it. Rectangles in physical pixels.</summary>
public sealed record MonitorInfo(
    string Key,
    string DeviceName,
    bool IsPrimary,
    Native.RECT Bounds,
    Native.RECT WorkArea,
    uint Dpi)
{
    public double Scale => Dpi / 96.0;

    /// <summary>"Pantalla 2 (1360×768)" for menus.</summary>
    public string Label(int index) => $"Pantalla {index + 1}{(IsPrimary ? " (principal)" : "")} · {Bounds.Width}×{Bounds.Height}";

    /// <summary>Whether a panel saved with this monitor key belongs here.</summary>
    public bool Owns(string panelMonitor, IReadOnlyList<MonitorInfo> all) =>
        panelMonitor == Key
        || (IsPrimary && (panelMonitor == MonitorKeys.Primary || all.All(m => m.Key != panelMonitor)));
}

internal static class Monitors
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var info = new Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (!Native.GetMonitorInfo(handle, ref info)) return true;
            var dpi = Native.GetDpiForMonitor(handle, 0, out var dx, out _) == 0 ? dx : 96u;
            list.Add(new MonitorInfo(StableKey(info.szDevice), info.szDevice, (info.dwFlags & 1) != 0, info.rcMonitor, info.rcWork, dpi));
            return true;
        }, IntPtr.Zero);

        // Primary first, then left to right: "Pantalla 1" is the main one.
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    /// <summary>
    /// The monitor's device interface path (it carries the model and port), which survives
    /// reboots; "\\.\DISPLAY2" alone can change when monitors are plugged in another order.
    /// </summary>
    private static string StableKey(string deviceName)
    {
        var dd = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
        return Native.EnumDisplayDevices(deviceName, 0, ref dd, Native.EDD_GET_DEVICE_INTERFACE_NAME) && !string.IsNullOrEmpty(dd.DeviceID)
            ? dd.DeviceID
            : deviceName;
    }
}
