using System.Runtime.InteropServices;
using System.Text;

namespace SysMonitor.Native;

/// <summary>What one pass over the disks came to.</summary>
internal enum SmartScanStatus
{
    Ok,
    /// <summary>DiskInfoCore.dll is not next to the exe, or is not the version we speak.</summary>
    Unavailable,
    /// <summary>The library found nothing because the process is not elevated.</summary>
    NeedAdmin,
    Failed,
}

/// <summary>DISK_STATUS as the library reports it.</summary>
internal enum SmartHealth { Unknown = 0, Good = 1, Caution = 2, Bad = 3 }

/// <summary>One disk as DiskInfoCore read it. Null means the disk has no such value.</summary>
internal sealed record SmartReading(
    int Drive,
    string Model,
    int? TempC,
    int? LifePercent,
    SmartHealth Health,
    bool Full);

/// <summary>
/// The C ABI of DiskInfoCore.dll (DiskInfo repo, docs/CORE_ABI.md), which wraps
/// CrystalDiskInfo's CAtaSmart. It needs an elevated process: without one it
/// finds no disk at all and says so (DI_E_ACCESS).
///
/// The scan takes seconds and starts COM on the calling thread, so it belongs on
/// a thread of its own and never on the UI or the sampler's tick.
/// </summary>
internal static class SmartCore
{
    private const string Dll = "DiskInfoCore.dll";
    private const int AbiMajor = 1;
    private const int DiAccess = -2;                 // DI_E_ACCESS
    private const int DiNa = int.MinValue;           // DI_NA

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeDisk
    {
        public uint StructSize;
        public int PhysicalDriveId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Model;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Serial;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Firmware;
        public int InterfaceKind;
        public int Media;
        public int TemperatureC;
        public int Health;
        public int LifePercent;
        public long PowerOnHours;
        public uint Access;
        public uint Source;
    }

    [DllImport(Dll), DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern uint di_abi_version();

    [DllImport(Dll), DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int di_open(out IntPtr handle, uint flags);

    [DllImport(Dll), DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern void di_close(IntPtr handle);

    [DllImport(Dll), DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int di_scan(IntPtr handle);

    [DllImport(Dll), DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int di_disk_count(IntPtr handle);

    [DllImport(Dll), DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int di_disk_info(IntPtr handle, int index, ref NativeDisk disk);

    // Two locks, not one: a scan holds its lock for seconds, and the sampler
    // asks IsAvailable on every temperature pass.
    private static readonly object ProbeGate = new();
    private static readonly object ScanGate = new();
    private static bool? _available;

    /// <summary>
    /// Can we talk to the library? Decided once: a missing file or a different
    /// ABI major does not fix itself while the app runs, and asking again every
    /// pass would log the same failure each time.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            lock (ProbeGate)
            {
                return _available ??= Probe();
            }
        }
    }

    private static bool Probe()
    {
        try
        {
            return (di_abi_version() >> 16) == AbiMajor;
        }
        catch (Exception error) when (error is DllNotFoundException
                                          or EntryPointNotFoundException
                                          or BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Read every disk. Serialised, because the library keeps process-wide state
    /// (CoInitializeSecurity, file-level globals) and is not safe to run twice at once.
    /// </summary>
    public static (SmartScanStatus Status, IReadOnlyList<SmartReading> Disks) Scan()
    {
        if (!IsAvailable)
        {
            return (SmartScanStatus.Unavailable, Array.Empty<SmartReading>());
        }

        lock (ScanGate)
        {
            if (di_open(out IntPtr handle, 0) != 0)
            {
                return (SmartScanStatus.Failed, Array.Empty<SmartReading>());
            }
            try
            {
                int rc = di_scan(handle);
                if (rc == DiAccess)
                {
                    return (SmartScanStatus.NeedAdmin, Array.Empty<SmartReading>());
                }
                if (rc != 0)
                {
                    return (SmartScanStatus.Failed, Array.Empty<SmartReading>());
                }

                int count = di_disk_count(handle);
                var disks = new List<SmartReading>(Math.Max(count, 0));
                for (int i = 0; i < count; i++)
                {
                    var native = new NativeDisk { StructSize = (uint)Marshal.SizeOf<NativeDisk>() };
                    if (di_disk_info(handle, i, ref native) == 0)
                    {
                        disks.Add(ToReading(native));
                    }
                }
                return (SmartScanStatus.Ok, disks);
            }
            finally
            {
                di_close(handle);
            }
        }
    }

    /// <summary>The library's "no value" sentinels become null; everything else passes through.</summary>
    internal static SmartReading ToReading(NativeDisk d) => new(
        d.PhysicalDriveId,
        Utf8(d.Model),
        d.TemperatureC == DiNa ? null : d.TemperatureC,
        d.LifePercent == DiNa ? null : d.LifePercent,
        d.Health is >= 0 and <= 3 ? (SmartHealth)d.Health : SmartHealth.Unknown,
        d.Access == 0);

    internal static string Utf8(byte[]? field)
    {
        if (field is null)
        {
            return string.Empty;
        }
        int end = Array.IndexOf(field, (byte)0);
        return Encoding.UTF8.GetString(field, 0, end < 0 ? field.Length : end);
    }
}
