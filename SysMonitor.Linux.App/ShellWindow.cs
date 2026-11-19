using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sampling;
using SysMonitor.Model;
using Path = Avalonia.Controls.Shapes.Path;

namespace SysMonitor.Linux.App;

/// <summary>
/// The one window (spec sections 0-3): three stacked views, a borderless
/// transparent shell with a 12 px shadow pad, custom drag and resize, and the
/// 400 ms redraw tick.
///
/// Placement, pointer handling and timers live here; everything the views show
/// comes from <see cref="ShellModel"/>.
/// </summary>
internal sealed class ShellWindow : Window
{
    private enum DragMode
    {
        None,
        Pending,
        Move,
        Resize,
    }

    private readonly ShellModel _shell;
    private readonly StartupOptions _options;
    private readonly Diagnostics _diagnostics;
    private readonly IRestarter _restarter = new NoopRestarter();
    private readonly ITray _tray = new NoopTray();

    private readonly WidgetView _widget;
    private readonly OverallView _overall;
    private readonly FullView _full;
    private readonly Border _panel;
    private readonly Path _grip;
    private readonly Grid _widgetCorner = new();
    private readonly ContextMenu _mainMenu = new();
    private bool _pointerOver;

    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _heartbeat;
    private readonly CancellationTokenSource _stop = new();
    private readonly AutoResetEvent _wake = new(false);

    private readonly Placement _placement = new();
    private readonly Dictionary<string, PanelWindow> _panels = new(StringComparer.Ordinal);

    private Snapshot? _latest;
    private Snapshot? _rendered;
    private ViewMode _lastMode;
    private bool _applyingRoute;
    private double _appliedFontScale;
    private volatile bool _memoryVisible;
    private int _forceMemory;

    // Pointer state. Every resize frame is computed from _startRect, never from
    // the previous frame (spec section 1).
    private DragMode _drag;
    private ResizeEdge _edge;
    private PointD _pressScreen;
    private RectD _startRect;
    private double _dragScale = 1;

    public ShellWindow(ShellModel shell, StartupOptions options, Diagnostics diagnostics)
    {
        _shell = shell;
        _options = options;
        _diagnostics = diagnostics;
        _lastMode = shell.View.Mode;
        _appliedFontScale = shell.Config.FontScale;

        Title = "System Monitor";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = shell.Config.AlwaysOnTop;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        TransparencyBackgroundFallback = Brushes.Transparent;
        FontFamily = Ui.Family;
        WindowStartupLocation = WindowStartupLocation.Manual;
        RequestedThemeVariant = App.ThemeFor(shell.Config.Theme);

        SizeD initial = WindowGeometry.WindowSize(
            WindowGeometry.PanelSize(_lastMode, shell.Config));
        Width = initial.Width;
        Height = initial.Height;

        _panel = new Border
        {
            Margin = new Thickness(WindowGeometry.ShadowPad),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
        };

        _widget = new WidgetView(shell);
        _overall = new OverallView(shell, Dispatch);
        _full = new FullView(shell, Dispatch);

        var views = new Grid();
        views.Children.Add(_widget);
        views.Children.Add(_overall);
        views.Children.Add(_full);
        _panel.Child = views;

        _grip = new Path
        {
            Data = Avalonia.Media.Geometry.Parse("M0,14 L14,0 M5,14 L14,5 M10,14 L14,10"),
            Width = 14,
            Height = 14,
            Stretch = Stretch.None,
            StrokeThickness = 1.4,
            Opacity = 0.65,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 15, 15),
        };

        var root = new Grid { Background = Brushes.Transparent };
        // Attached to the root, so a right-click anywhere without its own menu
        // gets the main one, at the pointer, rebuilt in the current language.
        _mainMenu.Opening += (_, _) => FillMainMenu();
        root.ContextMenu = _mainMenu;
        root.Children.Add(_panel);
        root.Children.Add(_grip);

        // The Widget's close and pause buttons sit in the shadow pad, above the
        // panel. A Border clips its child to its rounded corner, so they live in
        // the window rather than inside the widget view.
        _widgetCorner.HorizontalAlignment = HorizontalAlignment.Right;
        _widgetCorner.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(_widgetCorner);
        Content = root;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _tick.Tick += (_, _) => OnTick();
        _heartbeat = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, options.HeartbeatSeconds)),
        };
        _heartbeat.Tick += (_, _) =>
            _diagnostics.Heartbeat(Volatile.Read(ref _latest), _shell.View.Mode.ToKey());

        // Model events -> window.
        shell.DataChanged += OnDataChanged;
        shell.StructureChanged += OnStructureChanged;
        shell.AppearanceChanged += OnAppearanceChanged;
        shell.RouteChanged += OnRouteChanged;
        shell.TopmostChanged += () => Topmost = shell.Config.AlwaysOnTop;
        shell.NudgeRequested += () => _wake.Set();
        shell.MemoryRefreshRequested += () =>
        {
            Interlocked.Exchange(ref _forceMemory, 1);
            _wake.Set();
        };
        shell.OpenRequested += OpenPanel;
        shell.RestartRequested += () => _restarter.Restart();
        shell.CloseRequested += Close;

        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        SizeChanged += (_, _) => UpdateScales();
        KeyDown += OnKeyDown;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerExited += (_, _) =>
        {
            _pointerOver = false;
            UpdateWidgetCorner();
        };
        PointerEntered += (_, _) =>
        {
            _pointerOver = true;
            UpdateWidgetCorner();
        };

        ApplyMode(_lastMode);
        UpdateMemoryVisibility();
        UpdateBrushes();
        UpdateWidgetCorner();
        _widget.Render();
        _overall.RenderSidebar();
        _overall.RenderCards();
        _full.Render();
    }

    // ------------------------------------------------------------- startup

    private void OnOpened(object? sender, EventArgs e)
    {
        ViewMode mode = _shell.View.Mode;
        _lastMode = mode;
        ApplyMode(mode);

        SizeD panel = SizeFor(mode);
        SizeD window = WindowGeometry.WindowSize(panel);
        RectD rect;
        if (_shell.Config.PosX is { } x && _shell.Config.PosY is { } y)
        {
            rect = new RectD(x, y, window.Width, window.Height);
        }
        else
        {
            RectD work = PrimaryWorkArea();
            PointD at = WindowGeometry.FirstRunPosition(work, window);
            rect = new RectD(at.X, at.Y, window.Width, window.Height);
        }
        rect = WindowGeometry.KeepOnScreen(rect, WorkAreaAt(rect.TopLeft));
        ApplyRect(rect);
        UpdateScales();

        _tick.Start();
        _heartbeat.Start();
        _ = Task.Run(SampleLoop);

        _diagnostics.Line(
            $"window opened {window.Width}x{window.Height} at {rect.X},{rect.Y} " +
            $"render_scale={RenderScaling} platform={PlatformImpl?.GetType().Name ?? "?"} " + DescribeScreens());
        if (_shell.Config.AutoCheckUpdates)
        {
            _ = AutoCheckAsync();
        }
    }

    private async Task AutoCheckAsync()
    {
        await _shell.CheckUpdatesAsync();
        if (_shell.Updates.State == UpdatePanelState.Phase.CheckFailed
            && _shell.Config.AutoCheckUpdates)
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
            if (_shell.Config.AutoCheckUpdates)
            {
                await _shell.CheckUpdatesAsync();
            }
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SavePlacement();
        _tick.Stop();
        _heartbeat.Stop();
        _stop.Cancel();
        _wake.Set();
        foreach (PanelWindow panel in _panels.Values.ToList())
        {
            panel.Close();
        }
        _diagnostics.Line("closing");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // No pidfd may outlive the window: close whatever a pending kill holds.
        _shell.CancelKills();
        _shell.DataChanged -= OnDataChanged;
        _shell.StructureChanged -= OnStructureChanged;
        _shell.AppearanceChanged -= OnAppearanceChanged;
        _shell.RouteChanged -= OnRouteChanged;
    }

    // ---------------------------------------------------------- sampling

    private void SampleLoop()
    {
        var sampler = new LinuxSampler();
        long lastProcess = 0;
        long lastService = 0;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                long now = Environment.TickCount64;
                bool forced = Interlocked.Exchange(ref _forceMemory, 0) != 0;
                MemoryCadence.Plan plan = MemoryCadence.Next(
                    now, lastProcess, lastService, _memoryVisible, forced);
                Snapshot snapshot = sampler.Sample(
                    plan.Processes, plan.Services, keepMemory: _memoryVisible);
                if (plan.Processes)
                {
                    lastProcess = now;
                }
                if (plan.Services)
                {
                    lastService = now;
                }
                Volatile.Write(ref _latest, snapshot);
            }
            catch (Exception exception)
            {
                _diagnostics.Line("sample failed: " + exception.Message);
            }

            int interval = Math.Max(250, _shell.Config.MetricsIntervalMs);
            _wake.WaitOne(interval);
        }
    }

    /// <summary>
    /// Read processes and services only while the RAM tab shows. Coming back
    /// to it forces one read instead of waiting out the cadence.
    /// </summary>
    private void UpdateMemoryVisibility()
    {
        bool visible = _shell.FullRamVisible;
        bool gained = visible && !_memoryVisible;
        _memoryVisible = visible;
        if (visible && (gained || _shell.Snapshot?.Processes.Count is null or 0))
        {
            Interlocked.Exchange(ref _forceMemory, 1);
            _wake.Set();
        }
    }

    private void OnTick()
    {
        Snapshot? latest = Volatile.Read(ref _latest);
        if (!ReferenceEquals(latest, _rendered))
        {
            _rendered = latest;
            _shell.SetSnapshot(latest);
        }
        _shell.Tick();
    }

    // ------------------------------------------------------------ events

    private void OnDataChanged()
    {
        _widget.Render();
        _overall.RenderCards();
        _full.RenderData();
        UpdateMemoryVisibility();
        UpdateWidgetCorner();
    }

    private void OnStructureChanged()
    {
        if (_applyingRoute)
        {
            return;
        }
        _full.Render();
        _widget.Render();
        _overall.RenderSidebar();
        _overall.RenderCards();
        UpdateWidgetCorner();
        UpdateMemoryVisibility();
        _appliedFontScale = _shell.Config.FontScale;
        RequestedThemeVariant = App.ThemeFor(_shell.Config.Theme);
        UpdateBrushes();
        foreach (PanelWindow panel in _panels.Values)
        {
            panel.Render();
        }
    }

    private void OnAppearanceChanged()
    {
        UpdateBrushes();
        SyncDials();
        UpdateWidgetCorner();
        ScaleFonts();
    }

    /// <summary>Opacity only touches the two background brushes (spec section 1).</summary>
    /// <summary>Rebuild the Widget's two corner buttons in the current state.</summary>
    private void UpdateWidgetCorner()
    {
        _widgetCorner.Children.Clear();
        if (_shell.View.Mode != ViewMode.Widget)
        {
            return;
        }

        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        Border close = Ui.WindowIcon(
            IconName.Close,
            ramp.Tiny,
            palette.Label,
            () => _shell.Close(),
            _shell.Language["close"]);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        // Panel edge is 12 in; the button's right edge is 8 from the window.
        close.Margin = new Thickness(0, 0, 8, 0);
        close.Opacity = _pointerOver ? 1.0 : 0.35;
        _widgetCorner.Children.Add(close);

        Border pause = Ui.WindowIcon(
            _shell.Paused ? IconName.Play : IconName.Pause,
            ramp.Small,
            palette.Label,
            () => _shell.TogglePause(),
            _shell.PauseTooltip);
        pause.HorizontalAlignment = HorizontalAlignment.Right;
        pause.VerticalAlignment = VerticalAlignment.Top;
        pause.Margin = new Thickness(0, 0, 30, 0);
        pause.Opacity = _pointerOver ? 1.0 : 0.35;
        _widgetCorner.Children.Add(pause);
    }

    private void UpdateBrushes()
    {
        Palette palette = _shell.Palette;
        _panel.Background = Ui.Brush(palette.PanelBrush(_shell.Config.Opacity));
        _panel.BorderBrush = Ui.Brush(palette.Border);
        _grip.Stroke = Ui.Brush(palette.Label);
        _overall.SetOpacity(_shell.Config.Opacity);
    }

    private void SyncDials()
    {
        foreach (DialView dial in _overall.Dials)
        {
            dial.SetValue(_shell.DialValue(dial.Key));
        }
        foreach (PanelWindow panel in _panels.Values)
        {
            panel.SyncDials();
        }
    }

    /// <summary>
    /// The font scale rewrites the type ramp. Rather than rebuild (which would
    /// destroy the dial under the pointer), scale the live text by the ratio.
    /// A later rebuild reads the new sizes from the ramp anyway.
    /// </summary>
    private void ScaleFonts()
    {
        double scale = _shell.Config.FontScale;
        if (Math.Abs(scale - _appliedFontScale) < 0.001)
        {
            return;
        }
        double ratio = scale / _appliedFontScale;
        _appliedFontScale = scale;
        ScaleText(_panel, ratio);
        foreach (PanelWindow panel in _panels.Values)
        {
            if (panel.Content is Visual content)
            {
                ScaleText(content, ratio);
            }
        }
    }

    private static void ScaleText(Visual root, double ratio)
    {
        if (root is TextBlock text)
        {
            text.FontSize = Math.Max(1, text.FontSize * ratio);
        }
        foreach (Visual child in root.GetVisualChildren())
        {
            ScaleText(child, ratio);
        }
    }

    private void OnRouteChanged()
    {
        if (_applyingRoute)
        {
            return;
        }
        _applyingRoute = true;
        try
        {
            SavePlacement();
            ViewMode from = _lastMode;
            ViewMode to = _shell.View.Mode;

            ApplyMode(to);
            _full.Render();
            _widget.Render();
            _overall.RenderSidebar();
            _overall.RenderCards();
            UpdateWidgetCorner();
            UpdateMemoryVisibility();
            _appliedFontScale = _shell.Config.FontScale;

            RectD current = CurrentRect();
            PointD origin = _placement.ApplySwitch(from, to, current);
            SizeD panel = SizeFor(to);
            SizeD window = WindowGeometry.WindowSize(panel);
            RectD rect = new(origin.X, origin.Y, window.Width, window.Height);
            rect = WindowGeometry.KeepOnScreen(rect, WorkAreaAt(rect.TopLeft));
            ApplyRect(rect);

            _lastMode = to;
            UpdateScales();
            UpdateBrushes();
            SavePlacement();
        }
        finally
        {
            _applyingRoute = false;
        }
    }

    private void ApplyMode(ViewMode mode)
    {
        _widget.IsVisible = mode == ViewMode.Widget;
        _overall.IsVisible = mode == ViewMode.Overall;
        _full.IsVisible = mode == ViewMode.Full;
        _panel.CornerRadius = mode == ViewMode.Full ? new CornerRadius(0) : new CornerRadius(14);
        _grip.IsVisible = mode != ViewMode.Full;
        _widgetCorner.IsVisible = mode == ViewMode.Widget;
    }

    // ----------------------------------------------------------- actions

    private void Dispatch(string action)
    {
        switch (action)
        {
            case ActionKeys.OpenFull:
                _shell.SwitchTo(ViewMode.Full);
                break;
            case ActionKeys.OpenOverall:
                _shell.SwitchTo(ViewMode.Overall);
                break;
            case ActionKeys.OpenWidget:
                _shell.SwitchTo(ViewMode.Widget);
                break;
            case ActionKeys.OpenSettings:
            case ActionKeys.OpenManual:
            case ActionKeys.OpenAbout:
                OpenPanel(action switch
                {
                    ActionKeys.OpenSettings => WindowIds.Settings,
                    ActionKeys.OpenManual => WindowIds.Manual,
                    _ => WindowIds.About,
                });
                break;
            case ActionKeys.CheckUpdates:
                _ = _shell.CheckUpdatesAsync();
                break;
            case ActionKeys.DownloadInstall:
                _ = DownloadAsync();
                break;
            case ActionKeys.Restart:
                _shell.Restart();
                break;
            case ActionKeys.Close:
                _shell.Close();
                break;
            case ActionKeys.ResetSize:
                _shell.ResetSize();
                break;
            case ActionKeys.ContextExpand:
                _shell.ContextExpand();
                break;
            case ActionKeys.ContextFullScreen:
                _shell.ContextFullScreen();
                break;
            case ActionKeys.ProjectSource:
                DiskTools.Launch("xdg-open", "https://github.com/epinephrinerx/SystemMonitor");
                break;
        }
    }

    private async Task DownloadAsync()
    {
        if (await _shell.DownloadUpdateAsync())
        {
            // The installer has started; the spec says the app exits.
            _tray.Hide();
            Close();
        }
    }

    private void OpenPanel(string id)
    {
        if (_panels.TryGetValue(id, out PanelWindow? existing))
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }
            existing.Activate();
            return;
        }

        string title = id switch
        {
            WindowIds.Settings => _shell.Language["settings"],
            WindowIds.Manual => _shell.Language["manual"],
            _ => _shell.Language["about"],
        };
        Func<ShellModel, IReadOnlyList<UiRow>> rows = id switch
        {
            WindowIds.Settings => shell => shell.Settings,
            WindowIds.Manual => shell => shell.Manual,
            _ => shell => shell.About,
        };

        var window = new PanelWindow(
            _shell,
            id,
            title,
            PanelWindow.SizeFor(id),
            PanelWindow.IsResizable(id),
            rows,
            Dispatch);
        window.Closed += (_, _) => _panels.Remove(id);
        // The window manager places a new window itself, so the centring is
        // applied once it is mapped, not before.
        window.Opened += (_, _) =>
        {
            // The window manager places a transient window once it is mapped, so
            // asking before that is ignored; repeat shortly after.
            var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            retry.Tick += (_, _) =>
            {
                retry.Stop();
                PlacePanel(window);
            };
            retry.Start();
        };
        _panels[id] = window;
        window.Show(this);
    }

    /// <summary>Centre a secondary window on the main one, kept on screen.</summary>
    private void PlacePanel(Window panel)
    {
        RectD owner = CurrentRect();
        double width = panel.Width;
        double height = panel.Height;
        RectD rect = new(
            owner.X + (owner.Width - width) / 2,
            owner.Y + (owner.Height - height) / 2,
            width,
            height);
        rect = WindowGeometry.KeepOnScreen(rect, WorkAreaAt(owner.TopLeft));
        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        panel.Position = new PixelPoint(
            (int)Math.Round(rect.X * scale),
            (int)Math.Round(rect.Y * scale));
    }

    // ------------------------------------------------------------ input

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F11:
                e.Handled = true;
                _shell.F11();
                break;
            case Key.Escape:
                e.Handled = true;
                _shell.Escape();
                break;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || IsInteractive(e.Source as Visual))
        {
            return;
        }
        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            _shell.DoubleClick();
            return;
        }

        double width = Math.Max(1, Bounds.Width);
        double height = Math.Max(1, Bounds.Height);
        ResizeEdge edge = WindowGeometry.HitTest(
            new SizeD(width, height), new PointD(point.Position.X, point.Position.Y));
        if (edge != ResizeEdge.None)
        {
            BeginResize(edge, e);
            e.Handled = true;
            return;
        }

        // A click until it moves 4 px; then it is a drag (spec section 1).
        _drag = DragMode.Pending;
        _dragScale = RenderScaling <= 0 ? 1 : RenderScaling;
        _pressScreen = PointerScreen(e);
        _startRect = CurrentRect();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag == DragMode.None)
        {
            UpdateCursor(e);
            return;
        }

        PointD now = PointerScreen(e);
        double dx = now.X - _pressScreen.X;
        double dy = now.Y - _pressScreen.Y;

        if (_drag == DragMode.Pending)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                // The release happened outside the window: the press was a click.
                _drag = DragMode.None;
                return;
            }
            if (Math.Abs(dx) < 4 && Math.Abs(dy) < 4)
            {
                return;
            }
            _drag = DragMode.Move;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        if (_drag == DragMode.Move)
        {
            RectD moved = new(
                _startRect.X + dx,
                _startRect.Y + dy,
                _startRect.Width,
                _startRect.Height);
            if (_shell.Config.Snap)
            {
                // The monitor under the window centre decides the edges (spec 1).
                moved = WindowGeometry.Snap(
                    moved,
                    WorkAreaAt(new PointD(
                        moved.X + moved.Width / 2,
                        moved.Y + moved.Height / 2)));
            }
            ApplyPosition(moved);
            e.Handled = true;
            return;
        }

        if (_drag == DragMode.Resize)
        {
            RectD resized = ResizeRect(dx, dy, now);
            ApplyRect(resized);
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag == DragMode.None)
        {
            return;
        }

        bool changed = _drag is DragMode.Move or DragMode.Resize;
        DragMode finished = _drag;
        _drag = DragMode.None;
        e.Pointer.Capture(null);

        if (changed)
        {
            if (finished == DragMode.Move)
            {
                _placement.NoteMoved();
            }
            if (finished == DragMode.Resize)
            {
                StoreSize();
            }
            SavePlacement();
        }
        UpdateCursor(e);
    }

    private void BeginResize(ResizeEdge edge, PointerPressedEventArgs e)
    {
        _drag = DragMode.Resize;
        _edge = edge;
        _dragScale = RenderScaling <= 0 ? 1 : RenderScaling;
        _pressScreen = PointerScreen(e);
        _startRect = CurrentRect();
        e.Pointer.Capture(this);
        Cursor = CursorFor(edge);
    }

    private RectD ResizeRect(double dx, double dy, PointD pointer)
    {
        if (_lastMode == ViewMode.Widget)
        {
            // The ratio belongs to the panel: the pad is removed before the
            // resize and added back afterwards (spec section 1).
            RectD area = UnionWorkArea(_startRect, pointer);
            return WindowGeometry.ResizeWidgetWindow(
                _startRect,
                _edge,
                dx,
                dy,
                area,
                WindowGeometry.MinPanelSize(ViewMode.Widget),
                WindowGeometry.MaxPanelSize(ViewMode.Widget));
        }

        SizeD min = WindowGeometry.WindowLimitsMin(_lastMode);
        SizeD max = WindowGeometry.WindowLimitsMax(_lastMode);
        return WindowGeometry.Resize(_startRect, _edge, dx, dy, min, max);
    }

    /// <summary>An arrow over a control; the resize cursor at the window edges.</summary>
    private void UpdateCursor(PointerEventArgs e)
    {
        Point point = e.GetPosition(this);
        ResizeEdge edge = WindowGeometry.HitTest(
            new SizeD(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height)),
            new PointD(point.X, point.Y));
        Cursor = edge == ResizeEdge.None || IsInteractive(e.Source as Visual)
            ? new Cursor(StandardCursorType.Arrow)
            : CursorFor(edge);
    }

    private static Cursor CursorFor(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left or ResizeEdge.Right => new Cursor(StandardCursorType.SizeWestEast),
        ResizeEdge.Top or ResizeEdge.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
        ResizeEdge.TopLeft or ResizeEdge.BottomRight => new Cursor(StandardCursorType.TopLeftCorner),
        ResizeEdge.TopRight or ResizeEdge.BottomLeft => new Cursor(StandardCursorType.TopRightCorner),
        _ => new Cursor(StandardCursorType.Arrow),
    };

    /// <summary>Buttons, sliders, scrollbars and anything marked nodrag keep their press.</summary>
    private static bool IsInteractive(Visual? source)
    {
        for (Visual? visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button or ToggleButton or Slider or ScrollBar or TextBox)
            {
                return true;
            }
            if (visual is Control control && control.Classes.Contains("nodrag"))
            {
                return true;
            }
            if (visual is Window)
            {
                break;
            }
        }
        return false;
    }

    private void FillMainMenu()
    {
        _mainMenu.Items.Clear();
        foreach (UiRow row in _shell.ContextMenu)
        {
            if (row is not ActionRow action)
            {
                _mainMenu.Items.Add(new Separator());
                continue;
            }
            var item = new MenuItem { Header = Ui.Thai(action.Label) };
            item.Click += (_, _) => Dispatch(action.Action);
            _mainMenu.Items.Add(item);
        }
    }

    // -------------------------------------------------------- placement

    private RectD CurrentRect()
    {
        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        double width = double.IsNaN(Width) ? Bounds.Width : Width;
        double height = double.IsNaN(Height) ? Bounds.Height : Height;
        return new RectD(Position.X / scale, Position.Y / scale, width, height);
    }

    /// <summary>Remember the panel size the user just dragged out (spec section 1).</summary>
    private void StoreSize()
    {
        SizeD panel = WindowGeometry.PanelFromWindow(
            new SizeD(Math.Max(1, Width), Math.Max(1, Height)));
        switch (_shell.View.Mode)
        {
            case ViewMode.Widget:
                _shell.Config.WidgetW = panel.Width;
                _shell.Config.WidgetH = panel.Height;
                break;
            case ViewMode.Overall:
                _shell.Config.OverallW = panel.Width;
                _shell.Config.OverallH = panel.Height;
                _shell.Config.OverallSized = true;
                break;
            default:
                _shell.Config.FullW = panel.Width;
                _shell.Config.FullH = panel.Height;
                break;
        }
    }

    private void SavePlacement()
    {
        if (double.IsNaN(Width) || double.IsNaN(Height))
        {
            return;
        }
        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        _shell.Config.PosX = Position.X / scale;
        _shell.Config.PosY = Position.Y / scale;
        _shell.Save();
    }

    private SizeD SizeFor(ViewMode mode)
    {
        if (mode == ViewMode.Overall && !_shell.Config.OverallSized)
        {
            // Auto-fit: measure the view, divide the layout scale out (we measure
            // at scale 1), ceil and clamp to the work area (spec section 1).
            RectD work = WorkAreaAt(CurrentRect().TopLeft);
            Size natural = _overall.MeasureNatural(
                Math.Max(1, work.Width - WindowGeometry.ShadowPad * 2),
                Math.Max(1, work.Height - WindowGeometry.ShadowPad * 2));
            SizeD fit = WindowGeometry.AutoFit(
                new SizeD(natural.Width, natural.Height),
                1.0,
                WindowGeometry.MinPanelSize(ViewMode.Overall),
                work);
            _shell.Config.OverallW = fit.Width;
            _shell.Config.OverallH = fit.Height;
            return fit;
        }
        return WindowGeometry.PanelSize(mode, _shell.Config);
    }

    private void UpdateScales()
    {
        SizeD panel = WindowGeometry.PanelFromWindow(
            new SizeD(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height)));
        _widget.SetScale(WindowGeometry.ContentScale(
            panel, WindowGeometry.ReferenceSize(ViewMode.Widget)));
        _overall.SetScale(WindowGeometry.ContentScale(
            panel, WindowGeometry.ReferenceSize(ViewMode.Overall)));
    }

    private void ApplyRect(RectD rect)
    {
        Width = Math.Max(1, rect.Width);
        Height = Math.Max(1, rect.Height);
        ApplyPosition(rect);
    }

    private void ApplyPosition(RectD rect)
    {
        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        Position = new PixelPoint(
            (int)Math.Round(rect.X * scale),
            (int)Math.Round(rect.Y * scale));
    }

    private string DescribeScreens()
    {
        var text = new System.Text.StringBuilder();
        if (Screens.Primary is { } primary)
        {
            text.Append("primary=").Append(primary.Bounds).Append('/')
                .Append(primary.WorkingArea).Append(" scale=")
                .Append(primary.Scaling.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }
        foreach (Screen screen in Screens.All)
        {
            text.Append(" screen=").Append(screen.Bounds).Append('/')
                .Append(screen.WorkingArea).Append(" scale=")
                .Append(screen.Scaling.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" primary=").Append(screen.IsPrimary);
        }
        return text.ToString();
    }

    private RectD PrimaryWorkArea()
    {
        Screen? screen = Screens.Primary;
        return screen is null
            ? new RectD(0, 0, 1920, 1080)
            : Dip(screen.WorkingArea, screen.Scaling);
    }

    private RectD WorkAreaAt(PointD point)
    {
        Screen? screen = ScreenAt(point);
        return screen is null ? PrimaryWorkArea() : Dip(screen.WorkingArea, screen.Scaling);
    }

    private Screen? ScreenAt(PointD point)
    {
        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        var pixel = new PixelPoint(
            (int)Math.Round(point.X * scale),
            (int)Math.Round(point.Y * scale));
        return Screens.ScreenFromPoint(pixel) ?? Screens.Primary;
    }

    /// <summary>The union of the start monitor and the monitor under the pointer.</summary>
    private RectD UnionWorkArea(RectD rect, PointD pointer)
    {
        RectD first = WorkAreaAt(rect.TopLeft);
        RectD second = WorkAreaAt(pointer);
        double x = Math.Min(first.X, second.X);
        double y = Math.Min(first.Y, second.Y);
        double right = Math.Max(first.Right, second.Right);
        double bottom = Math.Max(first.Bottom, second.Bottom);
        return new RectD(x, y, right - x, bottom - y);
    }

    private static RectD Dip(PixelRect rect, double scale)
    {
        double s = scale <= 0 ? 1 : scale;
        return new RectD(rect.X / s, rect.Y / s, rect.Width / s, rect.Height / s);
    }

    private PointD PointerScreen(PointerEventArgs e)
    {
        PixelPoint pixel = this.PointToScreen(e.GetPosition(this));
        // A drag keeps the DPI it started with (spec section 1).
        double scale = _drag == DragMode.None ? RenderScaling : _dragScale;
        if (scale <= 0)
        {
            scale = 1;
        }
        return new PointD(pixel.X / scale, pixel.Y / scale);
    }
}
