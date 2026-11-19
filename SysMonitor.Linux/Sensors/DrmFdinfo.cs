using System.Globalization;

namespace SysMonitor.Linux.Sensors;

/// <summary>One DRM client as a /proc/&lt;pid&gt;/fdinfo file describes it.</summary>
public sealed class DrmClient
{
    public long ClientId { get; init; }
    public string PciDevice { get; init; } = string.Empty;

    /// <summary>Cumulative engine time in nanoseconds, keyed by engine name.</summary>
    public IReadOnlyDictionary<string, long> Engines { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
}

/// <summary>
/// The DRM lines of fdinfo.  They are how the kernel reports GPU work per
/// client without root: every open render node exposes its engine counters.
/// </summary>
public static class DrmFdinfo
{
    public static DrmClient? Parse(string text)
    {
        long clientId = 0;
        string pci = string.Empty;
        var engines = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = line[..colon];
            string value = line[(colon + 1)..].Trim();

            if (key == "drm-pdev")
            {
                pci = value;
            }
            else if (key == "drm-client-id")
            {
                long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out clientId);
            }
            else if (key.StartsWith("drm-engine-", StringComparison.Ordinal))
            {
                string engine = key["drm-engine-".Length..];
                string[] fields = SysFs.Fields(value);
                if (fields.Length > 0
                    && long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ns)
                    && ns >= 0)
                {
                    engines[engine] = ns;
                }
            }
        }

        if (pci.Length == 0 || clientId <= 0)
        {
            return null;
        }

        return new DrmClient { ClientId = clientId, PciDevice = pci, Engines = engines };
    }
}
