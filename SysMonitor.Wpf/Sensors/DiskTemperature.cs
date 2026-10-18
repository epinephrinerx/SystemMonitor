using SysMonitor.Model;

namespace SysMonitor.Sensors;

/// <summary>
/// Picks the temperature to show for one physical disk.
///
/// The storage stack goes first: it works without elevation, and on the disks
/// that answer it, it matched SMART read at the same moment (2026-10-12: 45 and
/// 45, 51 and 51). SMART only fills in for the disks the stack cannot read --
/// USB bridges and external NVMe enclosures, where it fails with error 1117 or 87.
///
/// A disk with no sensor stays null. Nothing here estimates.
/// </summary>
public static class DiskTemperature
{
    /// <summary>
    /// A reading worth showing. Zero is excluded on purpose: a Realtek NVMe
    /// enclosure reported 0 C through WMI while SMART read 34 C, so a zero is a
    /// bridge saying "no sensor", not a frozen disk.
    /// </summary>
    public static bool Plausible(int? celsius) => celsius is > 0 and < 150;

    /// <param name="storage">The storage-stack reading, null when it failed.</param>
    /// <param name="external">The external reading (e.g. from an extension), null when none.</param>
    public static TempResult Merge(int? storage, int? external)
    {
        if (Plausible(storage))
        {
            return new TempResult(storage, TempSource.Storage);
        }
        if (Plausible(external))
        {
            return new TempResult(external, TempSource.Smart);
        }
        return new TempResult(null, TempSource.None);
    }
}
