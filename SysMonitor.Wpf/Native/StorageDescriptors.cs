namespace SysMonitor.Native;

/// <summary>
/// The byte-level reading of the storage structures, kept apart from the
/// calls that fetch them so the layouts can be tested without a drive.
///
/// Every one of these got a field offset wrong at some point; the temperature
/// descriptor shipped reading a reserved byte for weeks.  Hence the tests.
/// </summary>
internal static class StorageDescriptors
{
    /// <summary>STORAGE_BUS_TYPE to the label the sidebar shows.</summary>
    private static readonly string[] BusTypes =
    {
        "Unknown", "SCSI", "ATAPI", "ATA", "1394", "SSA", "Fibre", "USB",
        "RAID", "iSCSI", "SAS", "SATA", "SD", "MMC", "Virtual", "Virtual",
        "Spaces", "NVMe", "SCM", "UFS",
    };

    /// <summary>
    /// STORAGE_TEMPERATURE_DATA_DESCRIPTOR: Version 0, Size 4, InfoCount 12,
    /// an eight-byte Reserved1 array at 16, then TemperatureInfo from 24.
    /// Each STORAGE_TEMPERATURE_INFO is 16 bytes and puts its signed reading
    /// at +2, so the first one lives at 26 -- byte 18 is reserved and reads
    /// as zero, which is why a drive at 55 C once reported 0.
    /// </summary>
    public static int? Temperature(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 40)
        {
            return null;
        }
        int infoCount = BitConverter.ToUInt16(raw[12..14]);
        uint declared = BitConverter.ToUInt32(raw[4..8]);
        if (infoCount <= 0)
        {
            return null;
        }
        // Trust neither the declared size nor the returned length alone.
        uint usable = Math.Min(declared, (uint)raw.Length);
        if (24 + infoCount * 16 > usable)
        {
            return null;
        }
        short temp = BitConverter.ToInt16(raw[26..28]);
        return temp > -50 && temp < 150 ? temp : null;
    }

    /// <summary>
    /// DEVICE_SEEK_PENALTY_DESCRIPTOR: Version 0, Size 4, IncursSeekPenalty
    /// at 8.  A drive that has to seek is spinning rust.
    /// </summary>
    public static string? MediaType(ReadOnlySpan<byte> raw) =>
        Holds(raw, 9) ? (raw[8] != 0 ? "HDD" : "SSD") : null;

    /// <summary>
    /// STORAGE_ADAPTER_DESCRIPTOR: BusType sits at 24, after the transfer
    /// length, page count, alignment mask and four one-byte flags.
    /// </summary>
    public static string? BusType(ReadOnlySpan<byte> raw)
    {
        if (!Holds(raw, 25))
        {
            return null;
        }
        int bus = raw[24];
        return bus < BusTypes.Length ? BusTypes[bus] : "Unknown";
    }

    /// <summary>
    /// Does the descriptor really carry <paramref name="needed"/> bytes?
    ///
    /// Every one of these structures opens with Version at 0 and Size at 4,
    /// and the device states there how much of itself it filled in. A driver
    /// can return a buffer longer than the data it wrote, so the length that
    /// came back is only half the question; the temperature parser checked
    /// both from the start and these two did not.
    /// </summary>
    private static bool Holds(ReadOnlySpan<byte> raw, int needed)
    {
        if (raw.Length < needed || raw.Length < 8)
        {
            return false;
        }
        uint declared = BitConverter.ToUInt32(raw[4..8]);
        // A device that reports nothing at all is taken at the returned
        // length; one that reports a size has to cover the field we want.
        return declared == 0 || declared >= needed;
    }

    /// <summary>
    /// STORAGE_DEVICE_NUMBER: DeviceType 0, DeviceNumber 4.  Windows reports
    /// -1 for a volume that does not map onto one physical drive.
    /// </summary>
    public static int? DeviceNumber(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 8)
        {
            return null;
        }
        int number = BitConverter.ToInt32(raw[4..8]);
        return number < 0 ? null : number;
    }

    /// <summary>DISK_PERFORMANCE: BytesRead 0, BytesWritten 8.</summary>
    public static (long Read, long Written)? IoCounters(ReadOnlySpan<byte> raw) =>
        raw.Length < 16 ? null
        : (BitConverter.ToInt64(raw[0..8]), BitConverter.ToInt64(raw[8..16]));
}
