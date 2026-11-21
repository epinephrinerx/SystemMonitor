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

    /// <summary>
    /// Video memory this client holds, in bytes. Old kernels write
    /// <c>drm-memory-&lt;region&gt;</c>; newer ones write
    /// <c>drm-total-&lt;region&gt;</c>. Null when neither line is present.
    /// </summary>
    public long? MemoryBytes { get; init; }

    /// <summary>
    /// Resident part of the video memory, when the kernel reports it. Kept
    /// apart from <see cref="MemoryBytes"/> so the two are never added twice.
    /// </summary>
    public long? ResidentBytes { get; init; }
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
        long memoryBytes = 0;
        bool hasMemory = false;
        long totalBytes = 0;
        bool hasTotal = false;
        long residentBytes = 0;
        bool hasResident = false;

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
            else if (key.StartsWith("drm-memory-", StringComparison.Ordinal))
            {
                if (TryMemoryBytes(value, out long bytes))
                {
                    memoryBytes += bytes;
                    hasMemory = true;
                }
            }
            else if (key.StartsWith("drm-total-", StringComparison.Ordinal))
            {
                if (TryMemoryBytes(value, out long bytes))
                {
                    totalBytes += bytes;
                    hasTotal = true;
                }
            }
            else if (key.StartsWith("drm-resident-", StringComparison.Ordinal))
            {
                if (TryMemoryBytes(value, out long bytes))
                {
                    residentBytes += bytes;
                    hasResident = true;
                }
            }
        }

        if (pci.Length == 0 || clientId <= 0)
        {
            return null;
        }

        return new DrmClient
        {
            ClientId = clientId,
            PciDevice = pci,
            Engines = engines,
            // Every reported region adds to the client's total. A newer kernel
            // writes drm-total-* instead of drm-memory-*; a fixture that has
            // both is summed as the handoff says, and a real kernel never emits
            // both for the same region.
            MemoryBytes = hasMemory || hasTotal ? memoryBytes + totalBytes : null,
            ResidentBytes = hasResident ? residentBytes : null,
        };
    }

    /// <summary>
    /// A memory line is a number and an optional unit: <c>KiB</c>, <c>MiB</c>
    /// or nothing (bytes).
    /// </summary>
    private static bool TryMemoryBytes(string value, out long bytes)
    {
        bytes = 0;
        string[] fields = SysFs.Fields(value);
        if (fields.Length == 0
            || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
            || number < 0)
        {
            return false;
        }

        long multiplier = fields.Length > 1
            ? fields[1] switch
            {
                "KiB" => 1024L,
                "MiB" => 1024L * 1024L,
                "GiB" => 1024L * 1024L * 1024L,
                _ => 1L,
            }
            : 1L;

        try
        {
            bytes = checked(number * multiplier);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
