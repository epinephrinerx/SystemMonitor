using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private const double ShadowPad = WindowGeometry.ShadowPad;
    private const double SnapMargin = 25;

    /// <summary>How far the pointer moves before a press counts as a drag.</summary>
    private const double DragThreshold = 4;

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
    private readonly TrayIcon _tray = new();
    private Mode _mode = Mode.Widget;
    private bool _exiting;
    private Rect _beforeFull;
    private bool _closing;

    /// <summary>The widget rotates, overall lists, full graphs.</summary>
    private enum Mode
    {
        Widget,
        Overall,
        Full,
    }

    private bool _isOverall => _mode == Mode.Overall;

    private Point _dragOrigin;
    private Point _windowOrigin;
    private bool _dragging;

    /// <summary>
    /// Where the pointer went down, while it is still unclear whether this is
    /// a drag or just a click. Null when nothing is pending.
    /// </summary>
    private Point? _pendingDrag;

    private Edge _resizing = Edge.None;

    /// <summary>
    /// Where the window was when the drag began. Every frame is computed from
    /// this rather than from the last one: dragging a left or top edge moves
    /// the window as well as sizing it, and an incremental version drifts --
    /// worse once the size clamps and the pointer keeps going.
    /// </summary>
    private Rect _resizeOrigin;

    /// <summary>
    /// The scale in force when the drag began. Every delta is converted with
    /// this rather than with whatever the window's DPI is at the moment: drag
    /// across a boundary between monitors of different scaling and the two
    /// disagree, which makes the window jump mid-gesture.
    /// </summary>
    private DpiScale _dragDpi;

    public MainWindow(AppConfig config, Sampler sampler, string[]? args = null)
    {
        _config = config;
        _sampler = sampler;
        _model = new WidgetViewModel(config);

        InitializeComponent();
        DataContext = _model;

        Topmost = config.AlwaysOnTop;
        ApplyFontScale();
        ApplyPanelSize();
        RestorePosition();
        BuildSidebar();

        CloseButton.Click += (_, _) => Close();
        CollapseButton.Click += (_, _) => SetMode(Mode.Widget);
        FullScreenButton.Click += (_, _) => SetMode(Mode.Full);
        WidgetCloseButton.Click += (_, _) => Close();
        WidgetPrevButton.Click += (_, _) => Step(-1);
        WidgetNextButton.Click += (_, _) => Step(1);
        FullCloseButton.Click += (_, _) => Close();
        FullCollapseButton.Click += (_, _) => SetMode(Mode.Overall);
        KeyDown += OnKeyDown;

        _timer.Interval = Tick;
        _timer.Tick += OnTick;
        _timer.Start();

        // --heartbeat <seconds> shortens the interval so a memory question can
        // be answered in minutes instead of hours.
        _heartbeat.Interval = HeartbeatInterval(args);
        _heartbeat.Tick += (_, _) =>
        {
            Snapshot snap = _sampler.Current;
            Diag.Heartbeat(_mode.ToString().ToLowerInvariant(),
                           $"cores={snap.Cores.Count} disks={snap.Disks.Count}");
            // Hand back pages the GC has already released. Costs nothing and
            // keeps a widget that sits idle for days from looking like it is
            // hoarding memory.
            Win32.TrimWorkingSet();
        };
        _heartbeat.Start();

        BuildContextMenu();

        _tray.ShowRequested += RestoreFromTray;
        _tray.ExitRequested += () =>
        {
            _exiting = true;
            Close();
        };

        // --expanded and --full open straight into a view. Checking those
        // layouts otherwise means driving clicks into the user's desktop.
        //
        // Deferred to Loaded: full screen needs the monitor under the window,
        // and asking for the DPI of a window that has no handle yet answers
        // for the primary monitor at best.
        Mode start = args is not null && args.Contains("--full") ? Mode.Full
            : args is not null && args.Contains("--expanded") ? Mode.Overall
            : Mode.Widget;
        if (start != Mode.Widget)
        {
            Loaded += (_, _) => SetMode(start);
        }
        // Checked once the window has a handle, so the monitor layout and the
        // DPI are real. A position saved against a monitor that has since been
        // unplugged would otherwise put the widget somewhere unreachable, with
        // no way back short of editing the config by hand.
        // Checked once the window has a handle, so the monitor layout and the
        // DPI are real. A position saved against a monitor that has since been
        // unplugged would otherwise put the widget somewhere unreachable.
        Loaded += (_, _) => KeepOnScreen();
        Diag.Write($"window ready mode={start.ToString().ToLowerInvariant()}");
    }

    private static TimeSpan HeartbeatInterval(string[]? args)
    {
        if (args is null)
        {
            return Heartbeat;
        }
        int index = Array.FindIndex(args,
            a => a.Equals("--heartbeat", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length
            && int.TryParse(args[index + 1], out int seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }
        return Heartbeat;
    }

    // ------------------------------------------------------------- geometry
    private void ApplyPanelSize()
    {
        if (_mode == Mode.Full)
        {
            // A window, not an OS full-screen mode. It opens at its smallest
            // and is enlarged by whoever wants it bigger, rather than taking
            // the screen from them and making them give it back.
            if (_config.FullW <= 0 || _config.FullH <= 0)
            {
                _config.FullW = AppConfig.MinFull.W;
                _config.FullH = AppConfig.MinFull.H;
            }
            Width = _config.FullW + ShadowPad * 2;
            Height = _config.FullH + ShadowPad * 2;
            return;
        }
        Width = (_isOverall ? _config.OverallW : _config.WidgetW) + ShadowPad * 2;
        Height = (_isOverall ? _config.OverallH : _config.WidgetH) + ShadowPad * 2;
    }

    /// <summary>The monitor's full bounds, taskbar included, in DIPs.</summary>
    private Rect MonitorBounds(double x, double y)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        var physical = Win32.MonitorBounds((int)(x * dpi.DpiScaleX), (int)(y * dpi.DpiScaleY));
        if (physical is null)
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth,
                            SystemParameters.PrimaryScreenHeight);
        }
        (int left, int top, int right, int bottom) = physical.Value;
        return new Rect(left / dpi.DpiScaleX, top / dpi.DpiScaleY,
                        (right - left) / dpi.DpiScaleX, (bottom - top) / dpi.DpiScaleY);
    }

    private void RestorePosition()
    {
        if (_config.PosX is double x && _config.PosY is double y)
        {
            Left = x;
            Top = y;
            return;
        }
        // First run: bottom-right of the primary monitor. Not the monitor
        // under the cursor -- there is no window yet to ask the DPI of, so
        // this is the one screen whose coordinates are known to be right.
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
                _model.UpdateWidget(snap);
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
            bool rotate = _mode == Mode.Widget && _model.ViewCount > 1
                          && DateTime.UtcNow - _lastRotate >= RotateEvery;
            if (!fresh && !rotate)
            {
                return;
            }
            _shown = snap;

            if (_tray.Visible)
            {
                _tray.Update(TrayTooltip());
            }

            // Every snapshot feeds the graphs, whichever view is on screen, so
            // the full view opens with history behind it rather than empty.
            if (fresh)
            {
                _model.PushHistory(snap);
            }

            if (_mode == Mode.Full)
            {
                return;
            }
            if (_isOverall)
            {
                _model.UpdateOverall(snap);
                return;
            }

            if (rotate)
            {
                _lastRotate = DateTime.UtcNow;
                _model.ViewIndex = (_model.ViewIndex + 1) % _model.ViewCount;
            }
            _model.UpdateWidget(snap);
        }
        catch (Exception error)
        {
            // A widget must never die because a frame failed to draw.
            Diag.ReportException("Tick", error);
        }
    }

    /// <summary>
    /// Move the mini view on by hand. The rotation timer restarts from here,
    /// so stepping to something does not then have it swept away a moment
    /// later by a rotation that was already half-way through.
    /// </summary>
    private void Step(int by)
    {
        if (_model.ViewCount == 0)
        {
            return;
        }
        _model.ViewIndex = (_model.ViewIndex + by + _model.ViewCount) % _model.ViewCount;
        _lastRotate = DateTime.UtcNow;
        _model.UpdateWidget(_sampler.Current);
    }

    // ----------------------------------------------------------- mode switch
    private void SetMode(Mode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        // Leaving full screen has to put the window back where it was; the
        // config only knows panel sizes, not the position it was dragged to.
        if (_mode == Mode.Full)
        {
            Left = _beforeFull.Left;
            Top = _beforeFull.Top;
        }
        else if (mode == Mode.Full)
        {
            _beforeFull = new Rect(Left, Top, Width, Height);
        }

        _mode = mode;
        WidgetView.Visibility = mode == Mode.Widget ? Visibility.Visible : Visibility.Collapsed;
        OverallView.Visibility = mode == Mode.Overall ? Visibility.Visible : Visibility.Collapsed;
        FullView.Visibility = mode == Mode.Full ? Visibility.Visible : Visibility.Collapsed;
        Panel.CornerRadius = new CornerRadius(mode == Mode.Full ? 0 : 14);
        Grip.Visibility = mode == Mode.Full ? Visibility.Collapsed : Visibility.Visible;

        ApplyPanelSize();
        if (mode != Mode.Full)
        {
            KeepOnScreen();
        }

        Snapshot snap = _sampler.Current;
        switch (mode)
        {
            case Mode.Overall:
                _model.UpdateOverall(snap);
                FitOverallToContent();
                break;
            case Mode.Full:
                FullTitle.Text = new Lang(_config.Lang)["full_data"];
                _model.PushHistory(snap);
                if (_config.FullTab.Length > 0)
                {
                    _model.Select(_config.FullTab);
                }
                break;
            default:
                _model.RebuildViews(snap);
                _model.UpdateWidget(snap);
                break;
        }
    }

    /// <summary>Escape steps back one level; F11 toggles full screen.</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            SetMode(_mode == Mode.Full ? Mode.Overall : Mode.Full);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape)
        {
            return;
        }
        SetMode(_mode == Mode.Full ? Mode.Overall : Mode.Widget);
        e.Handled = true;
    }

    /// <summary>
    /// Shrink the expanded window onto its contents.
    ///
    /// A fixed size is wrong for somebody: the room needed depends on how many
    /// cores, drives and adapters the machine has and on the font scale. The
    /// content is measured with no constraint and the window takes exactly
    /// that, held between its minimum and the screen it is on.
    ///
    /// Skipped once the user has dragged an edge: their size wins from then
    /// on, and "reset window size" is how they hand it back.
    /// </summary>
    private void FitOverallToContent()
    {
        if (_config.OverallSized)
        {
            return;
        }

        // Layout has to have run at least once for the measure to mean
        // anything, and on the first entry it has not.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_mode != Mode.Overall || _config.OverallSized)
            {
                return;
            }

            OverallView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size wanted = OverallView.DesiredSize;
            if (wanted.Width <= 0 || wanted.Height <= 0)
            {
                return;
            }

            Rect area = WorkArea(Left + Width / 2, Top + Height / 2);
            _config.OverallW = Math.Clamp(Math.Ceiling(wanted.Width),
                                      AppConfig.MinOverall.W, area.Width - ShadowPad * 2);
            _config.OverallH = Math.Clamp(Math.Ceiling(wanted.Height),
                                      AppConfig.MinOverall.H, area.Height - ShadowPad * 2);
            Width = _config.OverallW + ShadowPad * 2;
            Height = _config.OverallH + ShadowPad * 2;
            KeepOnScreen();
        });
    }

    private void ResetSize()
    {
        _config.OverallSized = false;
        _config.FullW = 0;
        _config.FullH = 0;
        _config.WidgetW = 270;
        _config.WidgetH = 90;
        _config.OverallW = 630;
        _config.OverallH = 480;
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
    /// <summary>
    /// Start a resize before anything else sees the click.
    ///
    /// The bubbling handler never fired over the expanded view: ScrollViewer
    /// marks MouseLeftButtonDown handled to take focus, so the corner of the
    /// one view that most needs resizing was the one place it did not work.
    /// </summary>
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (e.ClickCount > 1)
        {
            return;
        }
        // The title-bar buttons and the close button on the widget both sit in
        // a corner, which is now a resize zone. Claiming the click there would
        // make them dead.
        if (OverControl(e.OriginalSource))
        {
            return;
        }

        Point point = e.GetPosition(this);
        Edge edge = WindowGeometry.HitTest(point, Width, Height);
        if (edge == Edge.None)
        {
            // Not an edge, so it may be a drag of the window. Remember where
            // it started but let the click through: the full view is covered
            // by scroll viewers, and ScrollViewer marks MouseLeftButtonDown
            // handled to take focus, so the window's own handler never sees a
            // press over its contents. Claiming it here instead would take the
            // click away from everything inside.
            _pendingDrag = point;
            _windowOrigin = new Point(Left, Top);
            _dragDpi = VisualTreeHelper.GetDpi(this);
            return;
        }
        _pendingDrag = null;
        _dragOrigin = PointToScreen(point);
        _resizeOrigin = new Rect(Left, Top, Width, Height);
        _dragDpi = VisualTreeHelper.GetDpi(this);
        _resizing = edge;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            // Mini opens, expanded fills the screen, full comes back to mini.
            SetMode(_mode == Mode.Widget ? Mode.Overall
                    : _mode == Mode.Overall ? Mode.Full : Mode.Widget);
            return;
        }

        if (_mode == Mode.Full)
        {
            return;     // nothing to drag or resize when it fills the screen
        }

        // Edges and drags are both settled in the preview pass: this view is
        // covered by controls that would otherwise swallow the press.
    }

    /// <summary>
    /// Turn a pending press into a drag once the pointer has actually moved.
    ///
    /// The threshold is what separates dragging the window from clicking
    /// something on it: a press that never moves stays a click and reaches
    /// whatever was under it.
    /// </summary>
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_pendingDrag is not Point origin || _dragging || _resizing != Edge.None)
        {
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pendingDrag = null;
            return;
        }

        Point now = e.GetPosition(this);
        if (Math.Abs(now.X - origin.X) < DragThreshold
            && Math.Abs(now.Y - origin.Y) < DragThreshold)
        {
            return;
        }

        _dragOrigin = PointToScreen(origin);
        _pendingDrag = null;
        _dragging = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point point = e.GetPosition(this);

        if (!_dragging && _resizing == Edge.None)
        {
            Cursor = OverControl(e.OriginalSource)
                ? Cursors.Arrow
                : CursorFor(WindowGeometry.HitTest(point, Width, Height));
            return;
        }

        Point now = PointToScreen(point);
        double dx = now.X - _dragOrigin.X;
        double dy = now.Y - _dragOrigin.Y;

        if (_resizing != Edge.None)
        {
            Resize(new Vector(dx / _dragDpi.DpiScaleX, dy / _dragDpi.DpiScaleY));
            return;
        }

        double left = _windowOrigin.X + dx / _dragDpi.DpiScaleX;
        double top = _windowOrigin.Y + dy / _dragDpi.DpiScaleY;
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
        if (!_dragging && _resizing == Edge.None)
        {
            return;
        }
        _dragging = false;
        _resizing = Edge.None;
        _pendingDrag = null;
        ReleaseMouseCapture();
        // Position as well as size: dragging a left or top edge moves the
        // window, and the two have to be remembered together or it jumps back
        // on the next start.
        SavePlacement();
    }

    /// <summary>
    /// Is the pointer on something that wants the click itself? Walks up from
    /// whatever was hit, because the source is usually a piece of a control's
    /// template rather than the control.
    /// </summary>
    private static bool OverControl(object? source)
    {
        for (DependencyObject? node = source as DependencyObject;
             node is not null;
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or Slider or ScrollBar or Thumb)
            {
                return true;
            }
        }
        return false;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        _pendingDrag = null;
    }

    private static Cursor CursorFor(Edge edge) => edge switch
    {
        Edge.Left or Edge.Right => Cursors.SizeWE,
        Edge.Top or Edge.Bottom => Cursors.SizeNS,
        Edge.Left | Edge.Top or Edge.Right | Edge.Bottom => Cursors.SizeNWSE,
        Edge.Right | Edge.Top or Edge.Left | Edge.Bottom => Cursors.SizeNESW,
        _ => Cursors.Arrow,
    };

    private void Resize(Vector delta)
    {
        (var min, var max) = WindowGeometry.Limits(ViewOf(_mode));

        // Dragged below what the tabs need, the full view steps back to the
        // expanded one rather than showing something cramped. Its own minimum
        // is smaller, so the drag carries on from there.
        if (_mode == Mode.Full)
        {
            Rect wanted = WindowGeometry.Resize(_resizeOrigin, _resizing, delta,
                                                AppConfig.MinOverall, max);
            Size panelSize = WindowGeometry.Panel(wanted.Width, wanted.Height);
            if (WindowGeometry.TooSmallForFull(panelSize.Width, panelSize.Height))
            {
                _config.FullW = 0;      // it will fill the work area next time
                SetMode(Mode.Overall);
                _resizeOrigin = new Rect(Left, Top, Width, Height);
                return;
            }
        }

        Rect window = WindowGeometry.Resize(_resizeOrigin, _resizing, delta, min, max);
        Left = window.Left;
        Top = window.Top;
        Width = window.Width;
        Height = window.Height;

        Size panel = WindowGeometry.Panel(window.Width, window.Height);
        switch (_mode)
        {
            case Mode.Full:
                _config.FullW = panel.Width;
                _config.FullH = panel.Height;
                break;
            case Mode.Overall:
                _config.OverallW = panel.Width;
                _config.OverallH = panel.Height;
                _config.OverallSized = true;
                break;
            default:
                _config.WidgetW = panel.Width;
                _config.WidgetH = panel.Height;
                break;
        }
    }

    private static WindowGeometry.View ViewOf(Mode mode) => mode switch
    {
        Mode.Full => WindowGeometry.View.Full,
        Mode.Overall => WindowGeometry.View.Overall,
        _ => WindowGeometry.View.Widget,
    };

    /// <summary>A tab was clicked; show it and remember which.</summary>
    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            _model.Select(key);
            _config.FullTab = key;
            _config.Save();
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
        var full = new MenuItem();
        var reset = new MenuItem { Header = new Lang(_config.Lang)["reset_size"] };
        var close = new MenuItem { Header = new Lang(_config.Lang)["close"] };

        toggle.Click += (_, _) => SetMode(_isOverall ? Mode.Widget : Mode.Overall);
        full.Click += (_, _) => SetMode(_mode == Mode.Full ? Mode.Overall : Mode.Full);
        reset.Click += (_, _) => ResetSize();
        close.Click += (_, _) => Close();

        menu.Opened += (_, _) =>
        {
            var lang = new Lang(_config.Lang);
            toggle.Header = _isOverall ? lang["collapse"] : lang["expand_hint"];
            full.Header = _mode == Mode.Full ? lang["exit_fullscreen"] : lang["fullscreen"];
            reset.Header = lang["reset_size"];
            close.Header = lang["close"];
        };

        menu.Items.Add(toggle);
        menu.Items.Add(full);
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

        // Above the settings, because it is the way into the full view and
        // not a setting: F11 and a double-click are invisible to anyone who
        // has not been told about them.
        var fullData = new Button
        {
            Style = (Style)FindResource("SidebarButton"),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = "",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0),
                    },
                    new TextBlock
                    {
                        Text = lang["full_data"],
                        FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Tahoma"),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        fullData.Click += (_, _) => SetMode(Mode.Full);
        Sidebar.Children.Add(fullData);

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

        // Live while dragging: the Tk build redrew the whole canvas here and
        // destroyed the item the pointer had grabbed.
        Sidebar.Children.Add(Dial(lang["opacity"], 0.35, 1.0, _config.Opacity,
            value => $"{value * 100:F0}%",
            value =>
            {
                _config.Opacity = value;
                _model.ApplyTheme();
            }));

        Sidebar.Children.Add(Dial(lang["font_size"], 0.8, 1.6, _config.FontScale,
            value => $"{value * 100:F0}%",
            value =>
            {
                _config.FontScale = value;
                ApplyFontScale();
            }));

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
        Sidebar.Children.Add(Check(lang["cpu_temp"], _config.CpuTemperature, value =>
        {
            _config.CpuTemperature = value;
            _sampler.Nudge();
            Refresh();
        }));
        Sidebar.Children.Add(Check(lang["show_gpu"], _config.ShowGpu, value =>
        {
            _config.ShowGpu = value;
            _sampler.Nudge();
            Refresh();
        }));
        Sidebar.Children.Add(Check(lang["show_network"], _config.ShowNetwork, value =>
        {
            _config.ShowNetwork = value;
            Refresh();
        }));
        Sidebar.Children.Add(Check(lang["per_adapter"], _config.NetworkMode == "separated",
                                   value =>
        {
            _config.NetworkMode = value ? "separated" : "total";
            Refresh();
        }));

        Sidebar.Children.Add(Heading(lang["close_action"]));
        Sidebar.Children.Add(Choice(
            new[] { ("exit", lang["close_to_exit"]), ("tray", lang["close_to_tray"]) },
            _config.CloseAction,
            value =>
            {
                _config.CloseAction = value;
                _config.Save();
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
        if (_isOverall)
        {
            _model.UpdateOverall(snap);
        }
        else
        {
            _model.UpdateWidget(snap);
        }
        _config.Save();
    }

    /// <summary>
    /// A labelled slider that shows where it is. A bare track leaves the user
    /// guessing whether they are at 60% or 65%.
    /// </summary>
    private UIElement Dial(string text, double min, double max, double value,
                           Func<double, string> format, Action<double> onChange)
    {
        var readout = new TextBlock
        {
            Text = format(value),
            Style = (Style)FindResource("Label"),
            Foreground = _model.MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var caption = new TextBlock
        {
            Text = text.ToUpperInvariant(),
            Style = (Style)FindResource("Label"),
            Foreground = _model.LabelBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var header = new Grid { Margin = new Thickness(0, 10, 0, 4) };
        header.Children.Add(caption);
        header.Children.Add(readout);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Margin = new Thickness(0, 0, 0, 6),
        };
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = format(e.NewValue);
            onChange(e.NewValue);
        };
        // Saving on release, not on every pixel of the drag.
        slider.PreviewMouseUp += (_, _) => _config.Save();

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(slider);
        return panel;
    }

    /// <summary>
    /// Rewrite the type ramp. The styles take their sizes from these resources
    /// through DynamicResource, so everything that draws text follows.
    /// </summary>
    private void ApplyFontScale()
    {
        double scale = Math.Clamp(_config.FontScale, 0.8, 1.6);
        var ramp = new (string Key, double Size)[]
        {
            ("FontTiny", 9), ("FontSmall", 10), ("FontLabel", 10),
            ("FontBody", 11), ("FontValue", 14), ("FontHeading", 15),
        };
        foreach ((string key, double size) in ramp)
        {
            Application.Current.Resources[key] = Math.Round(size * scale, 1);
        }
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

    // ----------------------------------------------------------------- tray
    /// <summary>
    /// Put the widget behind a tray icon. The sampler keeps running -- it is
    /// the cheap half -- but the UI timer stops, because nothing it draws is
    /// on screen.
    /// </summary>
    private void HideToTray()
    {
        SavePlacement();
        _timer.Stop();
        _tray.Show(new Lang(_config.Lang), TrayTooltip());
        Hide();
        Diag.Write("hidden to tray");
    }

    private void RestoreFromTray()
    {
        _tray.Hide();
        Show();
        Activate();
        _timer.Start();
        OnTick(this, EventArgs.Empty);
    }

    /// <summary>
    /// What the tray icon says on hover. While the window is away this is the
    /// only thing still reporting, so it carries the headline figures.
    /// </summary>
    private string TrayTooltip()
    {
        Snapshot snap = _sampler.Current;
        if (!snap.Ready)
        {
            return "SysMonitor";
        }
        string text = $"SysMonitor\nCPU {snap.CpuTotal}%  ·  RAM {snap.Ram.Usage}%";
        return snap.CpuTemp is int temp
            ? text + $"  ·  {Palette.TempText(temp, snap.CpuTempEstimated)}"
            : text;
    }

    // ------------------------------------------------------------ shutdown
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The close button means whichever of the two the user chose.
        if (!_exiting && _config.CloseAction == "tray")
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _closing = true;
        _timer.Stop();
        _heartbeat.Stop();
        SavePlacement();
        _sampler.Stop();
        _tray.Dispose();
        base.OnClosing(e);
    }
}
