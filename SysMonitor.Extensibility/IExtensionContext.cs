namespace SysMonitor.Extensibility;

/// <summary>
/// Execution context passed from the host to an extension.
/// </summary>
public interface IExtensionContext
{
    /// <summary>
    /// Absolute path to the directory where the extension and its files reside.
    /// </summary>
    string ExtensionDirectory { get; }

    /// <summary>
    /// Host UI language code ("th" or "en").
    /// </summary>
    string Language { get; }

    /// <summary>
    /// Indicates whether the host is running with elevated (administrator) privileges.
    /// </summary>
    bool HostIsElevated { get; }

    /// <summary>
    /// Extensibility contract version supported by the host.
    /// </summary>
    Version HostContractVersion { get; }

    /// <summary>
    /// Writes a diagnostic message to the host's log.
    /// </summary>
    /// <param name="message">Log message.</param>
    void Log(string message);
}
