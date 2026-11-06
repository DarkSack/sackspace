using System.Windows;
using System.Windows.Controls;
using SackSpace.Core;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>The last things you copied; a click puts one back on the clipboard to paste it again.</summary>
public sealed class ClipboardWidget : DockPanel, IDisposable
{
    private readonly WidgetContext _c;
    private readonly ClipboardWatcher _clips;
    private readonly StackPanel _list = new();

    public ClipboardWidget(WidgetContext c)
    {
        _c = c;
        _clips = c.Services.Clipboard;
        _clips.Keep = (int)c.Settings.GetDouble("keep", 25);
        var header = Ui.Header(Glyphs.Document, Loc.T("Portapapeles"), Ui.IconButton(Glyphs.Delete, Loc.T("Borrar el historial"), _clips.Clear, 28));
        SetDock(header, Dock.Top);
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _clips.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        _list.Children.Clear();
        if (_clips.Entries.Count == 0)
        {
            _list.Children.Add(Ui.Text(Loc.T("Lo que copies desde ahora aparece aquí. Se guarda solo en memoria y se borra al cerrar SackSpace; lo que los gestores de contraseñas marcan como privado no entra."), wrap: true));
            return;
        }
        foreach (var e in _clips.Entries.ToList())
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = e.Preview(), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13, MaxHeight = 38, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(Ui.Text((e.Files ? Loc.T("Archivos") + " · " : "") + e.At.ToString("HH:mm", Loc.Culture)));
            var row = Ui.Row(stack, () => { if (_clips.Paste(e)) _c.Toast(Loc.T("Copiado: pégalo donde quieras")); });
            row.ToolTip = Loc.T("Copiar otra vez");
            _list.Children.Add(row);
        }
    }

    public void Dispose() => _clips.Changed -= Refresh;
}
