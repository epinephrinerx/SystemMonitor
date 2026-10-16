using SysMonitor.Extensibility;

namespace SysMonitor.Extensions;

/// <summary>
/// Immutable view bundle for one extension, collected on the sampler thread.
/// </summary>
public sealed record ExtensionViewData(
    string Id,
    string DisplayName,
    ExtensionView View);
