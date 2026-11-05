using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SackSpace.Shell;

/// <summary>Small builders for the views written in code, so they all look alike.</summary>
public static class Ui
{
    /// <summary>The app speaks Spanish: dates in the user's Spanish culture, or Spain's.</summary>
    public static readonly CultureInfo Culture =
        CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "es" ? CultureInfo.CurrentCulture : new CultureInfo("es-ES");

    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Culture) + s[1..];

    public static MenuItem Item(string header, string? glyph, Action? click, bool isChecked = false)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        if (glyph is not null) item.Icon = new TextBlock { Text = glyph };
        if (click is not null) item.Click += (_, _) => click();
        return item;
    }

    public static TextBlock Glyph(string glyph, double size = 14, string brush = "TextSecondaryBrush")
    {
        var t = new TextBlock { Text = glyph, FontSize = size, Style = Style("Icon") };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Text(string text, string style = "Caption", double? size = null, bool wrap = false)
    {
        var t = new TextBlock { Text = text, Style = Style(style) };
        if (size is { } s) t.FontSize = s;
        if (wrap) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }
        return t;
    }

    public static Button IconButton(string glyph, string tooltip, Action click, double size = 32)
    {
        var b = new Button { Content = glyph, ToolTip = tooltip, Style = Style("IconButton"), Width = size, Height = size };
        b.Click += (_, _) => click();
        return b;
    }

    public static Button Pill(string text, Action click, bool accent = false, string? glyph = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, Style = Style("Icon"), FontSize = 12, Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(new TextBlock { Text = text });
            content = row;
        }
        var b = new Button { Content = content, Style = Style(accent ? "AccentPillButton" : "PillButton") };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A thin rounded bar filled to <paramref name="used"/> (0-1); red when almost full, like Explorer.</summary>
    public static Grid Bar(double used)
    {
        used = Math.Clamp(used, 0, 1);
        // Two star columns: the bar fills to the used share without measuring anything.
        var bar = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(used, 0.001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - used, 0.001), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(3) };
        if (used >= 0.9) fill.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        else fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        bar.Children.Add(fill);
        return bar;
    }

    /// <summary>A rounded image (artwork, app icons).</summary>
    public static Border Picture(ImageSource? source, double size, double radius)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius) };
        if (source is not null) b.Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
        return b;
    }

    public static Style Style(string key) => (Style)Application.Current.FindResource(key);

    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>Marks a widget's title row (PanelState.HideTitle hides it).</summary>
    public const string HeaderTag = "header";

    /// <summary>A header row: glyph, title and anything on the right.</summary>
    public static DockPanel Header(string glyph, string title, params UIElement[] right)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true, Tag = HeaderTag };
        foreach (var r in right.Reverse())
        {
            DockPanel.SetDock(r, Dock.Right);
            dock.Children.Add(r);
        }
        var icon = Glyph(glyph, 14, "AccentBrush");
        icon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(new TextBlock { Text = title, Style = Style("Title"), VerticalAlignment = VerticalAlignment.Center });
        return dock;
    }

    /// <summary>
    /// A time field that always reads "HH:mm" (00:00 to start with): digits overwrite in
    /// place and skip the colon, Backspace sets a digit back to 0, and on leaving it what
    /// was typed or pasted is tidied ("1100" → "11:00"). Text stays a plain string.
    /// </summary>
    public static TextBox TimeBox(string? value, double width = 80)
    {
        var box = new TextBox
        {
            Style = Style("FieldTextBox"), Text = Core.Model.AutoSwitch.NormalizeTime(value) ?? "00:00", MaxLength = 5, Width = width,
            VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        void Put(int at, char digit, int caret)
        {
            var chars = box.Text.PadRight(5, '0').ToCharArray();
            chars[2] = ':';
            chars[at] = digit;
            box.Text = new string(chars);
            box.CaretIndex = caret;
        }
        box.GotKeyboardFocus += (_, _) => box.Dispatcher.BeginInvoke(() => { box.SelectionLength = 0; box.CaretIndex = 0; });
        box.PreviewTextInput += (_, e) =>
        {
            e.Handled = true;
            foreach (var c in e.Text.Where(char.IsAsciiDigit))
            {
                var at = box.CaretIndex == 2 ? 3 : box.CaretIndex;
                if (at >= 5) return;
                Put(at, c, at == 1 ? 3 : at + 1);
            }
        };
        box.PreviewKeyDown += (_, e) =>
        {
            var at = box.CaretIndex;
            switch (e.Key)
            {
                case System.Windows.Input.Key.Space:
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Back:
                    e.Handled = true;
                    if (at == 3) at = 2;
                    if (at > 0) Put(at - 1, '0', at - 1);
                    break;
                case System.Windows.Input.Key.Delete:
                    e.Handled = true;
                    if (at == 2) at = 3;
                    if (at < 5) Put(at, '0', at);
                    break;
            }
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (Core.Model.AutoSwitch.NormalizeTime(box.Text) is { } tidy && tidy != box.Text) box.Text = tidy;
        };
        return box;
    }

    /// <summary>Grey hint text shown over an empty TextBox.</summary>
    public static Grid WithPlaceholder(TextBox box, string hint)
    {
        var grid = new Grid();
        var t = new TextBlock
        {
            Text = hint,
            IsHitTestVisible = false,
            FontSize = box.FontSize,
            Padding = box.Padding,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = box.VerticalContentAlignment == VerticalAlignment.Top ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        void Sync() => t.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();
        grid.Children.Add(box);
        grid.Children.Add(t);
        return grid;
    }
}
