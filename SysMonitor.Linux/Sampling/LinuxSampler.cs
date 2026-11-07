using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sampling
{
    public sealed class LinuxSampler
    {
        private readonly string _procRoot;
        private readonly string _sysRoot;
        private readonly Func<string, (long TotalBytes, long FreeBytes)>? _fsUsage;
        private long? _prevNow;
        private IReadOnlyList<CpuTimes>? _prevCores;
        private CpuTimes? _prevTotal;
        private Dictionary<string, NetCounters>? _prevNet;
        private Dictionary<string, DiskCounters>? _prevDisk;
        private bool _ready;
        private readonly object _gate = new object();

        public LinuxSampler(string procRoot, string sysRoot, Func<string, (long TotalBytes, long FreeBytes)>? fsUsage = null)
        {
            _procRoot = procRoot ?? string.Empty;
            _sysRoot = sysRoot ?? string.Empty;
            _fsUsage = fsUsage;
        }

        public Snapshot Sample(long nowMillis)
        {
            lock (_gate)
            {
                bool wasReady = _ready;
                try
                {
                    double seconds = _prevNow is long p ? ((double)nowMillis - (double)p) / 1000.0 : 0;

                    var cpu = Safe(() => SampleCpu(seconds), (Array.Empty<Core>(), 0));
                    var ram = Safe(() => SampleRam(), new Ram());
                    var temperature = Safe(() => SampleTemperature(), (null, false));
                    var disks = Safe(() => SampleDisks(seconds), Array.Empty<Disk>());
                    var network = Safe(() => SampleNetwork(seconds), Array.Empty<Adapter>());
                    var modules = SampleModules();

                    _ready = true;
                    _prevNow = nowMillis;

                    return new Snapshot
                    {
                        Cores = cpu.cores,
                        Ram = ram,
                        Disks = disks,
                        Adapters = network,
                        Modules = modules,
                        CpuTotal = cpu.total,
                        CpuTemp = temperature.cpuTemp,
                        CpuTempEstimated = temperature.cpuTempEstimated,
                        Ready = wasReady
                    };
                }
                catch (Exception)
                {
                    _ready = true;
                    return new Snapshot { Ready = wasReady };
                }
            }
        }

        private (IReadOnlyList<Core> cores, int total) SampleCpu(double seconds)
        {
            string? stat = Read(_procRoot, "stat");
            if (stat == null)
            {
                _prevCores = null;
                _prevTotal = null;
                return (Array.Empty<Core>(), 0);
            }

            var cur = ProcStat.ParseCores(stat);
            var total = ProcStat.ParseTotal(stat);

            var cores = cur.Select((c, i) => new Core
            {
                Usage = _prevCores != null && i < _prevCores.Count ? ProcStat.Usage(_prevCores[i], c) : 0
            }).ToList();

            int totalUsage = _prevTotal is CpuTimes pt ? ProcStat.Usage(pt, total) : 0;

            _prevCores = cur;
            _prevTotal = total;

            return (cores, totalUsage);
        }

        private Ram SampleRam()
        {
            return MemInfo.Parse(Read(_procRoot, "meminfo") ?? "");
        }

        private (int? cpuTemp, bool cpuTempEstimated) SampleTemperature()
        {
            if (string.IsNullOrEmpty(_sysRoot))
            {
                return (null, false);
            }

            var zones = new List<(string Name, int MilliCelsius)>();
            foreach (var dir in Dirs(Path.Combine(_sysRoot, "class", "hwmon")))
            {
                if (Path.GetFileName(dir).StartsWith("hwmon", StringComparison.Ordinal))
                {
                    string? name = Read(dir, "name")?.Trim();
                    string? tempText = Read(dir, "temp1_input")?.Trim();
                    if (name != null && tempText != null && int.TryParse(tempText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int milli))
                    {
                        if (!name.StartsWith("nvme", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("drivetemp", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("amdgpu", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("nouveau", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("radeon", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("iwlwifi", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("ath1", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("bat", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("ucsi", StringComparison.OrdinalIgnoreCase))
                        {
                            zones.Add((name, milli));
                        }
                    }
                }
            }

            int? cpuTemp = HwmonThermal.PickCpu(zones);
            bool cpuTempEstimated = false;

            return (cpuTemp, cpuTempEstimated);
        }

        private IReadOnlyList<Disk> SampleDisks(double seconds)
        {
            var mounts = MountInfo.Parse(Read(_procRoot, "self", "mountinfo") ?? "");
            string? ds = Read(_procRoot, "diskstats");
            var curDisk = new Dictionary<string, DiskCounters>();
            if (ds != null)
            {
                foreach (var c in DiskStats.Parse(ds))
                {
                    if (!curDisk.ContainsKey(c.Name))
                    {
                        curDisk[c.Name] = c;
                    }
                }
            }

            var parentNumber = new Dictionary<string, int>();
            var physical = new Dictionary<string, PhysicalDisk>();

            var disks = mounts.Select(entry =>
            {
                string parent = BlockDevice.ParentDisk(entry.Device);
                if (parent != "")
                {
                    if (!parentNumber.ContainsKey(parent))
                    {
                        parentNumber[parent] = parentNumber.Count;
                        physical[parent] = BlockDevice.Read(_sysRoot ?? "", parent, parentNumber[parent]);
                    }
                }

                PhysicalDisk? pd = parent != "" ? physical[parent] : null;
                string media = (pd == null || pd.Media == "") ? "Disk" : pd.Media;
                string bus = (pd == null || pd.Bus == "") ? "Unknown" : pd.Bus;

                string t = entry.MountPoint.TrimEnd('/');
                int k = t.LastIndexOf('/');
                string last = k >= 0 ? t.Substring(k + 1) : t;
                string label = last == "" ? "/" : last;

                long total = 0, free = 0;
                try
                {
                    if (_fsUsage != null)
                    {
                        (total, free) = _fsUsage(entry.MountPoint);
                    }
                    else
                    {
                        var di = new DriveInfo(entry.MountPoint);
                        total = di.TotalSize;
                        free = di.AvailableFreeSpace;
                    }
                }
                catch (Exception)
                {
                    total = 0;
                    free = 0;
                }

                if (total <= 0)
                {
                    total = 0;
                    free = 0;
                }

                free = Math.Min(Math.Max(free, 0), total);
                long used = total - free;
                int usage = total > 0 ? (int)Math.Round(used * 100.0 / total, MidpointRounding.AwayFromZero) : 0;
                usage = Math.Clamp(usage, 0, 100);
                double usedGb = used / 1073741824.0;
                double totalGb = total / 1073741824.0;

                (double readMb, double writeMb) = parent != "" && _prevDisk != null && _prevDisk.TryGetValue(parent, out var pc) && curDisk.TryGetValue(parent, out var cc)
                    ? DiskStats.Rate(pc, cc, seconds)
                    : (0, 0);

                return new Disk
                {
                    Letter = entry.MountPoint,
                    Label = label,
                    Media = media,
                    Bus = bus,
                    Usage = usage,
                    UsedGb = usedGb,
                    TotalGb = totalGb,
                    ReadMb = readMb,
                    WriteMb = writeMb,
                    Temp = null,
                    Estimated = false,
                    Detail = new DriveDetail
                    {
                        Letter = entry.MountPoint,
                        DiskNumber = parent != "" ? parentNumber[parent] : (int?)null,
                        PartitionNumber = BlockDevice.PartitionNumber(entry.Device),
                        FileSystem = entry.FileSystem,
                        Label = label,
                        Disk = pd
                    }
                };
            }).ToList();

            _prevDisk = ds == null ? null : curDisk;

            return disks;
        }

        private IReadOnlyList<Adapter> SampleNetwork(double seconds)
        {
            string? nd = Read(_procRoot, "net", "dev");
            if (nd == null)
            {
                _prevNet = null;
                return Array.Empty<Adapter>();
            }

            var cur = NetDev.Parse(nd);

            var dict = new Dictionary<string, NetCounters>();
            foreach (var c in cur)
            {
                if (!dict.ContainsKey(c.Name))
                {
                    dict[c.Name] = c;
                }
            }

            var adapters = cur.Select(c =>
            {
                bool safe = SafeName(c.Name);
                bool wireless = safe && !string.IsNullOrEmpty(_sysRoot) && NetDev.IsWireless(Path.Combine(_sysRoot, "class", "net"), c.Name);
                double speed = 0;
                if (safe)
                {
                    string? st = Read(_sysRoot, "class", "net", c.Name, "speed")?.Trim();
                    if (st != null && int.TryParse(st, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sp) && sp > 0)
                    {
                        speed = sp;
                    }
                }

                (double down, double up) = _prevNet != null && _prevNet.TryGetValue(c.Name, out var pn)
                    ? NetDev.Rate(pn, c, seconds)
                    : (0, 0);

                return new Adapter
                {
                    Id = c.Name,
                    Name = c.Name,
                    Wireless = wireless,
                    SpeedMbps = speed,
                    DownMb = down,
                    UpMb = up,
                    TotalReceived = c.Received,
                    TotalSent = c.Sent
                };
            }).ToList();

            _prevNet = dict;

            return adapters;
        }

        private IReadOnlyList<Module> SampleModules()
        {
            return Array.Empty<Module>();
        }

        private string? Read(string root, params string[] parts)
        {
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            try
            {
                return File.ReadAllText(Path.Combine(root, Path.Combine(parts)));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private string[] Dirs(string dir)
        {
            if (string.IsNullOrEmpty(dir))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.Ordinal).ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private bool SafeName(string n)
        {
            return !string.IsNullOrEmpty(n) && n != "." && n != ".." && !n.Contains('/') && !n.Contains('\\') && !n.Contains('\0');
        }

        private T Safe<T>(Func<T> action, T fallback)
        {
            try
            {
                return action();
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
