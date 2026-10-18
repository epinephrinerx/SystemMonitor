using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using SysMonitor.Extensibility;

namespace SysMonitor.Extensions;

/// <summary>
/// Discovers, validates, and loads extensions from the extensions/ root directory according
/// to the trust model and manifest specifications.
/// </summary>
public static class ExtensionLoader
{
    internal static Action<AssemblyLoadContext>? AlcCreatedForTesting;

    /// <summary>Called with the path just before a native library is loaded (tests only).</summary>
    internal static Action<string>? NativeLoadingForTesting;

    /// <summary>Unload runs the extension's own Unloading handlers, which can throw; cleanup must go on.</summary>
    private static void SafeUnload(AssemblyLoadContext? alc)
    {
        try
        {
            alc?.Unload();
        }
        catch
        {
            // The caller goes on to free the native libraries and report the original failure.
        }
    }

    /// <summary>
    /// Loads all extensions located in subdirectories of options.ExtensionsRoot.
    /// Never throws exceptions out of this method, except ArgumentNullException if options is null.
    /// </summary>
    public static IReadOnlyList<ExtensionLoadResult> LoadAll(ExtensionLoaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var results = new List<ExtensionLoadResult>();

        try
        {
            if (string.IsNullOrWhiteSpace(options.ExtensionsRoot) || !Directory.Exists(options.ExtensionsRoot))
            {
                return results;
            }

            string[] subdirs = Directory.GetDirectories(options.ExtensionsRoot);
            Array.Sort(subdirs, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));

            var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string dir in subdirs)
            {
                try
                {
                    ExtensionLoadResult result = LoadSingleFolder(dir, options, loadedIds);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    string folderName = Path.GetFileName(dir);
                    options.Log.SafeLog($"Unexpected error processing extension folder '{folderName}': {ex.Describe()}");
                    results.Add(new ExtensionLoadResult(
                        folderName,
                        null,
                        ExtensionStatus.LoadFailed,
                        $"Unexpected error: {ex.Describe()}",
                        null,
                        null));
                }
            }
        }
        catch (Exception ex)
        {
            options.Log.SafeLog($"ExtensionLoader.LoadAll encountered top-level error: {ex.Describe()}");
        }

        return results;
    }

    private static bool TryValidateFolder(
        string folder,
        string? extensionsRoot,
        out string folderName,
        out ExtensionStatus errorStatus,
        out string? errorDetail)
    {
        folderName = string.IsNullOrWhiteSpace(folder)
            ? string.Empty
            : Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            errorStatus = ExtensionStatus.UnsafePath;
            errorDetail = "Extension folder does not exist or path is empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(extensionsRoot))
        {
            errorStatus = ExtensionStatus.UnsafePath;
            errorDetail = "ExtensionsRoot is empty or not configured";
            return false;
        }

        string fullFolderPath;
        string fullRootPath;
        try
        {
            fullFolderPath = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            fullRootPath = Path.GetFullPath(extensionsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            errorStatus = ExtensionStatus.UnsafePath;
            errorDetail = $"Invalid path format: {ex.Message}";
            return false;
        }

        string expectedPath = Path.Combine(fullRootPath, folderName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parentDir = Path.GetDirectoryName(fullFolderPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!string.Equals(parentDir, fullRootPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(fullFolderPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            errorStatus = ExtensionStatus.UnsafePath;
            errorDetail = $"Extension folder '{fullFolderPath}' is outside ExtensionsRoot '{fullRootPath}'";
            return false;
        }

        errorStatus = ExtensionStatus.Loaded;
        errorDetail = null;
        return true;
    }

    /// <summary>
    /// Loads a single extension folder using the same rules and security checks as LoadAll.
    /// Never throws exceptions out of this method, except ArgumentNullException if options is null.
    /// </summary>
    public static ExtensionLoadResult LoadFolder(string folder, ExtensionLoaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!TryValidateFolder(folder, options.ExtensionsRoot, out string folderName, out var errorStatus, out var errorDetail))
        {
            return new ExtensionLoadResult(
                folderName,
                null,
                errorStatus,
                errorDetail ?? "Validation failed",
                null,
                null,
                null);
        }

        try
        {
            var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return LoadSingleFolder(folder, options, loadedIds, inspectOnly: false);
        }
        catch (Exception ex)
        {
            options.Log.SafeLog($"Unexpected error loading extension folder '{folderName}': {ex.Describe()}");
            return new ExtensionLoadResult(
                folderName,
                null,
                ExtensionStatus.LoadFailed,
                $"Unexpected error: {ex.Describe()}",
                null,
                null,
                null);
        }
    }

    /// <summary>
    /// Inspects an extension folder structurally and verifies all declared file hashes
    /// without loading any code, creating an ALC, or loading native libraries.
    /// Does not check trust, approvals, IsEnabled, or duplicates.
    /// Never throws exceptions out of this method, except ArgumentNullException if options is null.
    /// </summary>
    public static ExtensionInspection Inspect(string folder, ExtensionLoaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!TryValidateFolder(folder, options.ExtensionsRoot, out string folderName, out var errorStatus, out var errorDetail))
        {
            return new ExtensionInspection(
                errorStatus,
                errorDetail ?? "Validation failed",
                null,
                null,
                false);
        }

        try
        {
            ExtensionLoadResult result = LoadSingleFolder(folder, options, loadedIds: null, inspectOnly: true);
            bool isFirstParty = !string.IsNullOrWhiteSpace(result.ManifestSha256)
                && options.FirstParty?.IsFirstParty(result.ManifestSha256) == true;

            return new ExtensionInspection(
                result.Status,
                result.Detail,
                result.Manifest,
                result.ManifestSha256,
                isFirstParty);
        }
        catch (Exception ex)
        {
            options.Log.SafeLog($"Unexpected error inspecting extension folder '{folderName}': {ex.Describe()}");
            return new ExtensionInspection(
                ExtensionStatus.LoadFailed,
                $"Unexpected error: {ex.Describe()}",
                null,
                null,
                false);
        }
    }

    internal static ExtensionLoadResult LoadSingleFolder(
        string dir,
        ExtensionLoaderOptions options,
        HashSet<string>? loadedIds,
        bool inspectOnly = false)
    {
        string folderName = Path.GetFileName(dir);

        // 1. Reparse point check on the root extension folder
        try
        {
            FileAttributes folderAttr = File.GetAttributes(dir);
            if ((folderAttr & FileAttributes.ReparsePoint) != 0)
            {
                return new ExtensionLoadResult(
                    folderName,
                    null,
                    ExtensionStatus.UnsafePath,
                    "Extension folder is a junction, symbolic link, or reparse point",
                    null,
                    null);
            }
        }
        catch (Exception ex)
        {
            return new ExtensionLoadResult(
                folderName,
                null,
                ExtensionStatus.UnsafePath,
                $"Failed to inspect folder attributes: {ex.Describe()}",
                null,
                null);
        }

        // Check that all subdirectories in the entire subtree are not reparse points
        try
        {
            string[] subdirectories = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories);
            foreach (string subDir in subdirectories)
            {
                FileAttributes subAttr = File.GetAttributes(subDir);
                if ((subAttr & FileAttributes.ReparsePoint) != 0)
                {
                    return new ExtensionLoadResult(
                        folderName,
                        null,
                        ExtensionStatus.UnsafePath,
                        $"Intermediate directory '{Path.GetRelativePath(dir, subDir)}' is a reparse point",
                        null,
                        null);
                }
            }
        }
        catch (Exception ex)
        {
            return new ExtensionLoadResult(
                folderName,
                null,
                ExtensionStatus.UnsafePath,
                $"Failed to inspect subdirectories: {ex.Describe()}",
                null,
                null);
        }

        // 2. Read and parse manifest
        string manifestPath = Path.Combine(dir, "extension.json");
        if (!File.Exists(manifestPath))
        {
            return new ExtensionLoadResult(
                folderName,
                null,
                ExtensionStatus.ManifestInvalid,
                "extension.json not found",
                null,
                null);
        }

        byte[] manifestBytes;
        try
        {
            manifestBytes = File.ReadAllBytes(manifestPath);
        }
        catch (Exception ex)
        {
            return new ExtensionLoadResult(
                folderName,
                null,
                ExtensionStatus.ManifestInvalid,
                $"Could not read extension.json: {ex.Describe()}",
                null,
                null);
        }

        string manifestSha256 = ComputeSha256Hex(manifestBytes);

        if (!ExtensionManifest.TryParse(manifestBytes, out ExtensionManifest? manifest, out string? manifestError) || manifest == null)
        {
            return new ExtensionLoadResult(
                folderName,
                null,
                ExtensionStatus.ManifestInvalid,
                manifestError ?? "Failed to parse extension.json",
                manifestSha256,
                null);
        }

        // 3. Folder name must match manifest id exactly
        if (!string.Equals(manifest.Id, folderName, StringComparison.Ordinal))
        {
            return new ExtensionLoadResult(
                folderName,
                manifest.Id,
                ExtensionStatus.ManifestInvalid,
                $"Folder name '{folderName}' does not match manifest id '{manifest.Id}'",
                manifestSha256,
                null,
                manifest);
        }

        // 4. Contract version compatibility check
        var manifestContract = Version.Parse(manifest.Contract);
        var hostContract = ContractInfo.Version;
        if (manifestContract.Major != hostContract.Major || manifestContract.Minor > hostContract.Minor)
        {
            return new ExtensionLoadResult(
                folderName,
                manifest.Id,
                ExtensionStatus.ContractMismatch,
                $"Contract version '{manifest.Contract}' is incompatible with host contract '{hostContract}'",
                manifestSha256,
                null,
                manifest);
        }

        // 5. min_host version check with normalized Version comparison (Build/Revision -1 -> 0)
        if (!string.IsNullOrWhiteSpace(manifest.MinHost))
        {
            Version minHost = NormalizeVersion(Version.Parse(manifest.MinHost));
            Version hostVer = NormalizeVersion(options.HostVersion);
            if (minHost > hostVer)
            {
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.HostTooOld,
                    $"Extension requires host version >= {minHost}, but current host is {options.HostVersion}",
                    manifestSha256,
                    null,
                    manifest);
            }
        }

        // 6. Safe path validation for every file
        string canonicalDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
        var resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in manifest.Files)
        {
            string p = file.Path;
            if (string.IsNullOrWhiteSpace(p)
                || p.Contains(':')
                || p.StartsWith('/')
                || p.StartsWith('\\')
                || Path.IsPathRooted(p))
            {
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.UnsafePath,
                    $"File path '{p}' is rooted or contains forbidden characters",
                    manifestSha256,
                    null,
                    manifest);
            }

            string[] segments = p.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s == ".." || s == "."))
            {
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.UnsafePath,
                    $"File path '{p}' contains directory traversal segments",
                    manifestSha256,
                    null,
                    manifest);
            }

            string resolved;
            try
            {
                resolved = Path.GetFullPath(Path.Combine(dir, p));
            }
            catch (Exception ex)
            {
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.UnsafePath,
                    $"Failed to resolve file path '{p}': {ex.Describe()}",
                    manifestSha256,
                    null,
                    manifest);
            }

            if (!resolved.StartsWith(canonicalDir, StringComparison.OrdinalIgnoreCase))
            {
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.UnsafePath,
                    $"File path '{p}' resolves outside the extension directory",
                    manifestSha256,
                    null,
                    manifest);
            }

            // Check if file is a reparse point
            if (File.Exists(resolved))
            {
                try
                {
                    if ((File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
                    {
                        return new ExtensionLoadResult(
                            folderName,
                            manifest.Id,
                            ExtensionStatus.UnsafePath,
                            $"File '{p}' is a symbolic link or reparse point",
                            manifestSha256,
                            null,
                            manifest);
                    }
                }
                catch (Exception ex)
                {
                    return new ExtensionLoadResult(
                        folderName,
                        manifest.Id,
                        ExtensionStatus.UnsafePath,
                        $"Failed to check attributes for file '{p}': {ex.Describe()}",
                        manifestSha256,
                        null,
                        manifest);
                }
            }

            resolvedPaths[file.Path] = resolved;
        }

        // Rule 5(a): Extension directory must NOT contain any unlisted files (whole subtree).
        // Only extension.json and files declared in manifest.Files are permitted.
        try
        {
            string[] diskFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            var allowedRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "extension.json"
            };

            foreach (var file in manifest.Files)
            {
                allowedRelPaths.Add(file.Path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            }

            foreach (string diskFile in diskFiles)
            {
                // Verify file is not a reparse point
                if ((File.GetAttributes(diskFile) & FileAttributes.ReparsePoint) != 0)
                {
                    return new ExtensionLoadResult(
                        folderName,
                        manifest.Id,
                        ExtensionStatus.UnsafePath,
                        $"File '{Path.GetRelativePath(dir, diskFile)}' is a symbolic link or reparse point",
                        manifestSha256,
                        null,
                        manifest);
                }

                string relPath = Path.GetRelativePath(dir, diskFile)
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);

                if (!allowedRelPaths.Contains(relPath))
                {
                    return new ExtensionLoadResult(
                        folderName,
                        manifest.Id,
                        ExtensionStatus.UnsafePath,
                        $"Extension directory contains unlisted file '{relPath}'",
                        manifestSha256,
                        null,
                        manifest);
                }
            }
        }
        catch (Exception ex)
        {
            return new ExtensionLoadResult(
                folderName,
                manifest.Id,
                ExtensionStatus.UnsafePath,
                $"Failed to verify directory contents: {ex.Describe()}",
                manifestSha256,
                null,
                manifest);
        }

        // 7. Check that all declared files exist
        foreach (var file in manifest.Files)
        {
            string resolved = resolvedPaths[file.Path];
            if (!File.Exists(resolved))
            {
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.FileMissing,
                    $"Required file '{file.Path}' was not found on disk",
                    manifestSha256,
                    null,
                    manifest);
            }
        }

        bool isFirstParty = options.FirstParty.IsFirstParty(manifestSha256);
        if (!inspectOnly)
        {
            // 8. Trust decision based on manifest SHA-256
            if (options.HostIsElevated)
            {
                if (!isFirstParty)
                {
                    return new ExtensionLoadResult(
                        folderName,
                        manifest.Id,
                        ExtensionStatus.NotTrustedWhenElevated,
                        "Only first-party extensions can be loaded when host is elevated",
                        manifestSha256,
                        null,
                        manifest,
                        isFirstParty);
                }
            }
            else
            {
                if (!isFirstParty)
                {
                    bool isApproved = options.Approvals?.IsApproved(manifest.Id, manifestSha256) == true;
                    if (!isApproved)
                    {
                        return new ExtensionLoadResult(
                            folderName,
                            manifest.Id,
                            ExtensionStatus.NotApproved,
                            "Extension manifest is neither first-party nor approved by the user",
                            manifestSha256,
                            null,
                            manifest,
                            isFirstParty);
                    }
                }
            }

            // 8b. If extension is not enabled in settings, check if it should be kept loaded (e.g. for background metrics)
            if (options.IsEnabled != null && !options.IsEnabled(manifest.Id))
            {
                bool keepLoaded = options.KeepLoaded?.Invoke(manifest, isFirstParty) ?? false;
                if (!keepLoaded)
                {
                    return new ExtensionLoadResult(
                        folderName,
                        manifest.Id,
                        ExtensionStatus.Disabled,
                        "Extension is disabled in settings",
                        manifestSha256,
                        null,
                        manifest,
                        isFirstParty);
                }
            }
        }

        // 9. Read every file once and verify every SHA-256 BEFORE loading anything. A native
        // library's imports can be resolved from this folder, so no native code may run until
        // all files are known good. Native files stay open with FileShare.Read (no writer, no
        // delete) from the hash until the library is loaded, so they cannot be swapped between
        // the two.
        var fileBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var heldNative = new List<(ExtensionFile File, FileStream Stream)>();
        var nativeHandles = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in manifest.Files)
            {
                string resolved = resolvedPaths[file.Path];
                byte[] bytes;
                FileStream? held = null;
                try
                {
                    if (!inspectOnly && file.Native)
                    {
                        held = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var ms = new MemoryStream();
                        held.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                    else
                    {
                        bytes = File.ReadAllBytes(resolved);
                    }
                }
                catch (Exception ex)
                {
                    held?.Dispose();
                    return new ExtensionLoadResult(
                        folderName,
                        manifest.Id,
                        ExtensionStatus.LoadFailed,
                        $"Failed to read file '{file.Path}': {ex.Describe()}",
                        manifestSha256,
                        null,
                        manifest,
                        isFirstParty);
                }

                string actualHash = ComputeSha256Hex(bytes);
                if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    held?.Dispose();
                    return new ExtensionLoadResult(
                        folderName,
                        manifest.Id,
                        ExtensionStatus.HashMismatch,
                        $"File '{file.Path}' hash mismatch. Expected {file.Sha256}, got {actualHash}",
                        manifestSha256,
                        null,
                        manifest,
                        isFirstParty);
                }

                if (held != null)
                {
                    heldNative.Add((file, held));
                }
                else
                {
                    fileBytes[file.Path] = bytes;
                }
            }

            if (inspectOnly)
            {
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.Loaded,
                    "Extension structurally valid and all files verified",
                    manifestSha256,
                    null,
                    manifest,
                    isFirstParty);
            }

            // 10. Verify entry assembly is declared in 'files' and is not native
            var entryFile = manifest.Files.FirstOrDefault(f =>
                string.Equals(f.Path, manifest.Entry.Assembly, StringComparison.OrdinalIgnoreCase));

            if (entryFile == null || entryFile.Native)
            {
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.EntryNotFound,
                    $"Entry assembly '{manifest.Entry.Assembly}' is not declared in 'files' or is marked as native",
                    manifestSha256,
                    null,
                    manifest,
                    isFirstParty);
            }

            // 11. Duplicate ID check in the same run
            if (loadedIds != null && loadedIds.Contains(manifest.Id))
            {
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.DuplicateId,
                    $"An extension with id '{manifest.Id}' was already loaded in this run",
                    manifestSha256,
                    null,
                    manifest,
                    isFirstParty);
            }

            // Everything is verified: preload the native libraries while their files are still held.
            // Only this folder and System32 are searched for their imports. Every listed file was
            // hashed above and the scan found no unlisted file; a file added to a writable folder
            // in between is the same-user-writable-install limitation recorded in the ADR.
            foreach (var (file, _) in heldNative)
            {
                string resolved = resolvedPaths[file.Path];
                NativeLoadingForTesting?.Invoke(resolved);
                IntPtr hModule = NativeMethods.LoadLibraryEx(
                    resolved,
                    IntPtr.Zero,
                    NativeMethods.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | NativeMethods.LOAD_LIBRARY_SEARCH_SYSTEM32);

                if (hModule == IntPtr.Zero)
                {
                    int win32Err = Marshal.GetLastWin32Error();
                    FreeNativeLibraries(nativeHandles.Values);
                    return new ExtensionLoadResult(
                        folderName,
                        manifest.Id,
                        ExtensionStatus.LoadFailed,
                        $"Failed to load native library '{file.Path}' via LoadLibraryEx (Win32 error {win32Err})",
                        manifestSha256,
                        null,
                        manifest,
                        isFirstParty);
                }

                string fileName = Path.GetFileName(resolved);
                nativeHandles[file.Path] = hModule;
                nativeHandles[fileName] = hModule;
                if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    nativeHandles[fileName[..^4]] = hModule;
                }
            }
        }
        catch (Exception ex)
        {
            FreeNativeLibraries(nativeHandles.Values);
            return new ExtensionLoadResult(
                folderName,
                manifest.Id,
                ExtensionStatus.LoadFailed,
                $"Failed reading or loading files: {ex.Describe()}",
                manifestSha256,
                null,
                manifest,
                isFirstParty);
        }
        finally
        {
            foreach (var (_, stream) in heldNative)
            {
                stream.Dispose();
            }
        }

        // 12. Create AssemblyLoadContext and load extension
        ExtensionLoadContext? alc = null;
        try
        {
            var managedAssemblies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in manifest.Files)
            {
                if (!file.Native)
                {
                    string resolved = resolvedPaths[file.Path];
                    string fileName = Path.GetFileName(resolved);
                    byte[] bytes = fileBytes[file.Path];

                    managedAssemblies[fileName] = bytes;
                    managedAssemblies[file.Path] = bytes;
                    if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        managedAssemblies[fileName[..^4]] = bytes;
                    }
                }
            }

            alc = new ExtensionLoadContext($"Extension_{manifest.Id}", managedAssemblies, nativeHandles);
            AlcCreatedForTesting?.Invoke(alc);

            byte[] entryBytes = fileBytes[manifest.Entry.Assembly];
            Assembly entryAssembly;
            using (var stream = new MemoryStream(entryBytes))
            {
                entryAssembly = alc.LoadFromStream(stream);
            }

            Type? extType = entryAssembly.GetType(manifest.Entry.Type, throwOnError: false);
            if (extType == null || !typeof(IExtension).IsAssignableFrom(extType))
            {
                SafeUnload(alc);
                FreeNativeLibraries(nativeHandles.Values);
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.EntryNotFound,
                    $"Entry type '{manifest.Entry.Type}' was not found in '{manifest.Entry.Assembly}' or does not implement IExtension",
                    manifestSha256,
                    null,
                    manifest,
                    isFirstParty);
            }

            object? createdObj = Activator.CreateInstance(extType);
            if (createdObj is not IExtension extInstance)
            {
                SafeUnload(alc);
                FreeNativeLibraries(nativeHandles.Values);
                return new ExtensionLoadResult(
                    folderName,
                    manifest.Id,
                    ExtensionStatus.EntryNotFound,
                    $"Failed to cast instantiated entry '{manifest.Entry.Type}' to IExtension",
                    manifestSha256,
                    null,
                    manifest,
                    isFirstParty);
            }

            // Rule 11: Provide canonical/absolute path to IExtensionContext.ExtensionDirectory
            string absoluteExtDir = Path.GetFullPath(dir);

            var context = new ExtensionContext(
                extensionDirectory: absoluteExtDir,
                language: options.Language,
                hostIsElevated: options.HostIsElevated,
                hostContractVersion: hostContract,
                log: options.Log);

            extInstance.Initialize(context);

            loadedIds?.Add(manifest.Id);

            var loadedExt = new LoadedExtension(
                manifest,
                extInstance,
                alc,
                nativeHandles.Values.Distinct().ToList(),
                options.Log);

            return new ExtensionLoadResult(
                folderName,
                manifest.Id,
                ExtensionStatus.Loaded,
                "Extension loaded successfully",
                manifestSha256,
                loadedExt,
                manifest,
                isFirstParty);
        }
        catch (Exception ex)
        {
            // Rule 7: Unload and free libraries before calling log delegate
            SafeUnload(alc);
            FreeNativeLibraries(nativeHandles.Values);

            options.Log.SafeLog($"Extension '{manifest.Id}' failed during load or Initialize(): {ex.Describe()}");

            return new ExtensionLoadResult(
                folderName,
                manifest.Id,
                ExtensionStatus.LoadFailed,
                $"Load or initialization threw an exception: {ex.Describe()}",
                manifestSha256,
                null,
                manifest,
                isFirstParty);
        }
    }

    private static void FreeNativeLibraries(IEnumerable<IntPtr> handles)
    {
        foreach (IntPtr h in handles.Distinct())
        {
            if (h != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.FreeLibrary(h);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }
    }

    private static Version NormalizeVersion(Version v)
    {
        int major = v.Major >= 0 ? v.Major : 0;
        int minor = v.Minor >= 0 ? v.Minor : 0;
        int build = v.Build >= 0 ? v.Build : 0;
        int revision = v.Revision >= 0 ? v.Revision : 0;
        return new Version(major, minor, build, revision);
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
