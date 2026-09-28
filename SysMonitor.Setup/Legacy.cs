using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SysMonitor.Setup;

/// <summary>
/// The retired Python/Tk build, which installs beside this one rather than
/// over it.
///
/// Both are called SysMonitor, both install per-user, and both put themselves
/// in the Run key -- so a machine with both starts two widgets every morning
/// and the older window lands on top of the newer one. It took a process list
/// to work out that a screen "reverted to version 2.0" was version 2.0, still
/// installed and still starting itself.
/// </summary>
public static class Legacy
{
    /// <summary>
    /// The old build's own keys. Deliberately not the ones this installer
    /// uses: "SysMonitor" and "SysMonitor.NET" are different products as far
    /// as Windows is concerned, which is the whole reason they coexist.
    /// </summary>
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SysMonitor";
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "SysMonitor";

    /// <summary>What was found, or null when the old build is not installed.</summary>
    public sealed record Install(string Version, string Location, string UninstallCommand);

    public static Install? Find()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key is null)
            {
                return null;
            }

            string command = key.GetValue("QuietUninstallString") as string
                             ?? key.GetValue("UninstallString") as string
                             ?? string.Empty;
            if (command.Length == 0)
            {
                return null;
            }
            return new Install(
                key.GetValue("DisplayVersion") as string ?? "?",
                key.GetValue("InstallLocation") as string ?? string.Empty,
                command);
        }
        catch (Exception)
        {
            return null;    // a registry we cannot read is one we leave alone
        }
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
            if (run?.GetValue(RunValue) is not null)
            {
                run.DeleteValue(RunValue, throwOnMissingValue: false);
            }
            return Find() is null;
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
