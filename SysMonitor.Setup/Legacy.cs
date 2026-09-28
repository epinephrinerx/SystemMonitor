using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SysMonitor.Setup;

/// <summary>
/// The builds that install beside this one rather than over it: the retired
/// Python/Tk build ("SysMonitor") and the pre-4.0 C# build ("SysMonitor.NET",
/// this project before it took the name System Monitor).
///
/// All of them install per-user and all put themselves in the Run key -- so a
/// machine with two of them starts two widgets every morning and the older
/// window lands on top of the newer one. It took a process list to work out
/// that a screen "reverted to version 2.0" was version 2.0, still installed
/// and still starting itself.
/// </summary>
public static class Legacy
{
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// The old builds' own keys. Deliberately not the ones this installer
    /// uses: "SysMonitor", "SysMonitor.NET" and "SystemMonitor" are different
    /// products as far as Windows is concerned, which is the whole reason
    /// they coexist until this installer offers to clean them up.
    /// </summary>
    private static readonly (string UninstallKey, string RunValue)[] Identities =
    {
        (@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SysMonitor",
         "SysMonitor"),
        (@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SysMonitor.NET",
         "SysMonitor.NET"),
    };

    /// <summary>What was found, or null when the old build is not installed.</summary>
    public sealed record Install(string Version, string Location, string UninstallCommand,
                                 string RunValue, string UninstallKey);

    public static List<Install> Find()
    {
        List<Install> found = new();
        foreach ((string keyName, string runValue) in Identities)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyName);
                if (key is null)
                {
                    continue;
                }

                string command = key.GetValue("QuietUninstallString") as string
                                 ?? key.GetValue("UninstallString") as string
                                 ?? string.Empty;
                if (command.Length == 0)
                {
                    continue;
                }
                found.Add(new Install(
                    key.GetValue("DisplayVersion") as string ?? "?",
                    key.GetValue("InstallLocation") as string ?? string.Empty,
                    command, runValue, keyName));
            }
            catch (Exception)
            {
                // a registry we cannot read is one we leave alone
            }
        }
        return found;
    }

    /// <summary>
    /// Run the old build's own uninstaller and wait for it.
    ///
    /// Its uninstaller, not our own file deletion: it knows what it created,
    /// and this installer's rule is that it never removes a file it did not
    /// write.
    ///
    /// It does take one liberty we have to undo. It deletes the whole of
    /// `%APPDATA%\SysMonitor`, which is where both builds keep their settings
    /// -- so removing the old one throws away the new one's configuration and
    /// log as well. The settings are copied out first and put back afterwards.
    /// </summary>
    public static bool Remove(Install install)
    {
        string? backup = BackUpSettings();
        try
        {
            StopRunning(install.Location);

            (string exe, string arguments) = Split(install.UninstallCommand);
            if (exe.Length == 0 || !File.Exists(exe))
            {
                return false;
            }
            if (!arguments.Contains("/S", StringComparison.OrdinalIgnoreCase))
            {
                arguments = (arguments + " /S").Trim();
            }

            using Process? process = Process.Start(new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit(60_000);

            // Whatever it left behind in the Run key goes too: a startup entry
            // pointing at a program that is gone is a silent failure at boot.
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(install.RunValue) is not null)
            {
                run.DeleteValue(install.RunValue, throwOnMissingValue: false);
            }
            return !Find().Any(found => found.UninstallKey == install.UninstallKey);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            RestoreSettings(backup);
        }
    }

    /// <summary>
    /// A command line as the registry stores it: a quoted path, then the rest.
    /// </summary>
    public static (string Exe, string Arguments) Split(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end < 0
                ? (command.Trim('"'), string.Empty)
                : (command[1..end], command[(end + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space < 0
            ? (command, string.Empty)
            : (command[..space], command[(space + 1)..].Trim());
    }

    private static void StopRunning(string location)
    {
        if (location.Length == 0)
        {
            return;
        }
        foreach (Process process in Process.GetProcessesByName("SysMonitor"))
        {
            try
            {
                string? path = process.MainModule?.FileName;
                if (path is not null
                    && path.StartsWith(location, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(5_000);
                }
            }
            catch (Exception)
            {
                // A process we cannot inspect is one we do not touch.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string? BackUpSettings()
    {
        try
        {
            string folder = Installer.SettingsDir;
            if (!Directory.Exists(folder))
            {
                return null;
            }
            string backup = Path.Combine(Path.GetTempPath(),
                "sysmonitor-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);

            foreach (string file in Directory.GetFiles(folder))
            {
                File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), overwrite: true);
            }
            return backup;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void RestoreSettings(string? backup)
    {
        if (backup is null)
        {
            return;
        }
        try
        {
            string folder = Installer.SettingsDir;
            Directory.CreateDirectory(folder);

            foreach (string file in Directory.GetFiles(backup))
            {
                string target = Path.Combine(folder, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target);
                }
            }
            Directory.Delete(backup, recursive: true);
        }
        catch (Exception)
        {
            // The copies stay in the temp directory rather than being lost.
        }
    }
}
