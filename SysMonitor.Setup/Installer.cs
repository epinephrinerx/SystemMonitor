using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SysMonitor.Setup;

/// <summary>
/// Per-user install and uninstall: files, registry, shortcuts.
///
/// The safety rules here are not stylistic. An earlier uninstaller enumerated
/// the install directory and deleted what it found, which is one mistaken
/// target away from erasing someone's folder. Nothing recursive happens: the
/// set of files this installer owns is written down, and only those are
/// removed. Anything else found in the directory is left alone, and the
/// directory itself goes only if it ends up empty.
/// </summary>
public static class Installer
{
    public const string AppName = "SysMonitor";

    /// <summary>
    /// Everything this build owns is named apart from the Python build's:
    /// its install directory, its Run value, its uninstall key and its
    /// shortcuts. Both are called SysMonitor and both install per-user, so
    /// sharing any of those names would mean this installer overwriting a
    /// working application and its uninstaller claiming the other's files.
    /// </summary>
    public const string Key = "SysMonitor.NET";

    public const string DisplayName = "SysMonitor (.NET)";
    public const string Version = "3.0.1";
    public const string Publisher = "SysMonitor";
    public const string ExeName = "SysMonitor.exe";
    public const string UninstallName = "uninstall.exe";

    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SysMonitor.NET";
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Every file this installer creates. Nothing else is ever deleted.</summary>
    private static readonly string[] ProgramFiles = { ExeName, UninstallName };

    /// <summary>Settings and logs, removed only when the user asks for it.</summary>
    private static readonly string[] SettingsFiles =
    {
        "config.wpf.json", "config.wpf.json.tmp", "sysmonitor-wpf.log",
    };

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", Key);

    public static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string ExePath => Path.Combine(InstallDir, ExeName);
    public static string UninstallPath => Path.Combine(InstallDir, UninstallName);

    public static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        DisplayName + ".lnk");

    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        DisplayName + ".lnk");

    /// <summary>
    /// The full set of paths this installer may delete, resolved and filtered
    /// so nothing outside the two directories it owns can ever be returned.
    /// </summary>
    public static IEnumerable<string> OwnedPaths(bool includeSettings)
    {
        foreach (string name in ProgramFiles)
        {
            yield return Path.Combine(InstallDir, name);
        }
        yield return DesktopShortcut;
        yield return StartMenuShortcut;
        if (!includeSettings)
        {
            yield break;
        }
        foreach (string name in SettingsFiles)
        {
            yield return Path.Combine(SettingsDir, name);
        }
    }

    /// <summary>
    /// A target is deletable only if it resolves to a file directly inside one
    /// of the directories we own. Relative paths, roots and anything that
    /// escapes via ".." are rejected rather than trusted.
    ///
    /// The lexical check is not enough on its own. If the install directory is
    /// a junction, "InstallDir\SysMonitor.exe" is a perfectly well-formed path
    /// inside a directory we appear to own, and deleting it deletes a file
    /// somewhere else entirely. So the parent chain is walked for reparse
    /// points as well, and a link anywhere along it disqualifies the target.
    /// </summary>
    public static bool IsSafeTarget(string path, params string[] allowedParents)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }
        string full = Path.GetFullPath(path);
        if (Path.GetPathRoot(full) == full || string.IsNullOrEmpty(Path.GetFileName(full)))
        {
            return false;
        }

        string? parent = Path.GetDirectoryName(full);
        if (parent is null
            || !allowedParents.Any(allowed =>
                   string.Equals(Path.GetFullPath(allowed), parent,
                                 StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return !PassesThroughLink(parent);
    }

    /// <summary>
    /// Does any directory from <paramref name="directory"/> up to its root
    /// redirect somewhere else? A junction or symbolic link on the way means
    /// the path we resolved lexically is not the path Windows will open.
    /// </summary>
    private static bool PassesThroughLink(string directory)
    {
        try
        {
            for (var node = new DirectoryInfo(directory);
                 node is not null;
                 node = node.Parent)
            {
                if (node.Exists && node.LinkTarget is not null)
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception)
        {
            // Unreadable is indistinguishable from redirected, and the safe
            // answer to "might this go somewhere else" is yes.
            return true;
        }
    }

    /// <summary>
    /// Refuse to write into a directory that redirects elsewhere. Installing
    /// through a junction would overwrite whatever it points at.
    /// </summary>
    private static void RequireOwnDirectory(string directory)
    {
        if (Directory.Exists(directory) && PassesThroughLink(directory))
        {
            throw new InvalidOperationException(
                $"{directory} is a link to somewhere else and will not be written to.");
        }
    }

    // ------------------------------------------------------------- install
    public static bool IsInstalled => File.Exists(ExePath);

    public static string? InstalledVersion
    {
        get
        {
            try
            {
                return File.Exists(ExePath)
                    ? FileVersionInfo.GetVersionInfo(ExePath).FileVersion
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The app is framework-dependent, so the desktop runtime has to be there.
    /// Better to say so plainly than to install something that will not start.
    /// </summary>
    public static bool HasDesktopRuntime()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        if (!Directory.Exists(root))
        {
            return false;
        }
        return Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Any(name => int.TryParse(name?.Split('.').FirstOrDefault(), out int major)
                         && major >= 8);
    }

    public static void Install(bool desktopShortcut, bool startMenu, bool autostart)
    {
        RequireOwnDirectory(InstallDir);
        Directory.CreateDirectory(InstallDir);
        RequireOwnDirectory(InstallDir);

        StopRunningApp();
        WritePayload(ExePath);
        // The uninstaller is this same executable; it decides what to do from
        // where it was started.
        File.Copy(Environment.ProcessPath!, UninstallPath, overwrite: true);

        Shortcuts.Write(desktopShortcut, DesktopShortcut, ExePath, InstallDir);
        Shortcuts.Write(startMenu, StartMenuShortcut, ExePath, InstallDir);
        SetAutostart(autostart);
        RegisterUninstall();
    }

    private static void WritePayload(string destination)
    {
        using Stream? source = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("SysMonitor.exe")
            ?? throw new InvalidOperationException(
                "This setup was built without the application payload.");
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write);
        source.CopyTo(target);
    }

    /// <summary>A running copy holds its own file open; ask it to go first.</summary>
    private static void StopRunningApp()
    {
        foreach (Process process in Process.GetProcessesByName("SysMonitor"))
        {
            try
            {
                if (!string.Equals(process.MainModule?.FileName, ExePath,
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

    private static void RegisterUninstall()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", DisplayName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", Publisher);
        key.SetValue("DisplayIcon", ExePath);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", "\"" + UninstallPath + "\"");
        key.SetValue("QuietUninstallString", "\"" + UninstallPath + "\" /S");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", FileSizeKb(ExePath), RegistryValueKind.DWord);
    }

    private static int FileSizeKb(string path)
    {
        try
        {
            return (int)(new FileInfo(path).Length / 1024);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public static void SetAutostart(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(Key, "\"" + ExePath + "\"");
        }
        else
        {
            key.DeleteValue(Key, throwOnMissingValue: false);
        }
    }

    public static bool AutostartEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Key) is string;
    }

    // ----------------------------------------------------------- uninstall
    /// <summary>
    /// Remove what we own. <paramref name="skip"/> is the running uninstaller
    /// itself when it cannot delete its own file yet.
    /// </summary>
    public static void Uninstall(bool removeSettings, string? skip = null)
    {
        SetAutostart(false);

        foreach (string path in OwnedPaths(removeSettings))
        {
            if (skip is not null
                && string.Equals(Path.GetFullPath(path), Path.GetFullPath(skip),
                                 StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Delete(path);
        }

        RemoveIfEmpty(InstallDir);
        if (removeSettings)
        {
            RemoveIfEmpty(SettingsDir);
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        }
        catch (Exception)
        {
            // Already gone, or someone else removed it; either way we are done.
        }
    }

    private static void Delete(string path)
    {
        string[] parents =
        {
            InstallDir, SettingsDir,
            Path.GetDirectoryName(DesktopShortcut)!,
            Path.GetDirectoryName(StartMenuShortcut)!,
        };
        if (!IsSafeTarget(path, parents))
        {
            return;
        }
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A locked file stays; better than trying harder and doing damage.
        }
    }

    /// <summary>
    /// Remove the directory only when nothing is left in it. Anything the user
    /// put there keeps the directory, and keeps their file.
    /// </summary>
    private static void RemoveIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception)
        {
            // Not empty, or in use. Leave it.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(string source, string? destination, uint flags);

    private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    /// <summary>
    /// Hand the last file -- the running uninstaller -- to Windows to remove
    /// at the next restart. No child shell, no interpolated path, nothing that
    /// can be turned into a command.
    /// </summary>
    public static void DeleteSelfAtReboot(string path)
    {
        try
        {
            MoveFileExW(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
        }
        catch (Exception)
        {
            // A leftover 300 KB file is not worth a failure dialog.
        }
    }
}
