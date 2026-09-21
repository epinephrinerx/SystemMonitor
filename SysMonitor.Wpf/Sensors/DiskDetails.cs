using SysMonitor.Model;
using SysMonitor.Native;

namespace SysMonitor.Sensors;

/// <summary>
/// The static facts about drives: which physical disk a letter lives on, how
/// the partition sits on it, and what the hardware actually is.
///
/// Through the storage WMI namespace rather than more IOCTLs. The live figures
/// -- usage, throughput, temperature -- stay on the direct syscalls that keep
/// the widget cheap; this is queried once and cached, because a disk does not
/// change model number while the widget is running.
/// </summary>
internal static class DiskDetails
{
    private const string Storage = @"root\Microsoft\Windows\Storage";
    private const string Cimv2 = @"root\cimv2";

    private static IReadOnlyDictionary<string, DriveDetail>? _cached;

    /// <summary>Bus numbers as the storage namespace reports them.</summary>
    private static readonly Dictionary<int, string> BusTypes = new()
    {
        [1] = "SCSI", [2] = "ATAPI", [3] = "ATA", [4] = "1394", [5] = "SSA",
        [6] = "Fibre", [7] = "USB", [8] = "RAID", [9] = "iSCSI", [10] = "SAS",
        [11] = "SATA", [12] = "SD", [13] = "MMC", [15] = "File-backed virtual",
        [16] = "Storage spaces", [17] = "NVMe", [18] = "SCM", [19] = "UFS",
    };

    /// <summary>MSFT_PhysicalDisk media types.</summary>
    private static readonly Dictionary<int, string> MediaTypes = new()
    {
        [3] = "HDD", [4] = "SSD", [5] = "SCM",
    };

    /// <summary>
    /// Everything known about each drive letter. Empty entries are normal:
    /// a network or virtual drive has no physical disk behind it.
    /// </summary>
    public static IReadOnlyDictionary<string, DriveDetail> Read()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var disks = PhysicalDisks();
        var details = new Dictionary<string, DriveDetail>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Wmi.Query(Storage,
                     "SELECT DiskNumber, PartitionNumber, DriveLetter, Size, Offset, IsBoot " +
                     "FROM MSFT_Partition",
                     "DiskNumber", "PartitionNumber", "DriveLetter", "Size", "Offset", "IsBoot"))
        {
            string letter = Letter(row.GetValueOrDefault("DriveLetter"));
            if (letter.Length == 0)
            {
                continue;       // a partition with no letter is not ours to show
            }

            int number = (int)(Number(row.GetValueOrDefault("DiskNumber")) ?? -1);
            disks.TryGetValue(number, out PhysicalDisk? disk);

            details[letter] = new DriveDetail
            {
                Letter = letter,
                DiskNumber = number < 0 ? null : number,
                PartitionNumber = (int)(Number(row.GetValueOrDefault("PartitionNumber")) ?? 0),
                PartitionBytes = Number(row.GetValueOrDefault("Size")) ?? 0,
                PartitionOffset = Number(row.GetValueOrDefault("Offset")) ?? 0,
                IsBoot = row.GetValueOrDefault("IsBoot") as bool? ?? false,
                Disk = disk,
            };
        }

        foreach (var row in Wmi.Query(Storage,
                     "SELECT DriveLetter, FileSystem, FileSystemLabel, Size, SizeRemaining " +
                     "FROM MSFT_Volume",
                     "DriveLetter", "FileSystem", "FileSystemLabel", "Size", "SizeRemaining"))
        {
            string letter = Letter(row.GetValueOrDefault("DriveLetter"));
            if (letter.Length == 0 || !details.TryGetValue(letter, out DriveDetail? detail))
            {
                continue;
            }
            detail.FileSystem = Text(row.GetValueOrDefault("FileSystem"));
            detail.Label = Text(row.GetValueOrDefault("FileSystemLabel"));
        }

        _cached = details;
        return _cached;
    }

    /// <summary>
    /// The physical disks, by number. Two classes are needed: Win32_DiskDrive
    /// carries the serial and firmware, MSFT_PhysicalDisk the media type and
    /// spindle speed, and neither has all of it.
    /// </summary>
    private static Dictionary<int, PhysicalDisk> PhysicalDisks()
    {
        var disks = new Dictionary<int, PhysicalDisk>();

        foreach (var row in Wmi.Query(Storage,
                     "SELECT Number, FriendlyName, BusType, Size, PartitionStyle, HealthStatus " +
                     "FROM MSFT_Disk",
                     "Number", "FriendlyName", "BusType", "Size", "PartitionStyle", "HealthStatus"))
        {
            long? number = Number(row.GetValueOrDefault("Number"));
            if (number is null)
            {
                continue;
            }
            int bus = (int)(Number(row.GetValueOrDefault("BusType")) ?? 0);
            disks[(int)number] = new PhysicalDisk
            {
                Number = (int)number,
                Model = Text(row.GetValueOrDefault("FriendlyName")),
                Bus = BusTypes.GetValueOrDefault(bus, "Unknown"),
                Bytes = Number(row.GetValueOrDefault("Size")) ?? 0,
                PartitionStyle = (Number(row.GetValueOrDefault("PartitionStyle")) ?? 0) switch
                {
                    1 => "MBR",
                    2 => "GPT",
                    _ => string.Empty,
                },
                Healthy = (Number(row.GetValueOrDefault("HealthStatus")) ?? 0) == 0,
            };
        }

        foreach (var row in Wmi.Query(Storage,
                     "SELECT DeviceId, MediaType, SpindleSpeed FROM MSFT_PhysicalDisk",
                     "DeviceId", "MediaType", "SpindleSpeed"))
        {
            long? id = Number(row.GetValueOrDefault("DeviceId"));
            if (id is null || !disks.TryGetValue((int)id, out PhysicalDisk? disk))
            {
                continue;
            }
            disk.Media = MediaTypes.GetValueOrDefault(
                (int)(Number(row.GetValueOrDefault("MediaType")) ?? 0), string.Empty);

            // An SSD reports 0, and a disk that will not say reports uint.MaxValue.
            long spindle = Number(row.GetValueOrDefault("SpindleSpeed")) ?? 0;
            disk.Rpm = spindle > 0 && spindle < 100_000 ? (int)spindle : 0;
        }

        foreach (var row in Wmi.Query(Cimv2,
                     "SELECT Index, SerialNumber, FirmwareRevision, InterfaceType, Partitions " +
                     "FROM Win32_DiskDrive",
                     "Index", "SerialNumber", "FirmwareRevision", "InterfaceType", "Partitions"))
        {
            long? index = Number(row.GetValueOrDefault("Index"));
            if (index is null || !disks.TryGetValue((int)index, out PhysicalDisk? disk))
            {
                continue;
            }
            disk.Serial = Text(row.GetValueOrDefault("SerialNumber"));
            disk.Firmware = Text(row.GetValueOrDefault("FirmwareRevision"));
            disk.Interface = Text(row.GetValueOrDefault("InterfaceType"));
            disk.PartitionCount = (int)(Number(row.GetValueOrDefault("Partitions")) ?? 0);
        }

        return disks;
    }

    /// <summary>MSFT_Volume hands the letter back as a char, sometimes as 0.</summary>
    private static string Letter(object? value) => value switch
    {
        null => string.Empty,
        char c when char.IsLetter(c) => c.ToString().ToUpperInvariant(),
        string s when s.Length > 0 && char.IsLetter(s[0]) => s[..1].ToUpperInvariant(),
        ushort u when u > 0 && char.IsLetter((char)u) => ((char)u).ToString().ToUpperInvariant(),
        _ => string.Empty,
    };

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
