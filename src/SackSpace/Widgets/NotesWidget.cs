using Loc = SackSpace.Core.Loc;
using System.Windows;
using System.Windows.Controls;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>Free text saved as you type (the session writes it 600 ms after the last key).</summary>
public sealed class NotesWidget : DockPanel
{
    public NotesWidget(WidgetContext c)
    {
        var header = Ui.Header(Glyphs.Note, c.Settings.GetString("title", "Notas"));
        SetDock(header, Dock.Top);
        Children.Add(header);

        var box = new TextBox
        {
            Style = Ui.Style("GlassTextBox"),
            Text = c.Settings.GetString("text", ""),
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            FontSize = c.Settings.GetDouble("fontSize", 14),
        };
        box.TextChanged += (_, _) =>
        {
            c.Settings["text"] = box.Text;
            c.Save();
        };
        Children.Add(Ui.WithPlaceholder(box, Loc.T("Escribe aquí…")));
    }
}
