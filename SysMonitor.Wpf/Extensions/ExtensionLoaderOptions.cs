namespace SysMonitor.Extensions;

/// <summary>
/// Options configuring ExtensionLoader behavior.
/// </summary>
public sealed class ExtensionLoaderOptions
{
    /// <summary>
    /// Path to the extensions root folder (usually &lt;exe directory&gt;\extensions).
    /// </summary>
    public string ExtensionsRoot { get; init; } = string.Empty;

    /// <summary>
    /// Host application version (e.g. 4.2.0).
    /// </summary>
    public Version HostVersion { get; init; } = new(1, 0, 0);

    /// <summary>
    /// True if the host process is running as Administrator.
    /// </summary>
    public bool HostIsElevated { get; init; }

    /// <summary>
    /// UI language code ("th" or "en").
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// Allowlist of first-party extension manifest SHA-256 hashes.
    /// </summary>
    public FirstPartyAllowlist FirstParty { get; init; } = FirstPartyAllowlist.Empty;

    /// <summary>
    /// User approval store for trusted extensions (null if none).
    /// </summary>
    public ApprovalStore? Approvals { get; init; }

    /// <summary>
    /// Diagnostic logger callback.
    /// </summary>
    public Action<string>? Log { get; init; }

    /// <summary>
    /// Optional predicate returning whether an extension id is enabled (null = all enabled).
    /// </summary>
    public Func<string, bool>? IsEnabled { get; init; }

    /// <summary>
    /// Optional predicate deciding whether an extension should remain loaded even if disabled
    /// by the user (e.g. for first-party background metric providers like SMART temperature).
    /// </summary>
    public Func<ExtensionManifest, bool, bool>? KeepLoaded { get; init; }
}

