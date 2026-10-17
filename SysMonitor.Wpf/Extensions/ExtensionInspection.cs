namespace SysMonitor.Extensions;

/// <summary>
/// Result of inspecting an extension folder without loading any code or native libraries.
/// Status == Loaded means structurally valid: every declared file exists and its SHA-256 matches.
/// </summary>
public sealed record ExtensionInspection(
    ExtensionStatus Status,
    string Detail,
    ExtensionManifest? Manifest,
    string? ManifestSha256,
    bool IsFirstParty);
