using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>The identity a process keeps across samples: PID plus start time.</summary>
public readonly record struct ProcessIdentity(int Pid, ulong? StartTime);

/// <summary>What one process holds on the GPU in this sample.</summary>
public readonly record struct GpuUsage(double? Percent, long? MemoryBytes);

/// <summary>
/// Per-process GPU use from the DRM fdinfo of the processes that hold a
/// <c>/dev/dri/*</c> file descriptor. The scan is the expensive part, so it runs
/// every <see cref="IntervalMs"/> milliseconds and replays the last figures in
/// between; <see cref="ProcessStats"/> merges those figures onto its rows.
///
/// Only processes with a DRM descriptor are inspected, so a machine with no GPU
/// clients costs a directory listing per process and nothing else.
/// </summary>
public sealed class ProcessGpu
{
    public const int IntervalMs = 4000;

    private readonly string _root;

    /// <summary>Engine counters and memory per (process, DRM client) from the last real scan.</summary>
    private Dictionary<ProcessKey, Dictionary<ClientKey, ClientState>> _previous = new();

    /// <summary>The figures of the last real scan, by process identity, for the ticks in between.</summary>
    private Dictionary<ProcessKey, GpuUsage> _last = new();

    private long? _scanStamp;

    public ProcessGpu(string root = "/")
    {
        _root = string.IsNullOrEmpty(root) ? "/" : root;
    }

    /// <summary>
    /// The GPU figures for every identity, keyed by PID. A process with no DRM
    /// descriptor gets two nulls; a process seen for the first time gets a null
    /// percentage because there is no earlier counter to subtract; a process
    /// without a start time is never scanned and always gets two nulls.
    /// </summary>
    public IReadOnlyDictionary<int, GpuUsage> Read(
        long stamp, IReadOnlyList<ProcessIdentity> processes)
    {
        if (_scanStamp is long previousStamp
            && stamp >= previousStamp
            && stamp - previousStamp < IntervalMs)
        {
            return Project(_last, processes);
        }

        Dictionary<ProcessKey, Dictionary<ClientKey, ClientState>> previous = _previous;
        var next = new Dictionary<ProcessKey, Dictionary<ClientKey, ClientState>>();
        var values = new Dictionary<ProcessKey, GpuUsage>(processes.Count);
        double elapsedNs = _scanStamp is long before && stamp > before
            ? (stamp - before) * 1_000_000.0
            : 0;

        foreach (ProcessIdentity identity in processes)
        {
            // Amendment 2 item 2: a process with no start time has no identity,
            // so caching an engine counter for it would attribute a delta to
            // whatever later holds the PID. It is never scanned, cached or
            // projected, and therefore keeps two null GPU values.
            if (identity.StartTime is not > 0)
            {
                continue;
            }

            var key = new ProcessKey(identity.Pid, identity.StartTime);
            Dictionary<ClientKey, ClientState> clients = ReadClients(identity.Pid);
            next[key] = clients;

            previous.TryGetValue(key, out Dictionary<ClientKey, ClientState>? earlier);

            double busyNs = 0;
            int comparable = 0;
            long? memory = null;

            foreach (KeyValuePair<ClientKey, ClientState> client in clients)
            {
                if (client.Value.MemoryBytes is long bytes)
                {
                    memory = (memory ?? 0) + bytes;
                }

                if (earlier is null
                    || !earlier.TryGetValue(client.Key, out ClientState? earlierClient)
                    || elapsedNs <= 0)
                {
                    // First sight of this client (or no usable interval): the
                    // percentage is unknown, not zero.
                    continue;
                }

                // Amendment 2 item 3: a client with no engine counters in
                // either scan (NVIDIA proprietary fdinfo, for one) is not a
                // comparable measurement. Counting it as 0 % busy would invent
                // an idle GPU out of a blind one.
                if (client.Value.Engines.Count == 0 || earlierClient.Engines.Count == 0)
                {
                    continue;
                }

                long busiest = 0;
                foreach (string engine in client.Value.Engines.Keys.Union(
                             earlierClient.Engines.Keys, StringComparer.Ordinal))
                {
                    long after = client.Value.Engines.GetValueOrDefault(engine);
                    long earlierNs = earlierClient.Engines.GetValueOrDefault(engine);
                    long delta = after - earlierNs;
                    if (delta > busiest)
                    {
                        busiest = delta;
                    }
                }
                busyNs += busiest;
                comparable++;
            }

            double? percent = elapsedNs > 0 && comparable > 0
                ? Math.Round(
                    Math.Clamp(busyNs / elapsedNs * 100, 0, 100),
                    1,
                    MidpointRounding.AwayFromZero)
                : null;

            values[key] = new GpuUsage(percent, memory);
        }

        _previous = next;
        _last = values;
        _scanStamp = stamp;
        return Project(values, processes);
    }

    /// <summary>
    /// A PID-keyed view of the cached values, but only for the identities that
    /// are present now: a recycled PID never inherits the earlier process's
    /// figures, and a process that left is not returned at all.
    /// </summary>
    private static IReadOnlyDictionary<int, GpuUsage> Project(
        Dictionary<ProcessKey, GpuUsage> values, IReadOnlyList<ProcessIdentity> processes)
    {
        var result = new Dictionary<int, GpuUsage>(processes.Count);
        foreach (ProcessIdentity identity in processes)
        {
            if (values.TryGetValue(new ProcessKey(identity.Pid, identity.StartTime), out GpuUsage usage))
            {
                result[identity.Pid] = usage;
            }
        }
        return result;
    }

    /// <summary>
    /// The DRM clients of one process, de-duplicated by (PCI device, client id).
    /// A client is open on several descriptors, each with its own fdinfo and the
    /// same counters, so counting descriptors would multiply the use.
    /// </summary>
    private Dictionary<ClientKey, ClientState> ReadClients(int pid)
    {
        var clients = new Dictionary<ClientKey, ClientState>();
        string processDirectory = SysFs.Path(
            _root, "proc", pid.ToString(CultureInfo.InvariantCulture));
        string fdDirectory = Path.Combine(processDirectory, "fd");

        foreach (string fd in SysFs.Files(fdDirectory))
        {
            string? target = SysFs.LinkTarget(fd);
            if (target is null || !target.Contains("/dev/dri/", StringComparison.Ordinal))
            {
                continue;
            }

            string? text = SysFs.ReadText(Path.Combine(
                processDirectory, "fdinfo", Path.GetFileName(fd)));
            if (text is null || text.IndexOf("drm-pdev", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            DrmClient? client = DrmFdinfo.Parse(text);
            if (client is null)
            {
                continue;
            }

            var key = new ClientKey(client.PciDevice, client.ClientId);
            if (clients.ContainsKey(key))
            {
                continue;
            }

            clients[key] = new ClientState(client.Engines, client.MemoryBytes);
        }

        return clients;
    }

    private sealed record ClientState(
        IReadOnlyDictionary<string, long> Engines, long? MemoryBytes);

    private readonly record struct ProcessKey(int Pid, ulong? StartTime);

    private readonly record struct ClientKey(string PciDevice, long ClientId);
}
