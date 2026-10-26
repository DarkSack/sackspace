namespace SackSpace.Core.Hotkeys;

/// <summary>Same values as RegisterHotKey's MOD_* flags.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global shortcut such as "Ctrl+Alt+Space". The key name is WPF's Key name.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var mods = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var m = ParseModifier(part);
            if (m == HotkeyModifiers.None || mods.HasFlag(m)) return false;
            mods |= m;
        }

        var key = NormalizeKey(parts[^1]);
        if (key is null || ParseModifier(key) != HotkeyModifiers.None) return false;
        // A bare key would fire while typing anywhere: only F-keys may go without a modifier.
        if (mods == HotkeyModifiers.None && !IsFunctionKey(key)) return false;

        gesture = new HotkeyGesture(mods, key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Display(Key));
        return string.Join("+", parts);
    }

    private static string Display(string key) => key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1]) ? key[1..] : key;

    private static HotkeyModifiers ParseModifier(string s) => s.ToLowerInvariant() switch
    {
        "ctrl" or "control" or "ctl" => HotkeyModifiers.Ctrl,
        "alt" => HotkeyModifiers.Alt,
        "shift" or "mayús" or "mayus" => HotkeyModifiers.Shift,
        "win" or "windows" => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    /// <summary>Letters upper case, digits as WPF's D0–D9, a few friendly names; anything else as written.</summary>
    private static string? NormalizeKey(string s)
    {
        if (s.Length == 1 && char.IsAsciiLetter(s[0])) return s.ToUpperInvariant();
        if (s.Length == 1 && char.IsAsciiDigit(s[0])) return "D" + s;
        if (!s.All(char.IsAsciiLetterOrDigit)) return null;
        return s.ToLowerInvariant() switch
        {
            "space" or "espacio" => "Space",
            "left" or "izquierda" => "Left",
            "right" or "derecha" => "Right",
            "up" or "arriba" => "Up",
            "down" or "abajo" => "Down",
            "tab" => "Tab",
            "enter" or "return" => "Enter",
            "esc" or "escape" => "Escape",
            _ => char.ToUpperInvariant(s[0]) + s[1..],
        };
    }

    private static bool IsFunctionKey(string key) =>
        key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out var n) && n is >= 1 and <= 24;
}

/// <summary>The things a global shortcut can do.</summary>
public static class HotkeyActions
{
    public const string Peek = "peek";
    public const string Edit = "edit";
    public const string Next = "next";
    public const string Previous = "previous";
    public const string Settings = "settings";
    public const int WorkspaceSlots = 9;

    public static string Workspace(int number) => "workspace" + number;

    public static IReadOnlyDictionary<string, string> Defaults { get; } = BuildDefaults();

    private static Dictionary<string, string> BuildDefaults()
    {
        // Ctrl+Alt is AltGr on Spanish keyboards: Ctrl+Alt+2 or +E would steal "@" or "€".
        // Plain Ctrl+Alt only goes with keys that type nothing there; the rest add Shift.
        var d = new Dictionary<string, string>
        {
            [Peek] = "Ctrl+Alt+W",
            [Edit] = "Ctrl+Alt+Shift+E",
            [Next] = "Ctrl+Alt+Right",
            [Previous] = "Ctrl+Alt+Left",
            [Settings] = "Ctrl+Alt+Shift+S",
        };
        for (var i = 1; i <= WorkspaceSlots; i++) d[Workspace(i)] = $"Ctrl+Alt+Shift+{i}";
        return d;
    }

    public static string Label(string action) => action switch
    {
        Peek => "Mostrar u ocultar los paneles sobre las ventanas",
        Edit => "Editar el diseño",
        Next => "Siguiente espacio",
        Previous => "Espacio anterior",
        Settings => "Abrir los ajustes",
        _ when action.StartsWith("workspace", StringComparison.Ordinal) => $"Ir al espacio {action["workspace".Length..]}",
        _ => action,
    };
}
