using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SackSpace.Core.Model;
using SackSpace.Core.Widgets;
using SackSpace.Widgets;

namespace SackSpace.Shell;

/// <summary>A centred glass card with a title and a close button; the content goes in Body.</summary>
public abstract class SheetBase : Border
{
    protected readonly HostView Host;
    protected readonly StackPanel Body = new();

    protected SheetBase(HostView host, string glyph, string title, double width)
    {
        Host = host;
        Style = Ui.Style("Sheet");
        Width = width;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Padding = new Thickness(24, 20, 24, 24);
        MaxHeight = host.AreaSize.Height * 0.8;

        var dock = new DockPanel();
        var header = Ui.Header(glyph, title, Ui.IconButton(Glyphs.Close, "Cerrar", host.CloseSheet));
        ((TextBlock)header.Children[^1]).FontSize = 17;
        header.Margin = new Thickness(0, 0, 0, 16);
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) });
        Child = dock;
    }

    protected static TextBlock Section(string text) =>
        new() { Text = text, Style = Ui.Style("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 14, 0, 8) };
}

/// <summary>"Añadir widget": every registered widget, and the panels hidden in this workspace.</summary>
public sealed class GallerySheet : SheetBase
{
    public GallerySheet(HostView host) : base(host, Glyphs.Add, "Añadir widget", 600)
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var d in WidgetRegistry.All) grid.Children.Add(Card(d));
        Body.Children.Add(grid);

        var hidden = host.CurrentWorkspace?.Panels.Where(p => p.Hidden).ToList() ?? [];
        if (hidden.Count == 0) return;
        Body.Children.Add(Section("Ocultos en este espacio"));
        foreach (var panel in hidden)
        {
            var d = WidgetRegistry.Find(panel.Widget)?.Descriptor;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var show = Ui.Pill("Mostrar aquí", () => { host.RestorePanel(panel); host.CloseSheet(); }, glyph: Glyphs.View);
            DockPanel.SetDock(show, Dock.Right);
            row.Children.Add(show);
            var icon = Ui.Glyph(d?.Glyph ?? Glyphs.Info, 16);
            icon.Margin = new Thickness(4, 0, 12, 0);
            row.Children.Add(icon);
            var title = panel.Settings.GetString("title", d?.Name ?? panel.Widget);
            row.Children.Add(new TextBlock { Text = title, Style = Ui.Style("Title"), VerticalAlignment = VerticalAlignment.Center });
            Body.Children.Add(row);
        }
    }

    private Button Card(WidgetDescriptor d)
    {
        var circle = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        var bg = new Border { CornerRadius = new CornerRadius(20) };
        bg.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
        circle.Children.Add(bg);
        circle.Children.Add(Ui.Glyph(d.Glyph, 17, "AccentBrush"));

        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = d.Name, Style = Ui.Style("Title") });
        texts.Children.Add(Ui.Text(d.Description, wrap: true));
        ((TextBlock)texts.Children[^1]).Margin = new Thickness(0, 3, 0, 0);
        var stack = new DockPanel();
        DockPanel.SetDock(circle, Dock.Left);
        stack.Children.Add(circle);
        stack.Children.Add(texts);

        var face = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14), Child = stack };
        face.SetResourceReference(BackgroundProperty, "HoverBrush");
        var card = new Button
        {
            Content = face,
            Margin = new Thickness(0, 0, 10, 10),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Template = BareButton(),
            ToolTip = "Añadir a este espacio",
        };
        card.MouseEnter += (_, _) => face.SetResourceReference(BackgroundProperty, "PressedBrush");
        card.MouseLeave += (_, _) => face.SetResourceReference(BackgroundProperty, "HoverBrush");
        card.Click += (_, _) => { Host.CloseSheet(); Host.AddWidget(d); };
        return card;
    }

    /// <summary>A button that draws only its content.</summary>
    public static ControlTemplate BareButton()
    {
        var t = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        t.VisualTree = presenter;
        return t;
    }
}

/// <summary>A widget's options, built from its descriptor. Changes apply at once.</summary>
public sealed class WidgetSettingsSheet : SheetBase
{
    private readonly PanelHost _panel;

    public WidgetSettingsSheet(PanelHost panel, HostView host)
        : base(host, panel.Descriptor.Glyph, "Opciones · " + panel.Descriptor.Name, 440)
    {
        _panel = panel;
        var settings = panel.Panel.Settings;
        foreach (var s in panel.Descriptor.Settings)
        {
            var value = settings.Value(s);
            switch (s.Kind)
            {
                case WidgetSettingKind.Toggle:
                    var toggle = new CheckBox { Content = s.Label, Style = Ui.Style("ToggleSwitch"), IsChecked = value == "true", Margin = new Thickness(0, 6, 0, 10), FontSize = 14 };
                    toggle.Click += (_, _) => Set(s.Key, JsonValue.Create(toggle.IsChecked == true));
                    Body.Children.Add(toggle);
                    break;

                case WidgetSettingKind.Choice:
                    Body.Children.Add(Label(s.Label));
                    var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
                    foreach (var option in s.Options ?? [])
                    {
                        var chip = Ui.Pill(option, () => { }, accent: option == value);
                        chip.Margin = new Thickness(0, 0, 8, 8);
                        chip.Click += (_, _) =>
                        {
                            foreach (var c in chips.Children.OfType<Button>()) c.Style = Ui.Style(c == chip ? "AccentPillButton" : "PillButton");
                            Set(s.Key, JsonValue.Create(option));
                        };
                        chips.Children.Add(chip);
                    }
                    Body.Children.Add(chips);
                    break;

                case WidgetSettingKind.Number:
                    var shown = Label(s.Label + ": " + value);
                    Body.Children.Add(shown);
                    var slider = new Slider
                    {
                        Style = Ui.Style("AccentSlider"), Minimum = s.Min, Maximum = s.Max,
                        Value = double.Parse(value, CultureInfo.InvariantCulture), SmallChange = s.Step, LargeChange = s.Step,
                        IsSnapToTickEnabled = true, TickFrequency = s.Step, Margin = new Thickness(0, 0, 0, 12),
                    };
                    slider.ValueChanged += (_, e) =>
                    {
                        shown.Text = s.Label + ": " + e.NewValue.ToString(CultureInfo.InvariantCulture);
                        Set(s.Key, JsonValue.Create(e.NewValue));
                    };
                    Body.Children.Add(slider);
                    break;

                case WidgetSettingKind.Text:
                    Body.Children.Add(Label(s.Label));
                    var box = new TextBox { Style = Ui.Style("FieldTextBox"), Text = value, Margin = new Thickness(0, 0, 0, 12), MaxLength = 60 };
                    box.TextChanged += (_, _) => Set(s.Key, JsonValue.Create(box.Text));
                    Body.Children.Add(box);
                    break;
            }
        }
        var done = Ui.Pill("Listo", host.CloseSheet, accent: true);
        done.HorizontalAlignment = HorizontalAlignment.Right;
        done.Margin = new Thickness(0, 8, 0, 0);
        Body.Children.Add(done);
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 13, Margin = new Thickness(0, 4, 0, 8) };

    private void Set(string key, JsonNode? value)
    {
        _panel.Panel.Settings[key] = value;
        Host.Services.Session.MarkDirty();
        _panel.CreateWidget();
    }
}
