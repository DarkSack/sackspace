using System.Text.Json.Nodes;
using SackSpace.Core.Model;

namespace SackSpace.Core.Widgets;

public enum WidgetSettingKind { Toggle, Choice, Number, Text }

/// <summary>An option the user changes in the widget's settings sheet. The sheet is built from these.</summary>
public sealed record WidgetSetting(
    string Key,
    string Label,
    WidgetSettingKind Kind,
    string Default,
    IReadOnlyList<string>? Options = null,
    double Min = 0,
    double Max = 0,
    double Step = 1);

/// <summary>What the gallery shows about a widget and how big it starts.</summary>
public sealed record WidgetDescriptor(
    string Id,
    string Name,
    string Description,
    string Glyph,
    double DefaultWidth,
    double DefaultHeight,
    double MinWidth,
    double MinHeight,
    IReadOnlyList<WidgetSetting> Settings);

/// <summary>
/// The widgets that ship with the app. The app maps each id to its view; a new widget
/// is a descriptor here (or from a plugin) plus a factory registered in WidgetRegistry.
/// </summary>
public static class BuiltInWidgets
{
    public const string Clock = "clock";
    public const string Calendar = "calendar";
    public const string Notes = "notes";
    public const string Tasks = "tasks";
    public const string SystemMonitor = "system";
    public const string Media = "media";
    public const string Shortcuts = "shortcuts";
    public const string Disks = "disks";
    public const string Network = "network";
    public const string Weather = "weather";
    public const string Timer = "timer";
    public const string Schedule = "schedule";
    public const string Usage = "aiusage";
    public const string Storage = "storage";
    public const string Obs = "obs";
    public const string PlayTime = "playtime";
    public const string Twitch = "twitch";
    public const string Clipboard = "clipboard";

    public static IReadOnlyList<WidgetDescriptor> All { get; } =
    [
        new(Clock, Loc.T("Reloj"), Loc.T("La hora y la fecha en grande."), Glyphs.Clock, 360, 168, 200, 110,
        [
            new("format24", Loc.T("Formato de 24 horas"), WidgetSettingKind.Toggle, "true"),
            new("seconds", Loc.T("Mostrar segundos"), WidgetSettingKind.Toggle, "false"),
            new("date", Loc.T("Mostrar la fecha"), WidgetSettingKind.Toggle, "true"),
        ]),
        new(Calendar, Loc.T("Calendario"), Loc.T("El mes de un vistazo, con el día de hoy marcado."), Glyphs.Calendar, 300, 300, 240, 250,
        [
            new("weekNumbers", Loc.T("Números de semana"), WidgetSettingKind.Toggle, "false"),
        ]),
        new(Notes, Loc.T("Notas"), Loc.T("Apuntes rápidos que se guardan solos."), Glyphs.Note, 300, 250, 180, 140,
        [
            new("title", Loc.T("Título"), WidgetSettingKind.Text, "Notas"),
            new("fontSize", Loc.T("Tamaño de letra"), WidgetSettingKind.Number, "14", Min: 11, Max: 24),
        ]),
        new(Tasks, Loc.T("Tareas"), Loc.T("Una lista de pendientes para marcar y tachar."), Glyphs.Tasks, 360, 330, 220, 180,
        [
            new("title", Loc.T("Título"), WidgetSettingKind.Text, "Tareas"),
            new("hideDone", Loc.T("Ocultar las completadas"), WidgetSettingKind.Toggle, "false"),
        ]),
        new(SystemMonitor, Loc.T("Monitor del sistema"), Loc.T("Uso de procesador, memoria y gráfica en tiempo real."), Glyphs.Gauge, 360, 220, 220, 150,
        [
            new("interval", Loc.T("Actualizar cada"), WidgetSettingKind.Choice, "2 s", ["1 s", "2 s", "5 s"]),
            new("gpu", Loc.T("Mostrar la tarjeta gráfica"), WidgetSettingKind.Toggle, "false"),
        ]),
        new(Media, Loc.T("Reproductor"), Loc.T("Lo que suena en Spotify, el navegador o cualquier app, con sus controles."), Glyphs.Music, 360, 150, 260, 120,
        []),
        new(Shortcuts, Loc.T("Accesos"), Loc.T("Un grupo de apps, archivos y carpetas a un clic. Arrastra aquí desde el Explorador."), Glyphs.Apps, 360, 200, 160, 120,
        [
            new("title", Loc.T("Título"), WidgetSettingKind.Text, "Accesos"),
            new("iconSize", Loc.T("Tamaño de los iconos"), WidgetSettingKind.Choice, "Mediano", ["Pequeño", "Mediano", "Grande"]),
            new("labels", Loc.T("Mostrar nombres"), WidgetSettingKind.Toggle, "true"),
            new("autoRemove", Loc.T("Quitar solos los que se desinstalen o borren"), WidgetSettingKind.Toggle, "true"),
            new("autoAddGames", Loc.T("Añadir solos los juegos que instales en Steam o Epic"), WidgetSettingKind.Toggle, "false"),
        ]),
        new(Disks, Loc.T("Discos"), Loc.T("Espacio libre en cada unidad."), Glyphs.Drive, 320, 180, 220, 110,
        [
            new("removable", Loc.T("Incluir memorias USB"), WidgetSettingKind.Toggle, "true"),
        ]),
        new(Storage, Loc.T("Almacenamiento"), Loc.T("En qué se te va el espacio de cada disco: juegos, sistema, programas, datos de apps, fotos, archivos, desarrollo… Toca una categoría para ver sus carpetas más grandes. Se mide en tu PC, sin internet."), Glyphs.Storage, 400, 460, 260, 180,
        [
            new("extra", Loc.T(@"Carpetas propias: Categoría=carpeta, separadas por ; (p. ej. Juegos=D:\JUEGOS; Desarrollo=E:\Dev)"), WidgetSettingKind.Text, ""),
            new("interval", Loc.T("Medir de nuevo cada"), WidgetSettingKind.Choice, "1 día", ["6 h", "12 h", "1 día", "1 semana", "Solo a mano"]),
        ]),
        new(Obs, Loc.T("OBS"), Loc.T("Si estás en directo o grabando, cuánto llevas, la subida y los cuadros perdidos, y botones para el directo, la grabación, la repetición y las escenas. Usa el servidor WebSocket de OBS en tu PC (Herramientas › Ajustes del servidor WebSocket)."), Glyphs.Broadcast, 380, 260, 260, 150,
        [
            new("scenes", Loc.T("Mostrar las escenas"), WidgetSettingKind.Toggle, "true"),
            new("confirmStream", Loc.T("Preguntar antes de iniciar o terminar el directo"), WidgetSettingKind.Toggle, "true"),
            new("sackclips", Loc.T("Dirección del panel de SackClips (para el botón Clip)"), WidgetSettingKind.Text, "http://127.0.0.1:28790"),
        ]),
        new(PlayTime, Loc.T("Tiempo de juego"), Loc.T("Las horas por juego hoy, en la semana, en el mes y en total, y tu juego más jugado del mes. Cuenta solo mientras el juego está delante (Steam, Epic o cualquiera en pantalla completa exclusiva). Se guarda en tu PC."), Glyphs.Game, 340, 300, 240, 170,
        [
            new("count", Loc.T("Cuántos juegos mostrar"), WidgetSettingKind.Number, "6", Min: 1, Max: 20),
        ]),
        new(Twitch, Loc.T("Twitch"), Loc.T("Tu canal: si estás en directo, espectadores, seguidores y los nuevos, y el título y la categoría para cambiarlos desde aquí. Se conecta con un código en twitch.tv/activate; usa internet."), Glyphs.Broadcast, 360, 330, 260, 170,
        [
            new("clientId", Loc.T("Client ID de tu app de Twitch (solo si SackSpace no trae uno)"), WidgetSettingKind.Text, ""),
        ]),
        new(Clipboard, Loc.T("Portapapeles"), Loc.T("Lo último que copiaste (textos y archivos); un clic lo vuelve a copiar para pegarlo. Solo en memoria: se borra al cerrar SackSpace y no guarda lo que los gestores de contraseñas marcan como privado."), Glyphs.Document, 340, 320, 220, 150,
        [
            new("keep", Loc.T("Cuántos recordar"), WidgetSettingKind.Number, "25", Min: 5, Max: 100, Step: 5),
        ]),
        new(Network, Loc.T("Red"), Loc.T("Velocidad de bajada y subida en tiempo real."), Glyphs.Network, 360, 160, 220, 120,
        [
            new("interval", Loc.T("Actualizar cada"), WidgetSettingKind.Choice, "2 s", ["1 s", "2 s", "5 s"]),
        ]),
        new(Weather, Loc.T("Clima"), Loc.T("El tiempo de ahora y de los próximos días (Open-Meteo, sin cuenta). Usa internet."), Glyphs.Weather, 340, 200, 220, 120,
        [
            new("city", Loc.T("Ciudad"), WidgetSettingKind.Text, ""),
            new("days", Loc.T("Mostrar los próximos días"), WidgetSettingKind.Toggle, "true"),
        ]),
        new(Timer, Loc.T("Temporizador"), Loc.T("Cuenta atrás, Pomodoro o cronómetro, con aviso al terminar."), Glyphs.Timer, 300, 190, 220, 150,
        [
            new("mode", Loc.T("Modo"), WidgetSettingKind.Choice, "Temporizador", ["Temporizador", "Pomodoro", "Cronómetro"]),
            new("minutes", Loc.T("Minutos del temporizador"), WidgetSettingKind.Number, "10", Min: 1, Max: 180),
            new("work", Loc.T("Minutos de trabajo (Pomodoro)"), WidgetSettingKind.Number, "25", Min: 5, Max: 90, Step: 5),
            new("break", Loc.T("Minutos de descanso (Pomodoro)"), WidgetSettingKind.Number, "5", Min: 1, Max: 30),
        ]),
        new(Schedule, Loc.T("Horario"), Loc.T("Tu turno de hoy, cuánto falta para entrar, comer o salir, tus actividades y la semana. Los turnos se escriben en sus opciones; las actividades, con el +."), Glyphs.Work, 760, 520, 240, 150,
        [
            new("mon", Loc.T("Lunes (p. ej. 09:00-18:00 comida 14:00-15:00, o Descanso)"), WidgetSettingKind.Text, ""),
            new("tue", Loc.T("Martes"), WidgetSettingKind.Text, ""),
            new("wed", Loc.T("Miércoles"), WidgetSettingKind.Text, ""),
            new("thu", Loc.T("Jueves"), WidgetSettingKind.Text, ""),
            new("fri", Loc.T("Viernes"), WidgetSettingKind.Text, ""),
            new("sat", Loc.T("Sábado"), WidgetSettingKind.Text, ""),
            new("sun", Loc.T("Domingo"), WidgetSettingKind.Text, ""),
        ]),
        new(Usage, Loc.T("Uso de IA"), Loc.T("Cuánto llevas de las 5 horas y de la semana en tus cuentas de Claude y ChatGPT. Con una carpeta de Claude Code se pregunta a Anthropic con tu sesión (usa internet); con una de Claude Desktop se lee lo que la app guarda cada 15 min mientras está abierta; ChatGPT se lee de las sesiones de Codex en tu PC."), Glyphs.Gauge, 340, 280, 240, 150,
        [
            new("claude", Loc.T("Cuentas de Claude: Nombre=carpeta de Claude Code o de Claude Desktop, separadas por ;"), WidgetSettingKind.Text, @"Escritorio=%APPDATA%\Claude; Claude Code=%USERPROFILE%\.claude"),
            new("chatgpt", Loc.T("Cuentas de ChatGPT: Nombre=carpeta de Codex, separadas por ;"), WidgetSettingKind.Text, @"Principal=%USERPROFILE%\.codex"),
        ]),
    ];

    public static WidgetDescriptor? Find(string id) => All.FirstOrDefault(w => w.Id == id);
}

/// <summary>Typed reads and writes of a panel's settings object.</summary>
public static class PanelSettings
{
    public static bool GetBool(this JsonObject s, string key, bool fallback) =>
        s[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b
        : s[key] is JsonValue t && t.TryGetValue<string>(out var str) && bool.TryParse(str, out var parsed) ? parsed
        : fallback;

    public static double GetDouble(this JsonObject s, string key, double fallback) =>
        s[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d
        : s[key] is JsonValue n && n.TryGetValue<int>(out var i) ? i // set in code as an int, not yet saved
        : s[key] is JsonValue t && t.TryGetValue<string>(out var str) && double.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed
        : fallback;

    public static string GetString(this JsonObject s, string key, string fallback) =>
        s[key] is JsonValue v && v.TryGetValue<string>(out var str) ? str : fallback;

    /// <summary>A setting's value, or its declared default.</summary>
    public static string Value(this JsonObject s, WidgetSetting setting) => setting.Kind switch
    {
        WidgetSettingKind.Toggle => s.GetBool(setting.Key, bool.Parse(setting.Default)) ? "true" : "false",
        WidgetSettingKind.Number => s.GetDouble(setting.Key, double.Parse(setting.Default, System.Globalization.CultureInfo.InvariantCulture))
            .ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => s.GetString(setting.Key, setting.Default),
    };
}
