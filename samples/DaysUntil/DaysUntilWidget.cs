using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;
using SackSpace.Widgets;

namespace DaysUntil;

/// <summary>A plugin is any public IWidgetFactory with a parameterless constructor.</summary>
public sealed class DaysUntilFactory : IWidgetFactory
{
    public WidgetDescriptor Descriptor { get; } = new(
        "sample.daysuntil", "Cuenta de días", "Cuántos días faltan para una fecha.", Glyphs.Calendar,
        DefaultWidth: 260, DefaultHeight: 150, MinWidth: 200, MinHeight: 120,
        Settings:
        [
            new WidgetSetting("what", "Para qué", WidgetSettingKind.Text, "Vacaciones"),
            new WidgetSetting("date", "Fecha (AAAA-MM-DD)", WidgetSettingKind.Text, "2026-12-24"),
        ]);

    public FrameworkElement Create(WidgetContext context) => new DaysUntilView(context);
}

/// <summary>Options live in context.Settings; the panel is rebuilt when they change.</summary>
public sealed class DaysUntilView : StackPanel
{
    public DaysUntilView(WidgetContext c)
    {
        var what = c.Settings["what"]?.GetValue<string>() ?? "";
        var text = c.Settings["date"]?.GetValue<string>() ?? "";
        Children.Add(Ui.Header(Glyphs.Calendar, what));
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            Children.Add(Ui.Text("Escribe la fecha como 2026-12-24 en las opciones.", wrap: true));
            return;
        }
        var days = (date.Date - DateTime.Today).Days;
        Children.Add(Ui.Text(days switch { 0 => "¡Hoy!", < 0 => $"Hace {-days} días", 1 => "Mañana", _ => $"{days} días" }, size: 34));
    }
}
