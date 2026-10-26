using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SackSpace.Core.Model;
using SackSpace.Interop;
using SackSpace.Services;
using Forms = System.Windows.Forms;

namespace SackSpace.Shell;

/// <summary>
/// The icon next to the clock: a click shows the panels over the windows, a right
/// click opens a glass menu (workspaces, edit, settings, exit).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppServices _s;
    private readonly Action _exit;
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(AppServices services, Action exit)
    {
        _s = services;
        _exit = exit;
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/sackspace.ico"))!.Stream;
        using (stream)
        {
            _icon = new Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
                Visible = true,
            };
        }
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                // Clicking the tray already took the focus and lowered them: that click meant "lower".
                if (!_s.Hosts.IsRaised && DateTime.UtcNow - _s.Hosts.LastAutoLower < TimeSpan.FromMilliseconds(700)) return;
                _s.Hosts.ToggleRaised();
            }
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
        _s.Hosts.StateChanged += UpdateText;
        UpdateText();
    }

    private void UpdateText()
    {
        var name = _s.Session.Config.ActiveWorkspace?.Name;
        var text = name is null ? "SackSpace" : $"SackSpace · {name}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void ShowMenu()
    {
        var hosts = _s.Hosts;
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        foreach (var ws in _s.Session.Config.Workspaces)
            menu.Items.Add(Ui.Item(ws.Name, ws.Icon, () => hosts.SwitchTo(ws.Id), ws.Id == _s.Session.Config.ActiveWorkspace?.Id));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item(hosts.IsRaised ? "Bajar los paneles" : "Ver los paneles encima", Glyphs.View, hosts.ToggleRaised));
        menu.Items.Add(Ui.Item("Editar el diseño", Glyphs.Edit, () => hosts.SetEditing(true)));
        menu.Items.Add(Ui.Item("Ajustes", Glyphs.Settings, () => hosts.OpenSettings()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item("Salir de SackSpace", Glyphs.Power, _exit));
        // The menu only closes on an outside click if our process is in the foreground.
        if (hosts.Hosts.FirstOrDefault() is { Handle: var h } && h != IntPtr.Zero) Native.SetForegroundWindow(h);
        menu.IsOpen = true;
    }

    /// <summary>A Windows notification (it lands in the notification centre too).</summary>
    public void Notify(string title, string text) => _icon.ShowBalloonTip(8000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _s.Hosts.StateChanged -= UpdateText;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
