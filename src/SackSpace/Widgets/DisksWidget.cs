using Loc = SackSpace.Core.Loc;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>Free space on each local drive, as a bar; looked at again once a minute.</summary>
public sealed class DisksWidget : DockPanel, IDisposable
{
    private readonly WidgetContext _c;
    private readonly StackPanel _list = new();
    private readonly bool _removable;

    public DisksWidget(WidgetContext c)
    {
        _c = c;
        _removable = c.Settings.GetBool("removable", true);
        var header = Ui.Header(Glyphs.Drive, Loc.T("Discos"));
        SetDock(header, Dock.Top);
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        c.Services.Clock.Minute += Refresh;
        Refresh(DateTime.Now);
    }

    private void Refresh(DateTime _)
    {
        _list.Children.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!(d.DriveType == DriveType.Fixed || (_removable && d.DriveType == DriveType.Removable)) || !d.IsReady || d.TotalSize <= 0) continue;
                _list.Children.Add(Row(d));
            }
            catch (IOException) { } // a card reader without a card, a drive being ejected
            catch (UnauthorizedAccessException) { }
        }
        if (_list.Children.Count == 0) _list.Children.Add(Ui.Text(Loc.T("No hay unidades que mostrar."), wrap: true));
    }

    private static FrameworkElement Row(DriveInfo d)
    {
        const double gb = 1024d * 1024 * 1024;
        var used = (d.TotalSize - d.TotalFreeSpace) / (double)d.TotalSize;
        var letter = d.Name.TrimEnd('\\');
        var name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? letter : $"{letter}  {d.VolumeLabel}";

        var top = new DockPanel();
        var free = Ui.Text(string.Format(Ui.Culture, Loc.T("{0:0} GB libres de {1:0}"), d.TotalFreeSpace / gb, d.TotalSize / gb));
        SetDock(free, Dock.Right);
        top.Children.Add(free);
        top.Children.Add(new TextBlock { Text = name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(top);
        row.Children.Add(Ui.Bar(used));
        return row;
    }

    public void Dispose() => _c.Services.Clock.Minute -= Refresh;
}
