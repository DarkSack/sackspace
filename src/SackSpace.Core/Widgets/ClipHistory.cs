namespace SackSpace.Core.Widgets;

/// <summary>Something that was copied: text, or files (their paths, one per line).</summary>
public sealed record ClipEntry(string Text, bool Files, DateTime At)
{
    /// <summary>One line for the list: whitespace folded, cut at <paramref name="max"/> characters.</summary>
    public string Preview(int max = 120)
    {
        var flat = string.Join(' ', Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..(max - 1)] + "…";
    }
}

/// <summary>The newest copies first, without repeats; kept in memory only.</summary>
public static class ClipHistory
{
    public const int MaxTextLength = 100_000;

    /// <summary>Puts <paramref name="entry"/> on top (a repeat moves up) and keeps at most <paramref name="keep"/>.</summary>
    public static void Add(List<ClipEntry> list, ClipEntry entry, int keep)
    {
        if (string.IsNullOrWhiteSpace(entry.Text) || entry.Text.Length > MaxTextLength) return;
        list.RemoveAll(e => e.Files == entry.Files && e.Text == entry.Text);
        list.Insert(0, entry);
        if (list.Count > keep) list.RemoveRange(keep, list.Count - keep);
    }
}
