using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SysMonitor.Setup;

/// <summary>
/// A file in an extension payload to be installed or inspected.
/// </summary>
public sealed record PayloadFile(string Id, string Name, Func<Stream> Open)
{
    public PayloadFile(string id, string name, byte[] content)
        : this(id, name, () => new MemoryStream(content))
    {
    }

    public PayloadFile(string id, string name, string content)
        : this(id, name, () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)))
    {
    }
}

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
    public const string AppName = "SystemMonitor";

    /// <summary>
    /// Everything this build owns is named apart from the retired Python
    /// build ("SysMonitor") and the pre-4.0 C# build ("SysMonitor.NET"):
    /// its install directory, its Run value, its uninstall key and its
    /// shortcuts. Sharing any of those names would mean this installer
    /// overwriting a working application and its uninstaller claiming the
    /// other's files.
    /// </summary>
    public const string Key = "SystemMonitor";

    public const string DisplayName = "System Monitor";
    /// <summary>
    /// Read from this assembly rather than written down.
    ///
    /// It was a const, and it went stale: the projects were bumped to 3.2.0
    /// and this stayed at 3.1.0, so the wizard offered to "replace 3.2.0 with
    /// 3.1.0" and Add/Remove Programs recorded the older number. A version
    /// that has to be edited in three places is a version that will disagree
    /// with itself.
    /// </summary>
    public static string Version { get; } =
        typeof(Installer).Assembly.GetName().Version is System.Version version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";
    public const string Publisher = "System Monitor";
    public const string ExeName = "SystemMonitor.exe";
    public const string UninstallName = "uninstall.exe";

    /// <summary>
    /// Legacy root DiskInfoCore.dll from phase 3 and earlier.
    /// In phase 4, DiskInfoCore.dll is packaged inside extensions\smart\.
    /// Kept here so upgrades and uninstalls can clean up the legacy root file.
    /// </summary>
    public const string CoreName = "DiskInfoCore.dll";

    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SystemMonitor";
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Every file this installer creates. Nothing else is ever deleted.</summary>
    private static readonly string[] ProgramFiles = { ExeName, UninstallName, CoreName };

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
    /// <summary>
    /// Legacy root path for DiskInfoCore.dll in earlier versions.
    /// </summary>
    public static string CorePath => Path.Combine(InstallDir, CoreName);

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
        foreach (string extPath in ExtensionOwnedPaths(ReadExtensionPayload()))
        {
            yield return extPath;
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
    /// The folders Windows gives us, which are where our own directories are
    /// rooted. A redirection at or above one of these is the profile being
    /// redirected -- ordinary on a managed machine -- and is not our business.
    /// </summary>
    private static readonly string[] Anchors = new[]
    {
        Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.ApplicationData,
        Environment.SpecialFolder.DesktopDirectory,
        Environment.SpecialFolder.Programs,
        Environment.SpecialFolder.StartMenu,
        Environment.SpecialFolder.UserProfile,
    }.Select(Environment.GetFolderPath)
     .Where(path => path.Length > 0)
     .Select(Path.GetFullPath)
     .ToArray();

    /// <summary>
    /// Does anything between <paramref name="directory"/> and the folder it
    /// sits under redirect somewhere else?
    ///
    /// A junction or symbolic link on the way means the path we resolved by
    /// text is not the path Windows will open. The walk stops at the known
    /// folder it is rooted in: corporate profiles are routinely redirected
    /// with junctions, and refusing to uninstall on such a machine would be a
    /// worse failure than the one this guards against.
    ///
    /// **This is a check, not a guarantee.** It reads the state of the path
    /// and the delete happens afterwards; a link put in place in between would
    /// still be followed. Closing that properly means opening each component
    /// by handle and never by name, which is a great deal of Win32 for a
    /// per-user installer -- an attacker who can win this race can already run
    /// code as the user whose files are at stake.
    /// </summary>
    private static bool PassesThroughLink(string directory)
    {
        try
        {
            for (var node = new DirectoryInfo(Path.GetFullPath(directory));
                 node is not null;
                 node = node.Parent)
            {
                if (Anchors.Any(anchor => string.Equals(anchor, node.FullName,
                                                        StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
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

        IReadOnlyList<PayloadFile> extensions = ReadExtensionPayload();
        IReadOnlyList<string> notWritten = WriteExtensions(InstallDir, extensions);
        RemoveLegacyCore(InstallDir);

        // The uninstaller is this same executable; it decides what to do from
        // where it was started.
        File.Copy(Environment.ProcessPath!, UninstallPath, overwrite: true);

        Shortcuts.Write(desktopShortcut, DesktopShortcut, ExePath, InstallDir);
        Shortcuts.Write(startMenu, StartMenuShortcut, ExePath, InstallDir);
        SetAutostart(autostart);
        RegisterUninstall();

        // Everything else is in place by now, so a package that could not be completed does not
        // leave the application half-installed; it is still reported rather than hidden.
        if (notWritten.Count > 0)
        {
            throw new IOException(
                "The application was installed, but these extension files could not be written "
                + "(close System Monitor and run the setup again): " + string.Join(", ", notWritten));
        }
    }

    private static void RemoveLegacyCore(string installDir)
    {
        string legacyCorePath = Path.Combine(installDir, CoreName);
        if (File.Exists(legacyCorePath) && IsSafeTarget(legacyCorePath, installDir))
        {
            try
            {
                File.Delete(legacyCorePath);
            }
            catch (Exception)
            {
                // In use or locked: leave it.
            }
        }
    }

    private static void WritePayload(string destination)
    {
        using Stream? source = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("SystemMonitor.exe")
            ?? throw new InvalidOperationException(
                "This setup was built without the application payload.");
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write);
        source.CopyTo(target);
    }

    private static readonly Regex ExtensionIdRegex = new(
        @"^[a-z0-9][a-z0-9._-]{0,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Validates payload files. Rejects invalid extension IDs, directory traversal or separators
    /// in file names, empty names, and duplicate file names within the same extension ID.
    /// </summary>
    public static void ValidatePayload(IEnumerable<PayloadFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (PayloadFile file in files)
        {
            if (file is null)
            {
                throw new InvalidOperationException("Payload file cannot be null.");
            }
            if (string.IsNullOrEmpty(file.Id) || !ExtensionIdRegex.IsMatch(file.Id))
            {
                throw new InvalidOperationException($"Invalid extension id '{file.Id}'.");
            }
            if (string.IsNullOrWhiteSpace(file.Name))
            {
                throw new InvalidOperationException("Payload file name cannot be empty.");
            }
            if (file.Name.Contains('/') || file.Name.Contains('\\') || file.Name.Contains(".."))
            {
                throw new InvalidOperationException($"Payload file name '{file.Name}' contains invalid path characters.");
            }
            if (Path.IsPathFullyQualified(file.Name) || !string.IsNullOrEmpty(Path.GetPathRoot(file.Name)))
            {
                throw new InvalidOperationException($"Payload file name '{file.Name}' cannot be an absolute path.");
            }
            if (file.Name == "." || file.Name == ".." || Path.GetFileName(file.Name) != file.Name)
            {
                throw new InvalidOperationException($"Payload file name '{file.Name}' is not a valid file name.");
            }
            // Windows rewrites a trailing dot or space onto another name and ':' opens an alternate
            // data stream, so none of them can be allowed to dodge the duplicate check below.
            if (file.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || file.Name.EndsWith('.') || file.Name.EndsWith(' ') || file.Name.StartsWith(' '))
            {
                throw new InvalidOperationException($"Payload file name '{file.Name}' is not a valid file name.");
            }

            string key = $"{file.Id}/{file.Name}";
            if (!seen.Add(key))
            {
                throw new InvalidOperationException($"Duplicate payload file '{key}'.");
            }
        }
    }

    /// <summary>
    /// Reads embedded extension payload files from the current assembly's resources (named ext:id/file).
    /// </summary>
    public static IReadOnlyList<PayloadFile> ReadExtensionPayload() =>
        ReadExtensionPayload(typeof(Installer).Assembly);

    /// <summary>
    /// Reads embedded extension payload files from the specified assembly's resources (named ext:id/file).
    /// </summary>
    public static IReadOnlyList<PayloadFile> ReadExtensionPayload(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string[] resourceNames = assembly.GetManifestResourceNames();
        var files = new List<PayloadFile>();

        foreach (string resName in resourceNames)
        {
            if (!resName.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rest = resName.Substring("ext:".Length).Replace('\\', '/');
            int slashIndex = rest.IndexOf('/');
            if (slashIndex <= 0 || slashIndex == rest.Length - 1)
            {
                throw new InvalidOperationException(
                    $"Invalid extension resource name '{resName}'. Expected 'ext:<id>/<filename>'.");
            }

            string id = rest.Substring(0, slashIndex);
            string fileName = rest.Substring(slashIndex + 1);

            if (fileName.Contains('/') || fileName.Contains('\\'))
            {
                throw new InvalidOperationException(
                    $"Extension resource '{resName}' contains unexpected path separators in file name.");
            }

            string capturedName = resName;
            files.Add(new PayloadFile(
                id,
                fileName,
                () => assembly.GetManifestResourceStream(capturedName)
                      ?? throw new InvalidOperationException($"Resource '{capturedName}' not found.")));
        }

        ValidatePayload(files);
        return files;
    }

    /// <summary>
    /// Writes packaged extension files to installDir\extensions\&lt;id&gt;\.
    /// extension.json is written last for each package.
    /// Returns a list of file paths that failed to write due to being locked or in use.
    /// </summary>
    public static IReadOnlyList<string> WriteExtensions(string installDir, IReadOnlyList<PayloadFile> files)
    {
        ArgumentNullException.ThrowIfNull(installDir);
        ArgumentNullException.ThrowIfNull(files);

        ValidatePayload(files);

        if (files.Count == 0)
        {
            return Array.Empty<string>();
        }

        string extensionsRoot = Path.Combine(installDir, "extensions");
        if (Directory.Exists(installDir))
        {
            RequireOwnDirectory(installDir);
        }
        if (Directory.Exists(extensionsRoot))
        {
            RequireOwnDirectory(extensionsRoot);
        }

        var failedFiles = new List<string>();
        var idGroups = files.GroupBy(f => f.Id, StringComparer.Ordinal);

        foreach (var group in idGroups)
        {
            string targetDir = Path.Combine(extensionsRoot, group.Key);
            if (Directory.Exists(targetDir))
            {
                RequireOwnDirectory(targetDir);
            }
            Directory.CreateDirectory(targetDir);
            RequireOwnDirectory(targetDir);

            // Delete files directly inside extensions\<id> that are not part of the new package
            // (non-recursive, IsSafeTarget guarded, subfolders untouched)
            var packageFileNames = new HashSet<string>(
                group.Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            bool packageIntact = true;
            foreach (string existingFilePath in Directory.EnumerateFiles(targetDir))
            {
                string fileName = Path.GetFileName(existingFilePath);
                if (!packageFileNames.Contains(fileName))
                {
                    if (IsSafeTarget(existingFilePath, targetDir))
                    {
                        try
                        {
                            File.Delete(existingFilePath);
                        }
                        catch (Exception)
                        {
                            // In use or locked: it stays, and the loader rejects a package with an
                            // unlisted file, so the package counts as not written.
                            failedFiles.Add(existingFilePath);
                            packageIntact = false;
                        }
                    }
                }
            }

            // Write the package files: extension.json is written last, and only when every other
            // file of the package was written, so a half-written package is never made to look
            // complete. A locked/in-use file must not abort the other ids; report which failed.
            var orderedFiles = group.OrderBy(f =>
                string.Equals(f.Name, "extension.json", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

            foreach (PayloadFile file in orderedFiles)
            {
                string destPath = Path.Combine(targetDir, file.Name);
                bool isManifest = string.Equals(file.Name, "extension.json", StringComparison.OrdinalIgnoreCase);
                if (isManifest && !packageIntact)
                {
                    failedFiles.Add(destPath);
                    continue;
                }

                try
                {
                    // FileMode.Create would follow a file link and truncate whatever it points at.
                    if (File.Exists(destPath)
                        && (File.GetAttributes(destPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException($"{destPath} is a link and will not be written through.");
                    }
                    using Stream source = file.Open();
                    using var target = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    source.CopyTo(target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failedFiles.Add(destPath);
                    packageIntact = false;
                }
            }
        }

        return failedFiles;
    }

    /// <summary>
    /// Enumerates paths of all extension files in the payload for the current install directory.
    /// </summary>
    public static IEnumerable<string> ExtensionOwnedPaths(IEnumerable<PayloadFile> files) =>
        ExtensionOwnedPaths(InstallDir, files);

    /// <summary>
    /// Enumerates paths of all extension files in the payload under the given install directory.
    /// </summary>
    public static IEnumerable<string> ExtensionOwnedPaths(string installDir, IEnumerable<PayloadFile> files)
    {
        ArgumentNullException.ThrowIfNull(installDir);
        ArgumentNullException.ThrowIfNull(files);
        var list = files.ToList();
        ValidatePayload(list);
        foreach (PayloadFile file in list)
        {
            yield return Path.Combine(installDir, "extensions", file.Id, file.Name);
        }
    }

    /// <summary>
    /// Removes only the owned extension files and cleans up empty extension directories.
    /// Third-party extension folders and user files are left intact.
    /// </summary>
    public static void UninstallExtensions(string installDir, IEnumerable<PayloadFile> files)
    {
        ArgumentNullException.ThrowIfNull(installDir);
        ArgumentNullException.ThrowIfNull(files);

        var fileList = files.ToList();
        // Same rules as the write side: an id or name that is not plain must never turn into a
        // path outside extensions\<id>.
        ValidatePayload(fileList);
        string extensionsRoot = Path.Combine(installDir, "extensions");

        var idGroups = fileList.GroupBy(f => f.Id, StringComparer.Ordinal);
        foreach (var group in idGroups)
        {
            string extDir = Path.Combine(extensionsRoot, group.Key);
            foreach (PayloadFile file in group)
            {
                string filePath = Path.Combine(extDir, file.Name);
                if (IsSafeTarget(filePath, extDir))
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                    }
                    catch (Exception)
                    {
                        // In use or locked: leave it.
                    }
                }
            }

            RemoveIfEmpty(extDir);
        }

        RemoveIfEmpty(extensionsRoot);
        RemoveIfEmpty(installDir);
    }

    /// <summary>A running copy holds its own file open; ask it to go first.</summary>
    private static void StopRunningApp()
    {
        foreach (Process process in Process.GetProcessesByName("SystemMonitor"))
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
        key.SetValue("EstimatedSize", CalculateEstimatedSizeKb(), RegistryValueKind.DWord);
    }

    private static int CalculateEstimatedSizeKb()
    {
        long totalBytes = 0;
        if (File.Exists(ExePath))
        {
            totalBytes += new FileInfo(ExePath).Length;
        }
        if (File.Exists(CorePath))
        {
            totalBytes += new FileInfo(CorePath).Length;
        }
        string extensionsRoot = Path.Combine(InstallDir, "extensions");
        if (Directory.Exists(extensionsRoot))
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(extensionsRoot, "*", SearchOption.AllDirectories))
                {
                    totalBytes += new FileInfo(file).Length;
                }
            }
            catch (Exception)
            {
            }
        }
        return (int)(totalBytes / 1024);
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

        UninstallExtensions(InstallDir, ReadExtensionPayload());

        foreach (string path in ProgramFiles)
        {
            string full = Path.Combine(InstallDir, path);
            if (skip is not null
                && string.Equals(Path.GetFullPath(full), Path.GetFullPath(skip),
                                 StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Delete(full);
        }

        Delete(DesktopShortcut);
        Delete(StartMenuShortcut);

        if (removeSettings)
        {
            foreach (string name in SettingsFiles)
            {
                Delete(Path.Combine(SettingsDir, name));
            }
            RemoveIfEmpty(SettingsDir);
        }

        RemoveIfEmpty(InstallDir);

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
        // Extension files go through UninstallExtensions, which guards each folder itself; nothing
        // under extensions\ is deleted from here.
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
    public static void RemoveIfEmpty(string directory)
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
