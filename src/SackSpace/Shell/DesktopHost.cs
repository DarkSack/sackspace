using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SackSpace.Interop;
using SackSpace.Services;

namespace SackSpace.Shell;

/// <summary>
/// A transparent window over one monitor's work area that lives on the desktop: owned
/// by Explorer's desktop window (so Win+D and Alt+Tab leave it alone) and held at the
/// bottom of the z-order unless raised. Transparent pixels let clicks through to the
/// desktop icons; only the panels catch the mouse.
/// </summary>
public sealed class DesktopHost : Window
{
    private static readonly uint TaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

    private readonly HostManager _manager;
    private IntPtr _hwnd;
    private bool _raised;

    public HostView View { get; }
    public MonitorInfo Monitor => View.Monitor;
    /// <summary>Set by the manager before a planned close; anything else (Alt+F4) is refused.</summary>
    public bool AllowClose { get; set; }

    public DesktopHost(HostManager manager, AppServices services, MonitorInfo monitor)
    {
        _manager = manager;
        Title = "SackSpace";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Rough DIP position on the right monitor, so WPF starts with its DPI; Place() sets the exact pixels.
        Left = monitor.WorkArea.Left / monitor.Scale;
        Top = monitor.WorkArea.Top / monitor.Scale;
        Width = monitor.WorkArea.Width / monitor.Scale;
        Height = monitor.WorkArea.Height / monitor.Scale;

        View = new HostView(manager, services, monitor);
        Content = View;
        SourceInitialized += (_, _) => Setup();
        Deactivated += (_, _) => _manager.OnHostDeactivated();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            View.HandleEscape();
            e.Handled = true;
        };
    }

    private void Setup()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, (IntPtr)((ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW));
        AttachToDesktop();
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        Place();
    }

    /// <summary>Owner = the desktop window. Called again when Explorer restarts (its window is new).</summary>
    public void AttachToDesktop()
    {
        if (_hwnd == IntPtr.Zero) return;
        var progman = Native.FindWindow("Progman", null);
        if (progman != IntPtr.Zero) Native.SetWindowLongPtr(_hwnd, Native.GWL_HWNDPARENT, progman);
    }

    /// <summary>Exactly over the work area, in physical pixels.</summary>
    public void Place()
    {
        if (_hwnd == IntPtr.Zero) return;
        var w = Monitor.WorkArea;
        Native.SetWindowPos(_hwnd, _raised ? Native.HWND_TOPMOST : Native.HWND_BOTTOM,
            w.Left, w.Top, w.Width, w.Height, Native.SWP_NOACTIVATE);
    }

    public void SetRaised(bool raised)
    {
        _raised = raised;
        if (_hwnd == IntPtr.Zero) return;
        if (raised)
        {
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE);
        }
        else
        {
            Native.SetWindowPos(_hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    /// <summary>Takes the keyboard (raised mode, sheets).</summary>
    public void FocusHost()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.SetForegroundWindow(_hwnd);
        Activate();
    }

    public IntPtr Handle => _hwnd;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_WINDOWPOSCHANGING && !_raised)
        {
            // Clicking a panel would bring the window up over the apps; keep it on the desktop.
            var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
            if ((pos.flags & Native.SWP_NOZORDER) == 0 && pos.hwndInsertAfter != Native.HWND_BOTTOM)
            {
                pos.hwndInsertAfter = Native.HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        else if (msg == Native.WM_DPICHANGED)
        {
            // WPF resizes to its suggestion first; then back onto the work area.
            Dispatcher.BeginInvoke(Place, DispatcherPriority.Background);
        }
        else if (msg == TaskbarCreated)
        {
            Dispatcher.BeginInvoke(_manager.OnExplorerRestarted, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) e.Cancel = true;
        base.OnClosing(e);
    }
}
