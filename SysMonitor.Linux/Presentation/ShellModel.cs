using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// Everything the windows show, apart from the windows themselves: the config,
/// the language, the theme, the widget pages, the Overall cards, the sidebar
/// and the three secondary pages.
///
/// The window owns placement, input and drawing; this owns state and the rules
/// that change it. That split is what makes the behaviour testable without a
/// display, and it keeps a settings change in one place instead of one place
/// per window (the sidebar and Settings show the same four controls).
/// </summary>
public sealed class ShellModel
{
    private readonly IAutostart _autostart;
    private readonly IUpdater _updater;
    private readonly Func<long> _clock;
    private readonly string? _configPath;
    private readonly string _version;

    private Snapshot? _snapshot;
    private TypeRamp _ramp;
    private IReadOnlyList<WidgetPage> _widgetPages = Array.Empty<WidgetPage>();
    private OverallModel _overall = new();
    private IReadOnlyList<UiRow> _sidebar = Array.Empty<UiRow>();
    private IReadOnlyList<UiRow> _settings = Array.Empty<UiRow>();
    private IReadOnlyList<UiRow> _manual = Array.Empty<UiRow>();
    private IReadOnlyList<UiRow> _about = Array.Empty<UiRow>();
    private IReadOnlyList<UiRow> _contextMenu = Array.Empty<UiRow>();
    private FullModel _full = new();
    private IReadOnlyList<SidebarItem> _mainSidebar = Array.Empty<SidebarItem>();
    private MemoryTabModel _memory = new();
    private readonly MainState _main;
    private readonly FullSelection _fullSelection = new();
    private string _lastReconciledPage = string.Empty;
    private readonly ServiceControl _serviceControl;
    private readonly IProcessSignals _signals;
    private readonly int _ownPid;
    private readonly int _ownUid;

    /// <summary>
    /// The one kill flow, created in the constructor (Amendment 5). It is never
    /// replaced: <see cref="KillFlow.Reopen"/> clears the closed flag when the
    /// Full view opens again.
    /// </summary>
    private readonly KillFlow _kill;

    /// <summary>The Apps page's close and restart actions, on the same flow.</summary>
    private readonly AppActions _appActions;
    private string _memoryQuery = string.Empty;
    private string? _graphedGroup;
    private string? _memoryStatus;

    public ShellModel(
        AppConfig? config = null,
        Lang? lang = null,
        IAutostart? autostart = null,
        IUpdater? updater = null,
        string? version = null,
        string? configPath = null,
        Func<long>? clock = null,
        ICommandRunner? commands = null,
        IProcessSignals? killTargets = null,
        int ownPid = 0,
        int ownUid = -1)
    {
        Config = config ?? new AppConfig();
        Config.Normalise();
        _configPath = configPath;
        _clock = clock ?? (() => Environment.TickCount64);
        _ownPid = ownPid > 0 ? ownPid : Environment.ProcessId;
        _ownUid = ownUid;
        ICommandRunner runner = commands ?? new ProcessCommandRunner();
        _serviceControl = new ServiceControl(args =>
        {
            CommandResult? completed = runner.RunResult("systemctl", args, 15000);
            if (completed is not { } result)
            {
                // No program, no exit code, no stderr. The status line still
                // has to carry something; a number is at least not a
                // half-translated sentence.
                return "-1";
            }
            if (result.Ok)
            {
                return null;
            }
            string line = FirstLine(result.Error);
            return line.Length > 0
                ? line
                : result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
        _signals = killTargets ?? new NoopKillTargets();

        Language = lang ?? new Lang();
        Language.Set(Config.Lang);
        Config.Lang = Language.Code;

        // Eager, not lazy (Amendment 5): one flow for the life of the model,
        // sharing the language object so a language switch still changes the
        // sentences.
        _kill = new KillFlow(_signals, lang: Language, ownPid: _ownPid, ownUid: _ownUid);

        // The Apps page's actions share that one flow, so a force token belongs
        // to the flow the window is already holding.
        _appActions = new AppActions(
            _kill,
            commands: runner,
            lang: Language,
            ownPid: _ownPid,
            ownUid: _ownUid >= 0 ? _ownUid : null);

        _autostart = autostart ?? new NoopAutostart();
        _version = string.IsNullOrWhiteSpace(version) ? "4.3.0" : version!;
        _updater = updater ?? new NoopUpdater(_version);

        _ramp = new TypeRamp(Config.FontScale);
        View = new ViewState(ViewModeExtensions.Parse(Config.LastView, ViewMode.Widget));
        _main = new MainState(Config.MainPage, Config.SettingsOpen, Config.SidebarWidth);
        Rotation = new RotationState();
        Alerts = new DiskAlertTracker();
        History = new HistoryBoard();
        Updates = new UpdatePanelState(_version);

        // One auto-check for the life of the model: the window calls it and
        // keeps no copy of the retry rules (Amendment 2, item 6).
        AutoCheck = new UpdateAutoCheck(
            _updater, () => Config.AutoCheckUpdates, CheckForUpdatesAsync);

        Rebuild();
    }

    /// <summary>Raised when the numbers changed: a new snapshot or a new page.</summary>
    public event Action? DataChanged;

    /// <summary>Raised when text, rows or the palette changed: re-render everything.</summary>
    public event Action? StructureChanged;

    /// <summary>Raised when only opacity or the font scale changed: no rebuild needed.</summary>
    public event Action? AppearanceChanged;

    /// <summary>Raised on a view switch: the window must re-apply its placement.</summary>
    public event Action? RouteChanged;

    public event Action? TopmostChanged;
    public event Action? NudgeRequested;
    public event Action? MemoryRefreshRequested;
    public event Action? MemoryChanged;
    public event Action<string>? OpenRequested;
    public event Action? RestartRequested;
    public event Action? CloseRequested;

    public AppConfig Config { get; }
    public Lang Language { get; }
    public ViewState View { get; }
    public RotationState Rotation { get; }
    public DiskAlertTracker Alerts { get; }
    public HistoryBoard History { get; }
    public UpdatePanelState Updates { get; }

    /// <summary>The start-up check and its one retry (Amendment 2, item 6).</summary>
    public UpdateAutoCheck AutoCheck { get; }
    public string Version => _version;

    public Palette Palette => Palette.For(Config.Theme);
    public TypeRamp Ramp => _ramp;
    public Snapshot? Snapshot => _snapshot;
    public bool Ready => _snapshot is { Ready: true };

    public IReadOnlyList<WidgetPage> WidgetPages => _widgetPages;
    public OverallModel Overall => _overall;
    public FullModel Full => _full;

    /// <summary>What the Main window is showing: page, Settings, sidebar width.</summary>
    public MainState Main => _main;

    /// <summary>The sidebar's module items, in page order (§5).</summary>
    public IReadOnlyList<SidebarItem> MainSidebar => _mainSidebar;

    public MemoryTabModel Memory => _memory;

    /// <summary>The Apps page's close and restart actions (shared kill flow).</summary>
    public AppActions AppActions => _appActions;
    public IReadOnlyList<UiRow> Sidebar => _sidebar;
    public IReadOnlyList<UiRow> Settings => _settings;
    public IReadOnlyList<UiRow> Manual => _manual;
    public IReadOnlyList<UiRow> About => _about;

    /// <summary>The main right-click menu, rebuilt in the current language.</summary>
    public IReadOnlyList<UiRow> ContextMenu => _contextMenu;

    public WidgetPage? CurrentPage => Rotation.Page;
    public bool Paused => Rotation.Paused;
    public bool IsWidget => View.IsWidget;

    /// <summary>True only while the Main window is showing the Processes page.</summary>
    public bool MainProcessesVisible =>
        View.Mode == ViewMode.Main
        && string.Equals(_main.Page, PageKeys.Processes, StringComparison.Ordinal);

    /// <summary>True only while the Main window is showing the Apps page.</summary>
    public bool MainAppsVisible =>
        View.Mode == ViewMode.Main
        && string.Equals(_main.Page, PageKeys.Apps, StringComparison.Ordinal);

    /// <summary>True while either grid page is showing: the 2 s grid read runs.</summary>
    public bool MainGridVisible => MainAppsVisible || MainProcessesVisible;

    /// <summary>True while Main covers the screen (F11).</summary>
    public bool MainFullScreen => View.MainFullScreen;

    /// <summary>The page keys Main can show now: the fixed pages, then the module pages.</summary>
    public IReadOnlyList<string> MainPages()
    {
        var pages = new List<string>(3 + _full.Tabs.Count)
        {
            PageKeys.Overall,
            PageKeys.Apps,
            PageKeys.Processes,
        };
        pages.AddRange(_full.Tabs.Select(tab => tab.Key));
        return pages;
    }

    /// <summary>The result of the last memory operation: empty, cancelled or failed.</summary>
    public string MemoryStatus => _memoryStatus ?? string.Empty;

    /// <summary>The live search text of the memory tab.</summary>
    public string MemoryQuery => _memoryQuery;

    /// <summary>The process group whose chart is showing, if any.</summary>
    public string? GraphedProcess => _graphedGroup;

    public string PauseTooltip => Language[Rotation.Paused ? "resume_rotation" : "pause_rotation"];
    public bool AutostartEnabled => _autostart.IsEnabled;

    /// <summary>The empty panel's text, if any: "connecting" or "no_data".</summary>
    public string WidgetMessage => CurrentPage?.Message ?? string.Empty;

    // --------------------------------------------------------------- data

    /// <summary>
    /// Take one fresh sample. Returns true when a new snapshot landed, so the
    /// caller only redraws when there is something to draw.
    /// </summary>
    public bool SetSnapshot(Snapshot? snapshot)
    {
        if (ReferenceEquals(snapshot, _snapshot))
        {
            return false;
        }

        _snapshot = snapshot;
        if (snapshot is not null && snapshot.Ready)
        {
            History.Push(snapshot);
        }
        if (snapshot is not null)
        {
            // include_network = false keeps network drives out of the views,
            // the totals and the alert tracker alike.
            Alerts.Update(DeviceFilter.VisibleDisks(snapshot, Config));
        }

        RebuildData();
        DataChanged?.Invoke();
        return true;
    }

    /// <summary>Called on the UI tick: rotates the widget page when it is due.</summary>
    public bool Tick()
    {
        if (!Rotation.Tick(_clock(), View.IsWidget))
        {
            return false;
        }
        DataChanged?.Invoke();
        return true;
    }

    public void DismissAlert(string letter)
    {
        if (Alerts.Dismiss(letter))
        {
            // The page and card models carry the alert text, so they have to be
            // rebuilt for the banner to disappear now rather than next tick.
            RebuildData();
            DataChanged?.Invoke();
        }
    }

    // -------------------------------------------------------------- views

    public bool SwitchTo(ViewMode mode)
    {
        if (!View.Move(mode))
        {
            return false;
        }
        return AfterSwitch();
    }

    public bool DoubleClick() => Apply(View.DoubleClick);
    public bool F11() => Apply(View.F11);
    public bool Escape() => Apply(View.Escape);
    public bool ContextExpand() => Apply(View.ContextExpand);
    public bool ContextFullScreen() => Apply(View.ContextFullScreen);

    /// <summary>Show the one main window (R-030).</summary>
    public bool ShowMain() => SwitchTo(ViewMode.Main);

    /// <summary>Show the small Widget.</summary>
    public bool ShowWidget() => SwitchTo(ViewMode.Widget);

    /// <summary>The header's full-screen button.</summary>
    public bool ToggleFullScreen()
    {
        if (!View.ToggleFullScreen())
        {
            return false;
        }
        RouteChanged?.Invoke();
        return true;
    }

    private bool Apply(Func<bool> move)
    {
        if (!move())
        {
            return false;
        }
        return AfterSwitch();
    }

    private bool AfterSwitch()
    {
        Config.LastView = View.Mode.ToKey();
        Save();
        if (View.Mode != ViewMode.Widget)
        {
            // The Main window opened: a previous CancelAll (window closed) may
            // have sealed the one flow, so clear its closed flag. Amendment 5
            // allows either a new flow or Reopen; the flow is eager, so it is
            // this call.
            _kill.Reopen();
        }
        if (View.IsWidget)
        {
            Rotation.ResetTimer(_clock());
        }
        Rebuild();
        RouteChanged?.Invoke();
        StructureChanged?.Invoke();
        return true;
    }

    public bool TogglePause()
    {
        Rotation.TogglePause(_clock());
        DataChanged?.Invoke();
        return true;
    }

    public bool NextPage()
    {
        Rotation.Next(_clock());
        DataChanged?.Invoke();
        return true;
    }

    public bool PrevPage()
    {
        Rotation.Prev(_clock());
        DataChanged?.Invoke();
        return true;
    }

    // ------------------------------------------------------------- full view

    /// <summary>
    /// Show one of Main's pages and remember it as <c>main_page</c>. A key the
    /// snapshot no longer offers is ignored rather than saved. Selecting a page
    /// starts its "Highest" figures again.
    /// </summary>
    public bool SelectMainPage(string key)
    {
        if (!MainPages().Contains(key, StringComparer.Ordinal))
        {
            return false;
        }

        _fullSelection.Select(key);
        _main.Select(key);
        _lastReconciledPage = key;
        Config.MainPage = key;
        Save();
        RebuildFull();
        RebuildMainSidebar();
        StructureChanged?.Invoke();
        return true;
    }

    /// <summary>Expand or collapse Settings in the sidebar; the state is remembered.</summary>
    public bool SetSettingsOpen(bool open)
    {
        if (_main.SettingsOpen == open)
        {
            return true;
        }
        _main.SetSettingsOpen(open);
        Config.SettingsOpen = open;
        Save();
        StructureChanged?.Invoke();
        return true;
    }

    public bool ToggleSettings() => SetSettingsOpen(!_main.SettingsOpen);

    /// <summary>Resize the sidebar; the width is clamped 200-320 and remembered.</summary>
    public bool SetSidebarWidth(int width)
    {
        int clamped = MainState.ClampWidth(width);
        if (_main.SidebarWidth == clamped)
        {
            return true;
        }
        _main.SetSidebarWidth(clamped);
        Config.SidebarWidth = clamped;
        Save();
        StructureChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Sort the Apps grid by one column and remember it. Clicking the current
    /// column again reverses the direction; a new numeric column starts highest
    /// first, which is how a "top" list is read.
    /// </summary>
    public bool SetAppsSort(GridColumn column)
    {
        string key = ProcessGrid.Key(column);
        bool descending = string.Equals(Config.AppsSort, key, StringComparison.Ordinal)
            ? !Config.AppsSortDesc
            : column is not GridColumn.Name;
        if (string.Equals(Config.AppsSort, key, StringComparison.Ordinal)
            && Config.AppsSortDesc == descending)
        {
            return true;
        }

        Config.AppsSort = key;
        Config.AppsSortDesc = descending;
        Save();
        StructureChanged?.Invoke();
        return true;
    }

    /// <summary>Filter the memory tab as the person types; the tables update live.</summary>
    public void SetMemoryQuery(string? query)
    {
        string value = query ?? string.Empty;
        if (string.Equals(value, _memoryQuery, StringComparison.Ordinal))
        {
            return;
        }
        _memoryQuery = value;
        RebuildMemory();
        MemoryChanged?.Invoke();
    }

    /// <summary>
    /// Show or hide the chart of a process group. Picking the same group again
    /// hides it, as the spec says.
    /// </summary>
    public void ToggleProcessGraph(string name)
    {
        string? next = string.Equals(_graphedGroup, name, StringComparison.OrdinalIgnoreCase)
            ? null
            : name;
        _graphedGroup = next;
        RebuildMemory();
        RebuildFull();
        MemoryChanged?.Invoke();
        DataChanged?.Invoke();
    }

    /// <summary>The status line result: empty on success, else a ready-made sentence.</summary>
    public void SetMemoryStatus(string? text)
    {
        _memoryStatus = string.IsNullOrEmpty(text) ? null : text;
        MemoryChanged?.Invoke();
    }

    /// <summary>
    /// End one process. Called off the UI thread: SIGTERM then a wait of up to
    /// 3 s. The target carries the PID, the start time and the name captured
    /// from its row.
    ///
    /// When it survives, the result carries the <see cref="PendingKill"/> token
    /// that owns the pinned handle (Amendment 4). The caller keeps that token
    /// across the force question and hands the same token to
    /// <see cref="ForceKill"/>, so nothing pins twice and a stale answer can
    /// only reach its own token. One flow is kept for the life of the model, and
    /// the language table is read through the same object, so switching
    /// language still changes the sentences.
    /// </summary>
    public KillResult KillProcess(ProcessTarget target) => RecordKill(Kills.Terminate(target));

    /// <summary>SIGKILL through the token the person confirmed.</summary>
    public KillResult ForceKill(PendingKill pending) => RecordKill(Kills.Force(pending));

    /// <summary>The person declined the force question: close that token's handle.</summary>
    public void CancelKill(PendingKill pending) => Kills.Decline(pending);

    /// <summary>The window is closing: close every handle a pending kill holds.</summary>
    public void CancelKills() => Kills.CancelAll();

    /// <summary>
    /// The one kill flow, created in the constructor. Opening the Full view
    /// again calls <see cref="KillFlow.Reopen"/> on this same instance, so the
    /// model never holds a second flow. Internal so a test can prove it is one
    /// instance from construction.
    /// </summary>
    internal KillFlow Kills => _kill;

    /// <summary>Keep the status line and the refresh of the process list in one place.</summary>
    private KillResult RecordKill(KillResult result)
    {
        _memoryStatus = result.State == KillState.Failed
            ? Language.Get("mem_failed", result.Error ?? string.Empty)
            : null;
        MemoryRefreshRequested?.Invoke();
        MemoryChanged?.Invoke();
        return result;
    }

    /// <summary>Start, stop or restart a user unit. System units are refused here.</summary>
    public ServiceResult RunService(string action, MemService service)
    {
        if (!service.UserUnit)
        {
            _memoryStatus = Language["mem_system_readonly"];
            MemoryChanged?.Invoke();
            return ServiceResult.Failed(Language["mem_system_readonly"]);
        }

        ServiceResult result = _serviceControl.Apply(action, service.Name);
        _memoryStatus = result.Ok
            ? null
            : Language.Get("mem_systemctl_failed", result.Error ?? string.Empty);
        if (result.Ok)
        {
            MemoryRefreshRequested?.Invoke();
        }
        MemoryChanged?.Invoke();
        return result;
    }

    /// <summary>Change a user unit's startup: enable for Automatic, disable for Manual.</summary>
    public ServiceResult SetServiceStartup(MemService service, bool automatic)
    {
        if (!service.UserUnit)
        {
            _memoryStatus = Language["mem_system_readonly"];
            MemoryChanged?.Invoke();
            return ServiceResult.Failed(Language["mem_system_readonly"]);
        }

        ServiceResult result = _serviceControl.SetStartup(automatic, service.Name);
        _memoryStatus = result.Ok
            ? null
            : Language.Get("mem_systemctl_failed", result.Error ?? string.Empty);
        if (result.Ok)
        {
            MemoryRefreshRequested?.Invoke();
        }
        MemoryChanged?.Invoke();
        return result;
    }

    /// <summary>Read processes and services on the next sample, ignoring the cadence.</summary>
    public void RequestMemoryRefresh() => MemoryRefreshRequested?.Invoke();

    // ----------------------------------------------------------- settings

    /// <summary>
    /// Apply a checkbox. Returns false for a key this model does not know, so a
    /// typo in a view is visible instead of silent.
    /// </summary>
    public bool SetBool(string key, bool value)
    {
        bool changed;
        switch (key)
        {
            case SettingKeys.AlwaysOnTop:
                changed = Config.AlwaysOnTop != value;
                Config.AlwaysOnTop = value;
                break;
            case SettingKeys.LightMode:
                string theme = value ? "light" : "dark";
                changed = !string.Equals(Config.Theme, theme, StringComparison.Ordinal);
                Config.Theme = theme;
                break;
            case SettingKeys.ShowCpu:
                changed = Config.ShowCpu != value;
                Config.ShowCpu = value;
                break;
            case SettingKeys.PerCore:
                changed = IsSeparated(Config.CpuMode) != value;
                Config.CpuMode = value ? "separated" : "total";
                break;
            case SettingKeys.CpuTemp:
                changed = Config.CpuTemperature != value;
                Config.CpuTemperature = value;
                if (changed)
                {
                    NudgeRequested?.Invoke();
                }
                break;
            case SettingKeys.ShowRam:
                changed = Config.ShowRam != value;
                Config.ShowRam = value;
                break;
            case SettingKeys.ShowDisk:
                changed = Config.ShowDisk != value;
                Config.ShowDisk = value;
                break;
            case SettingKeys.PerDrive:
                changed = IsSeparated(Config.DiskMode) != value;
                Config.DiskMode = value ? "separated" : "total";
                break;
            case SettingKeys.ShowNetworkDrives:
                changed = Config.IncludeNetwork != value;
                Config.IncludeNetwork = value;
                if (changed)
                {
                    NudgeRequested?.Invoke();
                }
                break;
            case SettingKeys.ShowGpu:
                changed = Config.ShowGpu != value;
                Config.ShowGpu = value;
                if (changed)
                {
                    NudgeRequested?.Invoke();
                }
                break;
            case SettingKeys.ShowNetwork:
                changed = Config.ShowNetwork != value;
                Config.ShowNetwork = value;
                break;
            case SettingKeys.ShowBattery:
                changed = Config.ShowBattery != value;
                Config.ShowBattery = value;
                break;
            case SettingKeys.PerAdapter:
                changed = IsSeparated(Config.NetworkMode) != value;
                Config.NetworkMode = value ? "separated" : "total";
                break;
            case SettingKeys.TempEstimate:
                changed = Config.TempEstimate != value;
                Config.TempEstimate = value;
                if (changed)
                {
                    NudgeRequested?.Invoke();
                }
                break;
            case SettingKeys.IncludeRemovable:
                changed = Config.IncludeRemovable != value;
                Config.IncludeRemovable = value;
                break;
            case SettingKeys.IncludeWireless:
                changed = Config.IncludeWireless != value;
                Config.IncludeWireless = value;
                break;
            case SettingKeys.AutoCheckUpdates:
                changed = Config.AutoCheckUpdates != value;
                Config.AutoCheckUpdates = value;
                break;
            case SettingKeys.Snap:
                changed = Config.Snap != value;
                Config.Snap = value;
                break;
            case SettingKeys.Autostart:
                changed = _autostart.IsEnabled != value;
                if (changed)
                {
                    _autostart.Set(value);
                }
                break;
            case SettingKeys.CloseAction:
                string action = value ? "tray" : "exit";
                changed = !string.Equals(Config.CloseAction, action, StringComparison.Ordinal);
                Config.CloseAction = action;
                break;
            default:
                return false;
        }

        if (!changed)
        {
            return true;
        }

        if (key == SettingKeys.AlwaysOnTop)
        {
            TopmostChanged?.Invoke();
        }
        Save();
        Rebuild();
        StructureChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Apply a module page's option (§2). The keys are the config keys, so the
    /// page and the stored file agree; each one takes the old sidebar check's
    /// path, which saves and refreshes every view at once.
    /// </summary>
    public bool SetOption(string configKey, bool value) => configKey switch
    {
        "cpu_mode" => SetBool(SettingKeys.PerCore, value),
        "cpu_temperature" => SetBool(SettingKeys.CpuTemp, value),
        "temp_estimate" => SetBool(SettingKeys.TempEstimate, value),
        "disk_mode" => SetBool(SettingKeys.PerDrive, value),
        "include_removable" => SetBool(SettingKeys.IncludeRemovable, value),
        "include_network" => SetBool(SettingKeys.ShowNetworkDrives, value),
        "network_mode" => SetBool(SettingKeys.PerAdapter, value),
        "include_wireless" => SetBool(SettingKeys.IncludeWireless, value),
        _ => false,
    };

    /// <summary>
    /// Apply a slider. Opacity and font size are live: pass
    /// <paramref name="save"/> false while the pointer is down and save once on
    /// release, which is what the Windows build does.
    /// </summary>
    public bool SetDial(string key, double value, bool save = true)
    {
        if (!double.IsFinite(value))
        {
            return false;
        }

        bool changed;
        switch (key)
        {
            case SettingKeys.Opacity:
                double opacity = Math.Clamp(value, 0.35, 1.0);
                changed = Math.Abs(Config.Opacity - opacity) > 0.0005;
                Config.Opacity = opacity;
                break;
            case SettingKeys.FontSize:
                double scale = TypeRamp.ClampScale(value);
                changed = Math.Abs(Config.FontScale - scale) > 0.0005;
                Config.FontScale = scale;
                break;
            default:
                return false;
        }

        if (!changed)
        {
            // A dial release always saves, even when the live drag already
            // applied the value: the release is the commit.
            if (save)
            {
                Save();
            }
            return true;
        }

        _ramp = new TypeRamp(Config.FontScale);
        Rebuild();
        if (save)
        {
            Save();
        }
        AppearanceChanged?.Invoke();
        return true;
    }

    /// <summary>Apply a radio group: "close_action", "speed" or "language".</summary>
    public bool SetChoice(string key, string value)
    {
        switch (key)
        {
            case SettingKeys.CloseAction:
                if (value is not ("tray" or "exit"))
                {
                    return false;
                }
                if (string.Equals(Config.CloseAction, value, StringComparison.Ordinal))
                {
                    return true;
                }
                Config.CloseAction = value;
                break;
            case SettingKeys.Speed:
                if (value is not ("eco" or "balanced" or "fast"))
                {
                    return false;
                }
                if (string.Equals(Config.Speed, value, StringComparison.Ordinal))
                {
                    return true;
                }
                Config.Speed = value;
                Save();
                Rebuild();
                StructureChanged?.Invoke();
                NudgeRequested?.Invoke();
                return true;
            case SettingKeys.Language:
                if (!Language.Set(value))
                {
                    return false;
                }
                Config.Lang = Language.Code;
                break;
            default:
                return false;
        }

        Save();
        Rebuild();
        StructureChanged?.Invoke();
        return true;
    }

    /// <summary>The current value of one of the two dials.</summary>
    public double DialValue(string key) =>
        key == SettingKeys.FontSize ? Config.FontScale : Config.Opacity;

    /// <summary>The spec's "Reset window size": defaults back, Overall auto-fits again.</summary>
    public void ResetSize()
    {
        Config.OverallSized = false;
        Config.FullW = 0;
        Config.FullH = 0;
        Config.MainW = 0;
        Config.MainH = 0;
        Config.WidgetW = 270;
        Config.WidgetH = 104;
        Config.OverallW = 630;
        Config.OverallH = 480;
        Save();
        RouteChanged?.Invoke();
    }

    public void Restart()
    {
        Save();
        RestartRequested?.Invoke();
    }

    public void Close()
    {
        Save();
        CloseRequested?.Invoke();
    }

    public void Open(string windowId) => OpenRequested?.Invoke(windowId);

    public void Save() => Config.Save(_configPath);

    /// <summary>Wake the sampler: a setting that changes what is read was toggled.</summary>
    public void Nudge() => NudgeRequested?.Invoke();

    /// <summary>systemctl's first stderr line, which is the part that says why.</summary>
    private static string FirstLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        int newline = text.IndexOf('\n');
        string line = newline < 0 ? text : text[..newline];
        return line.TrimEnd('\r').Trim();
    }

    // ---------------------------------------------------------- updater

    /// <summary>One check. Errors become the panel's "check failed" line.</summary>
    public async Task<UpdateCheck> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (Updates.Busy)
        {
            return new UpdateCheck(false, null, "a check is already running");
        }

        Updates.StartCheck();
        Rebuild();
        StructureChanged?.Invoke();
        UpdateCheck result;
        try
        {
            result = await _updater.CheckAsync(cancellationToken).ConfigureAwait(true);
            if (result.Succeeded)
            {
                Updates.Checked(Updates.CurrentVersion, result.Version);
            }
            else
            {
                Updates.CheckFailed();
            }
        }
        catch (Exception exception)
        {
            result = new UpdateCheck(false, null, exception.Message);
            Updates.CheckFailed();
        }
        Rebuild();
        StructureChanged?.Invoke();
        return result;
    }

    /// <summary>The sidebar button's check.</summary>
    public Task<UpdateCheck> CheckUpdatesAsync() =>
        CheckForUpdatesAsync(CancellationToken.None);

    /// <summary>Download and start the installer; false clears the pending update.</summary>
    public async Task<bool> DownloadUpdateAsync()
    {
        if (Updates.Busy || Updates.PendingVersion is not { } version)
        {
            return false;
        }

        Updates.StartDownload();
        Rebuild();
        StructureChanged?.Invoke();
        bool installed;
        try
        {
            var progress = new Progress<int>(percent =>
            {
                Updates.DownloadProgress(percent);
                Rebuild();
                StructureChanged?.Invoke();
            });
            installed = await _updater
                .DownloadAndInstallAsync(version, progress, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            installed = false;
        }

        if (installed)
        {
            if (_updater.OpenedPackageInstaller)
            {
                Updates.PackageOpened();
            }
            else
            {
                Updates.Installing();
            }
        }
        else
        {
            Updates.DownloadFailed(_updater.SavedPackagePath);
        }
        Rebuild();
        StructureChanged?.Invoke();
        return installed;
    }

    // ---------------------------------------------------------- builders

    /// <summary>Rebuild every derived model from the config and the snapshot.</summary>
    public void Rebuild()
    {
        RebuildData();
        _sidebar = BuildSidebar();
        _settings = BuildSettings();
        _manual = BuildManual();
        _about = BuildAbout();
        _contextMenu = BuildContextMenu();
    }

    /// <summary>Only the parts that a fresh snapshot can change.</summary>
    private void RebuildData()
    {
        _widgetPages = WidgetBoard.Build(_snapshot, Config, Language, Palette, Alerts);
        Rotation.SetPages(_widgetPages);
        _overall = OverallBoard.Build(_snapshot, Config, Language, Palette, Alerts);
        RebuildMemory();
        RebuildFull();
        ReconcileMain();
        RebuildMainSidebar();
    }

    /// <summary>
    /// Keep <see cref="MainState"/> in step with the pages that exist now. A
    /// device that was unplugged falls back to Overall; the remembered key
    /// stays, so plugging it back in shows its page again.
    /// </summary>
    private void ReconcileMain()
    {
        string page = _main.Reconcile(MainPages(), Config.MainPage);
        if (!string.Equals(page, _lastReconciledPage, StringComparison.Ordinal))
        {
            // Reconcile reset the peaks for the new page; rebuild the cards so
            // each "Highest" seeds from the history already on screen.
            _lastReconciledPage = page;
            RebuildFull();
        }
    }

    private void RebuildMainSidebar() =>
        _mainSidebar = SidebarBoard.Build(_full, _snapshot, History, Language);

    /// <summary>
    /// Build both memory tables, clearing a graphed group that is no longer
    /// running (spec 5c: the selection clears when the group disappears).
    /// </summary>
    private void RebuildMemory()
    {
        _memory = MemoryTabBoard.Build(
            _snapshot, Language, _memoryQuery, _ownPid, _ownUid, _graphedGroup);

        // The graph clears when the group stops running, not when a search
        // hides it. An empty process list means sampling is off, not gone.
        if (_graphedGroup is not null
            && _snapshot is { Processes.Count: > 0 }
            && !_snapshot.Processes.Any(p =>
                string.Equals(p.Name, _graphedGroup, StringComparison.OrdinalIgnoreCase)))
        {
            _graphedGroup = null;
            _memory = MemoryTabBoard.Build(
                _snapshot, Language, _memoryQuery, _ownPid, _ownUid, null);
        }
    }

    private void RebuildFull()
    {
        FullModel built = FullBoard.Build(
            _snapshot,
            Config,
            Language,
            Palette,
            History,
            _fullSelection,
            _main.Page,
            _main.Peaks);

        // The page in Main is the selection. The fixed pages (Overall, Apps,
        // Processes) are not module pages, so nothing is selected then rather
        // than the first device pretending to be it.
        _full = new FullModel
        {
            Tabs = built.Tabs,
            SelectedKey = built.Tabs.FirstOrDefault(
                tab => string.Equals(tab.Key, _main.Page, StringComparison.Ordinal))?.Key,
        };
    }

    private IReadOnlyList<UiRow> BuildSidebar()
    {
        // R-030 §2: the Settings expander holds only the app-wide options. The
        // per-module options (cpu_mode, cpu_temperature, disk_mode,
        // include_network, network_mode) moved to the module pages, and the
        // show_* switches stay together under "Modules".
        var rows = new List<UiRow>
        {
            new HeadingRow(Language["window_settings"]),
            new CheckRow(SettingKeys.AlwaysOnTop, Language["always_on_top"], Config.AlwaysOnTop),
            new CheckRow(SettingKeys.LightMode, Language["light_mode"], IsLight),
            Dial(SettingKeys.Opacity, "opacity", 0.35, 1.0),
            Dial(SettingKeys.FontSize, "font_size", 0.8, 1.6),

            new HeadingRow(Language["modules"]),
            new CheckRow(SettingKeys.ShowCpu, Language["show_cpu"], Config.ShowCpu),
            new CheckRow(SettingKeys.ShowRam, Language["show_ram"], Config.ShowRam),
            new CheckRow(SettingKeys.ShowDisk, Language["show_disk"], Config.ShowDisk),
            new CheckRow(SettingKeys.ShowGpu, Language["show_gpu"], Config.ShowGpu),
            new CheckRow(SettingKeys.ShowNetwork, Language["show_network"], Config.ShowNetwork),
            new CheckRow(SettingKeys.ShowBattery, Language["show_battery"], Config.ShowBattery),

            // Phase 2a has no extensions, so the extensions heading and its
            // checks have nothing to list and are left out (spec section 4).
            // The expander itself is already called "Settings", so this row —
            // the one that opens the Settings window — must not repeat it
            // (R-030 fix 4).
            new NavRow(IconName.Settings, Language["settings_more"], ActionKeys.OpenSettings, TopMargin: 12),

            new HeadingRow(Language["updates"]),
            new CheckRow(SettingKeys.AutoCheckUpdates, Language["auto_check_updates"], Config.AutoCheckUpdates),
            new TextRow($"{Language["current_version"]} {_version}"),
        };

        rows.Add(new ActionRow(
            Updates.ButtonLabel(Language),
            Updates.State == UpdatePanelState.Phase.Found
                ? ActionKeys.DownloadInstall
                : ActionKeys.CheckUpdates,
            Enabled: !Updates.Busy));
        if (Updates.StatusText(Language) is { } status)
        {
            rows.Add(new TextRow(status));
        }

        // Restart, Manual and About are nav rows in the sidebar itself now, not
        // rows inside this expander (R-030 fix 5).
        return rows;
    }

    private IReadOnlyList<UiRow> BuildSettings()
    {
        return new List<UiRow>
        {
            new HeadingRow(Language["window_settings"]),
            new CheckRow(SettingKeys.AlwaysOnTop, Language["always_on_top"], Config.AlwaysOnTop),
            new CheckRow(SettingKeys.Snap, Language["snap"], Config.Snap),
            new CheckRow(SettingKeys.Autostart, Language["autostart"], _autostart.IsEnabled),
            new CheckRow(SettingKeys.LightMode, Language["light_mode"], IsLight),
            Dial(SettingKeys.Opacity, "opacity", 0.35, 1.0),
            Dial(SettingKeys.FontSize, "font_size", 0.8, 1.6),

            new HeadingRow(Language["close_action"]),
            new RadioRow(SettingKeys.CloseAction, new[]
            {
                new RadioOption("exit", Language["close_to_exit"], Config.CloseAction == "exit"),
                new RadioOption("tray", Language["close_to_tray"], Config.CloseAction == "tray"),
            }),

            new HeadingRow(Language["speed"]),
            new RadioRow(SettingKeys.Speed, new[]
            {
                new RadioOption("eco", Language["eco"], Config.Speed == "eco"),
                new RadioOption("balanced", Language["balanced"], Config.Speed == "balanced"),
                new RadioOption("fast", Language["fast"], Config.Speed == "fast"),
            }),

            new HeadingRow(Language["language"]),
            new RadioRow(SettingKeys.Language, new[]
            {
                // Both endonyms are hard-coded and never translated (spec 12).
                new RadioOption("th", "ไทย", Language.Code == "th"),
                new RadioOption("en", "English", Language.Code == "en"),
            }),

            // The SMART / administrator section is dropped on Linux: R-027
            // keeps the app unprivileged.
        };
    }

    private IReadOnlyList<UiRow> BuildManual()
    {
        var rows = new List<UiRow>();
        for (int i = 1; i <= 10; i++)
        {
            string heading = "manual_" + i + "_h";
            string text = "manual_" + i + "_t";
            if (!Language.Has(heading) || !Language.Has(text))
            {
                break;
            }
            rows.Add(new HeadingRow(Language[heading]));
            rows.Add(new TextRow(Language[text]));
            rows.Add(new SeparatorRow());
        }
        if (rows.Count > 0 && rows[^1] is SeparatorRow)
        {
            rows.RemoveAt(rows.Count - 1);
        }
        return rows;
    }

    private IReadOnlyList<UiRow> BuildAbout()
    {
        return new List<UiRow>
        {
            new TitleRow("System Monitor"),
            new TextRow($"{Language["about_version"]} {_version}"),
            new TextRow(Language["about_desc"]),

            new HeadingRow(Language["about_developer"]),
            new TextRow("Apichart Chantanis", Muted: false),

            new HeadingRow(Language["about_copyright"]),
            new TextRow("© 2026 Apichart Chantanis", Muted: false),

            new HeadingRow(Language["about_terms"]),
            new TextRow(Language["about_terms_text"]),

            new HeadingRow(Language["about_credit"]),
            new TextRow($"{Language["about_credit_text"]} epinephrinerx/SystemMonitor", Muted: false),

            new NavRow(IconName.FullData, Language["about_source"], ActionKeys.ProjectSource),
        };
    }

    private IReadOnlyList<UiRow> BuildContextMenu()
    {
        return new List<UiRow>
        {
            new ActionRow(Language[View.FirstMenuItemKey], ActionKeys.ContextExpand),
            new ActionRow(Language[View.FullScreenItemKey], ActionKeys.ContextFullScreen),
            new ActionRow(Language["reset_size"], ActionKeys.ResetSize),
            new ActionRow(Language["restart"], ActionKeys.Restart),
            new SeparatorRow(),
            new ActionRow(Language["close"], ActionKeys.Close),
        };
    }

    private DialRow Dial(string key, string captionKey, double minimum, double maximum)
    {
        double value = key == SettingKeys.FontSize ? Config.FontScale : Config.Opacity;
        return new DialRow(
            key,
            Language[captionKey],
            Readout(value),
            value,
            minimum,
            maximum);
    }

    /// <summary>The dial readout: a whole percentage, "NN%".</summary>
    public static string Readout(double value) => (int)Math.Round(value * 100) + "%";

    private bool IsLight => string.Equals(Config.Theme, "light", StringComparison.OrdinalIgnoreCase);

    private static bool IsSeparated(string mode) =>
        string.Equals(mode, "separated", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The kill target used when the window did not supply one: every attempt is
/// reported as failed rather than reaching a process. The app always supplies
/// the real one.
/// </summary>
internal sealed class NoopKillTargets : IProcessSignals
{
    public ProcessFacts? Read(int pid) => null;

    public PinnedResult Pin(int pid) => PinnedResult.Failed("process access is not available");

    public string? Signal(int pid, bool force) => "process access is not available";
}
