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

    public ShellModel(
        AppConfig? config = null,
        Lang? lang = null,
        IAutostart? autostart = null,
        IUpdater? updater = null,
        string? version = null,
        string? configPath = null,
        Func<long>? clock = null)
    {
        Config = config ?? new AppConfig();
        Config.Normalise();
        _configPath = configPath;
        _clock = clock ?? (() => Environment.TickCount64);

        Language = lang ?? new Lang();
        Language.Set(Config.Lang);
        Config.Lang = Language.Code;

        _autostart = autostart ?? new NoopAutostart();
        _version = string.IsNullOrWhiteSpace(version) ? "4.3.0" : version!;
        _updater = updater ?? new NoopUpdater(_version);

        _ramp = new TypeRamp(Config.FontScale);
        View = new ViewState(ViewModeExtensions.Parse(Config.LastView, ViewMode.Widget));
        Rotation = new RotationState();
        Alerts = new DiskAlertTracker();
        History = new HistoryBoard();
        Updates = new UpdatePanelState(_version);

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
    public string Version => _version;

    public Palette Palette => Palette.For(Config.Theme);
    public TypeRamp Ramp => _ramp;
    public Snapshot? Snapshot => _snapshot;
    public bool Ready => _snapshot is { Ready: true };

    public IReadOnlyList<WidgetPage> WidgetPages => _widgetPages;
    public OverallModel Overall => _overall;
    public IReadOnlyList<UiRow> Sidebar => _sidebar;
    public IReadOnlyList<UiRow> Settings => _settings;
    public IReadOnlyList<UiRow> Manual => _manual;
    public IReadOnlyList<UiRow> About => _about;

    /// <summary>The main right-click menu, rebuilt in the current language.</summary>
    public IReadOnlyList<UiRow> ContextMenu => _contextMenu;

    public WidgetPage? CurrentPage => Rotation.Page;
    public bool Paused => Rotation.Paused;
    public bool IsWidget => View.IsWidget;
    public string FullTitle => Language["full_data"];
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
    public bool OverallHeaderFull() => Apply(View.OverallHeaderFull);
    public bool OverallHeaderWidget() => Apply(View.OverallHeaderWidget);
    public bool SidebarFullData() => Apply(View.SidebarFullData);
    public bool SidebarOpenWidget() => Apply(View.SidebarOpenWidget);
    public bool FullHeaderOverall() => Apply(View.FullHeaderOverall);
    public bool FullHeaderWidget() => Apply(View.FullHeaderWidget);
    public bool FullRailOverall() => Apply(View.FullRailOverall);
    public bool FullRailWidget() => Apply(View.FullRailWidget);

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
            case SettingKeys.PerAdapter:
                changed = IsSeparated(Config.NetworkMode) != value;
                Config.NetworkMode = value ? "separated" : "total";
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

    // ---------------------------------------------------------- updater

    /// <summary>One check. Errors become the panel's "check failed" line.</summary>
    public async Task CheckUpdatesAsync()
    {
        if (Updates.Busy)
        {
            return;
        }

        Updates.StartCheck();
        Rebuild();
        StructureChanged?.Invoke();
        try
        {
            UpdateCheck result = await _updater.CheckAsync(CancellationToken.None)
                .ConfigureAwait(true);
            if (result.Succeeded)
            {
                Updates.Checked(Updates.CurrentVersion, result.Version);
            }
            else
            {
                Updates.CheckFailed();
            }
        }
        catch (Exception)
        {
            Updates.CheckFailed();
        }
        Rebuild();
        StructureChanged?.Invoke();
    }

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
            Updates.Installing();
        }
        else
        {
            Updates.DownloadFailed();
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
    }

    private IReadOnlyList<UiRow> BuildSidebar()
    {
        var rows = new List<UiRow>
        {
            new NavRow(IconName.FullData, Language["full_data"], ActionKeys.OpenFull),
            new NavRow(IconName.Widget, Language["open_widget"], ActionKeys.OpenWidget),

            new HeadingRow(Language["window_settings"]),
            new CheckRow(SettingKeys.AlwaysOnTop, Language["always_on_top"], Config.AlwaysOnTop),
            new CheckRow(SettingKeys.LightMode, Language["light_mode"], IsLight),
            Dial(SettingKeys.Opacity, "opacity", 0.35, 1.0),
            Dial(SettingKeys.FontSize, "font_size", 0.8, 1.6),

            new HeadingRow(Language["display_settings"]),
            new CheckRow(SettingKeys.ShowCpu, Language["show_cpu"], Config.ShowCpu),
            new CheckRow(SettingKeys.PerCore, Language["per_core"], IsSeparated(Config.CpuMode), Indented: true),
            new CheckRow(SettingKeys.CpuTemp, Language["cpu_temp"], Config.CpuTemperature, Indented: true),
            new CheckRow(SettingKeys.ShowRam, Language["show_ram"], Config.ShowRam),
            new CheckRow(SettingKeys.ShowDisk, Language["show_disk"], Config.ShowDisk),
            new CheckRow(SettingKeys.PerDrive, Language["per_drive"], IsSeparated(Config.DiskMode), Indented: true),
            new CheckRow(SettingKeys.ShowNetworkDrives, Language["show_network_drives"], Config.IncludeNetwork, Indented: true),
            new CheckRow(SettingKeys.ShowGpu, Language["show_gpu"], Config.ShowGpu),
            new CheckRow(SettingKeys.ShowNetwork, Language["show_network"], Config.ShowNetwork),
            new CheckRow(SettingKeys.PerAdapter, Language["per_adapter"], IsSeparated(Config.NetworkMode), Indented: true),

            // Phase 2a has no extensions, so the extensions heading and its
            // checks have nothing to list and are left out (spec section 4).
            new NavRow(IconName.Settings, Language["settings"], ActionKeys.OpenSettings, TopMargin: 12),

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

        rows.Add(new ActionRow(Language["restart"], ActionKeys.Restart));
        rows.Add(new NavRow(IconName.Manual, Language["manual"], ActionKeys.OpenManual));
        rows.Add(new NavRow(IconName.About, Language["about"], ActionKeys.OpenAbout));
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
