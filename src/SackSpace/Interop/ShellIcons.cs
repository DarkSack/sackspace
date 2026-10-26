using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SackSpace.Interop;

/// <summary>
/// Icons as Explorer shows them, for files, folders, shortcuts and Store apps
/// ("shell:AppsFolder\&lt;AUMID&gt;"), through IShellItemImageFactory.
///
/// Shell objects are STA COM: one background STA thread with a queue does all the
/// work, so the UI never waits on a slow network path. Results are cached.
/// Same approach as SackDesk's AppIcons, which learned the hard way that the shell's
/// bitmap is a bottom-up DIB with premultiplied alpha.
/// </summary>
internal static class ShellIcons
{
    private static readonly ConcurrentDictionary<(string, int), Task<ImageSource?>> Cache = new();
    private static readonly BlockingCollection<(string Path, int Size, TaskCompletionSource<ImageSource?> Done)> Queue = new();
    private static readonly Lazy<Thread> Worker = new(() =>
    {
        var t = new Thread(Loop) { IsBackground = true, Name = "ShellIcons STA" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return t;
    });

    /// <param name="sizePx">Pixel size wanted (DIPs × monitor scale).</param>
    public static Task<ImageSource?> GetAsync(string parsingName, int sizePx) =>
        Cache.GetOrAdd((parsingName, sizePx), key =>
        {
            _ = Worker.Value;
            var done = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Add((key.Item1, key.Item2, done));
            return done.Task;
        });

    private static void Loop()
    {
        foreach (var (path, size, done) in Queue.GetConsumingEnumerable())
        {
            try { done.SetResult(Extract(path, size)); }
            catch { done.SetResult(null); }
        }
    }

    private static ImageSource? Extract(string parsingName, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory) != 0 || factory is null)
            return null;

        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            if (factory.GetImage(new NativeSize { cx = size, cy = size }, SIIGBF.BiggerSizeOk | SIIGBF.IconOnly, out hbitmap) != 0
                || hbitmap == IntPtr.Zero)
                return null;
            return ToBitmapSource(hbitmap);
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) Native.DeleteObject(hbitmap);
            Marshal.ReleaseComObject(factory);
        }
    }

    private static BitmapSource? ToBitmapSource(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<DIBSECTION>(), out var ds) == 0) return null;
        int w = ds.dsBm.bmWidth, h = Math.Abs(ds.dsBm.bmHeight);
        if (ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32 || w <= 0 || h <= 0) return null;

        var stride = ds.dsBm.bmWidthBytes;
        var pixels = new byte[stride * h];
        Marshal.Copy(ds.dsBm.bmBits, pixels, 0, pixels.Length);

        // Old icons carry no alpha at all: treated as premultiplied they would vanish.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) { hasAlpha = true; break; }
        if (!hasAlpha)
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        if (ds.dsBmih.biHeight > 0)
        {
            var flipped = new byte[pixels.Length];
            for (var y = 0; y < h; y++) Buffer.BlockCopy(pixels, (h - 1 - y) * stride, flipped, y * stride, stride);
            pixels = flipped;
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr obj, int size, out DIBSECTION result);

    [Flags]
    private enum SIIGBF { BiggerSizeOk = 0x1, IconOnly = 0x4 }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);
}
