namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The icons the spec draws from Segoe MDL2 Assets. The presentation layer
/// names them; the view decides what shape to draw, so nothing here depends on
/// a font that Linux does not have.
/// </summary>
public enum IconName
{
    None,
    Close,
    Pause,
    Play,
    ChevronLeft,
    ChevronRight,
    ChevronUp,
    ChevronDown,
    FullScreen,
    Widget,
    Home,
    FullData,
    Settings,
    Manual,
    About,
    Dismiss,
    Delete,
    Lock,
    Restart,
    Stop,
    Link,
    Power,
    App,
}

/// <summary>
/// The setting a row edits. The view passes the key back to
/// <see cref="ShellModel.SetBool"/>, <see cref="ShellModel.SetDial"/> or
/// <see cref="ShellModel.SetChoice"/>; it never writes the config itself.
/// </summary>
public static class SettingKeys
{
    public const string AlwaysOnTop = "always_on_top";
    public const string LightMode = "light_mode";
    public const string Opacity = "opacity";
    public const string FontSize = "font_size";
    public const string ShowCpu = "show_cpu";
    public const string PerCore = "per_core";
    public const string CpuTemp = "cpu_temp";
    public const string ShowRam = "show_ram";
    public const string ShowDisk = "show_disk";
    public const string PerDrive = "per_drive";
    public const string ShowNetworkDrives = "show_network_drives";
    public const string ShowGpu = "show_gpu";
    public const string ShowNetwork = "show_network";
    public const string ShowBattery = "show_battery";
    public const string PerAdapter = "per_adapter";
    public const string TempEstimate = "temp_estimate";
    public const string IncludeRemovable = "include_removable";
    public const string IncludeWireless = "include_wireless";
    public const string AutoCheckUpdates = "auto_check_updates";
    public const string Snap = "snap";
    public const string Autostart = "autostart";
    public const string CloseAction = "close_action";
    public const string Speed = "speed";
    public const string Language = "language";
}

/// <summary>A button click, as a stable name the window can switch on.</summary>
public static class ActionKeys
{
    public const string OpenFull = "open_full";
    public const string OpenOverall = "open_overall";
    public const string OpenWidget = "open_widget";
    public const string OpenSettings = "open_settings";
    public const string OpenManual = "open_manual";
    public const string OpenAbout = "open_about";
    public const string CheckUpdates = "check_updates";
    public const string DownloadInstall = "download_install";
    public const string Restart = "restart";
    public const string Close = "close";
    public const string ResetSize = "reset_size";
    public const string ContextExpand = "context_expand";
    public const string ContextFullScreen = "context_fullscreen";
    public const string ProjectSource = "project_source";
}

/// <summary>The secondary windows, by id.</summary>
public static class WindowIds
{
    public const string Settings = "settings";
    public const string Manual = "manual";
    public const string About = "about";
}

/// <summary>
/// One row of a settings surface. The sidebar, the Settings window and the
/// Manual/About pages all render the same few row kinds, so their content is
/// data that can be checked without a window.
/// </summary>
public abstract record UiRow;

/// <summary>A section heading: uppercased, label colour.</summary>
public sealed record HeadingRow(string Text) : UiRow;

/// <summary>A navigation button: glyph plus label.</summary>
public sealed record NavRow(
    IconName Icon, string Label, string Action, string Tooltip = "", double TopMargin = 0) : UiRow;

/// <summary>A full-width action button.</summary>
public sealed record ActionRow(string Label, string Action, bool Enabled = true) : UiRow;

/// <summary>A checkbox. <paramref name="Indented"/> is the spec's 20 px inset.</summary>
public sealed record CheckRow(
    string Key, string Label, bool IsChecked, bool Indented = false, string Tooltip = "") : UiRow;

/// <summary>A slider with its caption and its "NN%" readout.</summary>
public sealed record DialRow(
    string Key, string Caption, string Readout, double Value, double Minimum, double Maximum) : UiRow;

/// <summary>A radio group, drawn as a wrapping row.</summary>
public sealed record RadioRow(string Key, IReadOnlyList<RadioOption> Options) : UiRow;

public sealed record RadioOption(string Value, string Label, bool Selected);

/// <summary>One line of static text (version line, status line, paragraph).</summary>
public sealed record TextRow(string Text, string Tooltip = "", bool Muted = true) : UiRow;

/// <summary>A page title: the large, text-coloured line at the top of About.</summary>
public sealed record TitleRow(string Text) : UiRow;

/// <summary>A thin rule, used by the context menu and the Manual page.</summary>
public sealed record SeparatorRow : UiRow;
