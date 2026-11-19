using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The module switches that decide which sampled devices a view may show.
///
/// <c>include_network</c> is a presentation rule: the sampler keeps reading
/// every mount, and the views leave the network filesystems out when the
/// setting is off. Filtering here keeps the widget, Overall and the alert
/// tracker on the same list.
/// </summary>
public static class DeviceFilter
{
    /// <summary>The drives a view may show: all of them, or the local ones only.</summary>
    public static IReadOnlyList<Disk> VisibleDisks(Snapshot snapshot, AppConfig config)
    {
        if (config.IncludeNetwork)
        {
            return snapshot.Disks;
        }
        return snapshot.Disks.Where(disk => !disk.IsNetwork).ToList();
    }
}
