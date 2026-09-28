using SysMonitor.Model;
using SysMonitor.Native;

namespace SysMonitor.Sensors;

/// <summary>
/// The static facts about the processor: which chip, how many of it, and how
/// fast it is rated.
///
/// Win32_Processor rather than the registry, because a two-socket machine has
/// two of everything and the registry key only describes logical processor 0.
/// Queried once and cached, like the drive identities: a CPU does not change
/// model number while the widget is running.
/// </summary>
internal static class CpuDetails
{
    private const string Cimv2 = @"root\cimv2";

    private static CpuInfo? _cached;

    public static CpuInfo Read()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        string name = string.Empty;
        string vendor = string.Empty;
        int sockets = 0, cores = 0, logical = 0, baseMhz = 0, l2 = 0, l3 = 0;
        bool? virtualization = null;

        foreach (var row in Wmi.Query(Cimv2,
                     "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, " +
                     "MaxClockSpeed, L2CacheSize, L3CacheSize, VirtualizationFirmwareEnabled " +
                     "FROM Win32_Processor",
                     "Name", "Manufacturer", "NumberOfCores", "NumberOfLogicalProcessors",
                     "MaxClockSpeed", "L2CacheSize", "L3CacheSize", "VirtualizationFirmwareEnabled"))
        {
            sockets++;

            // A second socket is the same part as the first; the counts add up
            // across them but the name and the clock do not.
            if (name.Length == 0)
            {
                name = Text(row.GetValueOrDefault("Name"));
                vendor = Text(row.GetValueOrDefault("Manufacturer"));
                baseMhz = (int)(Number(row.GetValueOrDefault("MaxClockSpeed")) ?? 0);
                l2 = (int)(Number(row.GetValueOrDefault("L2CacheSize")) ?? 0);
                l3 = (int)(Number(row.GetValueOrDefault("L3CacheSize")) ?? 0);
                virtualization = row.GetValueOrDefault("VirtualizationFirmwareEnabled") as bool?;
            }

            cores += (int)(Number(row.GetValueOrDefault("NumberOfCores")) ?? 0);
            logical += (int)(Number(row.GetValueOrDefault("NumberOfLogicalProcessors")) ?? 0);
        }

        // Environment knows the logical count without WMI, so a machine whose
        // WMI is broken still gets the one figure that matters most.
        if (logical == 0)
        {
            logical = Environment.ProcessorCount;
        }

        virtualization = Virtualised(virtualization);

        _cached = new CpuInfo
        {
            Name = Tidy(name),
            Vendor = vendor,
            Sockets = sockets,
            Cores = cores,
            Logical = logical,
            BaseMhz = baseMhz,
            L2Kb = l2,
            L3Kb = l3,
            Virtualization = virtualization,
        };
        return _cached;
    }

    /// <summary>
    /// Whether virtualization is on, the way Task Manager decides it.
    ///
    /// The firmware flag alone is wrong on any machine that is already running
    /// a hypervisor: once Hyper-V owns the virtualization extensions, Windows
    /// itself runs in the root partition and both
    /// VirtualizationFirmwareEnabled and IsProcessorFeaturePresent report
    /// false -- on a machine where virtualization is plainly working. A
    /// hypervisor being present settles it on its own; the firmware flag only
    /// has the answer when there is no hypervisor to ask about.
    /// </summary>
    private static bool? Virtualised(bool? firmware)
    {
        foreach (var row in Wmi.Query(Cimv2,
                     "SELECT HypervisorPresent FROM Win32_ComputerSystem",
                     "HypervisorPresent"))
        {
            if (row.GetValueOrDefault("HypervisorPresent") is true)
            {
                return true;
            }
        }
        return firmware;
    }

    /// <summary>
    /// Model names arrive padded and decorated -- "Intel(R) Core(TM) i7-9700K
    /// CPU @ 3.60GHz" with runs of spaces in it. The clock is stated on its own
    /// line, so the trailing "@ 3.60GHz" is dropped rather than repeated.
    /// </summary>
    private static string Tidy(string name)
    {
        int at = name.IndexOf(" @ ", StringComparison.Ordinal);
        if (at > 0)
        {
            name = name[..at];
        }
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries
                                                | StringSplitOptions.TrimEntries));
    }

    private static string Text(object? value) => value?.ToString()?.Trim() ?? string.Empty;

    private static long? Number(object? value) => value switch
    {
        null => null,
        long l => l,
        ulong u => (long)u,
        int i => i,
        uint u => u,
        short s => s,
        ushort u => u,
        byte b => b,
        double d => (long)d,
        string s when long.TryParse(s, out long parsed) => parsed,
        _ => null,
    };
}
