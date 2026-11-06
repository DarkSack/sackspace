using System.Globalization;
using System.Text;

namespace SackSpace.Core.Widgets;

/// <summary>
/// What was typed in the file search: words, "exact phrases", ext:pdf (or *.pdf), tipo:imagen,
/// en:carpeta (somewhere in the folder path), tam:&gt;10mb and fecha:semana / fecha:2025.
/// </summary>
public sealed record FileQuery(IReadOnlyList<string> Words, IReadOnlyList<string> Phrases, IReadOnlySet<string> Extensions, IReadOnlyList<string> InFolders)
{
    public long? MinBytes { get; init; }
    public long? MaxBytes { get; init; }
    /// <summary>Modified on or after (local time).</summary>
    public DateTime? From { get; init; }
    /// <summary>Modified before (local time).</summary>
    public DateTime? Until { get; init; }

    public bool HasFilters => MinBytes is not null || MaxBytes is not null || From is not null || Until is not null;

    public bool IsEmpty => Words.Count == 0 && Phrases.Count == 0 && Extensions.Count == 0 && InFolders.Count == 0 && !HasFilters;

    /// <summary>The words as typed, for the bonus when the name has them together.</summary>
    public string Joined { get; } = string.Join(' ', Words);

    /// <summary>Distinct letters and digits of each word (see <see cref="FileSearch.Mask"/>), for the quick test before a similar match.</summary>
    public ulong[] WordMasks { get; } = Words.Select(w => FileSearch.Mask(w)).ToArray();

    /// <summary>Size and date filters.</summary>
    public bool Passes(long bytes, DateTime modified) =>
        (MinBytes is not { } min || bytes >= min) && (MaxBytes is not { } max || bytes <= max)
        && (From is not { } from || modified >= from) && (Until is not { } until || modified < until);
}

/// <summary>How well a file matched; <see cref="Similar"/> = only by resemblance (a typo, missing letters).</summary>
public readonly record struct FileMatch(int Score, bool Similar);

/// <summary>
/// Matching file names the way people remember them: accents and case do not matter,
/// "informe_final-v2" has the words informe, final and v2, and a word with a typo or a
/// missing letter still finds the file (marked as similar, ranked after exact ones).
/// </summary>
public static class FileSearch
{
    /// <summary>Lower case, no accents, separators (_ - . , + ( ) [ ] and spaces) as one space: "Canción_Final (2).MP3" → "cancion final 2 mp3".</summary>
    public static string Normalize(string text)
    {
        var d = text.IsNormalized(NormalizationForm.FormD) ? text : text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (c < 128)
            {
                if (c is ' ' or '_' or '-' or '.' or ',' or '+' or '(' or ')' or '[' or ']' or ':' or ';' or '!' or '?') { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }
                else sb.Append(char.ToLowerInvariant(c));
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        }
        if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.ToString();
    }

    /// <summary>File kinds for tipo: (Spanish and English names).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Kinds = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["imagen"] = ["jpg", "jpeg", "png", "gif", "webp", "bmp", "tif", "tiff", "heic", "avif", "svg", "ico", "psd", "raw", "cr2", "nef", "dng"],
        ["video"] = ["mp4", "mkv", "mov", "avi", "webm", "wmv", "flv", "m4v", "ts"],
        ["audio"] = ["mp3", "wav", "flac", "ogg", "m4a", "aac", "wma", "opus"],
        ["documento"] = ["pdf", "doc", "docx", "odt", "rtf", "txt", "md", "xls", "xlsx", "ods", "csv", "ppt", "pptx", "odp", "epub"],
        ["codigo"] = ["cs", "js", "ts", "tsx", "jsx", "py", "java", "kt", "rs", "go", "c", "cpp", "h", "hpp", "html", "css", "json", "xml", "yml", "yaml", "ps1", "sh", "sql", "lua"],
        ["comprimido"] = ["zip", "rar", "7z", "tar", "gz", "xz", "bz2", "iso"],
        ["programa"] = ["exe", "msi", "bat", "cmd", "lnk", "appx", "msix"],
    };

    private static readonly Dictionary<string, string> KindAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["imagenes"] = "imagen", ["foto"] = "imagen", ["fotos"] = "imagen", ["image"] = "imagen", ["images"] = "imagen", ["photo"] = "imagen", ["photos"] = "imagen",
        ["videos"] = "video", ["musica"] = "audio", ["music"] = "audio", ["sonido"] = "audio",
        ["documentos"] = "documento", ["doc"] = "documento", ["docs"] = "documento", ["document"] = "documento", ["documents"] = "documento",
        ["code"] = "codigo", ["zip"] = "comprimido", ["archive"] = "comprimido",
        ["programas"] = "programa", ["app"] = "programa", ["apps"] = "programa", ["program"] = "programa",
    };

    /// <summary>Build output and caches: still found, but ranked after real documents.</summary>
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "class", "o", "obj", "pyc", "pdb", "dll", "cache", "tmp", "log", "map", "lock", "meta", "idx", "pack", "dat", "bin", "jar",
    };

    public static FileQuery Parse(string text) => Parse(text, DateTime.Now);

    public static FileQuery Parse(string text, DateTime now)
    {
        var words = new List<string>();
        var phrases = new List<string>();
        var ext = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<string>();
        long? min = null, max = null;
        DateTime? from = null, until = null;
        var rest = new StringBuilder();
        var parts = text.Split('"');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1) { var p = Normalize(parts[i]); if (p.Length > 0) phrases.Add(p); }
            else rest.Append(' ').Append(parts[i]);
        }
        foreach (var token in rest.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = token.IndexOf(':');
            var key = colon > 0 ? token[..colon].ToLowerInvariant() : "";
            var value = colon > 0 ? token[(colon + 1)..] : "";
            switch (key)
            {
                case "ext" when value.Length > 0:
                    foreach (var e in value.Split(',', StringSplitOptions.RemoveEmptyEntries)) ext.Add(e.TrimStart('*', '.').ToLowerInvariant());
                    continue;
                case "tipo" or "type" or "kind" when value.Length > 0:
                    foreach (var k in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kind = Normalize(k);
                        if (Kinds.TryGetValue(KindAliases.GetValueOrDefault(kind, kind), out var list)) ext.UnionWith(list);
                    }
                    continue;
                case "en" or "in" when value.Length > 0:
                    folders.Add(Normalize(value));
                    continue;
                case "tam" or "size" when value.Length > 1 && (value[0] is '>' or '<') && ParseBytes(value[1..]) is { } bytes:
                    if (value[0] == '>') min = bytes; else max = bytes;
                    continue;
                case "fecha" or "date" or "mod" when value.Length > 0 && ParseDate(value, now) is { } range:
                    (from, until) = range;
                    continue;
            }
            if (token.StartsWith("*.", StringComparison.Ordinal) && token.Length > 2) ext.Add(token[2..].ToLowerInvariant());
            else words.AddRange(Normalize(token).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        ext.Remove("");
        return new FileQuery(words, phrases, ext, folders) { MinBytes = min, MaxBytes = max, From = from, Until = until };
    }

    /// <summary>"10mb", "500k", "1.5gb", "2048" (bytes).</summary>
    public static long? ParseBytes(string text)
    {
        var t = text.Trim().ToLowerInvariant().Replace(',', '.');
        long mult = t.EndsWith("gb") || t.EndsWith('g') ? 1L << 30 : t.EndsWith("mb") || t.EndsWith('m') ? 1L << 20 : t.EndsWith("kb") || t.EndsWith('k') ? 1L << 10 : 1L;
        t = t.TrimEnd('b', 'g', 'm', 'k');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n >= 0 ? (long)(n * mult) : null;
    }

    /// <summary>hoy/today, ayer/yesterday, semana/week, mes/month, ano/year, or a year like 2025: [from, until).</summary>
    public static (DateTime From, DateTime? Until)? ParseDate(string text, DateTime now)
    {
        var today = now.Date;
        return Normalize(text) switch
        {
            "hoy" or "today" => (today, null),
            "ayer" or "yesterday" => (today.AddDays(-1), today),
            "semana" or "week" => (today.AddDays(-7), null),
            "mes" or "month" => (today.AddMonths(-1), null),
            "ano" or "year" => (today.AddYears(-1), null),
            var y when y.Length == 4 && int.TryParse(y, out var year) && year is > 1980 and < 2200 => (new DateTime(year, 1, 1), new DateTime(year + 1, 1, 1)),
            _ => null,
        };
    }

    /// <summary>One bit per letter a-z and digit 0-9 present (lower case, normalized text); bit 36 = any other letter.</summary>
    public static ulong Mask(ReadOnlySpan<char> text)
    {
        ulong m = 0;
        foreach (var c in text)
        {
            if (c is >= 'a' and <= 'z') m |= 1UL << (c - 'a');
            else if (c is >= '0' and <= '9') m |= 1UL << (26 + c - '0');
            else if (c > 127) m |= 1UL << 36;
        }
        return m;
    }

    /// <summary>Points off for build output and caches, so documents come first.</summary>
    public static int NoisePenalty(ReadOnlySpan<char> extension) =>
        extension.Length > 0 && Noise.GetAlternateLookup<ReadOnlySpan<char>>().Contains(extension) ? 15 : 0;

    /// <summary>
    /// Scores one file. <paramref name="name"/> and <paramref name="folder"/> are already
    /// <see cref="Normalize"/>d; <paramref name="extension"/> is lower case without the dot.
    /// Null when it does not match. Allocates nothing: it runs over every indexed file.
    /// </summary>
    public static FileMatch? Score(ReadOnlySpan<char> name, string folder, ReadOnlySpan<char> extension, FileQuery q, bool allowSimilar, ulong nameMask = ulong.MaxValue)
    {
        if (q.Extensions.Count > 0 && !HasExtension(q, extension)) return null;
        foreach (var f in q.InFolders) if (!folder.Contains(f, StringComparison.Ordinal)) return null;
        foreach (var p in q.Phrases) if (name.IndexOf(p, StringComparison.Ordinal) < 0) return null;

        int score = q.Phrases.Count * 40 + (q.Extensions.Count > 0 ? 5 : 0);
        var similar = false;
        for (var k = 0; k < q.Words.Count; k++)
        {
            var w = q.Words[k];
            var at = name.IndexOf(w, StringComparison.Ordinal);
            if (at == 0) { score += 40; continue; }
            if (at > 0) { score += name[at - 1] == ' ' ? 30 : 20; continue; }
            if (folder.Contains(w, StringComparison.Ordinal)) { score += 5; continue; } // only the folder: below any name match
            if (!allowSimilar || w.Length < 3) return null;
            // Quick test: a word one or two edits away lacks at most that many of its letters.
            if ((int)ulong.PopCount(q.WordMasks[k] & ~nameMask) > Allowed(w) + 1) return null;
            var best = Resemblance(w, name);
            if (best <= 0) return null;
            score += best;
            similar = true;
        }
        if (q.Words.Count > 1 && name.IndexOf(q.Joined, StringComparison.Ordinal) >= 0) score += 25;
        score += Math.Max(0, 6 - name.Length / 10) - NoisePenalty(extension); // shorter names are likelier what was meant
        return new FileMatch(score, similar);
    }

    private static bool HasExtension(FileQuery q, ReadOnlySpan<char> extension)
    {
        foreach (var e in q.Extensions) if (extension.Equals(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>12..6 for a word one or two edits away, 4 for its letters in order (a word with letters missing); 0 = no.</summary>
    // Edits a similar word may differ by: one, two for long words.
    private static int Allowed(string word) => word.Length >= 9 ? 2 : 1;

    private static int Resemblance(string word, ReadOnlySpan<char> name)
    {
        var allowed = Allowed(word);
        var best = 0;
        var rest = name;
        while (rest.Length > 0)
        {
            var space = rest.IndexOf(' ');
            var n = space < 0 ? rest : rest[..space];
            rest = space < 0 ? default : rest[(space + 1)..];
            if (n.Length + allowed < word.Length) continue;
            // Compare with the whole word and with its start of the same length ("factur" ~ "facturas").
            var d = Distance(word, n);
            if (n.Length > word.Length) d = Math.Min(d, Distance(word, n[..word.Length]));
            if (d <= allowed) best = Math.Max(best, 12 - 3 * d);
        }
        if (best == 0 && InOrder(word, name)) best = 4;
        return best;
    }

    [ThreadStatic] private static int[]? _rows;

    /// <summary>Optimal string alignment distance (a swap of two neighbours counts as one edit).</summary>
    public static int Distance(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.SequenceEqual(b)) return 0;
        if (b.Length > 64 || a.Length > 64) return int.MaxValue; // not a word
        // Three rolling rows of the classic table.
        var w = b.Length + 1;
        var buf = _rows is { Length: >= 192 } r ? r : _rows = new int[3 * 65];
        Span<int> prev2 = buf.AsSpan(0, w), prev = buf.AsSpan(65, w), cur = buf.AsSpan(130, w);
        for (var j = 0; j < w; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j < w; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, prev2[j - 2] + 1);
                cur[j] = v;
            }
            var t = prev2; prev2 = prev; prev = cur; cur = t;
        }
        return prev[w - 1];
    }

    // Every letter of the word appears in order inside one name word, a little longer than it
    // (the letters someone skipped: "prsupsto" in "presupuesto").
    private static bool InOrder(string word, ReadOnlySpan<char> text)
    {
        var span = word.Length + Math.Max(2, word.Length / 3);
        for (var start = text.IndexOf(word[0]); start >= 0;)
        {
            int i = 1, j = start + 1;
            for (; i < word.Length && j < text.Length && j - start < span && text[j] != ' '; j++)
                if (text[j] == word[i]) i++;
            if (i == word.Length) return true;
            var next = text[(start + 1)..].IndexOf(word[0]);
            start = next < 0 ? -1 : start + 1 + next;
        }
        return false;
    }
}
