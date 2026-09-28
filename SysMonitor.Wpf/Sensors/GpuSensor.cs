using System.Globalization;
using SysMonitor.Model;
using SysMonitor.Native;

namespace SysMonitor.Sensors;

/// <summary>
/// What the graphics adapter is doing, from the same counters Task Manager
/// reads.
///
/// The engine counter is published per process per engine, so a busy machine
/// has a thousand instances of it. Summing them all would count a frame drawn
/// by one process and copied by another as two hundred percent; Task Manager
/// groups by engine type -- 3D, copy, video decode, video encode -- sums
/// within a type, and shows the busiest type as "the" GPU figure. This does
/// the same.
///
/// Instance names look like
/// `pid_9152_luid_0x00000000_0x0000D6A1_phys_0_eng_3_engtype_3D`.
/// </summary>
internal sealed class GpuSensor : IDisposable
{
    private const string EnginePath = @"\GPU Engine(*)\Utilization Percentage";
    private const string DedicatedPath = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string SharedPath = @"\GPU Adapter Memory(*)\Shared Usage";

    private Pdh? _engines;
    private Pdh? _dedicated;
    private Pdh? _shared;
    private bool _opened;

    /// <summary>The adapter's name and driver, read once from WMI.</summary>
    private GpuAdapter? _adapter;

    public Gpu Read()
    {
        if (!_opened)
        {
            _opened = true;
            _engines = Pdh.Open(EnginePath);
            _dedicated = Pdh.Open(DedicatedPath);
            _shared = Pdh.Open(SharedPath);
            _adapter = Adapter();
        }
        if (_engines is null)
        {
            return new Gpu();
        }

        var byType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach ((string instance, double value) in _engines.Read())
        {
            if (value <= 0)
            {
                continue;       // most of the thousand instances are idle
            }
            string type = EngineType(instance);
            byType[type] = byType.GetValueOrDefault(type) + value;
        }

        var engines = byType
            .Select(pair => new GpuEngine
            {
                Name = Pretty(pair.Key),
                Usage = (int)Math.Round(Math.Clamp(pair.Value, 0, 100)),
            })
            .Where(e => e.Usage > 0)
            .OrderByDescending(e => e.Usage)
            .ToList();

        return new Gpu
        {
            Present = true,
            Name = _adapter?.Name ?? string.Empty,
            Driver = _adapter?.Driver ?? string.Empty,
            Usage = engines.Count == 0 ? 0 : engines[0].Usage,
            Engines = engines,
            DedicatedGb = Gigabytes(_dedicated),
            SharedGb = Gigabytes(_shared),
        };
    }

    /// <summary>
    /// The engine type out of an instance name. Everything after the last
    /// `engtype_`; an unrecognised shape falls back to the whole name rather
    /// than being dropped, so a future engine kind still shows up.
    /// </summary>
    private static string EngineType(string instance)
    {
        const string marker = "engtype_";
        int at = instance.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? instance : instance[(at + marker.Length)..];
    }

    /// <summary>Counter names are lower case and run together: "videodecode".</summary>
    private static string Pretty(string type) => type.ToLowerInvariant() switch
    {
        "3d" => "3D",
        "copy" => "Copy",
        "videodecode" => "Video decode",
        "videoencode" => "Video encode",
        "videoprocessing" => "Video processing",
        "compute" => "Compute",
        "security" => "Security",
        _ => type,
    };

    /// <summary>
    /// Adapter memory is published per adapter; a machine with two of them
    /// gets both, and the total is what the panel is going to state.
    /// </summary>
    private static double Gigabytes(Pdh? counter)
    {
        if (counter is null)
        {
            return 0;
        }
        double bytes = counter.Read().Sum(v => v.Value);
        return bytes <= 0 ? 0 : bytes / (1024.0 * 1024 * 1024);
    }

    private sealed record GpuAdapter(string Name, string Driver);

    /// <summary>
    /// Which adapter this is. The counters identify adapters by LUID, which is
    /// meaningless to a person, so the name comes from WMI instead.
    ///
    /// AdapterRAM is deliberately not read: it is a 32-bit field, so it wraps
    /// above 4 GB and reports nonsense on any modern card. The dedicated-usage
    /// counter is the honest figure and it is already here.
    /// </summary>
    private static GpuAdapter? Adapter()
    {
        foreach (var row in Wmi.Query(@"root\cimv2",
                     "SELECT Name, DriverVersion FROM Win32_VideoController",
                     "Name", "DriverVersion"))
        {
            string name = row.GetValueOrDefault("Name")?.ToString()?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                continue;
            }
            return new GpuAdapter(
                name,
                row.GetValueOrDefault("DriverVersion")?.ToString()?.Trim() ?? string.Empty);
        }
        return null;
    }

    public void Dispose()
    {
        _engines?.Dispose();
        _dedicated?.Dispose();
        _shared?.Dispose();
        _engines = _dedicated = _shared = null;
    }
}
