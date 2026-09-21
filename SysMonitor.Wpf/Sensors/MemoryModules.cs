using SysMonitor.Model;
using SysMonitor.Native;

namespace SysMonitor.Sensors;

/// <summary>
/// The physical memory modules: slot, size, speed, type, part number.
///
/// **Windows does not report per-module usage, and no API exposes it.** The
/// memory controller interleaves across channels, so "how much of DIMM 2 is
/// in use" is not a quantity the hardware tracks. What can be shown is what
/// is installed and where, which is what this reads.
///
/// Static for the life of the machine, so it is queried once and cached: a
/// DIMM does not appear while the widget is running.
/// </summary>
internal static class MemoryModules
{
    private const string Namespace = @"root\cimv2";
    private const string Query =
        "SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, " +
        "SMBIOSMemoryType, Manufacturer, PartNumber FROM Win32_PhysicalMemory";

    private static List<Module>? _cached;
    private static int _slots;

    /// <summary>
    /// How many slots the board has in total, not how many are filled.
    ///
    /// Win32_PhysicalMemory lists occupied slots only, so it can say "2
    /// modules" but never "2 of 4". The array class is the one that knows how
    /// many devices the board was built with. Zero when it will not say.
    /// </summary>
    public static int Slots
    {
        get
        {
            if (_cached is null)
            {
                Read();
            }
            return _slots;
        }
    }

    public static IReadOnlyList<Module> Read()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var modules = new List<Module>();
        foreach (var row in Wmi.Query(Namespace, Query,
                     "DeviceLocator", "BankLabel", "Capacity", "Speed",
                     "ConfiguredClockSpeed", "SMBIOSMemoryType",
                     "Manufacturer", "PartNumber"))
        {
            double capacity = ToDouble(row.GetValueOrDefault("Capacity")) ?? 0;
            if (capacity <= 0)
            {
                continue;
            }
            // The configured clock is what the module actually runs at; Speed
            // is what it is rated for, and the two differ whenever XMP is off.
            double speed = ToDouble(row.GetValueOrDefault("ConfiguredClockSpeed"))
                        ?? ToDouble(row.GetValueOrDefault("Speed")) ?? 0;

            modules.Add(new Module
            {
                Slot = Text(row.GetValueOrDefault("DeviceLocator")),
                Bank = Text(row.GetValueOrDefault("BankLabel")),
                Gb = capacity / (1024.0 * 1024 * 1024),
                Mhz = (int)Math.Round(speed),
                Kind = MemoryType(ToDouble(row.GetValueOrDefault("SMBIOSMemoryType"))),
                Manufacturer = Text(row.GetValueOrDefault("Manufacturer")),
                PartNumber = Text(row.GetValueOrDefault("PartNumber")),
            });
        }

        foreach (var row in Wmi.Query(Namespace,
                     "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray",
                     "MemoryDevices"))
        {
            _slots += (int)(ToDouble(row.GetValueOrDefault("MemoryDevices")) ?? 0);
        }

        // A board that will not report its array still has at least the slots
        // we can see filled.
        _slots = Math.Max(_slots, modules.Count);

        _cached = modules.OrderBy(m => m.Slot, StringComparer.OrdinalIgnoreCase).ToList();
        return _cached;
    }

    /// <summary>SMBIOS memory types, only the ones a desktop or laptop has.</summary>
    private static string MemoryType(double? code) => code switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 or 35 => "DDR5",
        _ => string.Empty,
    };

    private static string Text(object? value) => value?.ToString()?.Trim() ?? string.Empty;

    private static double? ToDouble(object? value) => value switch
    {
        null => null,
        double d => d,
        int i => i,
        uint u => u,
        long l => l,
        ulong u => u,
        ushort u => u,
        string s when double.TryParse(s, out double parsed) => parsed,
        _ => null,
    };
}
