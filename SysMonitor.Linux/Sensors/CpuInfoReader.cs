using System.Globalization;
using System.Runtime.InteropServices;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// /proc/cpuinfo.  The blocks are free-form key/value lines, so the reader
/// keeps the few keys that describe the package and ignores the rest.
/// </summary>
public static class CpuInfoParser
{
    public static CpuInfo Parse(string text)
    {
        string name = string.Empty;
        string vendor = string.Empty;
        bool? virtualization = null;
        string virtualizationType = string.Empty;

        int logical = 0;
        var sockets = new HashSet<int>();
        var cores = new HashSet<(int Socket, int Core)>();
        bool hasCoreIds = false;

        var block = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                ApplyBlock(block, ref name, ref vendor, ref virtualization,
                           ref virtualizationType, ref logical, sockets, cores,
                           ref hasCoreIds);
                block.Clear();
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            // "flags" can occur once per block; keeping the first is enough
            // because every logical CPU reports the same feature set.
            block.TryAdd(key, value);
        }

        ApplyBlock(block, ref name, ref vendor, ref virtualization,
                   ref virtualizationType, ref logical, sockets, cores,
                   ref hasCoreIds);

        int coreCount = hasCoreIds ? cores.Count : logical;
        return new CpuInfo
        {
            Name = name,
            Vendor = vendor,
            Sockets = sockets.Count > 0 ? sockets.Count : (logical > 0 ? 1 : 0),
            Cores = coreCount,
            Logical = logical,
            Virtualization = virtualization,
            VirtualizationType = virtualizationType,
        };
    }

    /// <summary>
    /// The virtualisation feature as a name.  "vmx" is Intel VT-x, "svm" is
    /// AMD-V; a CPU with neither reports an empty string rather than a guess.
    /// </summary>
    public static string VirtualizationType(IEnumerable<string> flags)
    {
        foreach (string flag in flags)
        {
            if (flag == "vmx")
            {
                return "VT-x";
            }
            if (flag == "svm")
            {
                return "AMD-V";
            }
        }
        return string.Empty;
    }

    private static void ApplyBlock(
        Dictionary<string, string> block,
        ref string name,
        ref string vendor,
        ref bool? virtualization,
        ref string virtualizationType,
        ref int logical,
        HashSet<int> sockets,
        HashSet<(int Socket, int Core)> cores,
        ref bool hasCoreIds)
    {
        if (block.Count == 0 || !block.ContainsKey("processor"))
        {
            return;
        }

        logical++;

        if (name.Length == 0 && block.TryGetValue("model name", out string? model))
        {
            // "Intel(R) Core(TM) i5-1035G1 CPU @ 1.00GHz" -> the chip name;
            // the clock is reported separately as BaseMhz.
            int at = model.IndexOf(" CPU @ ", StringComparison.Ordinal);
            name = at > 0 ? model[..at] : model;
        }
        if (vendor.Length == 0 && block.TryGetValue("vendor_id", out string? vendorId))
        {
            vendor = vendorId;
        }

        int socket = block.TryGetValue("physical id", out string? physical)
                     && int.TryParse(physical, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)
            ? s
            : 0;
        sockets.Add(socket);

        if (block.TryGetValue("core id", out string? coreText)
            && int.TryParse(coreText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int core))
        {
            hasCoreIds = true;
            cores.Add((socket, core));
        }

        if (block.TryGetValue("flags", out string? flags))
        {
            string[] parts = SysFs.Fields(flags);
            if (parts.Contains("vmx") || parts.Contains("svm"))
            {
                virtualization = true;
                if (virtualizationType.Length == 0)
                {
                    virtualizationType = VirtualizationType(parts);
                }
            }
            else if (virtualization is null)
            {
                virtualization = false;
            }
        }
    }
}

/// <summary>One cache level as /sys/devices/system/cpu/cpu*/cache describes it.</summary>
public readonly record struct CpuCacheInfo(int Level, string Type, int SizeKb, string SharedCpuList);

/// <summary>
/// Cache totals.  Every logical CPU repeats its L1/L2 per core, and both
/// hyper-threads share the same cache directory target, so the same cache is
/// deduplicated by level and the list of CPUs sharing it.
/// </summary>
public static class CpuCaches
{
    public static IReadOnlyList<CpuCacheInfo> Distinct(IEnumerable<CpuCacheInfo> caches)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CpuCacheInfo>();

        foreach (CpuCacheInfo cache in caches)
        {
            string key = cache.SharedCpuList.Length > 0
                ? $"{cache.Level}|{cache.Type}|{cache.SharedCpuList}"
                : $"{cache.Level}|{cache.Type}|{result.Count}";
            if (seen.Add(key))
            {
                result.Add(cache);
            }
        }
        return result;
    }

    public static int L2Kb(IEnumerable<CpuCacheInfo> caches) => Sum(caches, 2);

    public static int L3Kb(IEnumerable<CpuCacheInfo> caches) => Sum(caches, 3);

    /// <summary>"512K", "1M", "32768K" to kilobytes.  Zero when unreadable.</summary>
    public static int ParseSizeKb(string text)
    {
        string value = text.Trim();
        if (value.Length == 0)
        {
            return 0;
        }

        char unit = char.ToUpperInvariant(value[^1]);
        string number = char.IsDigit(unit) ? value : value[..^1];
        if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
        {
            return 0;
        }

        return unit switch
        {
            'K' => size,
            'M' => size * 1024,
            'G' => size * 1024 * 1024,
            _ => size,
        };
    }

    private static int Sum(IEnumerable<CpuCacheInfo> caches, int level)
    {
        long total = 0;
        foreach (CpuCacheInfo cache in Distinct(caches))
        {
            if (cache.Level != level)
            {
                continue;
            }
            // L2 is per-core data or unified; an instruction cache at L2
            // (ARM) still counts, so the type is informational only.
            total += cache.SizeKb;
        }
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }
}

/// <summary>
/// Reads what the processor is: /proc/cpuinfo plus the cache and frequency
/// files under /sys/devices/system/cpu.  Everything is under the root.
/// </summary>
public static class CpuInfoReader
{
    public static CpuInfo Read(string root = "/")
    {
        string? cpuinfo = SysFs.ReadText(root, "proc", "cpuinfo");
        CpuInfo baseInfo = cpuinfo is null ? new CpuInfo() : CpuInfoParser.Parse(cpuinfo);

        int baseMhz = BaseMhz(root);
        IReadOnlyList<CpuCacheInfo> caches = Caches(root);

        return new CpuInfo
        {
            Name = baseInfo.Name,
            Vendor = baseInfo.Vendor,
            Sockets = baseInfo.Sockets,
            Cores = baseInfo.Cores,
            Logical = baseInfo.Logical,
            Virtualization = baseInfo.Virtualization,
            VirtualizationType = baseInfo.VirtualizationType,
            BaseMhz = baseMhz,
            MaxMhz = MaxMhz(root),
            Architecture = ArchitectureName(RuntimeInformation.OSArchitecture),
            L2Kb = CpuCaches.L2Kb(caches),
            L3Kb = CpuCaches.L3Kb(caches),
        };
    }

    /// <summary>The rated clock: base_frequency, then the maximum boost, else 0.</summary>
    public static int BaseMhz(string root = "/")
    {
        long? khz = SysFs.ReadLong(root, "sys", "devices", "system", "cpu", "cpu0", "cpufreq", "base_frequency")
                    ?? SysFs.ReadLong(root, "sys", "devices", "system", "cpu", "cpu0", "cpufreq", "cpuinfo_max_freq");
        if (khz is not long value || value <= 0)
        {
            return 0;
        }
        return (int)Math.Min(value / 1000, int.MaxValue);
    }

    /// <summary>
    /// The highest clock the package will run at, from cpu0's
    /// <c>cpuinfo_max_freq</c>.  Null when the cpufreq driver does not publish it.
    /// </summary>
    public static int? MaxMhz(string root = "/")
    {
        long? khz = SysFs.ReadLong(root, "sys", "devices", "system", "cpu", "cpu0", "cpufreq", "cpuinfo_max_freq");
        return khz is long value && value > 0 ? KilohertzToMhz(value) : null;
    }

    /// <summary>
    /// One core's current clock, from <c>cpu&lt;N&gt;/cpufreq/scaling_cur_freq</c>.
    /// The file is in kHz; a value that is missing or not positive is null.
    /// </summary>
    public static int? CurrentMhz(string root, int index)
    {
        string cpu = "cpu" + index.ToString(CultureInfo.InvariantCulture);
        long? khz = SysFs.ReadLong(root, "sys", "devices", "system", "cpu", cpu,
            "cpufreq", "scaling_cur_freq");
        return khz is long value && value > 0 ? KilohertzToMhz(value) : null;
    }

    private static int KilohertzToMhz(long khz) =>
        (int)Math.Min(khz / 1000, int.MaxValue);

    /// <summary>The processor architecture as uname would print it.</summary>
    public static string ArchitectureName(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x86_64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "aarch64",
        Architecture.Arm => "arm",
        Architecture.Armv6 => "armv6",
        Architecture.S390x => "s390x",
        Architecture.LoongArch64 => "loongarch64",
        _ => architecture.ToString().ToLowerInvariant(),
    };

    public static IReadOnlyList<CpuCacheInfo> Caches(string root = "/")
    {
        var caches = new List<CpuCacheInfo>();
        string cpuRoot = SysFs.Path(root, "sys", "devices", "system", "cpu");

        foreach (string cpuDir in SysFs.Directories(cpuRoot))
        {
            string name = Path.GetFileName(cpuDir);
            if (!IsCpu(name))
            {
                continue;
            }

            foreach (string cacheDir in SysFs.Directories(cpuDir, "cache"))
            {
                if (!Path.GetFileName(cacheDir).StartsWith("index", StringComparison.Ordinal))
                {
                    continue;
                }

                int? level = SysFs.ReadInt(cacheDir, "level");
                string? size = SysFs.ReadTrimmed(cacheDir, "size");
                if (level is not int levelValue || size is null)
                {
                    continue;
                }

                string type = SysFs.ReadTrimmed(cacheDir, "type") ?? string.Empty;
                string shared = SysFs.ReadTrimmed(cacheDir, "shared_cpu_list") ?? string.Empty;
                caches.Add(new CpuCacheInfo(levelValue, type, CpuCaches.ParseSizeKb(size), shared));
            }
        }

        return caches;
    }

    internal static bool IsCpu(string name) =>
        name.Length > 3
        && name.StartsWith("cpu", StringComparison.Ordinal)
        && int.TryParse(name[3..], NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
