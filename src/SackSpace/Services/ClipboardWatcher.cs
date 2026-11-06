using Loc = SackSpace.Core.Loc;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SackSpace.Core.Widgets;
using SackSpace.Interop;

namespace SackSpace.Services;

/// <summary>
/// Remembers the last things copied (text and files) while SackSpace runs, in memory only:
/// nothing is written to disk. Copies that password managers mark as private
/// (ExcludeClipboardContentFromMonitorProcessing, CanIncludeInClipboardHistory = 0) are skipped.
/// Starts the first time a clipboard panel asks for it.
/// </summary>
public sealed partial class ClipboardWatcher
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly uint Exclude = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint History = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    private static readonly uint ViewerIgnore = RegisterClipboardFormatW("Clipboard Viewer Ignore");
    private readonly HwndSource _source;

    public List<ClipEntry> Entries { get; } = [];
    public int Keep { get; set; } = 25;
    public event Action? Changed;

    public ClipboardWatcher()
    {
        _source = new HwndSource(new HwndSourceParameters("SackSpaceClipboard") { ParentWindow = Native.HWND_MESSAGE, WindowStyle = 0 });
        _source.AddHook(WndProc);
        AddClipboardFormatListener(_source.Handle);
    }

    public void Clear()
    {
        Entries.Clear();
        Changed?.Invoke();
    }

    /// <summary>Puts an entry back on the clipboard (it moves to the top of the list).</summary>
    public bool Paste(ClipEntry entry)
    {
        try
        {
            if (entry.Files)
            {
                var files = new System.Collections.Specialized.StringCollection();
                files.AddRange(entry.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                Clipboard.SetFileDropList(files);
            }
            else Clipboard.SetText(entry.Text);
            return true;
        }
        catch (COMException ex)
        {
            Log.Warn("El portapapeles está ocupado", ex);
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) Read();
        return IntPtr.Zero;
    }

    private void Read()
    {
        if (IsPrivate()) return;
        try
        {
            ClipEntry? entry = null;
            if (Clipboard.ContainsFileDropList())
                entry = new ClipEntry(string.Join('\n', Clipboard.GetFileDropList().Cast<string>()), true, DateTime.Now);
            else if (Clipboard.ContainsText())
                entry = new ClipEntry(Clipboard.GetText(), false, DateTime.Now);
            if (entry is null) return;
            ClipHistory.Add(Entries, entry, Keep);
            Changed?.Invoke();
        }
        catch (COMException) { } // another app holds the clipboard right now; the next copy is read anyway
    }

    private static bool IsPrivate()
    {
        if (IsClipboardFormatAvailable(Exclude) || IsClipboardFormatAvailable(ViewerIgnore)) return true;
        if (!IsClipboardFormatAvailable(History)) return false;
        try
        {
            // A DWORD 0 means "do not keep this in a history".
            return Clipboard.GetData("CanIncludeInClipboardHistory") is System.IO.MemoryStream { Length: >= 4 } s && BitConverter.ToInt32(s.ToArray(), 0) == 0;
        }
        catch (COMException) { return true; }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AddClipboardFormatListener(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);
}
