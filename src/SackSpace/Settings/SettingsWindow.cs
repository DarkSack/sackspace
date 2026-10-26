using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using SackSpace.Core.Hotkeys;
using SackSpace.Core.Model;
using SackSpace.Core.Storage;
using SackSpace.Core.Themes;
using SackSpace.Interop;
using SackSpace.Services;
using SackSpace.Shell;

namespace SackSpace.Settings;

/// <summary>
/// The settings window: a normal Windows 11 window (Mica, dark or light like the theme)
/// with a page list on the left. Every change applies and saves at once; there is no
/// "Save" button.
/// </summary>
public sealed class SettingsWindow : Window
{
    private static readonly (string Id, string Glyph, string Title)[] Pages =
    [
        ("general", Glyphs.Settings, "General"),
        ("apariencia", Glyphs.Color, "Apariencia"),
        ("espacios", Glyphs.Grid, "Espacios"),
        ("atajos", Glyphs.Keyboard, "Atajos de teclado"),
        ("datos", Glyphs.Shield, "Datos y privacidad"),
        ("acerca", Glyphs.Info, "Acerca de"),
    ];

    public static readonly string[] Swatches =
        ["#7C9CFF", "#4FD1C5", "#FF5C8A", "#B48CFF", "#FFB454", "#5CD67A", "#FF7A59", "#4DB8FF", "#E879F9", "#A3A3A3"];

    private readonly AppServices _s;
    private readonly bool _mica = Environment.OSVersion.Version.Build >= 22621;
    private readonly StackPanel _nav = new() { Margin = new Thickness(12, 0, 12, 12) };
    private readonly StackPanel _page = new() { MaxWidth = 820, Margin = new Thickness(36, 48, 36, 36) };
    private readonly ScrollViewer _scroll;
    private string _current = "general";
    private KeyEventHandler? _capture;

    private AppConfig Config => _s.Session.Config;

    public SettingsWindow(AppServices services)
    {
        _s = services;
        Title = "Ajustes de SackSpace";
        Width = 980;
        Height = 700;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/sackspace.ico"));
        SetResourceReference(FontFamilyProperty, "AppFont");
        SetResourceReference(ForegroundProperty, "TextBrush");
        if (_mica)
        {
            // The whole window is "frame", so Mica shows through; DWM still draws the caption buttons.
            WindowChrome.SetWindowChrome(this, new WindowChrome { GlassFrameThickness = new Thickness(-1), CaptionHeight = 40, ResizeBorderThickness = new Thickness(6) });
            Background = Brushes.Transparent;
        }

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new DockPanel();
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 14, 0, 22) };
        brand.Children.Add(new Image { Source = Icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) });
        brand.Children.Add(new TextBlock { Text = "SackSpace", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(brand, Dock.Top);
        left.Children.Add(brand);
        left.Children.Add(_nav);
        root.Children.Add(left);

        _scroll = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var pageBg = new Border { CornerRadius = new CornerRadius(10, 0, 0, 0), Child = _scroll, Margin = new Thickness(0, 40, 0, 0) };
        pageBg.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        Grid.SetColumn(pageBg, 1);
        root.Children.Add(pageBg);
        Content = root;

        _s.Theme.Changed += ApplyBackdrop;
        Closed += (_, _) => { _s.Theme.Changed -= ApplyBackdrop; EndCapture(); };
        ShowPage("general");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = _s.Theme.Current.IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        if (_mica)
        {
            HwndSource.FromHwnd(hwnd)!.CompositionTarget!.BackgroundColor = Colors.Transparent;
            var mica = 2;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
        }
        else
        {
            var t = _s.Theme.Current.GlassTint;
            Background = new SolidColorBrush(Color.FromRgb(t.R, t.G, t.B));
        }
    }

    // ── Navigation ───────────────────────────────────────────

    public void ShowPage(string id)
    {
        EndCapture();
        if (Pages.All(p => p.Id != id)) id = "general";
        _current = id;
        RenderNav();
        _page.Children.Clear();
        switch (id)
        {
            case "general": General(); break;
            case "apariencia": Appearance(); break;
            case "espacios": Workspaces(); break;
            case "atajos": Hotkeys(); break;
            case "datos": Data(); break;
            default: About(); break;
        }
    }

    private void Refresh()
    {
        var offset = _scroll.VerticalOffset;
        ShowPage(_current);
        _scroll.ScrollToVerticalOffset(offset);
    }

    private void RenderNav()
    {
        _nav.Children.Clear();
        foreach (var (id, glyph, title) in Pages)
        {
            var selected = id == _current;
            var row = new Grid();
            var bar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Visibility = selected ? Visibility.Visible : Visibility.Hidden };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0) };
            var icon = Ui.Glyph(glyph, 15, selected ? "AccentBrush" : "TextSecondaryBrush");
            icon.Margin = new Thickness(0, 0, 14, 0);
            line.Children.Add(icon);
            line.Children.Add(new TextBlock { Text = title, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(bar);
            row.Children.Add(line);
            var face = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(0, 9, 0, 9), Margin = new Thickness(0, 0, 0, 2), Child = row, Background = Brushes.Transparent };
            if (selected) face.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            var b = new Button { Content = face, Template = GallerySheet.BareButton(), Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            b.MouseEnter += (_, _) => face.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            b.MouseLeave += (_, _) => { if (!selected) face.Background = Brushes.Transparent; };
            b.Click += (_, _) => ShowPage(id);
            _nav.Children.Add(b);
        }
    }

    // ── Building blocks ──────────────────────────────────────

    private void Heading(string text, string? subtitle = null)
    {
        var t = new TextBlock { Text = text, FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, subtitle is null ? 22 : 6) };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _page.Children.Add(t);
        if (subtitle is not null)
        {
            var s = Ui.Text(subtitle, size: 13, wrap: true);
            s.Margin = new Thickness(0, 0, 0, 22);
            _page.Children.Add(s);
        }
    }

    private void Group(string text) =>
        _page.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 10) });

    /// <summary>A Windows 11 style settings card: title, description, control on the right.</summary>
    private Border Card(string title, string? description, UIElement? right, UIElement? below = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        texts.Children.Add(new TextBlock { Text = title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (description is not null)
        {
            var d = Ui.Text(description, size: 12.5, wrap: true);
            d.Margin = new Thickness(0, 3, 0, 0);
            texts.Children.Add(d);
        }
        grid.Children.Add(texts);
        if (right is not null)
        {
            Grid.SetColumn(right, 1);
            if (right is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(right);
        }
        var stack = new StackPanel();
        stack.Children.Add(grid);
        if (below is not null) stack.Children.Add(below);
        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 6), Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        _page.Children.Add(card);
        return card;
    }

    private static CheckBox Toggle(bool value, Action<bool> changed)
    {
        var t = new CheckBox { Style = Ui.Style("ToggleSwitch"), IsChecked = value };
        // Checked/Unchecked, not Click: keyboard, mouse and accessibility tools all toggle it.
        t.Checked += (_, _) => changed(true);
        t.Unchecked += (_, _) => changed(false);
        return t;
    }

    private void Notify(ChangeKind kind) => _s.Session.Notify(kind);

    /// <summary>A button that asks "¿Seguro?" on the first click and acts on the second.</summary>
    private static Button Confirm(string text, string confirmText, Action act)
    {
        var armed = false;
        var b = Ui.Pill(text, () => { });
        b.Click += (_, _) =>
        {
            if (armed) { act(); return; }
            armed = true;
            b.Content = confirmText;
            b.Style = Ui.Style("AccentPillButton");
        };
        b.MouseLeave += (_, _) => { if (!armed) return; armed = false; b.Content = text; b.Style = Ui.Style("PillButton"); };
        return b;
    }

    public static Button Swatch(string hex, bool selected, double size, Action pick)
    {
        var color = Argb.TryParse(hex, out var a) ? Color.FromArgb(a.A, a.R, a.G, a.B) : Colors.Gray;
        var dot = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = new SolidColorBrush(color) };
        var ring = new Border { CornerRadius = new CornerRadius(size / 2 + 4), Padding = new Thickness(2), BorderThickness = new Thickness(2), Child = dot, Margin = new Thickness(0, 0, 4, 0) };
        if (selected) ring.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
        else ring.BorderBrush = Brushes.Transparent;
        var b = new Button { Content = ring, Template = GallerySheet.BareButton(), Cursor = Cursors.Hand, ToolTip = hex };
        b.Click += (_, _) => pick();
        return b;
    }

    // ── Pages ────────────────────────────────────────────────

    private void General()
    {
        var g = Config.General;
        Heading("General");
        var autostart = Toggle(g.StartWithWindows, v => { g.StartWithWindows = v; Notify(ChangeKind.General); });
        autostart.IsEnabled = Autostart.Available;
        Card("Iniciar con Windows",
            Autostart.Available ? "SackSpace se abre solo al entrar en tu sesión." : "No disponible en una compilación de desarrollo (dotnet run).",
            autostart);
        Card("Usar todas las pantallas", "Cada monitor tiene sus propios paneles. Apagado, solo la pantalla principal.",
            Toggle(g.UseAllMonitors, v => { g.UseAllMonitors = v; Notify(ChangeKind.General); }));
        Card("Barra de espacios", "Las pestañas arriba de la pantalla principal para cambiar de espacio.",
            Toggle(g.ShowWorkspaceBar, v => { g.ShowWorkspaceBar = v; Notify(ChangeKind.General); }));
        Card("Iconos del escritorio por espacio", "Cada espacio muestra solo los iconos que elijas: clic derecho sobre un icono, o el lápiz de la barra › Iconos. SackSpace oculta los de Windows mientras está abierto; al salir vuelven. No se mueve ni cambia ningún archivo.",
            Toggle(g.DesktopIcons, v => { g.DesktopIcons = v; Notify(ChangeKind.General); }));
        Card("Animaciones", "Movimientos suaves al cambiar de espacio y al mover paneles. Se apagan solas si Windows tiene los efectos desactivados.",
            Toggle(g.Animations, v => { g.Animations = v; Notify(ChangeKind.General); }));
        Card("Pausar con pantalla completa", "Relojes y medidores descansan mientras juegas o ves un vídeo a pantalla completa, y con la sesión bloqueada.",
            Toggle(g.PauseInFullscreen, v => { g.PauseInFullscreen = v; Notify(ChangeKind.General); }));
        Card("Descansar detrás de ventanas maximizadas", "Una pantalla tapada por una ventana maximizada o a pantalla completa deja de dibujar sus paneles hasta que se despeje. Ahorra procesador; no cambia nada de lo que ves.",
            Toggle(g.RestBehindWindows, v => { g.RestBehindWindows = v; Notify(ChangeKind.General); }));
        Card("Bloquear el diseño", "Mantener pulsado un panel ya no lo pone en modo edición, para no moverlo sin querer. El lápiz de la barra y el atajo siguen funcionando.",
            Toggle(g.LockLayout, v => { g.LockLayout = v; Notify(ChangeKind.General); }));

        Group("Cómo se usa");
        Card("Mover y cambiar el tamaño", "Mantén pulsado un panel, o usa el lápiz de la barra. Arrastra el panel para moverlo y la esquina para cambiar su tamaño; se alinean solos.", null);
        Card("Ver los paneles sobre las ventanas", $"{Shortcut(HotkeyActions.Peek)} o un clic en el icono de SackSpace junto al reloj. Esc o un clic fuera los vuelve a bajar.", null);
        Card("Cambiar de espacio", $"Las pestañas, la rueda del ratón sobre ellas o {Shortcut(HotkeyActions.Previous)} / {Shortcut(HotkeyActions.Next)}.", null);
    }

    private string Shortcut(string action)
    {
        var s = Config.Hotkeys.GetValueOrDefault(action);
        return string.IsNullOrEmpty(s) ? "su atajo" : s;
    }

    private void Appearance()
    {
        var a = Config.Appearance;
        var t = _s.Theme.Current;
        Heading("Apariencia");

        Group("Tema");
        var themes = new WrapPanel();
        foreach (var theme in _s.Theme.Catalog.Themes) themes.Children.Add(ThemeCard(theme, theme.Id == t.Base.Id));
        _page.Children.Add(themes);
        var themeTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        themeTools.Children.Add(Ui.Pill("Abrir la carpeta de temas", () => Open(_s.Theme.ThemesFolder), glyph: Glyphs.Folder));
        var reload = Ui.Pill("Recargar", () => { _s.Theme.ReloadCatalog(); _s.ApplyTheme(false); Refresh(); }, glyph: Glyphs.Undo);
        reload.Margin = new Thickness(8, 0, 0, 0);
        themeTools.Children.Add(reload);
        _page.Children.Add(themeTools);
        var hint = Ui.Text("Para añadir un tema, copia su archivo .json en esa carpeta y pulsa Recargar.", size: 12, wrap: true);
        hint.Margin = new Thickness(2, 8, 0, 0);
        _page.Children.Add(hint);
        foreach (var error in _s.Theme.Catalog.Errors)
        {
            var e = Ui.Text("No se cargó: " + error, size: 12, wrap: true);
            e.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x7A));
            _page.Children.Add(e);
        }

        Group("Color de acento");
        var modes = new WrapPanel();
        foreach (var (mode, label) in new[] { (AccentMode.Workspace, "El de cada espacio"), (AccentMode.Wallpaper, "Del fondo de pantalla"), (AccentMode.Theme, "El del tema"), (AccentMode.Custom, "Personalizado") })
        {
            var chip = Ui.Pill(label, () => { a.AccentMode = mode; Notify(ChangeKind.Appearance); Refresh(); }, accent: a.AccentMode == mode);
            chip.Margin = new Thickness(0, 0, 8, 8);
            modes.Children.Add(chip);
        }
        _page.Children.Add(modes);
        if (a.AccentMode == AccentMode.Custom)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var hex in Swatches)
                row.Children.Add(Swatch(hex, hex.Equals(a.CustomAccent, StringComparison.OrdinalIgnoreCase), 26, () => { a.CustomAccent = hex; Notify(ChangeKind.Appearance); Refresh(); }));
            var box = new TextBox { Style = Ui.Style("FieldTextBox"), Text = a.CustomAccent, Width = 120, Margin = new Thickness(12, 0, 0, 0), MaxLength = 9, VerticalAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) =>
            {
                if (!Argb.TryParse(box.Text, out var parsed)) return;
                a.CustomAccent = parsed.WithAlpha(1).ToHex();
                Notify(ChangeKind.Appearance);
            };
            row.Children.Add(box);
            _page.Children.Add(row);
        }

        Group("Cristal");
        Card("Cristal transparente", "Deja ver el escritorio en vivo, sin difuminar, también los fondos animados de Wallpaper Engine o Lively. Si cuesta leer, sube la opacidad.",
            Toggle(a.ClearGlass, v => { a.ClearGlass = v; Notify(ChangeKind.Appearance); }));
        Slide("Opacidad del cristal", "Más alto, más fácil de leer sobre fondos con mucho detalle.", 0.1, 0.95, 0.05, t.GlassOpacity,
            v => $"{v * 100:0} %", v => a.GlassOpacity = v);
        Slide("Desenfoque", "Cuánto se difumina el fondo de pantalla detrás de los paneles.", 0, 80, 2, t.BlurRadius,
            v => $"{v:0} px", v => a.BlurRadius = v);
        Slide("Esquinas", "Radio de los bordes redondeados.", 0, 32, 1, t.CornerRadius,
            v => $"{v:0} px", v => a.CornerRadius = v);
        var reset = Ui.Pill("Volver a los valores del tema", () =>
        {
            a.GlassOpacity = a.BlurRadius = a.CornerRadius = null;
            Notify(ChangeKind.Appearance);
            Refresh();
        }, glyph: Glyphs.Undo);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 6, 0, 0);
        _page.Children.Add(reset);

        Group("Fondo detrás del cristal");
        var custom = a.CustomWallpaper;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.Pill(custom is null ? "Usar una imagen…" : "Cambiar…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Imagen para el cristal", Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff" };
            if (dialog.ShowDialog(this) != true) return;
            a.CustomWallpaper = dialog.FileName;
            Notify(ChangeKind.Appearance);
            Refresh();
        }, glyph: Glyphs.Folder));
        if (custom is not null)
        {
            var auto = Ui.Pill("Automático", () => { a.CustomWallpaper = null; Notify(ChangeKind.Appearance); Refresh(); }, glyph: Glyphs.Undo);
            auto.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(auto);
        }
        var screens = _s.Hosts.Views.Select((v, i) => $"{(v.Monitor.IsPrimary ? "Pantalla principal" : $"Pantalla {i + 1}")}: {_s.Wallpaper.Get(v.Monitor).Source}");
        Card(custom is null ? "Automático" : "Imagen propia",
            custom is null
                ? "Se difumina lo que muestra cada pantalla. Con Wallpaper Engine o Lively se usa la vista previa de su fondo.\n" + string.Join("\n", screens)
                : Path.GetFileName(custom) + " en todas las pantallas.",
            buttons);
    }

    private void Slide(string title, string description, double min, double max, double step, double value, Func<double, string> format, Action<double> set)
    {
        var shown = new TextBlock { Text = format(value), FontSize = 13, Width = 56, TextAlignment = TextAlignment.Right };
        var slider = new Slider
        {
            Style = Ui.Style("AccentSlider"), Minimum = min, Maximum = max, Value = value, Width = 220,
            TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 4,
        };
        slider.ValueChanged += (_, e) =>
        {
            shown.Text = format(e.NewValue);
            set(Math.Round(e.NewValue, 3));
            Notify(ChangeKind.Appearance);
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(slider);
        right.Children.Add(shown);
        Card(title, description, right);
    }

    private Button ThemeCard(ThemeDefinition theme, bool selected)
    {
        Color C(string hex) => Argb.TryParse(hex, out var a) ? Color.FromArgb(a.A, a.R, a.G, a.B) : Colors.Gray;
        var accent = C(theme.Accent);
        var tint = C(theme.GlassTint);
        var fg = C(theme.Foreground);

        var glass = new Border
        {
            CornerRadius = new CornerRadius(Math.Min(14, theme.CornerRadius / 1.6)),
            Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(theme.GlassOpacity + 0.15, 0, 1) * 255), tint.R, tint.G, tint.B)),
            BorderBrush = new SolidColorBrush(C(theme.Border)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(18, 16, 18, 16),
            Padding = new Thickness(12, 10, 12, 10),
        };
        var lines = new StackPanel();
        lines.Children.Add(new Border { Height = 6, Width = 54, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(fg), HorizontalAlignment = HorizontalAlignment.Left });
        lines.Children.Add(new Border { Height = 5, Width = 80, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(110, fg.R, fg.G, fg.B)), Margin = new Thickness(0, 7, 0, 9), HorizontalAlignment = HorizontalAlignment.Left });
        lines.Children.Add(new Border { Height = 14, Width = 14, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Left });
        glass.Child = lines;

        var preview = new Border
        {
            Width = 176, Height = 112,
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(Color.FromRgb((byte)(accent.R * 0.55), (byte)(accent.G * 0.55), (byte)(accent.B * 0.55)),
                theme.IsDark ? Color.FromRgb(18, 20, 28) : Color.FromRgb(226, 230, 240), 35),
            BorderThickness = new Thickness(2),
            Child = glass,
        };
        if (selected) preview.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        else preview.BorderBrush = Brushes.Transparent;

        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
        stack.Children.Add(preview);
        stack.Children.Add(new TextBlock { Text = theme.Name, FontSize = 13, Margin = new Thickness(4, 8, 0, 0), FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal });
        var b = new Button { Content = stack, Template = GallerySheet.BareButton(), Cursor = Cursors.Hand };
        b.Click += (_, _) =>
        {
            Config.Appearance.ThemeId = theme.Id;
            Notify(ChangeKind.Appearance);
            Refresh();
        };
        return b;
    }

    private void Workspaces()
    {
        Heading("Espacios", "Cada espacio guarda sus propios paneles, accesos y notas. Cambia con las pestañas de arriba, la rueda del ratón sobre ellas o los atajos.");
        var list = Config.Workspaces;
        for (var i = 0; i < list.Count; i++)
        {
            var ws = list[i];
            var index = i;

            var iconFace = new Grid { Width = 40, Height = 40 };
            iconFace.Children.Add(new Border { CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(Argb.TryParse(ws.Accent, out var ac) ? Color.FromRgb(ac.R, ac.G, ac.B) : Colors.Gray) });
            iconFace.Children.Add(new TextBlock { Text = ws.Icon, Style = Ui.Style("Icon"), FontSize = 16, Foreground = ac.Luminance > 0.45 ? new SolidColorBrush(Color.FromRgb(16, 19, 26)) : Brushes.White });
            var iconButton = new Button { Content = iconFace, Template = GallerySheet.BareButton(), Cursor = Cursors.Hand, ToolTip = "Cambiar el icono", Margin = new Thickness(0, 0, 16, 0) };
            iconButton.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = iconButton };
                var glyphs = new WrapPanel { Width = 6 * 40 };
                foreach (var g in Glyphs.WorkspaceChoices)
                {
                    var pick = Ui.IconButton(g, "", () => { ws.Icon = g; menu.IsOpen = false; Notify(ChangeKind.WorkspaceList); Refresh(); }, 36);
                    pick.ToolTip = null;
                    pick.FontSize = 16;
                    pick.Margin = new Thickness(2);
                    glyphs.Children.Add(pick);
                }
                menu.Items.Add(glyphs);
                menu.IsOpen = true;
            };

            var name = new TextBox { Style = Ui.Style("FieldTextBox"), Text = ws.Name, Width = 220, MaxLength = 24 };
            name.TextChanged += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(name.Text)) return;
                ws.Name = name.Text.Trim();
                Notify(ChangeKind.WorkspaceList);
            };
            var colors = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var hex in Swatches)
                colors.Children.Add(Swatch(hex, hex.Equals(ws.Accent, StringComparison.OrdinalIgnoreCase), 18, () =>
                {
                    ws.Accent = hex;
                    Notify(ChangeKind.WorkspaceList | ChangeKind.Appearance);
                    Refresh();
                }));
            var middle = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            middle.Children.Add(name);
            middle.Children.Add(colors);
            middle.Children.Add(Automatic(ws));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            if (ws.Id == Config.ActiveWorkspace?.Id) actions.Children.Add(new TextBlock { Text = "En uso", FontSize = 12, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("AccentBrush") });
            else actions.Children.Add(WithMargin(Ui.Pill("Mostrar", () => { _s.Hosts.SwitchTo(ws.Id); Refresh(); }), 0, 0, 8, 0));
            var up = Ui.IconButton(Glyphs.ChevronUp, "Subir", () => { Swap(index, index - 1); }, 32);
            up.IsEnabled = index > 0;
            var down = Ui.IconButton(Glyphs.ChevronDown, "Bajar", () => { Swap(index, index + 1); }, 32);
            down.IsEnabled = index < list.Count - 1;
            actions.Children.Add(up);
            actions.Children.Add(down);
            var delete = Confirm("Borrar", "¿Borrar con sus paneles?", () =>
            {
                list.Remove(ws);
                if (Config.ActiveWorkspaceId == ws.Id) Config.ActiveWorkspaceId = list[0].Id;
                Notify(ChangeKind.WorkspaceList | ChangeKind.Workspace | ChangeKind.Appearance);
                Refresh();
            });
            delete.IsEnabled = list.Count > 1;
            delete.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(delete);

            var row = new DockPanel();
            DockPanel.SetDock(iconButton, Dock.Left);
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(iconButton);
            row.Children.Add(actions);
            row.Children.Add(middle);
            var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 6), Child = row };
            card.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            _page.Children.Add(card);
        }

        var add = Ui.Pill("Añadir un espacio", () =>
        {
            var used = list.Select(w => w.Accent).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ws = new Workspace
            {
                Name = $"Espacio {list.Count + 1}",
                Icon = Glyphs.Star,
                Accent = Swatches.FirstOrDefault(s => !used.Contains(s)) ?? Swatches[0],
            };
            var area = _s.Hosts.PrimaryView?.LayoutBounds ?? new Core.Rect(0, 0, 1280, 720);
            var clock = Core.Widgets.BuiltInWidgets.Find(Core.Widgets.BuiltInWidgets.Clock)!;
            ws.Panels.Add(new PanelState
            {
                Widget = clock.Id,
                Bounds = Core.LayoutMath.FindFreeSpot(clock.DefaultWidth, clock.DefaultHeight, [], area),
            });
            list.Add(ws);
            Notify(ChangeKind.WorkspaceList);
            Refresh();
        }, accent: true, glyph: Glyphs.Add);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 10, 0, 0);
        _page.Children.Add(add);
    }

    private static readonly (DayOfWeek Day, string Letter)[] Week =
        [(DayOfWeek.Monday, "L"), (DayOfWeek.Tuesday, "M"), (DayOfWeek.Wednesday, "X"), (DayOfWeek.Thursday, "J"),
         (DayOfWeek.Friday, "V"), (DayOfWeek.Saturday, "S"), (DayOfWeek.Sunday, "D")];

    /// <summary>"Automático": hours and days, and programs, that bring this workspace up by itself.</summary>
    private FrameworkElement Automatic(Workspace ws)
    {
        var box = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        box.Children.Add(Ui.Text("Se abre solo: en este horario (días marcados, o todos) o al usar estos programas. Vacío, nunca.", size: 12, wrap: true));

        var hours = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        TextBox Time(string? value, string hint) =>
            new() { Style = Ui.Style("FieldTextBox"), Text = value ?? "", Width = 70, MaxLength = 5, ToolTip = hint, VerticalAlignment = VerticalAlignment.Center };
        var from = Time(ws.ScheduleFrom, "Desde, p. ej. 09:00");
        var to = Time(ws.ScheduleTo, "Hasta, p. ej. 18:00");
        var status = Ui.Text("", size: 12);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(10, 0, 0, 0);
        void SaveHours()
        {
            var a = from.Text.Trim();
            var b = to.Text.Trim();
            if (a.Length == 0 && b.Length == 0) { ws.ScheduleFrom = ws.ScheduleTo = null; status.Text = "sin horario"; }
            else if (AutoSwitch.TryParseTime(a, out var ta) && AutoSwitch.TryParseTime(b, out var tb) && ta != tb)
            {
                ws.ScheduleFrom = ta.ToString(@"hh\:mm");
                ws.ScheduleTo = tb.ToString(@"hh\:mm");
                status.Text = ta > tb ? "pasa la medianoche" : "";
            }
            else { status.Text = "formato 09:00"; return; } // half typed: keep the last good hours
            Notify(ChangeKind.WorkspaceList);
        }
        from.TextChanged += (_, _) => SaveHours();
        to.TextChanged += (_, _) => SaveHours();
        var fromLabel = WithMargin(Ui.Text("De", size: 13), 0, 0, 8, 0);
        fromLabel.VerticalAlignment = VerticalAlignment.Center;
        hours.Children.Add(fromLabel);
        hours.Children.Add(from);
        var and = WithMargin(Ui.Text("a", size: 13), 8, 0, 8, 0);
        and.VerticalAlignment = VerticalAlignment.Center;
        hours.Children.Add(and);
        hours.Children.Add(to);

        var days = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (day, letter) in Week)
        {
            var on = ws.ScheduleDays?.Contains(day) == true;
            var chip = Ui.Pill(letter, () => { }, accent: on);
            chip.MinWidth = 0;
            chip.Padding = new Thickness(9, 4, 9, 4);
            chip.Margin = new Thickness(0, 0, 4, 0);
            chip.ToolTip = Ui.Capitalize(Ui.Culture.DateTimeFormat.GetDayName(day)) + " (sin ninguno marcado: todos los días)";
            chip.Click += (_, _) =>
            {
                var list = ws.ScheduleDays ?? [];
                if (!list.Remove(day)) list.Add(day);
                ws.ScheduleDays = list.Count == 0 ? null : list;
                chip.Style = Ui.Style(list.Contains(day) ? "AccentPillButton" : "PillButton");
                Notify(ChangeKind.WorkspaceList);
            };
            days.Children.Add(chip);
        }
        hours.Children.Add(status);
        box.Children.Add(hours);
        box.Children.Add(days);

        var apps = new TextBox { Style = Ui.Style("FieldTextBox"), Text = string.Join(", ", ws.Apps ?? []), Width = 320, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        apps.TextChanged += (_, _) =>
        {
            ws.Apps = AutoSwitch.ParseApps(apps.Text);
            Notify(ChangeKind.WorkspaceList);
        };
        box.Children.Add(Ui.WithPlaceholder(apps, "Al usar estos programas: steam, discord…"));
        return box;
    }

    private static T WithMargin<T>(T e, double l, double t, double r, double b) where T : FrameworkElement
    {
        e.Margin = new Thickness(l, t, r, b);
        return e;
    }

    private void Swap(int a, int b)
    {
        var list = Config.Workspaces;
        (list[a], list[b]) = (list[b], list[a]);
        Notify(ChangeKind.WorkspaceList);
        Refresh();
    }

    private void Hotkeys()
    {
        Heading("Atajos de teclado", "Funcionan en todo Windows. Pulsa un atajo para cambiarlo: Supr lo quita y Esc cancela.");
        foreach (var action in HotkeyActions.Defaults.Keys)
        {
            var label = HotkeyActions.Label(action);
            if (action.StartsWith("workspace", StringComparison.Ordinal)
                && int.TryParse(action["workspace".Length..], out var n) && n <= Config.Workspaces.Count)
                label = $"Ir a «{Config.Workspaces[n - 1].Name}»";
            var failed = _s.FailedHotkeys.Contains(action);
            var current = Config.Hotkeys.GetValueOrDefault(action) ?? "";
            var button = Ui.Pill(current.Length == 0 ? "Sin atajo" : current, () => { });
            button.MinWidth = 150;
            button.Click += (_, _) => BeginCapture(action, button);
            var card = Card(label, failed ? "Otra app ya usa este atajo; elige otro." : null, button);
            if (failed && ((Grid)((StackPanel)card.Child).Children[0]).Children[0] is StackPanel texts && texts.Children.Count > 1)
                ((TextBlock)texts.Children[1]).Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x7A));
        }
        var reset = Ui.Pill("Restablecer los atajos", () =>
        {
            Config.Hotkeys = new Dictionary<string, string>(HotkeyActions.Defaults);
            Notify(ChangeKind.Hotkeys);
            Refresh();
        }, glyph: Glyphs.Undo);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 10, 0, 0);
        _page.Children.Add(reset);
    }

    private void BeginCapture(string action, Button button)
    {
        EndCapture();
        // Otherwise pressing the current shortcut would run it instead of being recorded.
        _s.Hotkeys?.UnregisterAll();
        button.Content = "Pulsa el atajo…";
        button.Style = Ui.Style("AccentPillButton");
        _capture = (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            if (key == Key.Escape) { EndCapture(); Refresh(); return; }
            if (key is Key.Delete or Key.Back)
            {
                Config.Hotkeys[action] = "";
            }
            else
            {
                var mods = HotkeyModifiers.None;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Ctrl;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;
                if (!HotkeyGesture.TryParse(new HotkeyGesture(mods, key.ToString()).ToString(), out var gesture))
                {
                    button.Content = "Usa Ctrl, Alt o Win con otra tecla";
                    return;
                }
                // One shortcut, one action: take it from whoever had it.
                foreach (var other in Config.Hotkeys.Where(kv => kv.Value == gesture.ToString() && kv.Key != action).Select(kv => kv.Key).ToList())
                    Config.Hotkeys[other] = "";
                Config.Hotkeys[action] = gesture.ToString();
            }
            EndCapture();
            Refresh();
        };
        PreviewKeyDown += _capture;
    }

    private void EndCapture()
    {
        if (_capture is null) return;
        PreviewKeyDown -= _capture;
        _capture = null;
        Notify(ChangeKind.Hotkeys); // registers the shortcuts again
    }

    private void Data()
    {
        Heading("Datos y privacidad");
        var path = new TextBox { Text = _s.DataFolder, IsReadOnly = true, Style = Ui.Style("GlassTextBox"), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) };
        path.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        Card("Todo se queda en este equipo",
            "SackSpace no tiene cuentas ni envía estadísticas. Solo el widget de Clima sale a internet, a Open-Meteo, con la ciudad que escribas. Tus espacios, notas y tareas se guardan en esta carpeta, con una copia de seguridad automática del último cambio:",
            null, path);

        var tools = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        tools.Children.Add(WithMargin(Ui.Pill("Abrir la carpeta", () => Open(_s.DataFolder), glyph: Glyphs.Folder), 0, 0, 8, 8));
        tools.Children.Add(WithMargin(Ui.Pill("Exportar una copia…", Export, glyph: Glyphs.Document), 0, 0, 8, 8));
        tools.Children.Add(WithMargin(Ui.Pill("Importar una copia…", Import, glyph: Glyphs.Undo), 0, 0, 8, 8));
        _page.Children.Add(tools);

        Group("Empezar de cero");
        Card("Restablecer SackSpace", "Vuelve a los cuatro espacios de ejemplo y a los ajustes de fábrica. Exporta una copia antes si quieres guardar lo tuyo.",
            Confirm("Restablecer", "¿Seguro? Pulsa otra vez", () =>
            {
                _s.Session.Replace(_s.CreateDefault());
                Refresh();
            }));
    }

    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar una copia de SackSpace",
            FileName = $"sackspace-{DateTime.Now:yyyy-MM-dd}.json",
            Filter = "Copia de SackSpace (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, ConfigStore.Serialize(Config));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo guardar la copia: " + ex.Message, "SackSpace", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Importar una copia de SackSpace", Filter = "Copia de SackSpace (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        AppConfig? imported = null;
        try
        {
            if (new FileInfo(dialog.FileName).Length < 20_000_000) imported = ConfigStore.TryDeserialize(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) { Log.Warn("No se pudo leer la copia", ex); }
        if (imported is null)
        {
            MessageBox.Show(this, "Ese archivo no es una copia válida de SackSpace.", "SackSpace", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _s.Session.Replace(ConfigStore.Normalize(imported));
        Refresh();
    }

    private void About()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        Heading("Acerca de");
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        head.Children.Add(new Image { Source = Icon, Width = 56, Height = 56, Margin = new Thickness(0, 0, 18, 0) });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = "SackSpace", FontSize = 22, FontWeight = FontWeights.SemiBold });
        names.Children.Add(Ui.Text($"Versión {version?.ToString(3) ?? "?"} · Hecho por Sackito", size: 13));
        head.Children.Add(names);
        _page.Children.Add(head);
        Card("Tu escritorio, ordenado por espacios", "Paneles de cristal con reloj, calendario, notas, tareas, monitor del sistema, reproductor y accesos, separados en espacios para cada momento del día.", null);
        Card("Registro de errores", "Si algo falla, este archivo ayuda a entender qué pasó. No guarda nada personal.",
            Ui.Pill("Abrir el registro", () => Open(Path.Combine(_s.DataFolder, "logs", "sackspace.log"))));
    }

    private static void Open(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Log.Warn($"No se pudo abrir '{path}'", ex); }
    }
}
