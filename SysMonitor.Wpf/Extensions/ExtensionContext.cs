using SysMonitor.Extensibility;

namespace SysMonitor.Extensions;

/// <summary>
/// Host context provided to extensions during initialization.
/// </summary>
internal sealed class ExtensionContext : IExtensionContext
{
    private readonly Action<string>? _log;

    public string ExtensionDirectory { get; }
    public string Language { get; }
    public bool HostIsElevated { get; }
    public Version HostContractVersion { get; }

    public ExtensionContext(
        string extensionDirectory,
        string language,
        bool hostIsElevated,
        Version hostContractVersion,
        Action<string>? log)
    {
        ExtensionDirectory = extensionDirectory;
        Language = language;
        HostIsElevated = hostIsElevated;
        HostContractVersion = hostContractVersion;
        _log = log;
    }

    public void Log(string message) => _log.SafeLog(message);
}
