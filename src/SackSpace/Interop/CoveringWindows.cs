using System.Text;

namespace SackSpace.Interop;

/// <summary>Which monitors are hidden behind a maximized or full-screen window of another app.</summary>
internal static class CoveringWindows
{
    private const long WS_EX_TRANSPARENT = 0x20;
    // The desktop itself spans every monitor; it covers nothing.
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static HashSet<string> Monitors(IReadOnlyList<MonitorInfo> monitors)
    {
        var covered = new HashSet<string>();
        var name = new StringBuilder(64);
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || !Native.GetWindowRect(hwnd, out var r)) return true;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            // Click-through overlays (game bars, FPS counters) span the screen but hide nothing.
            if ((Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() & WS_EX_TRANSPARENT) != 0) return true;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId) return true;
            name.Clear();
            Native.GetClassName(hwnd, name, name.Capacity);
            if (ShellClasses.Contains(name.ToString())) return true;

            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            foreach (var m in monitors)
            {
                var w = m.WorkArea;
                if (cx < m.Bounds.Left || cx >= m.Bounds.Right || cy < m.Bounds.Top || cy >= m.Bounds.Bottom) continue;
                // Maximized, or at least as big as the work area (borderless games, F11 browsers).
                if (Native.IsZoomed(hwnd) || (r.Left <= w.Left && r.Top <= w.Top && r.Right >= w.Right && r.Bottom >= w.Bottom))
                    covered.Add(m.Key);
                break;
            }
            return true;
        }, IntPtr.Zero);
        return covered;
    }
}
