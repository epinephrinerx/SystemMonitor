namespace SysMonitor.Extensions;

/// <summary>Which I18n key says why an extension is not running.</summary>
public static class ExtensionStatusText
{
    public static string KeyFor(ExtensionStatus status) => status switch
    {
        ExtensionStatus.NotApproved => "ext_status_not_approved",
        ExtensionStatus.NotTrustedWhenElevated => "ext_status_not_trusted_when_elevated",
        ExtensionStatus.ContractMismatch => "ext_status_contract_mismatch",
        ExtensionStatus.HostTooOld => "ext_status_host_too_old",
        ExtensionStatus.HashMismatch => "ext_status_hash_mismatch",
        ExtensionStatus.UnsafePath => "ext_status_unsafe_path",
        ExtensionStatus.FileMissing => "ext_status_file_missing",
        ExtensionStatus.ManifestInvalid => "ext_status_manifest_invalid",
        ExtensionStatus.EntryNotFound => "ext_status_entry_not_found",
        ExtensionStatus.LoadFailed => "ext_status_load_failed",
        ExtensionStatus.DuplicateId => "ext_status_duplicate_id",
        ExtensionStatus.Disabled => "ext_status_disabled",
        // Loaded has nothing to explain.
        _ => string.Empty,
    };

    /// <summary>
    /// Determines whether the sidebar checkbox for an extension can be toggled by the user.
    /// An enabled extension can always be toggled off. A disabled extension can only be toggled
    /// on if its status is Loaded or Disabled.
    /// </summary>
    public static bool CanToggle(ExtensionStatus status, bool enabled)
    {
        if (enabled)
        {
            return true;
        }

        return status is ExtensionStatus.Loaded or ExtensionStatus.Disabled;
    }

    /// <summary>
    /// Determines whether the user can approve an unapproved extension.
    /// Only unapproved extensions on a non-elevated host can be approved.
    /// </summary>
    public static bool CanApprove(ExtensionStatus status, bool hostElevated) =>
        status == ExtensionStatus.NotApproved && !hostElevated;
}
