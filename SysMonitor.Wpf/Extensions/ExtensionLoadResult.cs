namespace SysMonitor.Extensions;

/// <summary>
/// Result of attempting to load an extension folder.
/// </summary>
public sealed class ExtensionLoadResult
{
    /// <summary>
    /// Name of the folder under extensions/.
    /// </summary>
    public string FolderName { get; }

    /// <summary>
    /// Extension identifier read from the manifest, or null if unreadable.
    /// </summary>
    public string? Id { get; }

    /// <summary>
    /// Status indicating whether the extension was loaded or the reason it was skipped.
    /// </summary>
    public ExtensionStatus Status { get; }

    /// <summary>
    /// English diagnostic message explaining why loading succeeded or failed.
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// SHA-256 hash of extension.json bytes, or null if unreadable.
    /// </summary>
    public string? ManifestSha256 { get; }

    /// <summary>
    /// The loaded extension instance if Status == Loaded; otherwise null.
    /// </summary>
    public LoadedExtension? LoadedExtension { get; }

    public ExtensionLoadResult(
        string folderName,
        string? id,
        ExtensionStatus status,
        string detail,
        string? manifestSha256,
        LoadedExtension? loadedExtension)
    {
        FolderName = folderName;
        Id = id;
        Status = status;
        Detail = detail;
        ManifestSha256 = manifestSha256;
        LoadedExtension = loadedExtension;
    }
}
