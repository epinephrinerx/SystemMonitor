using System.Net.NetworkInformation;
using SysMonitor.Model;

namespace SysMonitor.Sensors;

/// <summary>
/// Upload and download rates per wired and wireless adapter.
///
/// Through <see cref="NetworkInterface"/> rather than GetIfTable2: the
/// counters, the media type and the link state all come from the base class
/// library, so this needs neither a P/Invoke of its own nor a package.
///
/// Rates reuse <see cref="IoRate"/>, so an adapter whose probe was skipped
/// gets the same treatment as a drive whose probe was skipped -- its own
/// elapsed time, not a shared tick.
/// </summary>
internal sealed class NetworkSensor
{
    private readonly Dictionary<string, ((long Read, long Written) Counters, long Stamp)> _previous = new();

    /// <summary>
    /// A link speed of zero means the adapter reports none; the UI treats that
    /// as "no percentage to draw" rather than dividing by it.
    /// </summary>
    public List<Adapter> Read(bool includeWireless = true)
    {
        var adapters = new List<Adapter>();
        var seen = new HashSet<string>();

        foreach (NetworkInterface nic in Interfaces(includeWireless))
        {
            string id = nic.Id;
            seen.Add(id);

            IPv4InterfaceStatistics stats;
            try
            {
                stats = nic.GetIPv4Statistics();
            }
            catch (NetworkInformationException)
            {
                continue;       // the adapter went away between calls
            }

            // Received maps to download, sent to upload; IoRate's names are
            // read/write because it was written for drives.
            var counters = (stats.BytesReceived, stats.BytesSent);
            long stamp = Environment.TickCount64;

            double down = 0, up = 0;
            if (_previous.TryGetValue(id, out var previous))
            {
                (down, up) = IoRate.PerSecond(previous.Counters, previous.Stamp,
                                              counters, stamp);
            }
            _previous[id] = (counters, stamp);

            adapters.Add(new Adapter
            {
                Id = id,
                Name = nic.Name,
                Description = nic.Description,
                Wireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                SpeedMbps = nic.Speed > 0 ? nic.Speed / 1_000_000.0 : 0,
                DownMb = down,
                UpMb = up,
                TotalReceived = stats.BytesReceived,
                TotalSent = stats.BytesSent,
            });
        }

        // An unplugged adapter must not leave a stale baseline behind to be
        // divided against when it comes back.
        foreach (string stale in _previous.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _previous.Remove(stale);
        }
        return adapters;
    }

    private static IEnumerable<NetworkInterface> Interfaces(bool includeWireless)
    {
        NetworkInterface[] all;
        try
        {
            all = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<NetworkInterface>();
        }

        return all.Where(nic =>
            nic.OperationalStatus == OperationalStatus.Up
            && (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                || nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet
                || (includeWireless
                    && nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)));
    }
}
