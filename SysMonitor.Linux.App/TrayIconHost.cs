using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// The tray icon from spec section 9, built on Avalonia's own
/// <see cref="TrayIcon"/> (a StatusNotifierItem on KDE), so no package is
/// added. The icon exists only while the window is hidden: it is created on
/// hide and removed on restore.
///
/// Some desktops have no StatusNotifier host at all. Creating the icon can
/// fail there, and <see cref="Show"/> says so instead of pretending it worked;
/// the window then stays reachable.
/// </summary>
internal sealed class TrayIconHost : ITray
{
    /// <summary>
    /// A generic 16×16 icon for when the packaged "SysMonitor.ico" is missing:
    /// a rounded teal square with a white window glyph.
    /// </summary>
    private const string FallbackIconBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAwUlEQVR42qXS2wpBQRQG4PVACCGEEEKe" +
        "SZIkSZIkSZIkya3z+Xx+ENeufxeamibtzZ6/5vKbf61aRFx08Qf0iTsMyRuMqStM6QvMmTMs2ROsuSNs" +
        "+QPshT3oW37FjuIOztIWUthV3sBdWUMKe6oreGtLkAz21RcgET9fUHw89jfmILFZ7QMeB5ozkJaxGQ62" +
        "piARi41KONSegMRmtZ15HO6MQeLYajvzONIdgf7dmcfR3hAkg2P9wecapTCLFGbRgt9pCnptQ6QpXQAA" +
        "AABJRU5ErkJggg==";

    private readonly ShellModel _shell;
    private readonly Action _onShow;
    private readonly Action _onExit;
    private readonly Diagnostics _diagnostics;
    private readonly TrayIcons _icons = new();

    private TrayIcon? _icon;
    private bool _attached;
    private bool _failureLogged;

    public TrayIconHost(ShellModel shell, Action onShow, Action onExit, Diagnostics diagnostics)
    {
        _shell = shell;
        _onShow = onShow;
        _onExit = onExit;
        _diagnostics = diagnostics;
    }

    public bool Available => _icon is not null;

    public bool Show(string tooltip)
    {
        try
        {
            if (Application.Current is not { } application)
            {
                return false;
            }

            if (!_attached)
            {
                // The collection is attached once; adding to it is what makes
                // the icon appear.
                TrayIcon.SetIcons(application, _icons);
                _attached = true;
            }

            if (_icon is { } existing)
            {
                existing.ToolTipText = tooltip;
                return true;
            }

            var icon = new TrayIcon
            {
                Icon = LoadIcon(),
                ToolTipText = tooltip,
                Menu = BuildMenu(),
            };
            icon.Clicked += (_, _) => Dispatcher.UIThread.Post(_onShow);
            // Assigned before it is registered, so a failure while the platform
            // creates its icon can still be removed by Dispose.
            _icon = icon;
            _icons.Add(icon);
            icon.IsVisible = true;
            return true;
        }
        catch (Exception exception)
        {
            // Amendment 1: the close path reports the fallback once per run, so
            // the tray adds its first failure only and retries quietly after.
            if (!_failureLogged)
            {
                _failureLogged = true;
                _diagnostics.Line("tray: the icon could not be created: " + exception.Message);
            }
            Dispose();
            return false;
        }
    }

    public void Hide()
    {
        if (_icon is not { } icon)
        {
            return;
        }

        _icon = null;
        try
        {
            icon.IsVisible = false;
            _icons.Remove(icon);
            icon.Dispose();
        }
        catch (Exception exception)
        {
            _diagnostics.Line("tray: the icon could not be removed: " + exception.Message);
        }
    }

    public void Dispose() => Hide();

    /// <summary>The menu is rebuilt on every show, so it follows a language change.</summary>
    private NativeMenu BuildMenu()
    {
        var show = new NativeMenuItem(Ui.Thai(_shell.Language["tray_show"]));
        show.Click += (_, _) => Dispatcher.UIThread.Post(_onShow);

        var exit = new NativeMenuItem(Ui.Thai(_shell.Language["tray_exit"]));
        exit.Click += (_, _) => Dispatcher.UIThread.Post(_onExit);

        var menu = new NativeMenu();
        menu.Items.Add(show);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    /// <summary>The app's own icon, or the embedded fallback when it is not installed.</summary>
    private static WindowIcon LoadIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "SysMonitor.ico");
        try
        {
            if (File.Exists(path))
            {
                return new WindowIcon(path);
            }
        }
        catch (Exception)
        {
            // An icon the platform cannot read is replaced by the fallback.
        }

        return new WindowIcon(new MemoryStream(Convert.FromBase64String(FallbackIconBase64)));
    }
}
