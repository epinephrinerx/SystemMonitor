using System.Diagnostics;
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
    private bool _rotationPaused;
    private Rect _dragWorkArea;
    private readonly ScaleTransform _contentTransform = new(1, 1);
    private readonly ScaleTransform _badgeTransform = new(1, 1);
    private readonly ScaleTransform _overallTransform = new(1, 1);
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
        // The widget's own remembered spot beats the legacy global one.
        RestoreViewPosition(Mode.Widget);
        BuildSidebar();

        CloseButton.Click += (_, _) => Close();
        WidgetButton.Click += (_, _) => SetMode(Mode.Widget);
        FullScreenButton.Click += (_, _) => SetMode(Mode.Full);
        WidgetCloseButton.Click += (_, _) => Close();
        WidgetPauseButton.Click += (_, _) => ToggleRotationPause();
        WidgetPrevButton.Click += (_, _) => Step(-1);
        WidgetNextButton.Click += (_, _) => Step(1);
        FullCloseButton.Click += (_, _) => Close();
        FullOverallButton.Click += (_, _) => SetMode(Mode.Overall);
        FullWidgetButton.Click += (_, _) => SetMode(Mode.Widget);
        KeyDown += OnKeyDown;

        // Windows can end the session without the window ever being asked to
        // close -- a shutdown or logoff while the widget sits in the tray --
        // so the placement goes to disk on the session's way out as well.
        Application.Current.SessionEnding += (_, _) => SavePlacement();

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
        Loaded += async (_, _) => await CheckForUpdateOnStart();
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
        ApplyWidgetScale();
        ApplyOverallScale();
    }

    /// <summary>
    /// The widget's content draws at whatever size the panel dictates: the
    /// reference is the default widget, so a bigger widget draws everything
    /// bigger and a smaller one everything smaller, while the buttons keep
    /// their fixed size outside the scaled layer. The paused badge rides
    /// inside the layer, so it counter-scales to stay readable -- which also
    /// means the title's reserved space for it and the side margins are
    /// written in screen pixels and divided by the scale here.
    /// </summary>
    private void ApplyWidgetScale()
    {
        if (_mode != Mode.Widget)
        {
            return;
        }
        double scale = WindowGeometry.ContentScale(
            WindowGeometry.Panel(Width, Height),
            WindowGeometry.WidgetReference);
        _contentTransform.ScaleX = _contentTransform.ScaleY = scale;
        _badgeTransform.ScaleX = _badgeTransform.ScaleY = 1 / scale;
        WidgetContentHost.LayoutTransform = _contentTransform;
        RotationPausedBadge.LayoutTransform = _badgeTransform;
        UpdateWidgetTitleMargin(scale);
        UpdateWidgetSideMargins(scale);
    }

    private double CurrentWidgetScale() =>
        WindowGeometry.ContentScale(WindowGeometry.Panel(Width, Height),
                                    WindowGeometry.WidgetReference);

    /// <summary>Screen-constant space for the screen-constant badge.</summary>
    private void UpdateWidgetTitleMargin(double scale)
    {
        WidgetTitle.Margin = _rotationPaused
            ? new Thickness(72 / scale, 0, 96, 0)
            : new Thickness(0, 0, 96, 0);
    }

    /// <summary>
    /// The step buttons' chips reach a fixed 14px into the content area;
    /// the content's side margin is written in scaled units, so it is
    /// widened by the scale to keep its 16 screen pixels.
    /// </summary>
    private void UpdateWidgetSideMargins(double scale)
    {
        var side = new Thickness(16 / scale, 0, 16 / scale, 0);
        WidgetMessage.Margin = side;
        WidgetContentPanel.Margin = side;
    }

    /// <summary>
    /// The overall view draws at whatever size its window is, measured
    /// against the default overall panel -- the same rule the widget uses,
    /// applied to the whole view: sidebar, corner buttons and content scale
    /// together.
    /// </summary>
    private void ApplyOverallScale()
    {
        if (_mode != Mode.Overall)
        {
            return;
        }
        double scale = WindowGeometry.ContentScale(
            WindowGeometry.Panel(Width, Height),
            WindowGeometry.OverallReference);
        _overallTransform.ScaleX = _overallTransform.ScaleY = scale;
        OverallView.LayoutTransform = _overallTransform;
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
            bool rotate = ShouldRotate(_mode == Mode.Widget, _model.ViewCount,
                                       _rotationPaused, _lastRotate,
                                       DateTime.UtcNow, RotateEvery);
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
                _model.UpdateAlerts(snap);
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

    /// <summary>
    /// Whether this tick should advance the widget's rotation. Kept static
    /// and pure so the pause rules can be tested with invented times rather
    /// than a live clock: a paused widget holds its page however long the
    /// user stares at it, and stepping while paused changes the page without
    /// ending the pause.
    /// </summary>
    internal static bool ShouldRotate(bool inWidgetMode, int viewCount, bool paused,
                                      DateTime lastRotate, DateTime now, TimeSpan every)
    {
        return !paused && inWidgetMode && viewCount > 1 && now - lastRotate >= every;
    }

    private void ToggleRotationPause()
    {
        _rotationPaused = !_rotationPaused;
        if (!_rotationPaused)
        {
            // Resuming starts the interval over: the stale _lastRotate would
            // otherwise jump the page on the very next tick, straight after
            // the user asked to keep looking at this one.
            _lastRotate = DateTime.UtcNow;
        }
        ApplyRotationPauseState();
    }

    /// <summary>Icon, tooltip and badge follow the pause state and language.</summary>
    private void ApplyRotationPauseState()
    {
        var lang = new Lang(_config.Lang);
        WidgetPauseButton.Content = _rotationPaused ? "\uE768" : "\uE769";
        WidgetPauseButton.ToolTip = _rotationPaused ? lang["resume_rotation"]
                                                    : lang["pause_rotation"];
        // The badge lives inside the title row, so it takes real space when
        // shown: the title steps right rather than being drawn over.
        RotationPausedBadge.Text = lang["paused"];
        RotationPausedBadge.Visibility = _rotationPaused ? Visibility.Visible
                                                         : Visibility.Collapsed;
        UpdateWidgetTitleMargin(CurrentWidgetScale());
    }

    // ----------------------------------------------------------- mode switch
    private void SetMode(Mode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        // Each view keeps its own spot: note where this one sits before the
        // window changes size or shape for the next view.
        SavePlacement();

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

        // Each view returns to its own remembered spot, so a bigger view
        // closing last never drags the widget from where the user put it.
        // The full-screen exit's _beforeFull fallback applies when the view
        // has never been placed.
        bool restored = RestoreViewPosition(mode);
        if (mode != Mode.Full || restored)
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
            // DesiredSize carries the layout transform: divide it back out,
            // or the fitted size compounds the scale every time the view
            // opens and the window drifts a little more each round.
            double scale = WindowGeometry.ContentScale(
                WindowGeometry.Panel(Width, Height), WindowGeometry.OverallReference);
            Size wanted = new(OverallView.DesiredSize.Width / scale,
                              OverallView.DesiredSize.Height / scale);
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
            ApplyOverallScale();
            KeepOnScreen();
        });
    }

    private void ResetSize()
    {
        _config.OverallSized = false;
        _config.FullW = 0;
        _config.FullH = 0;
        _config.WidgetW = WindowGeometry.WidgetReference.Width;
        _config.WidgetH = WindowGeometry.WidgetReference.Height;
        _config.OverallW = WindowGeometry.OverallReference.Width;
        _config.OverallH = WindowGeometry.OverallReference.Height;
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
        // The work area is judged once per drag, not per frame: the widget
        // grows against the screen it started on even if the pointer crosses
        // to another monitor mid-drag.
        _dragWorkArea = WorkArea(Left + Width / 2, Top + Height / 2);
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

    /// <summary>
    /// Resize the window, and nothing else.
    ///
    /// Dragging the full view below the size its tabs need used to switch it
    /// to the overall view, which meant a drag could change what you were
    /// looking at: you reached for a corner to make the window a little
    /// smaller and the contents were replaced under your hand. Which view is
    /// showing is a decision only a button makes now, and a drag that runs out
    /// of room simply stops at the view's own minimum.
    /// </summary>
    private void Resize(Vector delta)
    {
        (var min, var max) = WindowGeometry.Limits(ViewOf(_mode));

        Rect window = _mode == Mode.Widget
            ? WindowGeometry.ResizeWidget(
                  _resizeOrigin, _resizing, delta, min, max, _dragWorkArea)
            : WindowGeometry.Resize(_resizeOrigin, _resizing, delta, min, max);
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
        ApplyWidgetScale();
        ApplyOverallScale();
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
        // The view's own spot first: switching views, or closing from one,
        // must not drag the other views' remembered places around.
        switch (_mode)
        {
            case Mode.Widget:
                _config.WidgetPosX = Left;
                _config.WidgetPosY = Top;
                break;
            case Mode.Overall:
                _config.OverallPosX = Left;
                _config.OverallPosY = Top;
                break;
            case Mode.Full:
                _config.FullPosX = Left;
                _config.FullPosY = Top;
                break;
        }
        _config.PosX = Left;
        _config.PosY = Top;
        _config.Save();
    }

    /// <summary>
    /// Put the window back where this view was last seen. False when the
    /// view has no remembered spot, leaving whatever placement is current.
    /// </summary>
    private bool RestoreViewPosition(Mode mode)
    {
        (double? X, double? Y) pos = mode switch
        {
            Mode.Widget => (_config.WidgetPosX, _config.WidgetPosY),
            Mode.Overall => (_config.OverallPosX, _config.OverallPosY),
            _ => (_config.FullPosX, _config.FullPosY),
        };
        if (pos.X is double x && pos.Y is double y)
        {
            Left = x;
            Top = y;
            return true;
        }
        return false;
    }

    // -------------------------------------------------------- context menu
    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        FillWindowMenu(menu);
        ContextMenu = menu;
    }

    /// <summary>
    /// The window's own right-click items. Built fresh each time, because a
    /// menu item belongs to one menu and the drive menus end with these too.
    /// </summary>
    private void FillWindowMenu(ContextMenu menu)
    {
        var toggle = new MenuItem();
        var full = new MenuItem();
        var reset = new MenuItem { Header = new Lang(_config.Lang)["reset_size"] };
        var restart = new MenuItem { Header = new Lang(_config.Lang)["restart"] };
        var close = new MenuItem { Header = new Lang(_config.Lang)["close"] };

        toggle.Click += (_, _) => SetMode(_isOverall ? Mode.Widget : Mode.Overall);
        full.Click += (_, _) => SetMode(_mode == Mode.Full ? Mode.Overall : Mode.Full);
        reset.Click += (_, _) => ResetSize();
        restart.Click += (_, _) => Restart();
        close.Click += (_, _) => Close();

        menu.Opened += (_, _) =>
        {
            var lang = new Lang(_config.Lang);
            toggle.Header = _isOverall ? lang["collapse"] : lang["expand_hint"];
            full.Header = _mode == Mode.Full ? lang["exit_fullscreen"] : lang["fullscreen"];
            reset.Header = lang["reset_size"];
            restart.Header = lang["restart"];
            close.Header = lang["close"];
        };

        menu.Items.Add(toggle);
        menu.Items.Add(full);
        menu.Items.Add(reset);
        menu.Items.Add(restart);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
    }

    // ------------------------------------------------------------- sidebar
    /// <summary>
    /// The header icons say where they go only through their tooltips, and
    /// the language can change under them, so the text is (re)set wherever
    /// the sidebar is rebuilt -- on startup and on every language change.
    /// </summary>
    private void UpdateHeaderTooltips(Lang lang)
    {
        FullScreenButton.ToolTip = lang["full_data"];
        WidgetButton.ToolTip = lang["open_widget"];
        FullOverallButton.ToolTip = lang["go_overall"];
        FullWidgetButton.ToolTip = lang["open_widget"];
    }

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
        ForgetMirrors(SidebarGroup);
        UpdateHeaderTooltips(lang);
        ApplyRotationPauseState();

        // Above the settings, because it is the way into the full view and
        // not a setting: F11 and a double-click are invisible to anyone who
        // has not been told about them.
        Sidebar.Children.Add(NavButton("\uE9D9", lang["full_data"],
            () => SetMode(Mode.Full)));
        Sidebar.Children.Add(NavButton("\uE745", lang["open_widget"],
            () => SetMode(Mode.Widget)));

        // The full view's tab rail carries its own pair, so the way back is
        // printed where the tabs are rather than only in the corner icons.
        FullNav.Children.Clear();
        FullNav.Children.Add(NavButton("\uE80F", lang["go_overall"],
            () => SetMode(Mode.Overall)));
        FullNav.Children.Add(NavButton("\uE745", lang["open_widget"],
            () => SetMode(Mode.Widget)));

        Sidebar.Children.Add(Heading(lang["window_settings"]));
        AddWindowSettings(Sidebar, lang, everything: false);

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
        Sidebar.Children.Add(Check(lang["show_network_drives"], _config.IncludeNetwork,
                                   value =>
        {
            _config.IncludeNetwork = value;
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

        // The radio groups and the rest of the window settings live in the
        // settings window; this button is the way to it.
        Button settings = NavButton("\uE713", lang["settings"], OpenSettings);
        settings.Margin = new Thickness(0, 12, 0, 0);
        Sidebar.Children.Add(settings);

        Sidebar.Children.Add(Heading(lang["updates"]));
        Sidebar.Children.Add(Check(lang["auto_check_updates"], _config.AutoCheckUpdates,
                                   value => _config.AutoCheckUpdates = value));
        Sidebar.Children.Add(BuildUpdatePanel(lang));

        var restartLabel = new TextBlock
        {
            Text = lang["restart"],
            TextWrapping = TextWrapping.Wrap,
        };
        FollowRamp(restartLabel, TextBlock.FontSizeProperty, "FontNav");
        var restart = new Button
        {
            Content = restartLabel,
            Style = (Style)FindResource("SidebarButton"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        restart.Click += (_, _) => Restart();
        Sidebar.Children.Add(restart);

        Sidebar.Children.Add(NavButton("\uE897", lang["manual"], OpenManual));
        Sidebar.Children.Add(NavButton("\uE946", lang["about"], OpenAbout));

        RebuildSidePanels();
    }

    // --------------------------------------------------------------- updates
    /// <summary>
    /// The update corner: what version this is, a button to look for a newer
    /// one, and a line saying how that went.
    ///
    /// One button that changes what it does -- check, then download -- rather
    /// than two, one of which is meaningless until the other has been pressed.
    /// </summary>
    private UIElement BuildUpdatePanel(Lang lang)
    {
        var panel = new StackPanel();

        var version = new TextBlock
        {
            Text = $"{lang["current_version"]} {Updater.Current}",
            Foreground = _model.MutedBrush,
            FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Tahoma"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        FollowRamp(version, TextBlock.FontSizeProperty, "FontBody");
        panel.Children.Add(version);

        var status = new TextBlock
        {
            Foreground = _model.MutedBrush,
            FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Tahoma"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        FollowRamp(status, TextBlock.FontSizeProperty, "FontBody");

        var buttonLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };
        FollowRamp(buttonLabel, TextBlock.FontSizeProperty, "FontNav");
        var button = new Button
        {
            Content = buttonLabel,
            Style = (Style)FindResource("SidebarButton"),
        };
        buttonLabel.Text = lang["check_updates"];
        if (_pendingUpdate is not null)
        {
            // Found by the check at start-up (or an earlier press): the panel
            // is rebuilt on a language change and must not forget it.
            status.Text = $"{lang["update_found"]} {_pendingUpdate.Version}";
            status.Visibility = Visibility.Visible;
            buttonLabel.Text = lang["download_install"];
        }
        button.Click += async (_, _) => await RunUpdateStep(button, status, lang, buttonLabel);

        panel.Children.Add(button);
        panel.Children.Add(status);
        return panel;
    }

    /// <summary>The release the last check found, waiting to be downloaded.</summary>
    private Updater.Release? _pendingUpdate;

    /// <summary>
    /// Look for a newer release every time the program opens.
    ///
    /// Silent by design: no dialog and no download -- a newer version only
    /// shows up in the update corner of the sidebar, where the button installs
    /// it. A failed check says nothing either; at logon the network is often
    /// not up yet, so it is tried once more a minute later.
    /// </summary>
    private async Task CheckForUpdateOnStart()
    {
        if (!_config.AutoCheckUpdates)
        {
            return;
        }
        try
        {
            Updater.Release? release = await Updater.CheckAsync();
            if (release is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(60));
                // Switched off during the wait: honour it before the retry.
                if (!_config.AutoCheckUpdates)
                {
                    return;
                }
                release = await Updater.CheckAsync();
            }

            // A press of the button may have found it first.
            if (release is null || release.Version <= Updater.Current
                || _pendingUpdate is not null)
            {
                return;
            }
            _pendingUpdate = release;
            Diag.Write($"update available {release.Version}");
            BuildSidebar();
        }
        catch (Exception error)
        {
            Diag.ReportException("Update check at start", error);
        }
    }

    private async Task RunUpdateStep(Button button, TextBlock status, Lang lang,
                                     TextBlock buttonLabel)
    {
        button.IsEnabled = false;
        status.Visibility = Visibility.Visible;

        try
        {
            if (_pendingUpdate is null)
            {
                status.Text = lang["checking"];
                Updater.Release? release = await Updater.CheckAsync();

                if (release is null)
                {
                    status.Text = lang["check_failed"];
                }
                else if (release.Version <= Updater.Current)
                {
                    status.Text = lang["up_to_date"];
                }
                else
                {
                    _pendingUpdate = release;
                    status.Text = $"{lang["update_found"]} {release.Version}";
                    buttonLabel.Text = lang["download_install"];
                }
                return;
            }

            var progress = new Progress<double>(fraction =>
                status.Text = $"{lang["downloading"]} {fraction:P0}");
            status.Text = lang["downloading"];

            string? installer = await Updater.DownloadAsync(_pendingUpdate, progress);
            if (installer is null)
            {
                status.Text = lang["download_failed"];
                _pendingUpdate = null;
                buttonLabel.Text = lang["check_updates"];
                return;
            }

            status.Text = lang["installing"];
            Diag.Write($"launching installer {installer}");

            // The installer replaces this program's own files, so it cannot do
            // its work while we hold them open. Hand over and go.
            Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
            _config.Save();
            Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            Diag.ReportException("Update", error);
            status.Text = lang["check_failed"];
        }
        finally
        {
            button.IsEnabled = true;
        }
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
                           Func<double, string> format, Action<double> onChange,
                           string? key = null)
    {
        string group = _group;
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
        Register(key, v => slider.Value = (double)v);
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = format(e.NewValue);
            if (_mirroring)
            {
                return;     // set from its twin; the twin already ran the handler
            }
            onChange(e.NewValue);
            Mirror(key, group, e.NewValue);
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
        FontRamp.Apply(Application.Current.Resources, _config.FontScale);
    }

    /// <summary>
    /// Size an element's text from the type ramp rather than a literal, so the
    /// font-size slider reaches it. Controls built in code have no style to do
    /// this for them.
    /// </summary>
    private static void FollowRamp(FrameworkElement element, DependencyProperty property,
                                   string key) =>
        element.SetResourceReference(property, key);

    private TextBlock Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Style = (Style)FindResource("Label"),
        Foreground = _model.LabelBrush,
        Margin = new Thickness(0, 10, 0, 6),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>
    /// A sidebar-style navigation button: an MDL2 glyph beside a two-language
    /// label that wraps. Shared by the overall sidebar and the full view's
    /// tab rail, which is why it takes its click target with it. The pair
    /// sits in a fixed Auto/star grid rather than a horizontal stack --
    /// a stack hands its children unlimited width, and a label that can
    /// never run out of room never wraps.
    /// </summary>
    private Button NavButton(string glyph, string text, Action onClick)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var label = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Tahoma"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        FollowRamp(icon, TextBlock.FontSizeProperty, "FontNavIcon");
        FollowRamp(label, TextBlock.FontSizeProperty, "FontNav");
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(label, 1);
        var button = new Button
        {
            Style = (Style)FindResource("SidebarButton"),
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                },
                Children = { icon, label },
            },
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private CheckBox Check(string text, bool value, Action<bool> onChange, string? key = null)
    {
        string group = _group;
        var box = new CheckBox
        {
            // A TextBlock rather than a plain string: the check box's own
            // presenter never wraps, and Thai labels run past the sidebar's
            // fixed width.
            Content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
            },
            IsChecked = value,
            Foreground = _model.MutedBrush,
            FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Tahoma"),
            Margin = new Thickness(0, 3, 0, 3),
        };
        FollowRamp(box, Control.FontSizeProperty, "FontBody");
        Register(key, v => box.IsChecked = (bool)v);
        box.Checked += (_, _) => Toggled(true);
        box.Unchecked += (_, _) => Toggled(false);
        return box;

        void Toggled(bool on)
        {
            if (_mirroring)
            {
                return;     // set from its twin; the twin already ran the handler
            }
            onChange(on);
            Mirror(key, group, on);
        }
    }

    private UIElement Choice((string Key, string Text)[] options, string selected,
                             Action<string> onChange)
    {
        // A wrap panel, not a stack: a row of Thai option labels is wider
        // than the sidebar at the default width, and a stack panel would let
        // it run straight out of the column.
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach ((string key, string text) in options)
        {
            var button = new RadioButton
            {
                Content = text,
                IsChecked = key == selected,
                Foreground = _model.MutedBrush,
                FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Tahoma"),
                Margin = new Thickness(0, 0, 10, 0),
                GroupName = string.Join("-", options.Select(o => o.Key)),
            };
            FollowRamp(button, Control.FontSizeProperty, "FontBody");
            button.Checked += (_, _) => onChange(key);
            panel.Children.Add(button);
        }
        return panel;
    }

    /// <summary>
    /// Start a fresh copy and stand down.
    ///
    /// Settings are written first, so the new process reads the state this one
    /// was in rather than whatever was last saved. The two overlap for a
    /// moment, which is why the old one shuts down immediately afterwards
    /// rather than waiting to be closed: the new one is waiting on the
    /// single-instance lock the old one still holds.
    /// </summary>
    private void Restart()
    {
        if (Environment.ProcessPath is not string exe)
        {
            return;     // a hosted run has no executable of its own to start
        }

        SavePlacement();
        _config.Save();
        Diag.Write("restarting");

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                // Fully qualified: an unqualified Path here is
                // System.Windows.Shapes.Path, which the resize grip uses.
                WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? string.Empty,
            });
        }
        catch (Exception error)
        {
            // Nothing started, so there is nothing to hand over to: stay up.
            Diag.ReportException("Restart", error);
            return;
        }

        _closing = true;        // skip the close-to-tray path on the way out
        Application.Current.Shutdown();
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

        // The shell only writes the icon's settings entry once it has seen the
        // icon, so this has to come after Show -- and on the very first hide
        // the entry may still not be there, in which case the next one gets it.
        if (!_config.TrayPromoted && Environment.ProcessPath is string exe)
        {
            _config.TrayPromoted = TrayPromotion.Promote(exe);
        }

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
