using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

public sealed record WeatherDay(DateTime Date, int Code, double Max, double Min);
public sealed record WeatherReport(string Place, double Temperature, double Feels, int Code, double Humidity, double Wind, IReadOnlyList<WeatherDay> Days, DateTime FetchedAt);

/// <summary>
/// Open-Meteo (free, no key, no account): the city's coordinates once, then the forecast.
/// The one place SackSpace goes online, and only for a city the user typed. Reports are
/// shared by every panel with the same city and kept for half an hour.
/// </summary>
public static class WeatherService
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Dictionary<string, Task<WeatherReport>> Reports = new(StringComparer.OrdinalIgnoreCase);

    static WeatherService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("SackSpace/0.3");

    /// <summary>The cached report while fresh; otherwise a new request (a failed one is retried next time).</summary>
    public static Task<WeatherReport> Get(string city)
    {
        if (Reports.TryGetValue(city, out var task) && !(task.IsFaulted || task.IsCanceled)
            && (!task.IsCompletedSuccessfully || DateTime.UtcNow - task.Result.FetchedAt < MaxAge))
            return task;
        return Reports[city] = Fetch(city);
    }

    private static async Task<WeatherReport> Fetch(string city)
    {
        var places = await GetJson($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=es&format=json");
        var place = places["results"]?[0] ?? throw new InvalidOperationException("No se encontró la ciudad.");
        var lat = place["latitude"]!.GetValue<double>().ToString(CultureInfo.InvariantCulture);
        var lon = place["longitude"]!.GetValue<double>().ToString(CultureInfo.InvariantCulture);
        var name = place["name"]?.GetValue<string>() ?? city;

        var f = await GetJson($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}"
            + "&current=temperature_2m,apparent_temperature,weather_code,relative_humidity_2m,wind_speed_10m"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4");
        var now = f["current"]!;
        var daily = f["daily"]!;
        var days = new List<WeatherDay>();
        for (var i = 0; i < daily["time"]!.AsArray().Count; i++)
            days.Add(new WeatherDay(
                DateTime.ParseExact(daily["time"]![i]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                daily["weather_code"]![i]!.GetValue<int>(),
                daily["temperature_2m_max"]![i]!.GetValue<double>(),
                daily["temperature_2m_min"]![i]!.GetValue<double>()));
        return new WeatherReport(name, now["temperature_2m"]!.GetValue<double>(), now["apparent_temperature"]!.GetValue<double>(),
            now["weather_code"]!.GetValue<int>(), now["relative_humidity_2m"]!.GetValue<double>(), now["wind_speed_10m"]!.GetValue<double>(),
            days, DateTime.UtcNow);
    }

    private static async Task<JsonNode> GetJson(string url) =>
        JsonNode.Parse(await Http.GetStringAsync(url)) ?? throw new InvalidOperationException("Respuesta vacía.");
}

/// <summary>Now (symbol, temperature, sky) and the next days. Looks again every half hour while shown.</summary>
public sealed class WeatherWidget : Grid, IDisposable
{
    private readonly WidgetContext _c;
    private readonly string _city;
    private readonly bool _showDays;
    private bool _loading;

    public WeatherWidget(WidgetContext c)
    {
        _c = c;
        _city = c.Settings.GetString("city", "").Trim();
        _showDays = c.Settings.GetBool("days", true);
        if (_city.Length == 0)
        {
            Children.Add(Message("Escribe tu ciudad en las opciones del widget (el engranaje en modo edición, o clic derecho › Opciones)."));
            return;
        }
        Children.Add(Message("Cargando el tiempo de " + _city + "…"));
        c.Services.Clock.Minute += OnMinute;
        _ = Load();
    }

    private void OnMinute(DateTime now) => _ = Load();

    private async Task Load()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var task = WeatherService.Get(_city);
            if (task.IsCompletedSuccessfully && Children.Count > 0 && Tag == task) return; // already showing this report
            var report = await task;
            Tag = task;
            Show(report);
        }
        catch (Exception ex)
        {
            Log.Warn($"Clima de '{_city}'", ex);
            // Keep the last report on screen; say so only when there is nothing to show.
            if (Tag is null)
            {
                Children.Clear();
                Children.Add(Message(ex is InvalidOperationException ? $"No encontré «{_city}». Prueba con otro nombre." : "Sin conexión. Lo intento de nuevo en un rato."));
            }
        }
        finally { _loading = false; }
    }

    private void Show(WeatherReport r)
    {
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var (sky, symbol) = WeatherCodes.Describe(r.Code);
        var now = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var icon = Symbol(symbol, 44);
        icon.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(icon, Dock.Left);
        now.Children.Add(icon);
        var temp = new TextBlock { Text = $"{r.Temperature:0}°", FontSize = 40, FontWeight = FontWeights.Light, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        temp.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        DockPanel.SetDock(temp, Dock.Left);
        now.Children.Add(temp);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = r.Place, Style = Ui.Style("Title") });
        texts.Children.Add(Ui.Text(sky));
        texts.Children.Add(Ui.Text(string.Format(Ui.Culture, "Sensación {0:0}° · {1:0} % · {2:0} km/h", r.Feels, r.Humidity, r.Wind)));
        now.Children.Add(texts);
        Children.Add(now);

        if (!_showDays || r.Days.Count < 2) return;
        var days = new UniformGrid { Rows = 1, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var d in r.Days.Skip(1))
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(new TextBlock { Text = Ui.Capitalize(d.Date.ToString("ddd", Ui.Culture).TrimEnd('.')), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            var s = Symbol(WeatherCodes.Describe(d.Code).Symbol, 18);
            s.HorizontalAlignment = HorizontalAlignment.Center;
            s.ToolTip = WeatherCodes.Describe(d.Code).Text;
            col.Children.Add(s);
            var range = Ui.Text($"{d.Max:0}° / {d.Min:0}°");
            range.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(range);
            days.Children.Add(col);
        }
        SetRow(days, 1);
        Children.Add(days);
    }

    private static TextBlock Symbol(string text, double size)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol"), VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        return t;
    }

    private static TextBlock Message(string text)
    {
        var t = Ui.Text(text, wrap: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    public void Dispose() => _c.Services.Clock.Minute -= OnMinute;
}
