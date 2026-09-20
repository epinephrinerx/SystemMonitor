using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SysMonitor.Model;
using SysMonitor.Native;
using SysMonitor.Sensors;
using SysMonitor.ViewModels;

namespace SysMonitor;

public partial class MainWindow : Window
{
    private const double ShadowPad = 12;   // room the drop shadow needs
    private const double SnapMargin = 25;
    private const double GripSize = 16;    // bottom-right resize hit zone

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RotateEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(10);

    private readonly AppConfig _config;
    private readonly Sampler _sampler;
    private readonly WidgetViewModel _model;
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _heartbeat = new();

    private DateTime _lastRotate = DateTime.UtcNow;
    private Snapshot? _shown;
    private bool _expanded;
    private bool _closing;

    private Point _dragOrigin;
    private Point _windowOrigin;
    private bool _dragging;
    private bool _resizing;
    private Size _resizeOrigin;

    public MainWindow(AppConfig config, Sampler sampler, string[]? args = null)
    {
        _config = config;
        _sampler = sampler;
        _model = new WidgetViewModel(config);

        InitializeComponent();
        DataContext = _model;

        Topmost = config.AlwaysOnTop;
        ApplyPanelSize();
        RestorePosition();
        BuildSidebar();

        CloseButton.Click += (_, _) => Close();
        CollapseButton.Click += (_, _) => SetExpanded(false);

        _timer.Interval = Tick;
        _timer.Tick += OnTick;
        _timer.Start();

        _heartbeat.Interval = Heartbeat;
        _heartbeat.Tick += (_, _) =>
        {
            Snapshot snap = _sampler.Current;
            Diag.Heartbeat(_expanded ? "expanded" : "mini",
                           $"cores={snap.Cores.Count} disks={snap.Disks.Count}");
        };
        _heartbeat.Start();

        BuildContextMenu();

        // --expanded opens straight into the full view.  Checking that layout
        // otherwise means driving a double-click into the user's desktop.
        if (args is not null && args.Contains("--expanded"))
        {
            SetExpanded(true);
        }
    }

    // ------------------------------------------------------------- geometry
    private void ApplyPanelSize()
    {
        Width = (_expanded ? _config.ExpW : _config.MiniW) + ShadowPad * 2;
        Height = (_expanded ? _config.ExpH : _config.MiniH) + ShadowPad * 2;
    }

    private void RestorePosition()
    {
        if (_config.PosX is double x && _config.PosY is double y)
        {
            Left = x;
            Top = y;
            return;
        }
        // First run: bottom-right of the monitor holding the cursor.
        Rect area = WorkArea(0, 0);
        Left = area.Right - Width - 24;
        Top = area.Bottom - Height - 24;
    }

    /// <summary>The usable desktop under a point, in device-independent px.</summary>
    private Rect WorkArea(double x, double y)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        var physical = Win32.WorkArea((int)(x * dpi.DpiScaleX), (int)(y * dpi.DpiScaleY));
        if (physical is null)
        {
            return SystemParameters.WorkArea;
        }
        (int left, int top, int right, int bottom) = physical.Value;
        return new Rect(left / dpi.DpiScaleX, top / dpi.DpiScaleY,
                        (right - left) / dpi.DpiScaleX, (bottom - top) / dpi.DpiScaleY);
    }

    // ---------------------------------------------------------------- timer
    private void OnTick(object? sender, EventArgs e)
    {
        if (_closing)
        {
            return;
        }
        try
        {
            Snapshot snap = _sampler.Current;
            if (!snap.Ready)
            {
                _model.UpdateMini(snap);
                return;
            }

            if (_model.ViewCount == 0)
            {
                _model.RebuildViews(snap);
            }

            // The sampler publishes a new snapshot every couple of seconds, so
            // most ticks have nothing to say.  Repainting anyway is not free:
            // a per-pixel-alpha window is composited in software, so an
            // unchanged frame still costs a full redraw.
            bool fresh = !ReferenceEquals(snap, _shown);
            bool rotate = !_expanded && _model.ViewCount > 1
                          && DateTime.UtcNow - _lastRotate >= RotateEvery;
            if (!fresh && !rotate)
            {
                return;
            }
            _shown = snap;

            if (_expanded)
            {
                _model.UpdateExpanded(snap);
                return;
            }

            if (rotate)
            {
                _lastRotate = DateTime.UtcNow;
                _model.ViewIndex = (_model.ViewIndex + 1) % _model.ViewCount;
            }
            _model.UpdateMini(snap);
        }
        catch (Exception error)
        {
            // A widget must never die because a frame failed to draw.
            Diag.ReportException("Tick", error);
        }
    }

    // ----------------------------------------------------------- mode switch
    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
        {
            return;
        }
        _expanded = expanded;
        MiniView.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedView.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ApplyPanelSize();
        KeepOnScreen();

        Snapshot snap = _sampler.Current;
        if (expanded)
        {
            _model.UpdateExpanded(snap);
        }
        else
        {
            _model.RebuildViews(snap);
            _model.UpdateMini(snap);
        }
    }

    private void ResetSize()
    {
        _config.MiniW = 270;
        _config.MiniH = 90;
        _config.ExpW = 630;
        _config.ExpH = 480;
        ApplyPanelSize();
        KeepOnScreen();
        _config.Save();
    }

    private void KeepOnScreen()
    {
        Rect area = WorkArea(Left, Top);
        Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
        Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
    }

    // ------------------------------------------------------------- pointer
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            SetExpanded(!_expanded);
            return;
        }

        Point point = e.GetPosition(this);
        _dragOrigin = PointToScreen(point);
        _windowOrigin = new Point(Left, Top);

        if (InGrip(point))
        {
            _resizing = true;
            _resizeOrigin = new Size(Width, Height);
        }
        else
        {
            _dragging = true;
        }
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point point = e.GetPosition(this);

        if (!_dragging && !_resizing)
        {
            Cursor = InGrip(point) ? Cursors.SizeNWSE : Cursors.Arrow;
            return;
        }

        Point now = PointToScreen(point);
        double dx = now.X - _dragOrigin.X;
        double dy = now.Y - _dragOrigin.Y;

        if (_resizing)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            Resize(_resizeOrigin.Width + dx / dpi.DpiScaleX,
                   _resizeOrigin.Height + dy / dpi.DpiScaleY);
            return;
        }

        DpiScale scale = VisualTreeHelper.GetDpi(this);
        double left = _windowOrigin.X + dx / scale.DpiScaleX;
        double top = _windowOrigin.Y + dy / scale.DpiScaleY;
        if (_config.Snap)
        {
            (left, top) = SnapToEdges(left, top);
        }
        Left = left;
        Top = top;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging && !_resizing)
        {
            return;
        }
        _dragging = false;
        _resizing = false;
        ReleaseMouseCapture();
        SavePlacement();
    }

    private bool InGrip(Point point) =>
        point.X >= Width - ShadowPad - GripSize && point.X <= Width - ShadowPad
        && point.Y >= Height - ShadowPad - GripSize && point.Y <= Height - ShadowPad;

    private void Resize(double width, double height)
    {
        (double minW, double minH) = _expanded ? AppConfig.MinExp : AppConfig.MinMini;
        double maxW = _expanded ? 2000 : AppConfig.MaxMini.W;
        double maxH = _expanded ? 1400 : AppConfig.MaxMini.H;

        Width = Math.Clamp(width, minW + ShadowPad * 2, maxW + ShadowPad * 2);
        Height = Math.Clamp(height, minH + ShadowPad * 2, maxH + ShadowPad * 2);

        if (_expanded)
        {
            _config.ExpW = Width - ShadowPad * 2;
            _config.ExpH = Height - ShadowPad * 2;
        }
        else
        {
            _config.MiniW = Width - ShadowPad * 2;
            _config.MiniH = Height - ShadowPad * 2;
        }
    }

    private (double Left, double Top) SnapToEdges(double left, double top)
    {
        Rect area = WorkArea(left + Width / 2, top + Height / 2);
        if (Math.Abs(left - area.Left) < SnapMargin)
        {
            left = area.Left;
        }
        if (Math.Abs(area.Right - (left + Width)) < SnapMargin)
        {
            left = area.Right - Width;
        }
        if (Math.Abs(top - area.Top) < SnapMargin)
        {
            top = area.Top;
        }
        if (Math.Abs(area.Bottom - (top + Height)) < SnapMargin)
        {
            top = area.Bottom - Height;
        }
        return (left, top);
    }

    private void SavePlacement()
    {
        _config.PosX = Left;
        _config.PosY = Top;
        _config.Save();
    }

    // -------------------------------------------------------- context menu
    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        var toggle = new MenuItem();
        var reset = new MenuItem { Header = new Lang(_config.Lang)["reset_size"] };
        var close = new MenuItem { Header = new Lang(_config.Lang)["close"] };

        toggle.Click += (_, _) => SetExpanded(!_expanded);
        reset.Click += (_, _) => ResetSize();
        close.Click += (_, _) => Close();

        menu.Opened += (_, _) =>
        {
            var lang = new Lang(_config.Lang);
            toggle.Header = _expanded ? lang["collapse"] : lang["expand_hint"];
            reset.Header = lang["reset_size"];
            close.Header = lang["close"];
        };

        menu.Items.Add(toggle);
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        ContextMenu = menu;
    }

    // ------------------------------------------------------------- sidebar
    /// <summary>
    /// The settings column of the expanded view.  Built in code rather than
    /// XAML because every control writes straight back into the config and
    /// then nudges the sampler; a binding layer would add indirection without
    /// removing any of that wiring.
    /// </summary>
    private void BuildSidebar()
    {
        var lang = new Lang(_config.Lang);
        Sidebar.Children.Clear();

        Sidebar.Children.Add(Heading(lang["window_settings"]));
        Sidebar.Children.Add(Check(lang["always_on_top"], _config.AlwaysOnTop, value =>
        {
            _config.AlwaysOnTop = value;
            Topmost = value;
        }));
        Sidebar.Children.Add(Check(lang["snap"], _config.Snap, value => _config.Snap = value));
        string exePath = Environment.ProcessPath ?? string.Empty;
        Sidebar.Children.Add(Check(lang["autostart"], Startup.IsEnabled(exePath),
                                   value => Startup.Set(value, exePath)));
        Sidebar.Children.Add(Check(lang["light_mode"], _config.Theme == "light", value =>
        {
            _config.Theme = value ? "light" : "dark";
            _model.ApplyTheme();
        }));

        Sidebar.Children.Add(Heading(lang["opacity"]));
        var opacity = new Slider
        {
            Minimum = 0.35,
            Maximum = 1.0,
            Value = _config.Opacity,
            IsMoveToPointEnabled = true,
            Margin = new Thickness(0, 0, 0, 6),
        };
        // Live while dragging: the Tk build redrew the whole canvas here and
        // destroyed the item the pointer had grabbed.
        opacity.ValueChanged += (_, e) =>
        {
            _config.Opacity = e.NewValue;
            _model.ApplyTheme();
        };
        opacity.PreviewMouseUp += (_, _) => _config.Save();
        Sidebar.Children.Add(opacity);

        Sidebar.Children.Add(Heading(lang["display_settings"]));
        Sidebar.Children.Add(Check(lang["show_cpu"], _config.ShowCpu, value =>
        {
            _config.ShowCpu = value;
            Refresh();
        }));
        Sidebar.Children.Add(Check(lang["per_core"], _config.CpuMode == "separated", value =>
        {
            _config.CpuMode = value ? "separated" : "total";
            Refresh();
        }));
        Sidebar.Children.Add(Check(lang["show_ram"], _config.ShowRam, value =>
        {
            _config.ShowRam = value;
            Refresh();
        }));
        Sidebar.Children.Add(Check(lang["show_disk"], _config.ShowDisk, value =>
        {
            _config.ShowDisk = value;
            Refresh();
        }));
        Sidebar.Children.Add(Check(lang["per_drive"], _config.DiskMode == "separated", value =>
        {
            _config.DiskMode = value ? "separated" : "total";
            Refresh();
        }));

        Sidebar.Children.Add(Heading(lang["speed"]));
        Sidebar.Children.Add(Choice(
            new[] { ("eco", lang["eco"]), ("balanced", lang["balanced"]), ("fast", lang["fast"]) },
            _config.Speed,
            value =>
            {
                _config.Speed = value;
                _sampler.Nudge();
                _config.Save();
            }));

        Sidebar.Children.Add(Heading(lang["language"]));
        Sidebar.Children.Add(Choice(
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

    private void Refresh()
    {
        Snapshot snap = _sampler.Current;
        _model.RebuildViews(snap);
        if (_expanded)
        {
            _model.UpdateExpanded(snap);
        }
        else
        {
            _model.UpdateMini(snap);
        }
        _config.Save();
    }

    private TextBlock Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Style = (Style)FindResource("Label"),
        Foreground = _model.LabelBrush,
        Margin = new Thickness(0, 10, 0, 6),
    };

    private CheckBox Check(string text, bool value, Action<bool> onChange)
    {
        var box = new CheckBox
        {
            Content = text,
            IsChecked = value,
            Foreground = _model.MutedBrush,
            FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Tahoma"),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 3),
        };
        box.Checked += (_, _) => onChange(true);
        box.Unchecked += (_, _) => onChange(false);
        return box;
    }

    private UIElement Choice((string Key, string Text)[] options, string selected,
                             Action<string> onChange)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach ((string key, string text) in options)
        {
            var button = new RadioButton
            {
                Content = text,
                IsChecked = key == selected,
                Foreground = _model.MutedBrush,
                FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Tahoma"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 10, 0),
                GroupName = string.Join("-", options.Select(o => o.Key)),
            };
            button.Checked += (_, _) => onChange(key);
            panel.Children.Add(button);
        }
        return panel;
    }

    // ------------------------------------------------------------ shutdown
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _timer.Stop();
        _heartbeat.Stop();
        SavePlacement();
        _sampler.Stop();
        base.OnClosing(e);
    }
}
