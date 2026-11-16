using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace SysMonitor.Setup;

/// <summary>What the user chose in the wizard (or on the command line).</summary>
public sealed record InstallOptions(bool DesktopShortcut, bool StartMenu, bool Autostart);

/// <summary>
/// Where the files to install come from. The real setup embeds them; a test
/// supplies its own so an install can run against a temporary folder.
/// </summary>
public sealed record InstallSources(
    Func<Stream> OpenApp,
    string UninstallerSource,
    IReadOnlyList<PayloadFile> Extensions)
{
    public static InstallSources FromAssembly() => new(
        () => Assembly.GetExecutingAssembly().GetManifestResourceStream("SystemMonitor.exe")
              ?? throw new InvalidOperationException(
                  "This setup was built without the application payload."),
        Environment.ProcessPath!,
        Installer.ReadExtensionPayload());
}

/// <summary>
/// The parts of an install that reach outside the install folder: the
/// registry, the Scheduled Task, shortcuts and the running program. Behind an
/// interface so a test can record what would happen instead of doing it to the
/// machine it runs on.
/// </summary>
public interface IInstallHost
{
    /// <summary>Whether this process may write to Program Files and the machine registry.</summary>
    bool IsElevated { get; }

    /// <summary>Ask a running copy at <paramref name="exePath"/> to exit before its file is replaced.</summary>
    void StopRunningApp(string exePath);

    void WriteShortcut(bool wanted, string linkPath, string target, string workingDirectory);

    void RegisterUninstall(InstallLayout layout, int estimatedSizeKb);

    void UnregisterUninstall(InstallLayout layout);

    /// <summary>
    /// Start at logon on or off. For the machine-wide install "off" is a task that
    /// is there but switched off, so the app's own checkbox can switch it on
    /// again and an update does not lose the user's choice.
    /// </summary>
    void SetAutostart(InstallLayout layout, bool enabled);

    /// <summary>Take the start-up entry away altogether (uninstall, migration).</summary>
    void RemoveAutostart(InstallLayout layout);

    bool IsAutostartEnabled(InstallLayout layout);
}

/// <summary>The real thing: registry, Scheduled Task, shell shortcuts, processes.</summary>
public sealed class WindowsInstallHost : IInstallHost
{
    public static WindowsInstallHost Instance { get; } = new();

    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Installer.Key;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsElevated => Elevation.IsElevated();

    private static RegistryKey Root(InstallLayout layout) =>
        layout.UninstallHive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;

    public void StopRunningApp(string exePath)
    {
        foreach (Process process in Process.GetProcessesByName("SystemMonitor"))
        {
            try
            {
                if (!string.Equals(process.MainModule?.FileName, exePath,
                                   StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                process.CloseMainWindow();
                if (!process.WaitForExit(4000))
                {
                    process.Kill();
                    process.WaitForExit(4000);
                }
            }
            catch (Exception)
            {
                // A process we cannot inspect is a process we leave alone.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public void WriteShortcut(bool wanted, string linkPath, string target, string workingDirectory) =>
        Shortcuts.Write(wanted, linkPath, target, workingDirectory);

    public void RegisterUninstall(InstallLayout layout, int estimatedSizeKb)
    {
        using RegistryKey key = Root(layout).CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", Installer.DisplayName);
        key.SetValue("DisplayVersion", Installer.Version);
        key.SetValue("Publisher", Installer.Publisher);
        key.SetValue("DisplayIcon", layout.ExePath);
        key.SetValue("InstallLocation", layout.InstallDir);
        key.SetValue("UninstallString", "\"" + layout.UninstallPath + "\"");
        key.SetValue("QuietUninstallString", "\"" + layout.UninstallPath + "\" /S");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", estimatedSizeKb, RegistryValueKind.DWord);
    }

    public void UnregisterUninstall(InstallLayout layout)
    {
        try
        {
            Root(layout).DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        }
        catch (Exception)
        {
            // Already gone, or someone else removed it; either way we are done.
        }
    }

    public void SetAutostart(InstallLayout layout, bool enabled)
    {
        if (layout.Autostart == AutostartMechanism.ScheduledTask)
        {
            // The SID, not DOMAIN\name: it resolves the same for local, domain and
            // Microsoft/Azure AD accounts.
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            ScheduledTasks.Create(layout, identity.User!.Value, enabled);
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(Installer.Key, "\"" + layout.ExePath + "\"");
        }
        else
        {
            key.DeleteValue(Installer.Key, throwOnMissingValue: false);
        }
    }

    public void RemoveAutostart(InstallLayout layout)
    {
        if (layout.Autostart == AutostartMechanism.ScheduledTask)
        {
            ScheduledTasks.Delete(layout.ScheduledTaskName);
            return;
        }
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.DeleteValue(Installer.Key, throwOnMissingValue: false);
    }

    public bool IsAutostartEnabled(InstallLayout layout)
    {
        if (layout.Autostart == AutostartMechanism.ScheduledTask)
        {
            return ScheduledTasks.IsEnabled(layout.ScheduledTaskName);
        }
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Installer.Key) is string;
    }
}

/// <summary>
/// Keeps this setup's own file from being renamed or replaced while it runs.
/// A running program can be renamed on Windows, so a process of the same user
/// could swap another file into the setup's path after the UAC prompt, or
/// after the elevated copy has started, and have that file copied into
/// Program Files as the uninstaller. Holding the file open without delete
/// sharing blocks the rename and the overwrite, and still lets the loader and
/// the copy read it.
/// </summary>
public static class SelfImage
{
    private static FileStream? _held;

    /// <summary>Open the file for reading with write and delete sharing denied. Null when that is not possible.</summary>
    public static FileStream? Hold(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether this process holds its own executable (see <see cref="HoldOwn"/>).</summary>
    public static bool IsHeld => _held is not null;

    /// <summary>
    /// A machine-wide install copies this file into Program Files and starts an
    /// elevated copy of it, so it does not go ahead on a file it could not hold.
    /// </summary>
    public static void Require()
    {
        if (!IsHeld)
        {
            throw new IOException(
                "The setup could not lock its own file, so it will not install machine-wide. "
                + "Copy the setup to a folder of your own and run it from there.");
        }
    }

    /// <summary>Hold this process's own executable until the process ends.</summary>
    public static void HoldOwn()
    {
        if (_held is null && Environment.ProcessPath is string path)
        {
            _held = Hold(path);
        }
    }
}

/// <summary>Administrator rights: asking whether we have them, and asking for them.</summary>
public static class Elevation
{
    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Start this same executable again with the given arguments and a UAC
    /// prompt. Returns false when the user declines the prompt.
    /// </summary>
    public static bool Relaunch(IEnumerable<string> arguments) => Start(arguments, wait: false) is not null;

    /// <summary>
    /// The same, but waits for the elevated copy and returns its exit code
    /// (null when the user declines the prompt), so an unattended run can
    /// report what really happened.
    /// </summary>
    public static int? RelaunchAndWait(IEnumerable<string> arguments) => Start(arguments, wait: true);

    private static int? Start(IEnumerable<string> arguments, bool wait)
    {
        // The elevated copy is this same file: only start it from one that is held.
        SelfImage.Require();
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }
            if (!wait)
            {
                return 0;
            }
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user said no. Not a failure to report.
            return null;
        }
    }
}

/// <summary>
/// A directory only administrators can enter or change. The elevated
/// uninstaller copies itself somewhere to run, because a program cannot delete
/// its own folder; that place must not be one a user-level process can write
/// to, or it could swap the image or plant a DLL beside it and have it run
/// elevated.
/// </summary>
public static class PrivateDirectory
{
    /// <summary>Administrators and SYSTEM: full control. Nobody else, and nothing inherited.</summary>
    public static DirectorySecurity AdminOnlySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (WellKnownSidType sid in new[]
                 { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        return security;
    }

    /// <summary>
    /// Creates a new, uniquely named directory under <paramref name="parent"/>
    /// with the admin-only ACL applied at creation, so there is no moment at
    /// which it is open to anyone else.
    /// </summary>
    public static string Create(string parent, string prefix)
    {
        string path = Path.Combine(parent, prefix + Guid.NewGuid().ToString("N"));
        new DirectoryInfo(path).Create(AdminOnlySecurity());
        return path;
    }
}
