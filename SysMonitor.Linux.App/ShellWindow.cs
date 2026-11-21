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
/// The one window (spec sections 0-3): the Widget and the Main window, a
/// borderless transparent shell with a 12 px shadow pad, custom drag and
/// resize, and the 400 ms redraw tick.
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
    private readonly IRestarter _restarter;
    private readonly ITray _tray;
    private readonly ICommandRunner _commands;

    private readonly WidgetView _widget;
    private readonly MainView _mainView;
    private readonly Border _panel;
    private readonly Path _grip;
    private readonly Grid _widgetCorner = new();
    private readonly ContextMenu _mainMenu = new();
    private bool _pointerOver;

    // Full screen (R-030 §3): WindowState is the screen's business, and the
    // state before it is restored when Main leaves full screen.
    private bool _fullScreenApplied;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;
    private RectD? _rectBeforeFullScreen;

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

    // Close/quit state (spec section 9): close hiding to the tray is not a
    // quit, and a tray icon exists only while the window is hidden.
    private bool _quitting;
    private bool _shutDown;
    private volatile bool _hiddenToTray;

    // No-tray fallback (amendment 1): the window is borderless and normally
    // off the taskbar, so minimising it without a tray icon would strand it.
    // The fallback adds it to the taskbar until it is shown again, and its
    // line is logged once per run.
    private const bool ShowInTaskbarConfigured = false;
    private bool _fallbackLogged;
    private bool _fallbackInTaskbar;

    // Pointer state. Every resize frame is computed from _startRect, never from
    // the previous frame (spec section 1).
    private DragMode _drag;
    private ResizeEdge _edge;
    private PointD _pressScreen;
    private RectD _startRect;
    private double _dragScale = 1;

    public ShellWindow(
        ShellModel shell,
        StartupOptions options,
        Diagnostics diagnostics,
        ITray? tray = null,
        IRestarter? restarter = null,
        ICommandRunner? commands = null)
    {
        _shell = shell;
        _options = options;
        _diagnostics = diagnostics;
        _commands = commands ?? new ProcessCommandRunner();

        // Neither integration can be built before the window exists: the tray
        // needs its restore/exit callbacks, and a restart has to shut this copy
        // down once the new one has started.
        _tray = tray ?? new TrayIconHost(shell, RestoreFromTray, Quit, diagnostics);
        _restarter = restarter
            ?? new ProcessRestarter(Environment.ProcessPath, options.Arguments);
        _lastMode = shell.View.Mode;
        _appliedFontScale = shell.Config.FontScale;

        Title = "System Monitor";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = ShowInTaskbarConfigured;
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
        _mainView = new MainView(shell, Dispatch);

        // The Widget's close and pause buttons live inside the panel, just in
        // from its rounded top-right corner: the shadow pad stays empty, and a
        // change to the panel's margin (full screen drops it) carries them
        // along. The widget's title row keeps the same room clear.
        _widgetCorner.HorizontalAlignment = HorizontalAlignment.Right;
        _widgetCorner.VerticalAlignment = VerticalAlignment.Top;
        _widgetCorner.Margin = new Thickness(
            0, WidgetView.CornerInset, WidgetView.CornerInset, 0);

        var views = new Grid();
        views.Children.Add(_widget);
        views.Children.Add(_mainView);
        views.Children.Add(_widgetCorner);
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
        shell.RestartRequested += RestartApp;
        // The menu's Close and the widget's close button both go through
        // Window.Close, so the close action decides between tray and exit.
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
        UpdateProcessesVisibility();
        UpdateBrushes();
        UpdateWidgetCorner();
        _widget.Render();
        _mainView.Render();
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
        UpdateProcessesVisibility();

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
        // Amendment 2, item 6: the sequence (check, 60 s, retry, setting still
        // on) belongs to UpdateAutoCheck; the window only starts it.
        try
        {
            await _shell.AutoCheck.RunAsync(_stop.Token);
        }
        catch (OperationCanceledException)
        {
            // The app is quitting during the wait.
        }
    }

    /// <summary>
    /// The close button and the menu's Close (spec section 9): with
    /// <c>close_action=tray</c> the window hides and the tray keeps a way back;
    /// any other close, the tray's Exit item and session end quit for real.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_quitting && string.Equals(_shell.Config.CloseAction, "tray", StringComparison.Ordinal))
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _quitting = true;
        Shutdown();
    }

    /// <summary>Session end (logoff or shutdown): save the placement, then go.</summary>
    public void PrepareForShutdown()
    {
        _quitting = true;
        SavePlacement();
        _diagnostics.Line("session ending; placement saved");
    }

    /// <summary>Quit for good: the close action, the tray Exit item or a restart.</summary>
    private void Quit()
    {
        if (_quitting)
        {
            return;
        }
        _quitting = true;
        Close();
    }

    private void RestartApp()
    {
        SavePlacement();
        _shell.Save();
        if (_restarter.Restart())
        {
            _diagnostics.Line("restart: a new copy started; shutting down");
            Quit();
            return;
        }
        _diagnostics.Line("restart failed: the new copy did not start");
    }

    /// <summary>
    /// Hide to the tray: save the placement, stop the UI timer, show the icon
    /// and hide. The sampler keeps running, and while hidden each fresh sample
    /// still reaches the model, so history fills and the tooltip stays current.
    /// </summary>
    private void HideToTray()
    {
        SavePlacement();
        string tooltip = TrayText.Build(_shell.Snapshot, _shell.Language);

        // Amendment 2, item 5: the icon is a StatusNotifierItem, so a host has
        // to exist before the window may leave the screen. TrayIconHost can
        // still fail while creating it; both cases take the same way back.
        if (!TrayHost.IsAvailable(_commands) || !_tray.Show(tooltip))
        {
            FallbackToTaskbar();
            return;
        }

        ClosePanels();
        _hiddenToTray = true;
        _tick.Stop();
        Hide();
        UpdateProcessesVisibility();
        _diagnostics.Line("hidden to tray");
    }

    /// <summary>
    /// No tray (amendment 1): a borderless window that is also off the taskbar
    /// would be stranded once minimised, so put it back on the taskbar until it
    /// is shown again. Logged once per run.
    /// </summary>
    private void FallbackToTaskbar()
    {
        if (!_fallbackLogged)
        {
            _fallbackLogged = true;
            _diagnostics.Line("close to tray unavailable; minimising to the taskbar instead");
        }
        _fallbackInTaskbar = true;
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
    }

    /// <summary>A left click on the tray icon, or its Show item.</summary>
    private void RestoreFromTray()
    {
        if (!_hiddenToTray)
        {
            return;
        }

        _hiddenToTray = false;
        _tray.Hide();
        Show();

        // Amendment 3, item 1: a window hidden while Main was full screen comes
        // back full screen, so the model and the window agree and F11 toggles
        // from there. Everything else comes back as a normal window.
        if (_shell.View.Mode == ViewMode.Main && _shell.MainFullScreen)
        {
            _fullScreenApplied = true;
            _panel.Margin = new Thickness(0);
            WindowState = WindowState.FullScreen;
        }
        else
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        _tick.Start();
        OnTick();
        UpdateProcessesVisibility();
        _diagnostics.Line("restored from tray");
    }

    /// <summary>
    /// Amendment 1: a no-tray fallback keeps the window on the taskbar only
    /// while it is minimised. Showing it again restores the configured value,
    /// whether it came back through the window manager or through this class.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            if (_fallbackInTaskbar && WindowState != WindowState.Minimized)
            {
                _fallbackInTaskbar = false;
                ShowInTaskbar = ShowInTaskbarConfigured;
            }
            // Minimising (including the no-tray fallback) stops the process
            // read; restoring starts it again when Processes is showing.
            UpdateProcessesVisibility();
        }
    }

    /// <summary>Quit: stop the timers and the sampler, save, drop the tray.</summary>
    private void Shutdown()
    {
        if (_shutDown)
        {
            return;
        }
        _shutDown = true;
        _hiddenToTray = false;
        SavePlacement();
        _shell.Save();
        _tick.Stop();
        _heartbeat.Stop();
        _stop.Cancel();
        _wake.Set();
        ClosePanels();
        _tray.Hide();
        _diagnostics.Line("closing");
    }

    private void ClosePanels()
    {
        foreach (PanelWindow panel in _panels.Values.ToList())
        {
            panel.Close();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Shutdown();
        _tray.Dispose();
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
                if (_hiddenToTray)
                {
                    // The UI timer is stopped while hidden, so the sampler
                    // hands its sample to the UI thread: history keeps filling
                    // and the tray tooltip follows the numbers. Rotation waits
                    // for the window to come back.
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_hiddenToTray && !_stop.IsCancellationRequested)
                        {
                            Feed();
                        }
                    });
                }
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
    /// Recompute the process read after a switch. The process and service
    /// lists live on the Processes page now, so that page decides whether the
    /// sampler reads them.
    /// </summary>
    private void UpdateProcessesVisibility()
    {
        // Amendment 2, item 4: the process and service samplers run only while
        // the window is actually showing the Processes page.
        bool visible = _shell.MainProcessesVisible
            && !_hiddenToTray
            && IsVisible
            && WindowState != WindowState.Minimized;
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
        Feed();
        _shell.Tick();
    }

    /// <summary>Hand the newest sample to the model, once.</summary>
    private void Feed()
    {
        Snapshot? latest = Volatile.Read(ref _latest);
        if (!ReferenceEquals(latest, _rendered))
        {
            _rendered = latest;
            _shell.SetSnapshot(latest);
        }
    }

    // ------------------------------------------------------------ events

    private void OnDataChanged()
    {
        if (_hiddenToTray)
        {
            _tray.Show(TrayText.Build(_shell.Snapshot, _shell.Language));
        }
        _widget.Render();
        _mainView.RenderData();
        UpdateProcessesVisibility();
        UpdateWidgetCorner();
    }

    private void OnStructureChanged()
    {
        if (_applyingRoute)
        {
            return;
        }
        _mainView.Render();
        _widget.Render();
        UpdateWidgetCorner();
        UpdateProcessesVisibility();
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
        // The container is already inside the panel edge; the two icons keep
        // the same 18 px boxes (and hit areas) a 4 px gap apart.
        close.Margin = new Thickness(0);
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
        pause.Margin = new Thickness(0, 0, WidgetView.CornerStep, 0);
        pause.Opacity = _pointerOver ? 1.0 : 0.35;
        _widgetCorner.Children.Add(pause);
    }

    private void UpdateBrushes()
    {
        Palette palette = _shell.Palette;
        _panel.Background = Ui.Brush(palette.PanelBrush(_shell.Config.Opacity));
        _panel.BorderBrush = Ui.Brush(palette.Border);
        _grip.Stroke = Ui.Brush(palette.Label);
        _mainView.SetOpacity(_shell.Config.Opacity);
    }

    private void SyncDials()
    {
        foreach (DialView dial in _mainView.Dials)
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
        // Amendment 1: the small module cards size from the font scale, so a
        // live dial change must resize them in place as well as their text.
        _mainView.RescaleCards();
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
            ViewMode from = _lastMode;
            ViewMode to = _shell.View.Mode;
            bool enteringFull = to == ViewMode.Main
                && _shell.MainFullScreen
                && !_fullScreenApplied;
            bool leavingFull = !_shell.MainFullScreen && _fullScreenApplied;

            // Never write the screen-filling rect to the config; the normal
            // rect is recorded when full screen starts and restored on exit.
            if (!_fullScreenApplied)
            {
                SavePlacement();
            }

            ApplyMode(to);
            _mainView.Render();
            _widget.Render();
            UpdateWidgetCorner();
            _appliedFontScale = _shell.Config.FontScale;

            if (enteringFull)
            {
                if (from == ViewMode.Widget)
                {
                    // F11 from the Widget walks the same Widget -> Main
                    // transition as a double-click, so the Widget's origin is
                    // remembered before the screen is filled.
                    RectD current = CurrentRect();
                    PointD origin = _placement.ApplySwitch(from, to, current);
                    SizeD panel = SizeFor(to);
                    SizeD window = WindowGeometry.WindowSize(panel);
                    RectD normal = new(origin.X, origin.Y, window.Width, window.Height);
                    ApplyRect(WindowGeometry.KeepOnScreen(normal, WorkAreaAt(origin)));
                }
                // The screen owns the size while full screen; coming back
                // re-applies the remembered rect below.
                ApplyFullScreen(true);
                _lastMode = to;
                UpdateScales();
                UpdateBrushes();
                UpdateProcessesVisibility();
                return;
            }

            if (leavingFull)
            {
                // Restore the exact normal-window rect, and only save the
                // placement once the window is back to its normal geometry.
                ApplyFullScreen(false);
                if (_rectBeforeFullScreen is { } before)
                {
                    ApplyRect(before);
                }
                if (to != ViewMode.Main)
                {
                    RectD current = CurrentRect();
                    PointD origin = _placement.ApplySwitch(from, to, current);
                    SizeD window = WindowGeometry.WindowSize(SizeFor(to));
                    RectD rect = new(origin.X, origin.Y, window.Width, window.Height);
                    ApplyRect(WindowGeometry.KeepOnScreen(rect, WorkAreaAt(origin)));
                }
                _lastMode = to;
                UpdateScales();
                UpdateBrushes();
                UpdateProcessesVisibility();
                SavePlacement();
                return;
            }

            if (_fullScreenApplied)
            {
                // Still full screen (another page selected): keep the screen
                // geometry and remember nothing new.
                _lastMode = to;
                UpdateScales();
                UpdateBrushes();
                UpdateProcessesVisibility();
                return;
            }

            RectD currentRect = CurrentRect();
            PointD switchOrigin = _placement.ApplySwitch(from, to, currentRect);
            SizeD targetPanel = SizeFor(to);
            SizeD targetWindow = WindowGeometry.WindowSize(targetPanel);
            RectD target = new(
                switchOrigin.X, switchOrigin.Y, targetWindow.Width, targetWindow.Height);
            target = WindowGeometry.KeepOnScreen(target, WorkAreaAt(target.TopLeft));
            ApplyRect(target);

            _lastMode = to;
            UpdateScales();
            UpdateBrushes();
            UpdateProcessesVisibility();
            SavePlacement();
        }
        finally
        {
            _applyingRoute = false;
        }
    }

    /// <summary>
    /// R-030 §3: Main's full screen is <see cref="WindowState.FullScreen"/>, and
    /// leaving it restores the state it had before. The shadow pad is dropped
    /// while the screen is filled.
    /// </summary>
    private void ApplyFullScreen(bool full)
    {
        if (full)
        {
            if (_fullScreenApplied)
            {
                return;
            }
            _fullScreenApplied = true;
            _rectBeforeFullScreen = CurrentRect();
            _stateBeforeFullScreen = WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState;
            _panel.Margin = new Thickness(0);
            WindowState = WindowState.FullScreen;
            return;
        }

        if (!_fullScreenApplied)
        {
            return;
        }
        _fullScreenApplied = false;
        _panel.Margin = new Thickness(WindowGeometry.ShadowPad);
        WindowState = _stateBeforeFullScreen == WindowState.FullScreen
            ? WindowState.Normal
            : _stateBeforeFullScreen;
    }

    private void ApplyMode(ViewMode mode)
    {
        _widget.IsVisible = mode == ViewMode.Widget;
        _mainView.IsVisible = mode == ViewMode.Main;
        _panel.CornerRadius = mode == ViewMode.Main
            ? new CornerRadius(0)
            : new CornerRadius(14);
        _grip.IsVisible = mode == ViewMode.Widget;
        _widgetCorner.IsVisible = mode == ViewMode.Widget;
    }

    // ----------------------------------------------------------- actions

    private void Dispatch(string action)
    {
        switch (action)
        {
            case ActionKeys.OpenFull:
            case ActionKeys.OpenOverall:
                _shell.SwitchTo(ViewMode.Main);
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
            _shell.Save();
            Quit();
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
            default:
                _shell.Config.MainW = panel.Width;
                _shell.Config.MainH = panel.Height;
                break;
        }
    }

    private void SavePlacement()
    {
        // The screen-filling rect is not a placement: only the normal window
        // rect, remembered before full screen, may reach the config.
        if (_fullScreenApplied || double.IsNaN(Width) || double.IsNaN(Height))
        {
            return;
        }
        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        _shell.Config.PosX = Position.X / scale;
        _shell.Config.PosY = Position.Y / scale;
        _shell.Save();
    }

    private SizeD SizeFor(ViewMode mode) =>
        WindowGeometry.PanelSize(mode, _shell.Config);

    private void UpdateScales()
    {
        SizeD panel = WindowGeometry.PanelFromWindow(
            new SizeD(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height)));
        _widget.SetScale(WindowGeometry.ContentScale(
            panel, WindowGeometry.ReferenceSize(ViewMode.Widget)));
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
