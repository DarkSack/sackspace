using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using SackSpace.Interop;

namespace SackSpace.Services;

/// <summary>What a widget wants measured besides CPU and memory (those are always cheap).</summary>
[Flags]
public enum SampleKinds { Basic = 0, Network = 1, Gpu = 2 }

/// <summary>Gpu is null when not asked for or not readable; network speeds are bytes per second.</summary>
public sealed record SystemSample(double Cpu, double Ram, ulong UsedBytes, ulong TotalBytes, double? Gpu, double Down, double Up);

/// <summary>
/// CPU and memory use, read straight from the kernel (two cheap calls, no performance
/// counters); network and GPU only while a widget asks for them. Runs only while a
/// widget wants it, at the fastest interval asked for, and keeps the last 60 samples
/// so a new widget draws its graph at once.
/// </summary>
public sealed class SystemSampler
{
    public const int HistoryLength = 60;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly Dictionary<object, (TimeSpan Interval, SampleKinds Kinds)> _subscribers = new();
    private readonly List<double> _cpu = new(), _ram = new(), _gpu = new(), _down = new(), _up = new();
    private readonly Stopwatch _netClock = new();
    private ulong _lastIdle, _lastTotal;
    private long _lastRx = -1, _lastTx = -1;
    private GpuCounter? _gpuCounter;
    private SampleKinds _kinds;
    private bool _paused;

    public IReadOnlyList<double> CpuHistory => _cpu;
    public IReadOnlyList<double> RamHistory => _ram;
    public IReadOnlyList<double> GpuHistory => _gpu;
    public IReadOnlyList<double> DownHistory => _down;
    public IReadOnlyList<double> UpHistory => _up;
    public SystemSample? Last { get; private set; }
    public event Action<SystemSample>? Sampled;

    public SystemSampler() => _timer.Tick += (_, _) => Sample();

    public void Subscribe(object owner, TimeSpan interval, SampleKinds kinds = SampleKinds.Basic)
    {
        _subscribers[owner] = (interval, kinds);
        var added = kinds & ~_kinds;
        Update();
        if (Last is null || added != 0) Sample();
    }

    public void Unsubscribe(object owner)
    {
        _subscribers.Remove(owner);
        Update();
    }

    public bool Paused
    {
        get => _paused;
        set { _paused = value; Update(); }
    }

    private void Update()
    {
        _kinds = _subscribers.Values.Aggregate(SampleKinds.Basic, (k, s) => k | s.Kinds);
        if (!_kinds.HasFlag(SampleKinds.Network)) _lastRx = _lastTx = -1; // a stale baseline would show one huge spike
        if (_subscribers.Count == 0 || _paused) { _timer.Stop(); return; }
        var interval = _subscribers.Values.Min(s => s.Interval);
        if (_timer.Interval != interval) _timer.Interval = interval;
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void Sample()
    {
        double cpu = 0;
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            // Kernel time includes idle time.
            var total = kernel.Value + user.Value;
            if (_lastTotal != 0 && total > _lastTotal)
                cpu = Math.Clamp(1 - (idle.Value - _lastIdle) / (double)(total - _lastTotal), 0, 1);
            _lastIdle = idle.Value;
            _lastTotal = total;
        }

        var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        Native.GlobalMemoryStatusEx(ref mem);
        var used = mem.ullTotalPhys - mem.ullAvailPhys;
        var ram = mem.ullTotalPhys == 0 ? 0 : used / (double)mem.ullTotalPhys;

        double? gpu = null;
        if (_kinds.HasFlag(SampleKinds.Gpu))
        {
            _gpuCounter ??= new GpuCounter();
            gpu = _gpuCounter.Read();
            if (gpu is { } g) Push(_gpu, g);
        }

        double down = 0, up = 0;
        if (_kinds.HasFlag(SampleKinds.Network))
        {
            var (rx, tx) = NetworkBytes();
            var seconds = _netClock.Elapsed.TotalSeconds;
            if (_lastRx >= 0 && seconds > 0.2)
            {
                // Counters restart when an adapter goes down: never negative.
                down = Math.Max(0, rx - _lastRx) / seconds;
                up = Math.Max(0, tx - _lastTx) / seconds;
                Push(_down, down);
                Push(_up, up);
            }
            (_lastRx, _lastTx) = (rx, tx);
            _netClock.Restart();
        }

        Push(_cpu, cpu);
        Push(_ram, ram);
        Last = new SystemSample(cpu, ram, used, mem.ullTotalPhys, gpu, down, up);
        Sampled?.Invoke(Last);
    }

    /// <summary>Bytes received and sent so far by the adapters that are up (no loopback or tunnels).</summary>
    private static (long Rx, long Tx) NetworkBytes()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up
                    || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = n.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
        }
        catch (NetworkInformationException ex) { Log.Warn("No se pudo leer la red", ex); }
        return (rx, tx);
    }

    private static void Push(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > HistoryLength) list.RemoveAt(0);
    }
}
