namespace SysMonitor.Extensions;

/// <summary>
/// Status returned from an extension load attempt.
/// </summary>
public enum ExtensionStatus
{
    Loaded,
    ManifestInvalid,
    ContractMismatch,
    HostTooOld,
    FileMissing,
    HashMismatch,
    UnsafePath,
    NotApproved,
    NotTrustedWhenElevated,
    DuplicateId,
    EntryNotFound,
    LoadFailed,
}
