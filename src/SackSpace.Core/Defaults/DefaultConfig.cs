using Loc = SackSpace.Core.Loc;
using System.Text.Json.Nodes;
using SackSpace.Core.Hotkeys;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;

namespace SackSpace.Core.Defaults;

/// <summary>
/// The first-run setup: Personal, Trabajo, Gaming and Desarrollo, each with a layout
/// on the right of the primary monitor (the desktop icons live on the left).
/// </summary>
public static class DefaultConfig
{
    /// <summary>Top of the first row, under the workspace bar.</summary>
    public const double Top = 88;

    /// <param name="workWidth">Primary monitor's work area, in DIPs.</param>
    /// <param name="workHeight">Primary monitor's work area, in DIPs.</param>
    /// <param name="findApp">An app's shortcut by its Start menu name ("Steam"), or null when it is not installed.</param>
    /// <param name="knownFolder">A known folder's path ("Documents", "Downloads", "Pictures", "Music", "Desktop", "Videos", "UserProfile").</param>
    public static AppConfig Create(double workWidth, double workHeight, Func<string, string?> findApp, Func<string, string?> knownFolder)
    {
        var area = new Rect(0, 0, workWidth, workHeight);
        // Three columns from the right edge: 360 wide, then two of 300.
        var c0 = workWidth - LayoutMath.Margin - 360;
        var c1 = c0 - LayoutMath.Gap - 300;
        var c2 = c1 - LayoutMath.Gap - 300;

        PanelState P(string widget, double x, double y, double? w = null, double? h = null, JsonObject? settings = null)
        {
            var d = BuiltInWidgets.Find(widget)!;
            return new PanelState
            {
                Widget = widget,
                X = x,
                Y = y,
                Width = w ?? d.DefaultWidth,
                Height = h ?? d.DefaultHeight,
                Settings = settings ?? new JsonObject(),
            };
        }

        JsonObject Shortcuts(string title, IEnumerable<string?> paths)
        {
            var s = new JsonObject { ["title"] = title };
            ShortcutItem.WriteAll(s, paths.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => new ShortcutItem(ShortcutItem.NameFor(p), p)));
            return s;
        }

        JsonObject Note(string text) => new() { ["text"] = text };

        JsonObject Tasks(params string[] items)
        {
            var s = new JsonObject();
            TaskItem.WriteAll(s, items.Select(t => new TaskItem(t, false)));
            return s;
        }

        string?[] Apps(params string[] names) => names.Select(findApp).ToArray();

        var y2 = Top + 168 + LayoutMath.Gap;

        var personal = new Workspace
        {
            Name = Loc.T("Personal"), Icon = Glyphs.Person, Accent = "#7C9CFF",
            Panels =
            {
                P(BuiltInWidgets.Clock, c0, Top),
                P(BuiltInWidgets.Media, c0, y2),
                P(BuiltInWidgets.Shortcuts, c0, y2 + 150 + LayoutMath.Gap, 360, 190,
                    Shortcuts(Loc.T("Carpetas"), new[] { "Documents", "Downloads", "Pictures", "Music", "Videos" }.Select(knownFolder))),
                P(BuiltInWidgets.Calendar, c1, Top),
                P(BuiltInWidgets.Notes, c1, Top + 300 + LayoutMath.Gap, 300, 250,
                    Note(Loc.T("Bienvenido a SackSpace.\n\nMantén pulsado un panel, o toca el lápiz de la barra, para moverlo y cambiarle el tamaño.\n\nCtrl+Alt+W muestra los paneles encima de las ventanas."))),
            },
        };

        var work = new Workspace
        {
            Name = Loc.T("Trabajo"), Icon = Glyphs.Work, Accent = "#4FD1C5",
            Panels =
            {
                P(BuiltInWidgets.Clock, c0, Top),
                P(BuiltInWidgets.Tasks, c0, y2, 360, 330,
                    Tasks(Loc.T("Revisar el correo"), Loc.T("Preparar la reunión"), Loc.T("Enviar el informe"))),
                P(BuiltInWidgets.Calendar, c1, Top),
                P(BuiltInWidgets.Notes, c1, Top + 300 + LayoutMath.Gap, 300, 250, Note("")),
                P(BuiltInWidgets.Shortcuts, c2, Top, 300, 200,
                    Shortcuts(Loc.T("Trabajo"), [knownFolder("Documents"), knownFolder("Desktop"),
                        ..Apps("Outlook", "Word", "Excel", "PowerPoint", "Microsoft Teams", "Teams")])),
            },
        };

        var gaming = new Workspace
        {
            Name = "Gaming", Icon = Glyphs.Game, Accent = "#FF5C8A",
            Panels =
            {
                P(BuiltInWidgets.Clock, c0, Top),
                P(BuiltInWidgets.SystemMonitor, c0, y2),
                P(BuiltInWidgets.Media, c0, y2 + 220 + LayoutMath.Gap),
                P(BuiltInWidgets.Shortcuts, c1, Top, 300, 260,
                    Shortcuts(Loc.T("Juegos"), Apps("Steam", "Epic Games Launcher", "Xbox", "Discord", "CurseForge", "Battle.net", "EA", "Ubisoft Connect", "GOG GALAXY"))),
            },
        };

        var dev = new Workspace
        {
            Name = Loc.T("Desarrollo"), Icon = Glyphs.Code, Accent = "#B48CFF",
            Panels =
            {
                P(BuiltInWidgets.SystemMonitor, c0, Top),
                P(BuiltInWidgets.Tasks, c0, Top + 220 + LayoutMath.Gap, 360, 330, Tasks(Loc.T("Revisar los pull requests"))),
                P(BuiltInWidgets.Notes, c1, Top, 300, 300, Note("")),
                P(BuiltInWidgets.Shortcuts, c1, Top + 300 + LayoutMath.Gap, 300, 220,
                    Shortcuts(Loc.T("Herramientas"), [..Apps("Visual Studio Code", "Terminal", "Windows Terminal", "Visual Studio 2022",
                        "Docker Desktop", "JetBrains Toolbox", "GitHub Desktop", "Git Bash", "Postman"), knownFolder("UserProfile")])),
            },
        };

        var config = new AppConfig
        {
            Workspaces = { personal, work, gaming, dev },
            Hotkeys = new Dictionary<string, string>(HotkeyActions.Defaults),
        };
        config.ActiveWorkspaceId = personal.Id;

        // A small screen cannot hold the hand-made layout: tidy it into free places instead.
        var usable = new Rect(0, Top - LayoutMath.Margin, workWidth, workHeight - Top + LayoutMath.Margin);
        foreach (var ws in config.Workspaces)
        {
            var rects = ws.Panels.Select(p => p.Bounds).ToList();
            if (!rects.All(r => r.X >= 0 && r.Y >= 0 && r.Right <= area.Right && r.Bottom <= area.Bottom) || LayoutMath.Overlaps(rects))
            {
                var arranged = LayoutMath.Arrange(rects, usable);
                for (var i = 0; i < ws.Panels.Count; i++) ws.Panels[i].Bounds = arranged[i];
            }
        }
        return config;
    }
}
