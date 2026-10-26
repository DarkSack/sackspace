using System.Runtime.InteropServices;

namespace SackSpace.Interop;

/// <summary>
/// GPU use the way Task Manager's "3D" graph reads it: the PDH counter
/// "GPU Engine(*engtype_3D)\Utilization Percentage", summed over every process.
/// The first reading after opening is empty (it is a rate); null when Windows has no such counter.
/// </summary>
public sealed partial class GpuCounter : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x200;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    private IntPtr _query, _counter;

    public GpuCounter()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return; }
        if (PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _counter) != 0) Dispose();
        else PdhCollectQueryData(_query);
    }

    public bool Available => _query != IntPtr.Zero;

    /// <summary>0–1, or null when it cannot be read.</summary>
    public double? Read()
    {
        if (!Available || PdhCollectQueryData(_query) != 0) return null;
        uint size = 0;
        if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out _, IntPtr.Zero) != PDH_MORE_DATA || size == 0) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out var count, buffer) != 0) return null;
            double sum = 0;
            var itemSize = Marshal.SizeOf<Item>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<Item>(buffer + i * itemSize);
                if (item.Status == 0) sum += item.Value;
            }
            return Math.Clamp(sum / 100, 0, 1);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    // PDH_FMT_COUNTERVALUE_ITEM_W: the name pointer, then { CStatus, (padding), double }.
    [StructLayout(LayoutKind.Sequential)]
    private struct Item
    {
        public IntPtr Name;
        public uint Status;
        public double Value;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCollectQueryData(IntPtr query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static partial int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCloseQuery(IntPtr query);
}
