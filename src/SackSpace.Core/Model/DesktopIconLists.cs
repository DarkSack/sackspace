namespace SackSpace.Core.Model;

/// <summary>
/// Which desktop icons each workspace shows. A workspace without a list shows them all;
/// paths compare without case, like Windows does.
/// </summary>
public static class DesktopIconLists
{
    public static bool Shows(Workspace ws, string path) =>
        ws.DesktopIcons is not { } list || list.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>Shows or hides one icon in a workspace; the first change turns "all" into a list of what is there now.</summary>
    public static void Set(Workspace ws, string path, bool show, IEnumerable<string> allPaths)
    {
        ws.DesktopIcons ??= allPaths.ToList();
        ws.DesktopIcons.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (show) ws.DesktopIcons.Add(path);
    }

    /// <summary>A new icon appears in the workspace it was made in, not in the others.</summary>
    public static bool Created(AppConfig c, string path)
    {
        if (c.ActiveWorkspace?.DesktopIcons is not { } list || list.Contains(path, StringComparer.OrdinalIgnoreCase)) return false;
        list.Add(path);
        return true;
    }

    /// <summary>A renamed icon keeps its workspaces.</summary>
    public static bool Renamed(AppConfig c, string from, string to)
    {
        var changed = false;
        foreach (var list in c.Workspaces.Select(w => w.DesktopIcons).OfType<List<string>>())
            for (var i = 0; i < list.Count; i++)
                if (list[i].Equals(from, StringComparison.OrdinalIgnoreCase)) { list[i] = to; changed = true; }
        return changed;
    }
}
