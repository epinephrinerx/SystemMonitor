using System.IO;
using Microsoft.Win32;

namespace SysMonitor.Setup;

/// <summary>How the program is installed (R-022).</summary>
public enum InstallScope
{
    /// <summary>Under %LOCALAPPDATA%, the current user's registry, no UAC.</summary>
    PerUser,

    /// <summary>Under Program Files, the machine registry, one UAC prompt.</summary>
    Admin,
}

/// <summary>How the program is started at logon.</summary>
public enum AutostartMechanism
{
    /// <summary>A value under HKCU\...\Run.</summary>
    RunKey,

    /// <summary>A Scheduled Task that runs with the highest privileges.</summary>
    ScheduledTask,
}

/// <summary>
/// The Windows folders an install is rooted in. Passed in rather than read
/// inside <see cref="InstallLayout"/> so the layout can be tested against
/// made-up folders and never needs the real machine.
/// </summary>
public sealed record KnownFolders(
    string LocalAppData,
    string AppData,
    string ProgramFiles,
    string Desktop,
    string Programs,
    string CommonDesktop,
    string CommonPrograms)
{
    /// <summary>The folders of the user and machine this process runs on.</summary>
    public static KnownFolders Current() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
}

/// <summary>
/// Where one kind of install puts things and which registry hive and
/// start-up mechanism it uses. Pure data: nothing here touches the disk, the
/// registry or the task scheduler, so what each scope would do can be checked
/// without installing anything.
///
/// The settings folder is the same for both scopes. R-022 keeps the user's
/// config in %APPDATA%\SystemMonitor whichever way the program was installed.
/// </summary>
public sealed record InstallLayout
{
    private InstallLayout(
        InstallScope scope,
        string installDir,
        string settingsDir,
        string desktopDirectory,
        string startMenuDirectory)
    {
        Scope = scope;
        InstallDir = installDir;
        SettingsDir = settingsDir;
        DesktopDirectory = desktopDirectory;
        StartMenuDirectory = startMenuDirectory;
    }

    public InstallScope Scope { get; }
    public string InstallDir { get; }
    public string SettingsDir { get; }
    public string DesktopDirectory { get; }
    public string StartMenuDirectory { get; }

    public string ExePath => Path.Combine(InstallDir, Installer.ExeName);
    public string UninstallPath => Path.Combine(InstallDir, Installer.UninstallName);
    public string CorePath => Path.Combine(InstallDir, Installer.CoreName);
    public string ExtensionsDir => Path.Combine(InstallDir, "extensions");

    /// <summary>
    /// Written only by the machine-wide install. The program reads it to know
    /// it was installed for administrator use (phase 3); it lives under Program
    /// Files, where a standard user cannot create or edit it.
    /// </summary>
    public const string ModeMarkerName = "install-mode.json";
    public string ModeMarkerPath => Path.Combine(InstallDir, ModeMarkerName);
    public string DesktopShortcut => Path.Combine(DesktopDirectory, Installer.DisplayName + ".lnk");
    public string StartMenuShortcut => Path.Combine(StartMenuDirectory, Installer.DisplayName + ".lnk");

    /// <summary>Only the machine-wide install needs administrator rights to write.</summary>
    public bool RequiresElevation => Scope == InstallScope.Admin;

    /// <summary>The hive that holds this install's "Apps &amp; features" entry.</summary>
    public RegistryHive UninstallHive =>
        Scope == InstallScope.Admin ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;

    /// <summary>
    /// The machine-wide install must not use a Run value: that would start
    /// the program without elevation, and a value any user process can edit is
    /// the wrong place to launch an elevated program from.
    /// </summary>
    public AutostartMechanism Autostart =>
        Scope == InstallScope.Admin ? AutostartMechanism.ScheduledTask : AutostartMechanism.RunKey;

    /// <summary>The Scheduled Task name; only used by <see cref="AutostartMechanism.ScheduledTask"/>.</summary>
    public string ScheduledTaskName => Installer.Key;

    public static InstallLayout Create(InstallScope scope, KnownFolders folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        RequireAbsolute(folders.AppData, nameof(folders.AppData));
        string settings = Path.Combine(folders.AppData, Installer.AppName);

        if (scope == InstallScope.Admin)
        {
            RequireAbsolute(folders.ProgramFiles, nameof(folders.ProgramFiles));
            RequireAbsolute(folders.CommonDesktop, nameof(folders.CommonDesktop));
            RequireAbsolute(folders.CommonPrograms, nameof(folders.CommonPrograms));
            return new InstallLayout(
                scope,
                Path.Combine(folders.ProgramFiles, Installer.Key),
                settings,
                folders.CommonDesktop,
                folders.CommonPrograms);
        }

        RequireAbsolute(folders.LocalAppData, nameof(folders.LocalAppData));
        RequireAbsolute(folders.Desktop, nameof(folders.Desktop));
        RequireAbsolute(folders.Programs, nameof(folders.Programs));
        return new InstallLayout(
            scope,
            Path.Combine(folders.LocalAppData, "Programs", Installer.Key),
            settings,
            folders.Desktop,
            folders.Programs);
    }

    public static InstallLayout Create(InstallScope scope) => Create(scope, KnownFolders.Current());

    /// <summary>
    /// Null when Windows did not give a folder this scope needs. A per-user run
    /// must not die because the machine-wide folders cannot be resolved, or the
    /// other way round.
    /// </summary>
    public static InstallLayout? TryCreate(InstallScope scope, KnownFolders folders)
    {
        try
        {
            return Create(scope, folders);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static InstallLayout? TryCreate(InstallScope scope) => TryCreate(scope, KnownFolders.Current());

    /// <summary>
    /// A root that is empty or relative would put the install somewhere
    /// depending on the working directory. Better to stop than to guess.
    /// </summary>
    private static void RequireAbsolute(string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException(
                $"The Windows folder '{name}' is missing or not an absolute path.", name);
        }
    }
}
