using System.Drawing;
using System.Windows.Forms;

namespace SysMonitor;

/// <summary>
/// The notification-area icon the widget hides behind.
///
/// Through Windows Forms' <see cref="NotifyIcon"/> rather than a hand-rolled
/// Shell_NotifyIcon: it is part of the desktop framework, not a package, and
/// it is a hundred and fifty lines of message-window plumbing we would
/// otherwise own and have to get right. Its hidden window is pumped by the
/// WPF dispatcher, so there is no second message loop.
///
/// It exists only while the window is hidden. A widget that sits on the
/// desktop all day does not also need a permanent icon in the tray.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private NotifyIcon? _icon;

    public event Action? ShowRequested;
    public event Action? ExitRequested;

    public bool Visible => _icon is not null;

    /// <summary>Put the icon in the tray, with a menu in the current language.</summary>
    public void Show(Lang lang, string tooltip)
    {
        if (_icon is not null)
        {
            Update(tooltip);
            return;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(lang["tray_show"], null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(lang["tray_exit"], null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = Trim(tooltip),
            Visible = true,
            ContextMenuStrip = menu,
        };
        // Double-click is what everyone tries first.
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowRequested?.Invoke();
            }
        };
    }

    /// <summary>
    /// The tooltip, which is the whole point of the icon while the window is
    /// away: it should still say what the machine is doing.
    /// </summary>
    public void Update(string tooltip)
    {
        if (_icon is not null)
        {
            _icon.Text = Trim(tooltip);
        }
    }

    public void Hide()
    {
        if (_icon is null)
        {
            return;
        }
        // Setting Visible false first: disposing a visible icon sometimes
        // leaves it in the tray until the pointer passes over it.
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _icon = null;
    }

    public void Dispose() => Hide();

    /// <summary>
    /// Shell tooltips are capped at 63 characters, and a longer one is not
    /// truncated for you -- it is rejected, leaving no tooltip at all.
    /// </summary>
    internal static string Trim(string text) =>
        text.Length <= 63 ? text : text[..62] + "…";

    private static Icon LoadIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (path is not null)
            {
                Icon? own = Icon.ExtractAssociatedIcon(path);
                if (own is not null)
                {
                    return own;
                }
            }
        }
        catch (Exception error)
        {
            Diag.ReportException("Tray icon", error);
        }
        // Better a generic icon in the tray than no way back to the window.
        return SystemIcons.Application;
    }
}
