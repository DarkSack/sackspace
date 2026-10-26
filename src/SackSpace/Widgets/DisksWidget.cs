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
        var header = Ui.Header(Glyphs.Drive, "Discos");
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
        if (_list.Children.Count == 0) _list.Children.Add(Ui.Text("No hay unidades que mostrar.", wrap: true));
    }

    private static FrameworkElement Row(DriveInfo d)
    {
        const double gb = 1024d * 1024 * 1024;
        var used = (d.TotalSize - d.TotalFreeSpace) / (double)d.TotalSize;
        var letter = d.Name.TrimEnd('\\');
        var name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? letter : $"{letter}  {d.VolumeLabel}";

        var top = new DockPanel();
        var free = Ui.Text(string.Format(Ui.Culture, "{0:0} GB libres de {1:0}", d.TotalFreeSpace / gb, d.TotalSize / gb));
        SetDock(free, Dock.Right);
        top.Children.Add(free);
        top.Children.Add(new TextBlock { Text = name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });

        // Two star columns: the bar fills to the used share without measuring anything.
        var bar = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(used, 0.001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - used, 0.001), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(3) };
        // Almost full: red, like Explorer.
        if (used >= 0.9) fill.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
        else fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        bar.Children.Add(fill);

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(top);
        row.Children.Add(bar);
        return row;
    }

    public void Dispose() => _c.Services.Clock.Minute -= Refresh;
}
