using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysMonitor;

/// <summary>
/// The pages that open in a window of their own: settings, the manual and the
/// about page. Built here from the same helpers as the sidebar, so a control
/// looks and behaves the same wherever it is.
/// </summary>
public partial class MainWindow
{
    private const string SidebarGroup = "sidebar";
    private const string SettingsGroup = "settings";

    private PanelWindow? _settingsWindow;
    private PanelWindow? _manualWindow;
    private PanelWindow? _aboutWindow;

    // ------------------------------------------------------------ mirroring
    // A few settings appear both in the overall sidebar and in the settings
    // window. Each control registers how to set itself quietly; changing one
    // updates the other, without running the setting's own handler twice.
    private readonly Dictionary<string, List<(string Group, Action<object> Apply)>> _mirrors = new();
    private string _group = SidebarGroup;
    private bool _mirroring;

    private void Register(string? key, Action<object> apply)
    {
        if (key is null)
        {
            return;
        }
        if (!_mirrors.TryGetValue(key, out var list))
        {
            _mirrors[key] = list = new();
        }
        list.Add((_group, apply));
    }

    private void Mirror(string? key, string origin, object value)
    {
        if (key is null || !_mirrors.TryGetValue(key, out var list))
        {
            return;
        }
        _mirroring = true;
        try
        {
            foreach ((string group, Action<object> apply) in list.ToArray())
            {
                if (group != origin)
                {
                    apply(value);
                }
            }
        }
        finally
        {
            _mirroring = false;
        }
    }

    private void ForgetMirrors(string group)
    {
        foreach (var list in _mirrors.Values)
        {
            list.RemoveAll(entry => entry.Group == group);
        }
    }

    // --------------------------------------------------------- shared pieces
    /// <summary>
    /// The window settings. The overall sidebar keeps four of them in sight --
    /// always on top, light mode, opacity, font size -- and the settings window
    /// carries all six.
    /// </summary>
    private void AddWindowSettings(Panel target, Lang lang, bool everything)
    {
        target.Children.Add(Check(lang["always_on_top"], _config.AlwaysOnTop, value =>
        {
            _config.AlwaysOnTop = value;
            Topmost = value;
        }, key: "always_on_top"));

        if (everything)
        {
            target.Children.Add(Check(lang["snap"], _config.Snap, value => _config.Snap = value));
            string exePath = Environment.ProcessPath ?? string.Empty;
            target.Children.Add(Check(lang["autostart"], Startup.IsEnabled(exePath),
                                      value => Startup.Set(value, exePath)));
        }

        target.Children.Add(Check(lang["light_mode"], _config.Theme == "light", value =>
        {
            _config.Theme = value ? "light" : "dark";
            _model.ApplyTheme();
        }, key: "light_mode"));

        // Live while dragging: the Tk build redrew the whole canvas here and
        // destroyed the item the pointer had grabbed.
        target.Children.Add(Dial(lang["opacity"], 0.35, 1.0, _config.Opacity,
            value => $"{value * 100:F0}%",
            value =>
            {
                _config.Opacity = value;
                _model.ApplyTheme();
            }, key: "opacity"));

        target.Children.Add(Dial(lang["font_size"], FontRamp.Min, FontRamp.Max, _config.FontScale,
            value => $"{value * 100:F0}%",
            value =>
            {
                _config.FontScale = value;
                ApplyFontScale();
            }, key: "font_size"));
    }

    /// <summary>The choices that used to be radio groups in the sidebar.</summary>
    private void AddPreferences(Panel target, Lang lang)
    {
        target.Children.Add(Heading(lang["close_action"]));
        target.Children.Add(Choice(
            new[] { ("exit", lang["close_to_exit"]), ("tray", lang["close_to_tray"]) },
            _config.CloseAction,
            value =>
            {
                _config.CloseAction = value;
                _config.Save();
            }));

        target.Children.Add(Heading(lang["speed"]));
        target.Children.Add(Choice(
            new[] { ("eco", lang["eco"]), ("balanced", lang["balanced"]), ("fast", lang["fast"]) },
            _config.Speed,
            value =>
            {
                _config.Speed = value;
                _sampler.Nudge();
                _config.Save();
            }));

        target.Children.Add(Heading(lang["language"]));
        target.Children.Add(Choice(
            new[] { ("th", "ไทย"), ("en", "English") },
            _config.Lang,
            value =>
            {
                _config.Lang = value;
                _model.SetLanguage(value);
                BuildSidebar();
                BuildContextMenu();
                Refresh();
            }));
    }

    private TextBlock Paragraph(string text, bool muted = true)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted ? _model.MutedBrush : _model.TextBrush,
            FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Tahoma"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        FollowRamp(block, TextBlock.FontSizeProperty, "FontBody");
        return block;
    }

    // ---------------------------------------------------------- the windows
    private void OpenSettings() =>
        OpenPanel(ref _settingsWindow, 360, 600, resizable: true);

    private void OpenManual() =>
        OpenPanel(ref _manualWindow, 460, 560, resizable: true);

    private void OpenAbout() =>
        OpenPanel(ref _aboutWindow, 380, 440, resizable: false);

    /// <summary>One window per page: asking again brings the open one forward.</summary>
    private void OpenPanel(ref PanelWindow? slot, double width, double height, bool resizable)
    {
        if (slot is not null)
        {
            if (slot.WindowState == WindowState.Minimized)
            {
                slot.WindowState = WindowState.Normal;
            }
            slot.Activate();
            return;
        }

        var window = new PanelWindow(this, string.Empty, width, height,
                                     _model.WindowBrush, resizable);
        slot = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, _settingsWindow))
            {
                ForgetMirrors(SettingsGroup);
                _settingsWindow = null;
            }
            else if (ReferenceEquals(window, _manualWindow))
            {
                _manualWindow = null;
            }
            else if (ReferenceEquals(window, _aboutWindow))
            {
                _aboutWindow = null;
            }
        };
        FillPanel(window);
        window.Show();
    }

    /// <summary>Build the page a window shows, in the current language.</summary>
    private void FillPanel(PanelWindow window)
    {
        var lang = new Lang(_config.Lang);
        if (ReferenceEquals(window, _settingsWindow))
        {
            ForgetMirrors(SettingsGroup);
            _group = SettingsGroup;
            try
            {
                var body = new StackPanel();
                body.Children.Add(Heading(lang["window_settings"]));
                AddWindowSettings(body, lang, everything: true);
                AddPreferences(body, lang);
                window.SetBody(body, lang["settings"]);
            }
            finally
            {
                _group = SidebarGroup;
            }
        }
        else if (ReferenceEquals(window, _manualWindow))
        {
            window.SetBody(BuildManualBody(lang), lang["manual"]);
        }
        else if (ReferenceEquals(window, _aboutWindow))
        {
            window.SetBody(BuildAboutBody(lang), lang["about"]);
        }
    }

    /// <summary>The language or the layout changed: rewrite whatever is open.</summary>
    private void RebuildSidePanels()
    {
        foreach (PanelWindow? window in new[] { _settingsWindow, _manualWindow, _aboutWindow })
        {
            if (window is not null)
            {
                FillPanel(window);
            }
        }
    }

    private UIElement BuildManualBody(Lang lang)
    {
        var body = new StackPanel();
        for (int i = 1; lang[$"manual_{i}_h"] != $"manual_{i}_h"; i++)
        {
            body.Children.Add(Heading(lang[$"manual_{i}_h"]));
            body.Children.Add(Paragraph(lang[$"manual_{i}_t"]));
        }
        return body;
    }

    private UIElement BuildAboutBody(Lang lang)
    {
        var body = new StackPanel();

        var name = new TextBlock
        {
            Text = AboutInfo.Product,
            Style = (Style)FindResource("Value"),
            Foreground = _model.TextBrush,
            Margin = new Thickness(0, 0, 0, 2),
        };
        FollowRamp(name, TextBlock.FontSizeProperty, "FontHeading");
        body.Children.Add(name);
        body.Children.Add(Paragraph($"{lang["about_version"]} {Updater.Current}"));
        body.Children.Add(Paragraph(lang["about_desc"]));

        body.Children.Add(Heading(lang["about_developer"]));
        body.Children.Add(Paragraph(AboutInfo.Developer, muted: false));

        body.Children.Add(Heading(lang["about_copyright"]));
        body.Children.Add(Paragraph(AboutInfo.Copyright, muted: false));

        body.Children.Add(Heading(lang["about_terms"]));
        body.Children.Add(Paragraph(lang["about_terms_text"], muted: false));

        body.Children.Add(Heading(lang["about_credit"]));
        body.Children.Add(Paragraph($"{lang["about_credit_text"]} {AboutInfo.UpstreamName}"));

        var source = NavButton("", lang["about_source"], () => OpenRepository());
        source.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(source);
        return body;
    }

    /// <summary>
    /// Open the project page in the default browser. The address is a constant
    /// of this program, never something read from a file or the network.
    /// </summary>
    private static void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AboutInfo.Repository) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            Diag.ReportException("Open repository", error);
        }
    }
}
