using System.Globalization;
using System.Text.Json;

namespace SackSpace.Core;

/// <summary>
/// The app's languages. The Spanish text is the key: T("Reloj") gives "Clock" in English.
/// Each other language is Lang/&lt;code&gt;.json inside the app ({ "Reloj": "Clock", … }); a text
/// missing from it stays in Spanish, so a forgotten string never breaks anything. Placeholders
/// work as in string.Format. A new language is one more JSON file plus a line in <see cref="Languages"/>.
/// </summary>
public static class Loc
{
    public const string Spanish = "es";

    /// <summary>Code and the language's own name, in the order the settings list them.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
    [
        (Spanish, "Español"),
        ("en", "English"),
        ("pt", "Português (Brasil)"),
    ];

    private static Dictionary<string, string> _table = [];

    /// <summary>The language in use ("es", "en", "pt"). Set once at startup.</summary>
    public static string Language { get; private set; } = Spanish;

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>"auto" (or empty) follows Windows: its language if the app has it, else English.</summary>
    public static string Resolve(string? setting, CultureInfo ui)
    {
        if (Languages.Any(l => l.Code == setting)) return setting!;
        var code = ui.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == code) ? code : "en";
    }

    public static void Use(string code, CultureInfo? ui = null)
    {
        Language = Languages.Any(l => l.Code == code) ? code : Spanish;
        _table = Language == Spanish ? [] : Read(Language) ?? [];
        // Dates and numbers in the user's own variant when it is the same language (es-MX, pt-PT…).
        Culture = ui is not null && ui.TwoLetterISOLanguageName == Language && !ui.IsNeutralCulture ? ui
            : CultureInfo.GetCultureInfo(Language switch { "en" => "en-US", "pt" => "pt-BR", _ => "es-ES" });
    }

    /// <summary>A language's table from the app, or null if it has none.</summary>
    public static Dictionary<string, string>? Read(string code)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Lang.{code}.json");
        return stream is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }

    public static string T(string spanish) => _table.TryGetValue(spanish, out var text) && text.Length > 0 ? text : spanish;

    public static string T(string spanish, params object?[] args) => string.Format(Culture, T(spanish), args);
}
