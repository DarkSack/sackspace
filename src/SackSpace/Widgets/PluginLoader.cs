using System.IO;
using System.Reflection;
using SackSpace.Core.Widgets;

namespace SackSpace.Widgets;

/// <summary>A folder in plugins\ and what came of loading it.</summary>
public sealed record PluginInfo(string Name, string Folder, bool Enabled, IReadOnlyList<string> Widgets, string? Error);

/// <summary>
/// Third-party widgets: each plugin is a folder in &lt;data&gt;\plugins with a DLL of the same name
/// that has public IWidgetFactory classes (parameterless constructor). A plugin runs as normal
/// code inside SackSpace, so only the ones the user switched on in Settings are loaded,
/// once, at startup; switching one off takes effect after a restart.
/// </summary>
public static class PluginLoader
{
    public static string Folder(string dataFolder) => Path.Combine(dataFolder, "plugins");

    /// <summary>Every plugin folder found, loading (and registering the widgets of) the enabled ones.</summary>
    public static IReadOnlyList<PluginInfo> Load(string dataFolder, ICollection<string> enabled)
    {
        var root = Folder(dataFolder);
        if (!Directory.Exists(root)) return [];
        var list = new List<PluginInfo>();
        foreach (var dir in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (!enabled.Contains(name))
            {
                list.Add(new PluginInfo(name, dir, false, [], null));
                continue;
            }
            try
            {
                var dll = Path.Combine(dir, name + ".dll");
                if (!File.Exists(dll)) throw new FileNotFoundException(Core.Loc.T("Falta {0}.", name + ".dll"));
                var widgets = new List<string>();
                foreach (var type in Assembly.LoadFrom(dll).GetExportedTypes()
                             .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IWidgetFactory).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null))
                {
                    var factory = (IWidgetFactory)Activator.CreateInstance(type)!;
                    // A plugin cannot replace a built-in widget or another plugin's.
                    if (BuiltInWidgets.Find(factory.Descriptor.Id) is not null || WidgetRegistry.Find(factory.Descriptor.Id) is not null)
                    {
                        Log.Warn($"Plugin {name}: el widget '{factory.Descriptor.Id}' ya existe; se ignora");
                        continue;
                    }
                    WidgetRegistry.Register(factory);
                    widgets.Add(factory.Descriptor.Name);
                }
                list.Add(new PluginInfo(name, dir, true, widgets, widgets.Count == 0 ? Core.Loc.T("No trae ningún widget.") : null));
                Log.Info($"Plugin {name}: {widgets.Count} widget(s)");
            }
            catch (Exception ex)
            {
                Log.Error($"Plugin {name} no se pudo cargar", ex);
                list.Add(new PluginInfo(name, dir, true, [], ex is ReflectionTypeLoadException or BadImageFormatException or FileLoadException
                    ? Core.Loc.T("No es compatible con esta versión de SackSpace.") : ex.Message));
            }
        }
        return list;
    }
}
