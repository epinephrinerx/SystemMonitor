using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// The graphics adapters under /sys/class/drm.  What each driver exposes
/// differs: amdgpu publishes a busy percentage, i915/xe account engine time per
/// DRM client in fdinfo, and nvidia needs nvidia-smi, which is only asked while
/// the card is awake and then serves only the awake NVIDIA cards in one call.
///
/// The fdinfo scan reads the DRM engine counters and nothing else; it exposes
/// no per-process data, so it runs on every tick regardless of the caller's
/// process-list choice.
/// </summary>
public sealed class GpuReader
{
    private const int NvidiaSmiIntervalMs = 2000;
    private const int FdinfoIntervalMs = 2000;

    private static readonly IReadOnlyDictionary<string, NvidiaReading> NoNvidia =
        new Dictionary<string, NvidiaReading>(StringComparer.Ordinal);

    private readonly string _root;
    private readonly ICommandRunner _runner;
    private readonly PciDatabase _pci;
    private readonly Dictionary<string, FdinfoScan> _fdinfo =
        new(StringComparer.Ordinal);

    /// <summary>What a card is, read once and kept: it cannot change at run time.</summary>
    private readonly Dictionary<string, CardStatic> _static =
        new(StringComparer.Ordinal);

    private long? _nvidiaStamp;
    private IReadOnlyDictionary<string, NvidiaReading> _nvidia = NoNvidia;
    private IReadOnlyList<string> _nvidiaRequested = Array.Empty<string>();

    public GpuReader(
        string root = "/",
        ICommandRunner? runner = null,
        PciDatabase? pci = null)
    {
        _root = string.IsNullOrEmpty(root) ? "/" : root;
        _runner = runner ?? new ProcessCommandRunner();
        _pci = pci ?? PciDatabase.Load(_root);
    }

    /// <summary>Every adapter, primary (boot_vga) first.</summary>
    public IReadOnlyList<Gpu> Read(long stamp)
    {
        var cards = new List<CardScan>();

        foreach (string dir in SysFs.Directories(_root, "sys", "class", "drm"))
        {
            string name = Path.GetFileName(dir);
            if (!name.StartsWith("card", StringComparison.Ordinal)
                || !int.TryParse(name[4..], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                // Connectors (card1-eDP-1) and renderD128 are not adapters.
                continue;
            }

            cards.Add(Scan(dir, name, index));
        }

        // One nvidia-smi call answers for the awake NVIDIA cards in this tick.
        IReadOnlyDictionary<string, NvidiaReading> nvidia = NvidiaFigures(cards, stamp);

        var adapters = new List<(Gpu Gpu, int Index, bool Primary)>(cards.Count);
        foreach (CardScan card in cards)
        {
            adapters.Add(Build(card, nvidia, stamp));
        }

        return adapters
            .OrderByDescending(card => card.Primary)
            .ThenBy(card => card.Index)
            .Select(card => card.Gpu)
            .ToList();
    }

    /// <summary>What sysfs says about one card, before any vendor counter is read.</summary>
    private CardScan Scan(string dir, string name, int index)
    {
        string uevent = SysFs.ReadTrimmed(dir, "device", "uevent") ?? string.Empty;
        (int Vendor, int Device) ids = PciIds(uevent);

        string driver = SysFs.LinkTargetName(Path.Combine(dir, "driver"));
        if (driver.Length == 0)
        {
            driver = UeventValue(uevent, "DRIVER");
        }

        string fallback = ids.Vendor != 0 ? $"{ids.Vendor:X4}:{ids.Device:X4}" : name;
        string displayName = ShortName(_pci, ids.Vendor, ids.Device, fallback);

        return new CardScan(
            dir,
            name,
            index,
            uevent,
            driver,
            displayName,
            SysFs.ReadInt(dir, "device", "boot_vga") == 1,
            ids.Vendor);
    }

    /// <summary>
    /// The card's fixed properties: maker, slot, device name and maximum link.
    /// Read once per card; the values cannot change while the machine runs.
    /// </summary>
    private CardStatic Statics(CardScan card)
    {
        if (_static.TryGetValue(card.Dir, out CardStatic? known))
        {
            return known;
        }

        string manufacturer = _pci.VendorName(card.Vendor);
        if (manufacturer.Length == 0)
        {
            // pci.ids is optional; the three vendors every desktop has are
            // still known from their IDs alone.
            manufacturer = card.Vendor switch
            {
                0x8086 => "Intel Corporation",
                0x10DE => "NVIDIA Corporation",
                0x1002 => "Advanced Micro Devices, Inc. [AMD/ATI]",
                _ => string.Empty,
            };
        }

        var statics = new CardStatic(
            manufacturer,
            PciSlot(UeventValue(card.Uevent, "PCI_SLOT_NAME")),
            // The basename of the resolved device link is the kernel's own name
            // for the card: a PCI address, or a platform device such as
            // fde60000.gpu that has no PCI slot at all.
            SysFs.LinkTargetName(Path.Combine(card.Dir, "device")),
            LinkText(
                SysFs.ReadTrimmed(card.Dir, "device", "max_link_speed"),
                SysFs.ReadTrimmed(card.Dir, "device", "max_link_width")));
        _static[card.Dir] = statics;
        return statics;
    }

    private (Gpu Gpu, int Index, bool Primary) Build(
        CardScan card, IReadOnlyDictionary<string, NvidiaReading> nvidia, long stamp)
    {
        IReadOnlyList<GpuEngine> engines = Array.Empty<GpuEngine>();
        int usage = 0;
        double dedicatedGb = 0;
        double dedicatedTotalGb = 0;
        int? temp = Hwmon.DeviceTemperature(Path.Combine(card.Dir, "device"));

        CardStatic statics = Statics(card);
        int? coreClock = null;
        int? memoryClock = null;
        double? powerW = null;
        double? powerCapW = null;
        int? encoder = null;
        int? decoder = null;
        string linkMax = statics.MaxLink;
        string linkCurrent = LinkText(
            SysFs.ReadTrimmed(card.Dir, "device", "current_link_speed"),
            SysFs.ReadTrimmed(card.Dir, "device", "current_link_width"));

        if (IsDriver(card, "amdgpu"))
        {
            usage = Math.Clamp(
                SysFs.ReadInt(card.Dir, "device", "gpu_busy_percent") ?? 0, 0, 100);

            dedicatedGb = Gigabytes(SysFs.ReadLong(card.Dir, "device", "mem_info_vram_used"));
            dedicatedTotalGb = Gigabytes(SysFs.ReadLong(card.Dir, "device", "mem_info_vram_total"));

            coreClock = DpmClock(SysFs.ReadText(card.Dir, "device", "pp_dpm_sclk"));
            memoryClock = DpmClock(SysFs.ReadText(card.Dir, "device", "pp_dpm_mclk"));
            (powerW, powerCapW) = HwmonPower(Path.Combine(card.Dir, "device"));
        }
        else if (IsDriver(card, "i915") || IsDriver(card, "xe"))
        {
            (engines, usage) = FdinfoUsage(card.Name, card.Uevent, stamp);
            // i915 publishes the render clock here; xe keeps its own under gt/.
            coreClock = Positive(SysFs.ReadInt(card.Dir, "gt_act_freq_mhz"));
        }
        else if (IsDriver(card, "nvidia") && NvidiaAwake(card.Dir))
        {
            // A card that is asleep keeps Present and 0 %, even when another
            // card's figures happen to be cached for this tick.
            string address = NormalisePci(UeventValue(card.Uevent, "PCI_SLOT_NAME"));
            if (address.Length > 0 && nvidia.TryGetValue(address, out NvidiaReading? reading))
            {
                usage = reading.Usage;
                dedicatedGb = reading.UsedGb;
                dedicatedTotalGb = reading.TotalGb;
                temp ??= reading.Temperature;
                coreClock = reading.CoreClockMhz;
                memoryClock = reading.MemoryClockMhz;
                powerW = reading.PowerW;
                powerCapW = reading.PowerCapW;
                encoder = reading.EncoderUsage;
                decoder = reading.DecoderUsage;
                if (reading.LinkCurrent.Length > 0)
                {
                    linkCurrent = reading.LinkCurrent;
                }
                if (linkMax.Length == 0)
                {
                    linkMax = reading.LinkMax;
                }
            }
        }

        return (new Gpu
        {
            Present = true,
            Name = card.DisplayName,
            Driver = card.Driver,
            Manufacturer = statics.Manufacturer,
            PciSlot = statics.Slot,
            DeviceId = statics.DeviceId,
            Usage = usage,
            Engines = engines,
            DedicatedGb = dedicatedGb,
            DedicatedTotalGb = dedicatedTotalGb,
            Temp = temp,
            CoreClockMhz = coreClock,
            MemoryClockMhz = memoryClock,
            PowerW = powerW,
            PowerCapW = powerCapW,
            LinkCurrent = linkCurrent,
            LinkMax = linkMax,
            EncoderUsage = encoder,
            DecoderUsage = decoder,
        }, card.Index, card.Primary);
    }

    private (IReadOnlyList<GpuEngine> Engines, int Usage) FdinfoUsage(
        string card, string uevent, long stamp)
    {
        string pci = UeventValue(uevent, "PCI_SLOT_NAME");
        if (pci.Length == 0)
        {
            return (Array.Empty<GpuEngine>(), 0);
        }

        // The scan walks every process, so it runs at most once every two
        // seconds.  A call inside that window replays the figures the last real
        // scan computed; it must not become a baseline itself, because its
        // counters were not read and the elapsed time would then be too short.
        if (_fdinfo.TryGetValue(card, out FdinfoScan? previous)
            && stamp >= previous.Stamp
            && stamp - previous.Stamp < FdinfoIntervalMs)
        {
            return (previous.Engines, previous.Usage);
        }

        Dictionary<string, long> totals = ReadEngineTotals(pci);
        var engines = new List<GpuEngine>();
        int busiest = 0;

        if (previous is not null && stamp > previous.Stamp)
        {
            // Between the two most recent real scans, over the time that
            // actually passed between them: a scan skipped by the cache does
            // not shorten the interval.
            double elapsedMs = stamp - previous.Stamp;
            foreach (string engine in totals.Keys.Union(previous.Totals.Keys).OrderBy(e => e, StringComparer.Ordinal))
            {
                long before = previous.Totals.GetValueOrDefault(engine);
                long after = totals.GetValueOrDefault(engine);
                double percent = Math.Max(0, after - before) / (elapsedMs * 1_000_000.0) * 100;
                int value = (int)Math.Clamp(Math.Round(percent), 0, 100);
                engines.Add(new GpuEngine { Name = EngineName(engine), Usage = value });
                busiest = Math.Max(busiest, value);
            }
        }
        else
        {
            // The very first scan has no baseline to subtract, so it reports
            // the engines at 0 % and only records their counters.
            foreach (string engine in totals.Keys.OrderBy(e => e, StringComparer.Ordinal))
            {
                engines.Add(new GpuEngine { Name = EngineName(engine), Usage = 0 });
            }
        }

        IReadOnlyList<GpuEngine> ordered = engines
            .OrderByDescending(e => e.Usage)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
        _fdinfo[card] = new FdinfoScan(stamp, totals, ordered, busiest);
        return (ordered, busiest);
    }

    /// <summary>
    /// Sum engine nanoseconds over every DRM client on this card.  One client
    /// has many file descriptors and therefore many fdinfo files with the same
    /// counters, so the client id is counted once.
    /// </summary>
    private Dictionary<string, long> ReadEngineTotals(string pci)
    {
        var clients = new Dictionary<long, IReadOnlyDictionary<string, long>>();

        foreach (string pidDir in SysFs.Directories(_root, "proc"))
        {
            string pid = Path.GetFileName(pidDir);
            if (!int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            foreach (string file in SysFs.Files(Path.Combine(pidDir, "fdinfo")))
            {
                string? text = SysFs.ReadText(file);
                if (text is null || text.IndexOf("drm-pdev", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                DrmClient? client = DrmFdinfo.Parse(text);
                if (client is null
                    || !string.Equals(client.PciDevice, pci, StringComparison.Ordinal))
                {
                    continue;
                }

                clients.TryAdd(client.ClientId, client.Engines);
            }
        }

        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, long> engines in clients.Values)
        {
            foreach (KeyValuePair<string, long> pair in engines)
            {
                totals[pair.Key] = totals.GetValueOrDefault(pair.Key) + pair.Value;
            }
        }
        return totals;
    }

    /// <summary>
    /// The figures for this tick, from one nvidia-smi call shared by the awake
    /// NVIDIA cards.
    ///
    /// A card whose runtime power management is active, or unsupported (no
    /// runtime PM means it is always on), may be queried.  A suspended card, a
    /// missing status file or any other state is left alone: asking nvidia-smi
    /// would wake it.  Only the eligible cards are named on the command line,
    /// so a card in any other state is never touched, and when no card is
    /// eligible no command runs at all.
    /// </summary>
    private IReadOnlyDictionary<string, NvidiaReading> NvidiaFigures(
        IReadOnlyList<CardScan> cards, long stamp)
    {
        var eligible = new List<string>();
        var ids = new List<string>();

        foreach (CardScan card in cards)
        {
            if (!IsDriver(card, "nvidia") || !NvidiaAwake(card.Dir))
            {
                continue;
            }

            string address = UeventValue(card.Uevent, "PCI_SLOT_NAME");
            if (address.Length == 0)
            {
                continue;
            }

            eligible.Add(NormalisePci(address));
            string id = SmiPci(address);
            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            // No eligible card: no command at all, and no figures to report.
            return NoNvidia;
        }

        // The command is slow and wakes the GPU, so it runs at most once every
        // two seconds and the last answer serves the same cards in between.  A
        // card that was not part of that call needs its own run: the cached
        // rows say nothing about it.
        if (_nvidiaStamp is long previous
            && stamp >= previous
            && stamp - previous < NvidiaSmiIntervalMs
            && eligible.All(address => _nvidiaRequested.Contains(address)))
        {
            return _nvidia;
        }

        IReadOnlyDictionary<string, NvidiaReading> rows = QueryNvidia(ids);
        _nvidiaStamp = stamp;
        _nvidia = rows;
        _nvidiaRequested = eligible;
        return rows;
    }

    private static bool NvidiaAwake(string dir)
    {
        string? status = SysFs.ReadTrimmed(dir, "device", "power", "runtime_status");
        return status is not null
            && (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "unsupported", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The named cards in one call, keyed by PCI address so two NVIDIA adapters
    /// keep their own figures.  A row that matches no card is ignored.
    /// </summary>
    private IReadOnlyDictionary<string, NvidiaReading> QueryNvidia(IReadOnlyList<string> ids)
    {
        const string columns =
            "--query-gpu=pci.bus_id,utilization.gpu,memory.total,memory.used,temperature.gpu," +
            "clocks.gr,clocks.mem,power.draw,power.limit,utilization.encoder," +
            "utilization.decoder,pcie.link.gen.current,pcie.link.gen.max," +
            "pcie.link.width.current,pcie.link.width.max " +
            "--format=csv,noheader,nounits";

        string query = $"--id={string.Join(',', ids)} {columns}";
        string? output = _runner.Run("nvidia-smi", query, 3000);
        var rows = new Dictionary<string, NvidiaReading>(StringComparer.Ordinal);
        if (output is null)
        {
            return rows;
        }

        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length < 5)
            {
                continue;
            }

            string address = NormalisePci(fields[0]);
            if (address.Length == 0)
            {
                continue;
            }

            int? usage = Number(fields[1]);
            double? totalMb = Amount(fields[2]);
            double? usedMb = Amount(fields[3]);
            int? temperature = Number(fields[4]);

            rows[address] = new NvidiaReading(
                temperature is int t && t > 0 ? t : null,
                usedMb is double used && used > 0 ? used / 1024.0 : 0,
                totalMb is double total && total > 0 ? total / 1024.0 : 0,
                usage is int u ? Math.Clamp(u, 0, 100) : 0,
                Positive(Number(Field(fields, 5))),
                Positive(Number(Field(fields, 6))),
                Amount(Field(fields, 7)),
                Amount(Field(fields, 8)),
                Percentage(Number(Field(fields, 9))),
                Percentage(Number(Field(fields, 10))),
                Generation(Number(Field(fields, 11)), Number(Field(fields, 13))),
                Generation(Number(Field(fields, 12)), Number(Field(fields, 14))));
        }

        return rows;
    }

    private static string Field(string[] fields, int index) =>
        index < fields.Length ? fields[index] : string.Empty;

    /// <summary>
    /// A PCI address in one spelling: lower case, and the domain without its
    /// padding, because sysfs writes <c>0000:01:00.0</c> and nvidia-smi
    /// <c>00000000:01:00.0</c> for the same card.
    /// </summary>
    public static string NormalisePci(string address)
    {
        string text = address.Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        string[] parts = text.Split(':');
        if (parts.Length != 3)
        {
            return text;
        }

        if (long.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long domain))
        {
            parts[0] = domain.ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(':', parts);
    }

    /// <summary>
    /// A PCI address in the 8-digit-domain spelling nvidia-smi prints and
    /// accepts (<c>00000000:01:00.0</c>), from sysfs' shorter
    /// <c>0000:01:00.0</c>.  Any other text is passed through unchanged.
    /// </summary>
    private static string SmiPci(string address)
    {
        string text = address.Trim().ToLowerInvariant();
        string[] parts = text.Split(':');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[0].Length > 8)
        {
            return text;
        }

        return parts[0].PadLeft(8, '0') + ":" + parts[1] + ":" + parts[2];
    }

    /// <summary>
    /// A short, readable card name: the vendor everybody knows ("Intel",
    /// "NVIDIA", "AMD", or the pci.ids vendor otherwise) plus the marketing
    /// name. pci.ids writes that name in the last bracket of the device entry
    /// ("GP108BM [GeForce MX250]"), which is the part worth showing.
    /// </summary>
    public static string ShortName(PciDatabase pci, int vendor, int device, string fallback)
    {
        string vendorName = vendor switch
        {
            0x8086 => "Intel",
            0x10DE => "NVIDIA",
            0x1002 => "AMD",
            _ => pci.VendorName(vendor),
        };
        vendorName = vendorName.Trim();

        string deviceName = pci.DeviceName(vendor, device);
        string text = BracketText(deviceName);
        if (text.Length == 0)
        {
            text = deviceName.Trim();
        }
        if (text.Length == 0)
        {
            text = fallback.Trim();
        }

        if (vendorName.Length == 0)
        {
            return text;
        }
        return text.Length == 0 ? vendorName : vendorName + " " + text;
    }

    /// <summary>The text inside the last <c>[...]</c>, or empty.</summary>
    private static string BracketText(string name)
    {
        int close = name.LastIndexOf(']');
        if (close < 0)
        {
            return string.Empty;
        }
        int open = name.LastIndexOf('[', close);
        return open < 0 ? string.Empty : name[(open + 1)..close].Trim();
    }

    private static bool IsDriver(CardScan card, string driver) =>
        string.Equals(card.Driver, driver, StringComparison.OrdinalIgnoreCase);

    /// <summary>"render" to "3D", the names the UI already uses.</summary>
    public static string EngineName(string engine) => engine.ToLowerInvariant() switch
    {
        "render" => "3D",
        "copy" => "Copy",
        "video" => "Video decode",
        "video-enhance" => "Video enhance",
        _ => engine,
    };

    private static int? Number(string text)
    {
        if (IsUnavailable(text))
        {
            return null;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? (int)Math.Round(value)
            : null;
    }

    private static double? Amount(string text)
    {
        if (IsUnavailable(text))
        {
            return null;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    /// <summary>nvidia-smi's ways of saying "the driver has no value here".</summary>
    private static bool IsUnavailable(string text) =>
        text.Length == 0
        || text.Equals("[N/A]", StringComparison.OrdinalIgnoreCase)
        || text.Equals("[Not Supported]", StringComparison.OrdinalIgnoreCase)
        || text.Equals("N/A", StringComparison.OrdinalIgnoreCase);

    private static int? Positive(int? value) => value is int number && number > 0 ? number : null;

    private static int? Percentage(int? value) =>
        value is int number ? Math.Clamp(number, 0, 100) : null;

    /// <summary>
    /// The PCIe generation and width as the one-line link text.  A generation
    /// of 0 or a width of 0/255 means the card is not talking over PCIe.
    /// </summary>
    public static string Generation(int? generation, int? width)
    {
        if (generation is not int gen || gen <= 0 || gen > 9
            || width is not int lanes || lanes <= 0 || lanes == 255)
        {
            return string.Empty;
        }
        return $"PCIe {gen}.0 ×{lanes}";
    }

    /// <summary>
    /// sysfs' link text (<c>8.0 GT/s PCIe</c>, <c>4</c>) as
    /// <c>PCIe 3.0 ×4</c>.  GT/s is the per-lane rate, so it names the
    /// generation: 2.5 is 1.0, 5 is 2.0, 8 is 3.0, 16 is 4.0, 32 is 5.0,
    /// 64 is 6.0.  Anything else, or a width of 0/255, is empty.
    /// </summary>
    public static string LinkText(string? speed, string? width)
    {
        double? gts = Amount(speed?.Split(' ')[0] ?? string.Empty);
        int? lanes = Number(width ?? string.Empty);
        if (gts is not double rate || lanes is not int count || count <= 0 || count == 255)
        {
            return string.Empty;
        }

        int generation = rate switch
        {
            2.5 => 1,
            5 => 2,
            8 => 3,
            16 => 4,
            32 => 5,
            64 => 6,
            _ => 0,
        };
        return generation == 0 ? string.Empty : $"PCIe {generation}.0 ×{count}";
    }

    /// <summary>
    /// The current clock from an amdgpu DPM table, whose line for the active
    /// level ends with <c>*</c>: <c>1: 617Mhz *</c>.
    /// </summary>
    public static int? DpmClock(string? table)
    {
        if (table is null)
        {
            return null;
        }

        foreach (string raw in table.Split('\n'))
        {
            if (raw.IndexOf('*') < 0)
            {
                continue;
            }

            string line = raw.Replace("*", string.Empty).Trim();
            int colon = line.IndexOf(':');
            string rest = (colon >= 0 ? line[(colon + 1)..] : line).Trim();
            int unit = rest.IndexOf("Mhz", StringComparison.OrdinalIgnoreCase);
            if (unit >= 0)
            {
                rest = rest[..unit].Trim();
            }

            if (double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double mhz)
                && mhz > 0)
            {
                return (int)Math.Round(mhz);
            }
        }

        return null;
    }

    /// <summary>
    /// An amdgpu card's power draw and limit from its hwmon chip.  Both are
    /// micro-watts, and both may be absent.
    /// </summary>
    public static (double? DrawW, double? CapW) HwmonPower(string devicePath)
    {
        foreach (string chip in HwmonChips(devicePath))
        {
            double? average = MicroWatts(SysFs.ReadLong(chip, "power1_average"));
            double? cap = MicroWatts(SysFs.ReadLong(chip, "power1_cap"));
            if (average is not null || cap is not null)
            {
                return (average, cap);
            }
        }
        return (null, null);
    }

    private static IEnumerable<string> HwmonChips(string devicePath)
    {
        if (!Directory.Exists(devicePath))
        {
            yield break;
        }

        foreach (string child in SysFs.Directories(devicePath))
        {
            if (Path.GetFileName(child).StartsWith("hwmon", StringComparison.Ordinal))
            {
                yield return child;
            }
        }

        foreach (string child in SysFs.Directories(devicePath, "hwmon"))
        {
            if (Path.GetFileName(child).StartsWith("hwmon", StringComparison.Ordinal))
            {
                yield return child;
            }
        }
    }

    private static double? MicroWatts(long? value) =>
        value is long micro && micro > 0 ? micro / 1_000_000.0 : null;

    /// <summary>
    /// A PCI address in the form the entries are listed under, with the domain
    /// padded to four digits: <c>0000:2b:00.0</c>.  The matcher needs the
    /// unpadded form (<see cref="NormalisePci"/>); this one is for display.
    /// </summary>
    public static string PciSlot(string address)
    {
        string text = NormalisePci(address);
        string[] parts = text.Split(':');
        if (parts.Length != 3)
        {
            return text;
        }

        return long.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long domain)
            ? domain.ToString("x4", CultureInfo.InvariantCulture) + ":" + parts[1] + ":" + parts[2]
            : text;
    }

    /// <summary>Bytes to gibibytes, with anything non-positive treated as "unknown".</summary>
    private static double Gigabytes(long? bytes) =>
        bytes is long value && value > 0 ? value / 1073741824.0 : 0;

    private static (int Vendor, int Device) PciIds(string uevent)
    {
        string id = UeventValue(uevent, "PCI_ID");
        string[] parts = id.Split(':');
        if (parts.Length != 2)
        {
            return (0, 0);
        }

        int.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vendor);
        int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int device);
        return (vendor, device);
    }

    private static string UeventValue(string uevent, string key)
    {
        foreach (string line in uevent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = line.IndexOf('=');
            if (equals > 0 && line[..equals] == key)
            {
                return line[(equals + 1)..].Trim();
            }
        }
        return string.Empty;
    }

    private sealed record CardScan(
        string Dir,
        string Name,
        int Index,
        string Uevent,
        string Driver,
        string DisplayName,
        bool Primary,
        int Vendor);

    /// <summary>What a card is: these cannot change while the machine runs.</summary>
    private sealed record CardStatic(
        string Manufacturer, string Slot, string DeviceId, string MaxLink);

    private sealed record NvidiaReading(
        int? Temperature,
        double UsedGb,
        double TotalGb,
        int Usage,
        int? CoreClockMhz,
        int? MemoryClockMhz,
        double? PowerW,
        double? PowerCapW,
        int? EncoderUsage,
        int? DecoderUsage,
        string LinkCurrent,
        string LinkMax);

    private sealed record FdinfoScan(
        long Stamp,
        Dictionary<string, long> Totals,
        IReadOnlyList<GpuEngine> Engines,
        int Usage);
}
