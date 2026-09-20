using System.Runtime.InteropServices;
using System.Text;

namespace SysMonitor.Native;

/// <summary>
/// The Win32 calls SysMonitor needs, ported one-for-one from the Python
/// build's ctypes layer.  No WMI and no subprocesses on the hot path: these
/// are direct syscalls, which is what kept the widget near 0% CPU.
/// </summary>
internal static class Win32
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    private const uint DRIVE_REMOVABLE = 2;
    private const uint DRIVE_FIXED = 3;
    private const uint DRIVE_REMOTE = 4;
    private const uint DRIVE_RAMDISK = 6;

    // CTL_CODE(IOCTL_DISK_BASE 0x07, 0x0008, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IOCTL_DISK_PERFORMANCE = 0x00070020;
    // CTL_CODE(IOCTL_STORAGE_BASE 0x2d, 0x0500 / 0x0420, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;

    // STORAGE_PROPERTY_ID members we use.
    private const uint StorageAdapterProperty = 1;
    private const uint StorageDeviceSeekPenaltyProperty = 7;
    private const uint StorageDeviceTemperatureProperty = 22;
    private const uint PropertyStandardQuery = 0;

    private const int SystemProcessorPerformanceInformation = 8;
    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;

    /// <summary>STORAGE_BUS_TYPE to the label the sidebar shows.</summary>
    private static readonly string[] BusTypes =
    {
        "Unknown", "SCSI", "ATAPI", "ATA", "1394", "SSA", "Fibre", "USB",
        "RAID", "iSCSI", "SAS", "SATA", "SD", "MMC", "Virtual", "Virtual",
        "Spaces", "NVMe", "SCM", "UFS",
    };

    // ------------------------------------------------------------- imports
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetErrorMode(uint mode);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr device, uint code,
        byte[]? inBuffer, uint inSize, byte[] outBuffer, uint outSize,
        out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern uint GetLogicalDrives();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string root);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceExW(string directory,
        out ulong freeToCaller, out ulong total, out ulong totalFree);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(string root,
        StringBuilder name, uint nameSize, out uint serial, out uint maxComponent,
        out uint flags, StringBuilder fileSystem, uint fileSystemSize);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int infoClass,
        IntPtr buffer, uint length, out uint returned);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>Per-logical-processor time counters.  48 bytes on x64.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long Reserved1a;
        public long Reserved1b;
        public uint Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // -------------------------------------------------------------- process
    /// <summary>Stop Windows popping "no disk in drive" boxes when we probe.</summary>
    public static void QuietErrorDialogs() =>
        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);

    /// <summary>Hand freed pages back to Windows.  Costs nothing, keeps RSS honest.</summary>
    public static void TrimWorkingSet()
    {
        try
        {
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
        catch
        {
            // Purely an optimisation; never worth failing a sample over.
        }
    }

    // ------------------------------------------------------------- monitors
    /// <summary>
    /// Usable desktop bounds (taskbar excluded) of the monitor under a point,
    /// in physical pixels, or null if the call fails.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom)? WorkArea(int x, int y)
    {
        try
        {
            IntPtr monitor = MonitorFromPoint(new POINT { X = x, Y = y },
                                              MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(monitor, ref info))
            {
                return null;
            }
            return (info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ cpu
    /// <summary>Per logical core (idle, kernel, user) tick counts, or null.</summary>
    public static (long Idle, long Kernel, long User)[]? CpuTimes()
    {
        int entry = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
        int count = 64;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            uint size = (uint)(entry * count);
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint status = NtQuerySystemInformation(
                    SystemProcessorPerformanceInformation, buffer, size, out uint needed);
                if (status == 0)
                {
                    int cores = (int)(needed / entry);
                    var result = new (long, long, long)[cores];
                    for (int i = 0; i < cores; i++)
                    {
                        var item = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(
                            buffer + i * entry);
                        result[i] = (item.IdleTime, item.KernelTime, item.UserTime);
                    }
                    return result;
                }
                if (status == STATUS_INFO_LENGTH_MISMATCH)
                {
                    count *= 2;
                    continue;
                }
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ ram
    /// <summary>(used, total) physical RAM in bytes.</summary>
    public static (ulong Used, ulong Total)? MemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            return null;
        }
        return (status.ullTotalPhys - status.ullAvailPhys, status.ullTotalPhys);
    }

    // ---------------------------------------------------------------- disks
    /// <summary>Drive letters that currently hold a usable volume.</summary>
    public static List<string> LogicalDrives(bool includeRemovable = true,
                                             bool includeNetwork = false)
    {
        uint mask = GetLogicalDrives();
        var wanted = new HashSet<uint> { DRIVE_FIXED, DRIVE_RAMDISK };
        if (includeRemovable)
        {
            wanted.Add(DRIVE_REMOVABLE);
        }
        if (includeNetwork)
        {
            wanted.Add(DRIVE_REMOTE);
        }

        var letters = new List<string>();
        for (int i = 0; i < 26; i++)
        {
            if ((mask & (1u << i)) == 0)
            {
                continue;
            }
            string letter = ((char)('A' + i)).ToString();
            if (wanted.Contains(GetDriveTypeW(letter + ":\\")))
            {
                letters.Add(letter);
            }
        }
        return letters;
    }

    /// <summary>(used, total) bytes for a drive letter, or null if unreadable.</summary>
    public static (ulong Used, ulong Total)? DiskSpace(string letter)
    {
        if (!GetDiskFreeSpaceExW(letter + ":\\", out _, out ulong total, out ulong totalFree)
            || total == 0)
        {
            return null;
        }
        return (total - totalFree, total);
    }

    public static string VolumeLabel(string letter)
    {
        var name = new StringBuilder(261);
        var fileSystem = new StringBuilder(261);
        if (!GetVolumeInformationW(letter + ":\\", name, 261, out _, out _, out _,
                                   fileSystem, 261))
        {
            return string.Empty;
        }
        return name.ToString();
    }

    private static IntPtr OpenDevice(string path, uint access = 0)
    {
        IntPtr handle = CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        return handle == INVALID_HANDLE_VALUE ? IntPtr.Zero : handle;
    }

    private static byte[]? Ioctl(IntPtr handle, uint code, byte[]? input, int outSize)
    {
        var output = new byte[outSize];
        bool ok = DeviceIoControl(handle, code, input, (uint)(input?.Length ?? 0),
                                  output, (uint)outSize, out uint returned, IntPtr.Zero);
        if (!ok)
        {
            return null;
        }
        if (returned < outSize)
        {
            Array.Resize(ref output, (int)returned);
        }
        return output;
    }

    /// <summary>
    /// Lifetime (read, write) byte counters for a volume, or null.
    /// IOCTL_DISK_PERFORMANCE is maintained by Windows for free, so a call
    /// costs tens of microseconds -- no perf counters, no PowerShell.
    /// </summary>
    public static (long Read, long Written)? VolumeIoCounters(string letter)
    {
        IntPtr handle = OpenDevice(@"\\.\" + letter + ":");
        if (handle == IntPtr.Zero)
        {
            return null;
        }
        byte[]? raw;
        try
        {
            raw = Ioctl(handle, IOCTL_DISK_PERFORMANCE, null, 128);
        }
        finally
        {
            CloseHandle(handle);
        }
        if (raw is null || raw.Length < 16)
        {
            return null;
        }
        return (BitConverter.ToInt64(raw, 0), BitConverter.ToInt64(raw, 8));
    }

    public static int? PhysicalDriveNumber(string letter)
    {
        IntPtr handle = OpenDevice(@"\\.\" + letter + ":");
        if (handle == IntPtr.Zero)
        {
            return null;
        }
        byte[]? raw;
        try
        {
            raw = Ioctl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 32);
        }
        finally
        {
            CloseHandle(handle);
        }
        if (raw is null || raw.Length < 8)
        {
            return null;
        }
        int number = BitConverter.ToInt32(raw, 4);
        return number < 0 ? null : number;
    }

    private static byte[]? StorageQuery(IntPtr handle, uint propertyId, int outSize)
    {
        // STORAGE_PROPERTY_QUERY { DWORD PropertyId; DWORD QueryType; BYTE Extra[1]; }
        var query = new byte[12];
        BitConverter.GetBytes(propertyId).CopyTo(query, 0);
        BitConverter.GetBytes(PropertyStandardQuery).CopyTo(query, 4);
        return Ioctl(handle, IOCTL_STORAGE_QUERY_PROPERTY, query, outSize);
    }

    /// <summary>
    /// Static facts about a physical drive: media type from the seek-penalty
    /// descriptor ("SSD" or "HDD") and the adapter's bus label.
    /// </summary>
    public static (string Media, string Bus)? DriveHardware(int driveNumber)
    {
        IntPtr handle = OpenDevice(@"\\.\PhysicalDrive" + driveNumber);
        if (handle == IntPtr.Zero)
        {
            return null;
        }
        string? media = null;
        string? bus = null;
        try
        {
            byte[]? raw = StorageQuery(handle, StorageDeviceSeekPenaltyProperty, 32);
            if (raw is not null && raw.Length >= 9)
            {
                media = raw[8] != 0 ? "HDD" : "SSD";
            }
            raw = StorageQuery(handle, StorageAdapterProperty, 128);
            if (raw is not null && raw.Length >= 25)
            {
                int busType = raw[24];
                bus = busType < BusTypes.Length ? BusTypes[busType] : "Unknown";
            }
        }
        finally
        {
            CloseHandle(handle);
        }
        if (media is null && bus is null)
        {
            return null;
        }
        return (media ?? "Disk", bus ?? "Unknown");
    }

    /// <summary>
    /// Drive temperature in Celsius from the storage stack, or null.
    /// Works on NVMe and most modern SATA SSDs (Windows 10 1803+); older or
    /// USB-bridged drives simply do not expose one.
    /// </summary>
    public static int? DriveTemperature(int driveNumber)
    {
        foreach (uint access in new uint[] { 0, GENERIC_READ })
        {
            IntPtr handle = OpenDevice(@"\\.\PhysicalDrive" + driveNumber, access);
            if (handle == IntPtr.Zero)
            {
                continue;
            }
            byte[]? raw;
            try
            {
                raw = StorageQuery(handle, StorageDeviceTemperatureProperty, 256);
            }
            finally
            {
                CloseHandle(handle);
            }

            // The descriptor has an eight-byte Reserved1 array at offset 16.
            // TemperatureInfo starts at 24 and each complete entry is 16 bytes,
            // so the first signed reading lives at 26 -- not 18, which is
            // reserved and reads as zero.
            if (raw is null || raw.Length < 40)
            {
                continue;
            }
            int infoCount = BitConverter.ToUInt16(raw, 12);
            uint size = BitConverter.ToUInt32(raw, 4);
            if (infoCount > 0 && 24 + infoCount * 16 <= Math.Min(size, (uint)raw.Length))
            {
                short temp = BitConverter.ToInt16(raw, 26);
                if (temp > -50 && temp < 150)
                {
                    return temp;
                }
            }
        }
        return null;
    }
}
