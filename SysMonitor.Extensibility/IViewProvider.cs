namespace SysMonitor.Extensibility;

/// <summary>
/// Interface implemented by extensions alongside <see cref="IExtension"/> to supply UI views.
/// Implementations must be thread-safe and return cached/instant results without blocking.
/// </summary>
public interface IViewProvider
{
    /// <summary>
    /// Returns the localized display name for the extension.
    /// </summary>
    /// <param name="language">Language code ("th" or "en").</param>
    string GetDisplayName(string language);

    /// <summary>
    /// Returns the latest view snapshot for the extension in the specified language.
    /// Must return immediately and thread-safe.
    /// </summary>
    /// <param name="language">Language code ("th" or "en").</param>
    ExtensionView GetView(string language);
}
