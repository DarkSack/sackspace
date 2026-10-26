using System.Windows.Input;
using System.Windows.Interop;
using SackSpace.Core.Hotkeys;
using SackSpace.Interop;

namespace SackSpace.Services;

/// <summary>Global shortcuts through RegisterHotKey on a message-only window.</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _actions = new();
    private int _nextId = 1;

    public event Action<string>? Pressed;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("SackSpaceHotkeys")
        {
            ParentWindow = Native.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <summary>Registers the shortcuts; returns the actions whose shortcut is taken by another app or invalid.</summary>
    public IReadOnlyList<string> Register(IEnumerable<KeyValuePair<string, string>> map)
    {
        UnregisterAll();
        var failed = new List<string>();
        foreach (var (action, text) in map)
        {
            if (string.IsNullOrWhiteSpace(text)) continue; // turned off
            if (!HotkeyGesture.TryParse(text, out var gesture) || !Enum.TryParse<Key>(gesture.Key, out var key))
            {
                failed.Add(action);
                continue;
            }
            var id = _nextId++;
            var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (Native.RegisterHotKey(_source.Handle, id, (uint)gesture.Modifiers | Native.MOD_NOREPEAT, vk))
                _actions[id] = action;
            else
                failed.Add(action);
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
