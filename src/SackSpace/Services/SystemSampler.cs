using System.Windows.Threading;
using SackSpace.Interop;

namespace SackSpace.Services;

public sealed record SystemSample(double Cpu, double Ram, ulong UsedBytes, ulong TotalBytes);

/// <summary>
/// CPU and memory use, read straight from the kernel (two cheap calls, no performance
/// counters). Runs only while a widget wants it, at the fastest interval asked for,
/// and keeps the last 60 samples so a new widget draws its graph at once.
/// </summary>
public sealed class SystemSampler
{
    public const int HistoryLength = 60;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly Dictionary<object, TimeSpan> _subscribers = new();
    private readonly List<double> _cpu = new(), _ram = new();
    private ulong _lastIdle, _lastTotal;
    private bool _paused;

    public IReadOnlyList<double> CpuHistory => _cpu;
    public IReadOnlyList<double> RamHistory => _ram;
    public SystemSample? Last { get; private set; }
    public event Action<SystemSample>? Sampled;

    public SystemSampler() => _timer.Tick += (_, _) => Sample();

    public void Subscribe(object owner, TimeSpan interval)
    {
        _subscribers[owner] = interval;
        Update();
        if (Last is null) Sample();
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
        if (_subscribers.Count == 0 || _paused) { _timer.Stop(); return; }
        var interval = _subscribers.Values.Min();
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

        Push(_cpu, cpu);
        Push(_ram, ram);
        Last = new SystemSample(cpu, ram, used, mem.ullTotalPhys);
        Sampled?.Invoke(Last);
    }

    private static void Push(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > HistoryLength) list.RemoveAt(0);
    }
}
