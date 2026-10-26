using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Shell;

namespace SackSpace.Widgets;

/// <summary>A to-do list: type and Enter to add, tick to complete, × to delete.</summary>
public sealed class TasksWidget : DockPanel
{
    private readonly WidgetContext _c;
    private readonly List<TaskItem> _items;
    private readonly bool _hideDone;
    private readonly StackPanel _list = new();
    private readonly TextBlock _count = new() { Style = Ui.Style("Caption"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _input;
    private readonly Button _clearDone;

    public TasksWidget(WidgetContext c)
    {
        _c = c;
        _items = TaskItem.ReadAll(c.Settings);
        _hideDone = c.Settings.GetBool("hideDone", false);

        _clearDone = Ui.IconButton(Glyphs.Delete, "Borrar las completadas", () => { _items.RemoveAll(i => i.Done); Save(); }, 28);
        _clearDone.Margin = new Thickness(6, 0, 0, 0);
        var header = Ui.Header(Glyphs.Tasks, c.Settings.GetString("title", "Tareas"), _count, _clearDone);
        SetDock(header, Dock.Top);
        Children.Add(header);

        _input = new TextBox { Style = Ui.Style("GlassTextBox"), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 200 };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(_input.Text)) return;
            _items.Add(new TaskItem(_input.Text.Trim(), false));
            _input.Clear();
            Save();
        };
        var plus = Ui.Glyph(Glyphs.Add, 12, "AccentBrush");
        plus.Margin = new Thickness(0, 0, 10, 0);
        var inputRow = new DockPanel();
        DockPanel.SetDock(plus, Dock.Left);
        inputRow.Children.Add(plus);
        inputRow.Children.Add(Ui.WithPlaceholder(_input, "Nueva tarea"));
        var inputBox = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 8, 0, 0), Child = inputRow };
        inputBox.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        SetDock(inputBox, Dock.Bottom);
        Children.Add(inputBox);

        Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        Render();
    }

    private void Save()
    {
        TaskItem.WriteAll(_c.Settings, _items);
        _c.Save();
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (_hideDone && item.Done) continue;
            var index = i;
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), Background = System.Windows.Media.Brushes.Transparent };
            var check = new CheckBox { Style = Ui.Style("RoundCheck"), IsChecked = item.Done, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            check.Click += (_, _) => { _items[index] = item with { Done = !item.Done }; Save(); };
            var delete = Ui.IconButton(Glyphs.Close, "Borrar", () => { _items.RemoveAt(index); Save(); }, 24);
            delete.FontSize = 10;
            delete.Opacity = 0;
            delete.VerticalAlignment = VerticalAlignment.Top;
            row.MouseEnter += (_, _) => delete.Opacity = 1;
            row.MouseLeave += (_, _) => delete.Opacity = 0;
            var text = new TextBlock { Text = item.Text, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, item.Done ? "TextTertiaryBrush" : "TextBrush");
            if (item.Done) text.TextDecorations = TextDecorations.Strikethrough;
            DockPanel.SetDock(check, Dock.Left);
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(check);
            row.Children.Add(delete);
            row.Children.Add(text);
            _list.Children.Add(row);
        }
        var pending = _items.Count(i => !i.Done);
        _count.Text = _items.Count == 0 ? "" : pending == 0 ? "Todo hecho" : pending == 1 ? "1 pendiente" : $"{pending} pendientes";
        _clearDone.Visibility = _items.Any(i => i.Done) ? Visibility.Visible : Visibility.Collapsed;
        if (_items.Count == 0) _list.Children.Add(Ui.Text("Sin tareas. Escribe una abajo y pulsa Intro.", wrap: true));
    }
}
