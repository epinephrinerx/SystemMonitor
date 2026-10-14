namespace SysMonitor.Extensibility;

/// <summary>
/// Entry point interface that must be implemented by extensions.
/// </summary>
public interface IExtension
{
    /// <summary>
    /// Initializes the extension with context provided by the host.
    /// </summary>
    /// <param name="context">The extension execution context.</param>
    void Initialize(IExtensionContext context);

    /// <summary>
    /// Shuts down the extension, releasing any native or managed resources.
    /// </summary>
    void Shutdown();
}
