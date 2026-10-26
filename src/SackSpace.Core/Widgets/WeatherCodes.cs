namespace SackSpace.Core.Widgets;

/// <summary>Open-Meteo's WMO weather codes as words and a Segoe UI Symbol character.</summary>
public static class WeatherCodes
{
    public static (string Text, string Symbol) Describe(int code) => code switch
    {
        0 => ("Despejado", "☀"),
        1 => ("Casi despejado", "☀"),
        2 => ("Parcialmente nublado", "⛅"),
        3 => ("Nublado", "☁"),
        45 or 48 => ("Niebla", "☁"),
        51 or 53 or 55 or 56 or 57 => ("Llovizna", "☂"),
        61 or 63 or 66 or 80 or 81 => ("Lluvia", "☂"),
        65 or 67 or 82 => ("Lluvia fuerte", "☔"),
        71 or 73 or 75 or 77 or 85 or 86 => ("Nieve", "❄"),
        95 or 96 or 99 => ("Tormenta", "⛈"),
        _ => ("Sin datos", "☁"),
    };
}
