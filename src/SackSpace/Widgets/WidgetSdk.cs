using System.Text.Json.Nodes;
using System.Windows;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>
/// How a widget plugs in: a descriptor (name, icon, sizes, options) and a function that
/// builds its view. A view that subscribes to services implements IDisposable; it is
/// disposed when its panel leaves the screen (another workspace, removed, rebuilt).
/// </summary>
public interface IWidgetFactory
{
    WidgetDescriptor Descriptor { get; }
    FrameworkElement Create(WidgetContext context);
}

public sealed class WidgetFactory(WidgetDescriptor descriptor, Func<WidgetContext, FrameworkElement> create) : IWidgetFactory
{
    public WidgetDescriptor Descriptor { get; } = descriptor;
    public FrameworkElement Create(WidgetContext context) => create(context);
}

/// <summary>What a widget can reach: its own panel and data, the shared services and its screen.</summary>
public sealed class WidgetContext(PanelState panel, AppServices services, HostView host)
{
    public PanelState Panel { get; } = panel;
    public JsonObject Settings => Panel.Settings;
    public AppServices Services { get; } = services;
    public HostView Host { get; } = host;

    /// <summary>Pixels per DIP on this monitor, for crisp icons.</summary>
    public double Scale => Host.DpiScale;

    /// <summary>The widget changed its data: saved shortly.</summary>
    public void Save() => Services.Session.MarkDirty();

    public void Toast(string text) => Host.ShowSnackbar(text);

    /// <summary>Makes the panel this tall, and this wide if given (it stays inside the screen).</summary>
    public void Resize(double height, double? width = null) => Host.ResizePanel(Panel, height, width);
}

public static class WidgetRegistry
{
    private static readonly Dictionary<string, IWidgetFactory> Factories = new();

    public static void Register(IWidgetFactory factory) => Factories[factory.Descriptor.Id] = factory;

    public static IWidgetFactory? Find(string id) => Factories.GetValueOrDefault(id);

    /// <summary>Registered widgets in the gallery's order.</summary>
    public static IReadOnlyList<WidgetDescriptor> All =>
        BuiltInWidgets.All.Where(d => Factories.ContainsKey(d.Id))
            .Concat(Factories.Values.Select(f => f.Descriptor).Where(d => BuiltInWidgets.Find(d.Id) is null))
            .ToList();

    public static void RegisterBuiltIns()
    {
        WidgetDescriptor D(string id) => BuiltInWidgets.Find(id)!;
        Register(new WidgetFactory(D(BuiltInWidgets.Clock), c => new ClockWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Calendar), c => new CalendarWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Notes), c => new NotesWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Tasks), c => new TasksWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.SystemMonitor), c => new SystemMonitorWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Media), c => new MediaWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Shortcuts), c => new ShortcutsWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Disks), c => new DisksWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Network), c => new NetworkWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Weather), c => new WeatherWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Timer), c => new TimerWidget(c)));
        Register(new WidgetFactory(D(BuiltInWidgets.Schedule), c => new ScheduleWidget(c)));
    }
}
