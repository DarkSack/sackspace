using Loc = SackSpace.Core.Loc;
namespace SackSpace.Core.Widgets;

/// <summary>Open-Meteo's WMO weather codes as words and a Segoe UI Symbol character.</summary>
public static class WeatherCodes
{
    public static (string Text, string Symbol) Describe(int code) => code switch
    {
        0 => (Loc.T("Despejado"), "☀"),
        1 => (Loc.T("Casi despejado"), "☀"),
        2 => (Loc.T("Parcialmente nublado"), "⛅"),
        3 => (Loc.T("Nublado"), "☁"),
        45 or 48 => (Loc.T("Niebla"), "☁"),
        51 or 53 or 55 or 56 or 57 => (Loc.T("Llovizna"), "☂"),
        61 or 63 or 66 or 80 or 81 => (Loc.T("Lluvia"), "☂"),
        65 or 67 or 82 => (Loc.T("Lluvia fuerte"), "☔"),
        71 or 73 or 75 or 77 or 85 or 86 => (Loc.T("Nieve"), "❄"),
        95 or 96 or 99 => (Loc.T("Tormenta"), "⛈"),
        _ => (Loc.T("Sin datos"), "☁"),
    };
}
