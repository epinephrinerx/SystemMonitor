using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SysMonitor.Model;

namespace SysMonitor.ViewModels;

/// <summary>A titled group of meters; the expanded view is a list of these.</summary>
public sealed class Section : INotifyPropertyChanged
{
    private int _columns = 1;

    public required string Title { get; init; }
    public ObservableCollection<MeterRow> Rows { get; } = new();

    /// <summary>
    /// How many meters go on one line. A group with several members reads far
    /// better as a two-column grid than as one long column -- sixteen cores in
    /// a single column is most of a screen.
    /// </summary>
    public int Columns
    {
        get => _columns;
        set
        {
            if (_columns == value)
            {
                return;
            }
            _columns = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Columns)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Turns a <see cref="Snapshot"/> into what the window shows.
///
/// Rows are reused in place whenever the shape of the data is unchanged.  The
/// Tk build had to do this by hand because recreating canvas items leaked;
/// here it is simply cheaper than rebuilding a collection twenty times a
/// minute and it keeps the bars from restarting their layout each tick.
/// </summary>
public sealed class WidgetViewModel : INotifyPropertyChanged
{
    private readonly AppConfig _config;
    private Palette.Colours _palette;
    private Lang _lang;

    private string _miniTitle = string.Empty;
    private string _miniValue = string.Empty;
    private string _message = string.Empty;
    private bool _showMessage = true;
    private int _miniColumns = 1;
    private string _miniDetail = string.Empty;

    public WidgetViewModel(AppConfig config)
    {
        _config = config;
        _lang = new Lang(config.Lang);
        _palette = Palette.For(config.Theme);
        ApplyTheme();
    }

    // -------------------------------------------------------------- brushes
    public SolidColorBrush PanelBrush { get; } = new();
    public SolidColorBrush SidebarBrush { get; } = new();
    public SolidColorBrush ControlBrush { get; } = new();
    public SolidColorBrush ChipBrush { get; } = new();
    public SolidColorBrush TextBrush { get; } = new();
    public SolidColorBrush MutedBrush { get; } = new();
    public SolidColorBrush LabelBrush { get; } = new();
    public SolidColorBrush EdgeBrush { get; } = new();
    public SolidColorBrush BarEmptyBrush { get; } = new();

    /// <summary>Behind a graph, so the plot reads as a surface of its own.</summary>
    public SolidColorBrush PlotBrush { get; } = new();

    /// <summary>The square grid inside a graph: present, but never loud.</summary>
    public SolidColorBrush GridBrush { get; } = new();

    // ---------------------------------------------------------------- state
    public ObservableCollection<MeterRow> MiniRows { get; } = new();
    public ObservableCollection<Section> Sections { get; } = new();

    /// <summary>The full view: one tab per device, each with its own graphs.</summary>
    public ObservableCollection<DeviceTab> Tabs { get; } = new();

    public string MiniTitle
    {
        get => _miniTitle;
        private set => Set(ref _miniTitle, value);
    }

    public string MiniValue
    {
        get => _miniValue;
        private set => Set(ref _miniValue, value);
    }

    /// <summary>"Reading hardware..." or "Select something to display".</summary>
    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public bool ShowMessage
    {
        get => _showMessage;
        private set => Set(ref _showMessage, value);
    }

    public string TempLegend => _lang["temp_legend"];

    /// <summary>One line of context under the mini bars: GB used, MB/s, cores.</summary>
    public string MiniDetail
    {
        get => _miniDetail;
        private set => Set(ref _miniDetail, value);
    }

    /// <summary>
    /// Carries only the temperature badge for the mini heading line, so the
    /// badge template is the same one the meter rows use.
    /// </summary>
    public MeterRow MiniHeader { get; } = new();

    /// <summary>How many meters the mini view puts on one line.</summary>
    public int MiniColumns
    {
        get => _miniColumns;
        private set => Set(ref _miniColumns, value);
    }

    /// <summary>Which rotating mini view is on screen.</summary>
    public int ViewIndex { get; set; }

    public int ViewCount => _views.Count;

    private readonly List<(string Kind, int? From, int? To)> _views = new();

    // --------------------------------------------------------------- themes
    public void ApplyTheme()
    {
        _palette = Palette.For(_config.Theme);

        // The Electron build layered rgba() panels over the desktop and kept
        // the text opaque.  Tk could only fade a whole window; WPF composites
        // per-pixel alpha, so the original look comes back exactly.
        byte alpha = (byte)Math.Clamp(_config.Opacity * 255, 40, 255);
        PanelBrush.Color = WithAlpha(_palette.Panel, alpha);
        SidebarBrush.Color = WithAlpha(_palette.Sidebar, alpha);
        ControlBrush.Color = _palette.Control;
        ChipBrush.Color = _palette.Chip;
        TextBrush.Color = _palette.Text;
        MutedBrush.Color = _palette.Muted;
        LabelBrush.Color = _palette.Label;
        EdgeBrush.Color = _palette.Border;
        BarEmptyBrush.Color = _palette.BarEmpty;
        PlotBrush.Color = _palette.Plot;
        GridBrush.Color = _palette.Grid;

        ApplyTempColours(MiniHeader);
        foreach (MeterRow row in AllRows())
        {
            row.Empty = BarEmptyBrush;
            ApplyTempColours(row);
        }
        OnPropertyChanged(nameof(TempLegend));
    }

    public void SetLanguage(string code)
    {
        _lang = new Lang(code);
        OnPropertyChanged(nameof(TempLegend));
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private IEnumerable<MeterRow> AllRows() =>
        MiniRows.Concat(Sections.SelectMany(s => s.Rows));

    private void ApplyTempColours(MeterRow row)
    {
        (Color fore, Color back) = Palette.TempColors(row.Temp, _palette);
        row.TempFore = Palette.Brush(fore);
        row.TempBack = Palette.Brush(back);
        row.Hot = Palette.IsHot(row.Temp);
    }

    // ----------------------------------------------------------- view list
    /// <summary>
    /// Rebuild the rotation order from the display settings.  Mirrors the Tk
    /// build: CPU in blocks of four cores, then memory, then drives.
    /// </summary>
    public void RebuildViews(Snapshot snap)
    {
        _views.Clear();
        int cores = Math.Max(snap.Cores.Count, 1);

        if (_config.ShowCpu)
        {
            if (_config.CpuMode == "separated")
            {
                for (int start = 0; start < cores; start += 4)
                {
                    _views.Add(("cpu", start, Math.Min(start + 4, cores)));
                }
            }
            else
            {
                _views.Add(("cpu", null, null));
            }
        }
        if (_config.ShowRam)
        {
            _views.Add(("ram", null, null));
        }
        if (_config.ShowDisk)
        {
            if (_config.DiskMode == "separated")
            {
                for (int i = 0; i < snap.Disks.Count; i++)
                {
                    _views.Add(("disk", i, null));
                }
            }
            else
            {
                _views.Add(("disk", null, null));
            }
        }
        if (_config.ShowNetwork)
        {
            if (_config.NetworkMode == "separated")
            {
                for (int i = 0; i < snap.Adapters.Count; i++)
                {
                    _views.Add(("net", i, null));
                }
            }
            else if (snap.Adapters.Count > 0)
            {
                _views.Add(("net", null, null));
            }
        }
        if (_views.Count == 0)
        {
            _views.Add(("empty", null, null));
        }
        ViewIndex = Math.Min(ViewIndex, _views.Count - 1);
    }

    // ------------------------------------------------------------ mini view
    /// <summary>
    /// The rotating summary, laid out the way the Tk build had it: a heading
    /// line carrying the title, the temperature badge and the headline figure,
    /// then the bars, then one line of detail.  The rows themselves stay
    /// compact so the badge is never repeated inside them.
    /// </summary>
    public void UpdateMini(Snapshot snap)
    {
        if (!snap.Ready)
        {
            Message = _lang["connecting"];
            ShowMessage = true;
            return;
        }
        if (_views.Count == 0)
        {
            RebuildViews(snap);
        }

        (string kind, int? from, int? to) = _views[Math.Clamp(ViewIndex, 0, _views.Count - 1)];
        if (kind == "empty")
        {
            Message = _lang["no_data"];
            ShowMessage = true;
            return;
        }
        ShowMessage = false;

        switch (kind)
        {
            case "cpu" when from is null:
                Head($"{_lang["cpu"]} ({_lang["total"]})", snap.CpuTotal,
                     $"{snap.Cores.Count} {_lang["cores"]}",
                     snap.CpuTemp, snap.CpuTempEstimated);
                MiniColumns = 1;
                Fill(MiniRows, 1);
                Meter(MiniRows[0], string.Empty, snap.CpuTotal,
                      Palette.LoadColor(snap.CpuTotal), string.Empty,
                      null, false, compact: true);
                break;

            case "cpu":
            {
                int count = Math.Max(0, Math.Min(to!.Value, snap.Cores.Count) - from!.Value);
                Head($"{_lang["cpu"]}  C{from}-{to - 1}", snap.CpuTotal, string.Empty,
                     snap.CpuTemp, snap.CpuTempEstimated);
                // Four cores go side by side, as they did in the Tk build:
                // stacked in one column they do not fit the mini height.
                MiniColumns = count > 2 ? 2 : 1;
                Fill(MiniRows, count);
                for (int i = 0; i < count; i++)
                {
                    Core core = snap.Cores[from.Value + i];
                    Meter(MiniRows[i], "C" + (from.Value + i), core.Usage,
                          Palette.LoadColor(core.Usage), string.Empty,
                          null, false, compact: true);
                }
                break;
            }

            case "ram":
                Head(_lang["memory"], snap.Ram.Usage,
                     $"{snap.Ram.UsedGb:F1} / {snap.Ram.TotalGb:F1} GB {_lang["in_use"]}",
                     snap.Ram.Temp, snap.Ram.Estimated);
                MiniColumns = 1;
                Fill(MiniRows, 1);
                Meter(MiniRows[0], string.Empty, snap.Ram.Usage, Palette.LoadColor(snap.Ram.Usage, Palette.AccentRam),
                      string.Empty, null, false, compact: true);
                break;

            case "disk" when from is null:
            {
                (int usage, int? temp, double read, double write) = DiskTotals(snap);
                Head($"{_lang["disk"]} ({_lang["all_drives"]})", usage,
                     SpeedText(read, write), temp, false);
                MiniColumns = 1;
                Fill(MiniRows, 1);
                Meter(MiniRows[0], string.Empty, usage, Palette.LoadColor(usage, Palette.AccentDisk),
                      string.Empty, null, false, compact: true);
                break;
            }

            case "net" when from is null:
            {
                (double down, double up, double link) = NetTotals(snap);
                Head($"{_lang["network"]} ({_lang["all_adapters"]})",
                     LinkUsage(down, up, link), NetText(down, up), null, false);
                MiniColumns = 1;
                Fill(MiniRows, 1);
                Meter(MiniRows[0], string.Empty, LinkUsage(down, up, link),
                      Palette.LoadColor(LinkUsage(down, up, link), Palette.AccentNet), string.Empty, null, false, compact: true);
                break;
            }

            case "net":
            {
                if (from!.Value >= snap.Adapters.Count)
                {
                    return;
                }
                Adapter adapter = snap.Adapters[from.Value];
                Head(adapter.Name, adapter.Usage,
                     NetText(adapter.DownMb, adapter.UpMb), null, false);
                MiniColumns = 1;
                Fill(MiniRows, 1);
                Meter(MiniRows[0], string.Empty, adapter.Usage, Palette.LoadColor(adapter.Usage, Palette.AccentNet),
                      string.Empty, null, false, compact: true);
                break;
            }

            case "disk":
            {
                if (from!.Value >= snap.Disks.Count)
                {
                    return;
                }
                Disk disk = snap.Disks[from.Value];
                Head($"{_lang["drive"]} {disk.Title}", disk.Usage,
                     SpeedText(disk.ReadMb, disk.WriteMb), disk.Temp, disk.Estimated);
                MiniColumns = 1;
                Fill(MiniRows, 1);
                Meter(MiniRows[0], string.Empty, disk.Usage, Palette.LoadColor(disk.Usage, Palette.AccentDisk),
                      string.Empty, null, false, compact: true);
                break;
            }
        }
    }

    /// <summary>Set the mini heading line and the badge that belongs to it.</summary>
    private void Head(string title, int percent, string detail, int? temp, bool estimated)
    {
        MiniTitle = title.ToUpperInvariant();
        MiniValue = percent + "%";
        MiniDetail = detail;
        MiniHeader.Temp = temp;
        MiniHeader.Estimated = estimated;
        ApplyTempColours(MiniHeader);
    }

    // -------------------------------------------------------- expanded view
    public void UpdateExpanded(Snapshot snap)
    {
        if (!snap.Ready)
        {
            return;
        }

        var wanted = new List<string>();
        if (_config.ShowCpu)
        {
            wanted.Add("cpu");
        }
        if (_config.ShowRam)
        {
            wanted.Add("ram");
        }
        if (_config.ShowDisk)
        {
            wanted.Add("disk");
        }
        if (_config.ShowNetwork && snap.Adapters.Count > 0)
        {
            wanted.Add("net");
        }

        // Section identity is the title, so a display-settings change rebuilds
        // only what actually changed.
        var titles = wanted.Select(TitleFor).ToList();
        if (!Sections.Select(s => s.Title).SequenceEqual(titles))
        {
            Sections.Clear();
            foreach (string title in titles)
            {
                Sections.Add(new Section { Title = title });
            }
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            Section section = Sections[i];
            switch (wanted[i])
            {
                case "cpu":
                    if (_config.CpuMode == "separated")
                    {
                        Fill(section.Rows, snap.Cores.Count);
                        for (int c = 0; c < snap.Cores.Count; c++)
                        {
                            Core core = snap.Cores[c];
                            Meter(section.Rows[c], "C" + c, core.Usage,
                                  Palette.LoadColor(core.Usage), string.Empty,
                                  core.Temp, core.Estimated);
                        }
                    }
                    else
                    {
                        Fill(section.Rows, 1);
                        Meter(section.Rows[0], _lang["cpu"], snap.CpuTotal,
                              Palette.LoadColor(snap.CpuTotal),
                              $"{snap.Cores.Count} {_lang["cores"]}",
                              snap.CpuTemp, snap.CpuTempEstimated);
                    }
                    break;

                case "ram":
                    Fill(section.Rows, 1);
                    // What is fitted joins the reading rather than getting a
                    // section of its own; there is no per-module usage to show
                    // in one, and at the bottom of the list nobody found it.
                    Meter(section.Rows[0], _lang["memory"], snap.Ram.Usage,
                          Palette.LoadColor(snap.Ram.Usage, Palette.AccentRam),
                          $"{snap.Ram.UsedGb:F1} / {snap.Ram.TotalGb:F1} GB  {_lang["in_use"]}"
                          + Suffix(Module.Summarise(snap.Modules)),
                          snap.Ram.Temp, snap.Ram.Estimated);
                    break;

                case "net":
                    if (_config.NetworkMode == "separated")
                    {
                        Fill(section.Rows, snap.Adapters.Count);
                        for (int a = 0; a < snap.Adapters.Count; a++)
                        {
                            Adapter adapter = snap.Adapters[a];
                            string kind = adapter.Wireless ? _lang["wireless"] : _lang["wired"];
                            string link = adapter.SpeedMbps > 0
                                ? $"{adapter.SpeedMbps:F0} Mbps" : _lang["no_link"];
                            Meter(section.Rows[a], adapter.Name, adapter.Usage,
                                  Palette.LoadColor(adapter.Usage, Palette.AccentNet),
                                  $"{kind} · {link} · {NetText(adapter.DownMb, adapter.UpMb)}",
                                  null, false);
                        }
                    }
                    else
                    {
                        (double down, double up, double link) = NetTotals(snap);
                        Fill(section.Rows, 1);
                        Meter(section.Rows[0], _lang["all_adapters"],
                              LinkUsage(down, up, link), Palette.LoadColor(LinkUsage(down, up, link), Palette.AccentNet),
                              NetText(down, up), null, false);
                    }
                    break;

                case "disk":
                    if (_config.DiskMode == "separated")
                    {
                        Fill(section.Rows, snap.Disks.Count);
                        for (int d = 0; d < snap.Disks.Count; d++)
                        {
                            Disk disk = snap.Disks[d];
                            Meter(section.Rows[d], disk.Title, disk.Usage,
                                  Palette.LoadColor(disk.Usage, Palette.AccentDisk),
                                  $"{disk.Media} · {disk.Bus} · {disk.UsedGb:F0}/{disk.TotalGb:F0} GB · "
                                  + SpeedText(disk.ReadMb, disk.WriteMb),
                                  disk.Temp, disk.Estimated);
                        }
                    }
                    else
                    {
                        (int usage, int? temp, double read, double write) = DiskTotals(snap);
                        Fill(section.Rows, 1);
                        Meter(section.Rows[0], _lang["all_drives"], usage,
                              Palette.LoadColor(usage, Palette.AccentDisk), SpeedText(read, write), temp, false);
                    }
                    break;
            }

            // Only the per-core rows are short enough to share a line: a core
            // is "C7" and a bar. Everything else carries a line of detail
            // under the bar -- a drive names its media, bus, capacity and both
            // I/O rates -- and at half width that line is mostly ellipsis.
            section.Columns = ColumnsFor(wanted[i]);
        }
    }

    // -------------------------------------------------------- full view
    /// <summary>
    /// Append this sample to every graph and keep the tabs in step with the
    /// hardware that is present.
    ///
    /// Called on every snapshot whichever view is on screen, so the graphs
    /// already have history behind them the moment the full view opens rather
    /// than starting from an empty box.
    /// </summary>
    public void PushHistory(Snapshot snap)
    {
        if (!snap.Ready)
        {
            return;
        }

        var wanted = new List<PlannedTab>();

        if (_config.ShowCpu)
        {
            var cpu = new PlannedTab("cpu", _lang["cpu"], snap.CpuTotal + "%");
            cpu.Cards.Add(new Planned("cpu", _lang["cpu"], snap.CpuTotal, snap.CpuTotal + "%",
                $"{snap.Cores.Count} {_lang["cores"]}"
                + (snap.CpuTemp is int t
                   ? "  ·  " + Palette.TempText(t, snap.CpuTempEstimated)
                   : string.Empty),
                Palette.AccentCpu, 100, Percent, "100%"));

            if (_config.CpuMode == "separated")
            {
                for (int i = 0; i < snap.Cores.Count; i++)
                {
                    Core core = snap.Cores[i];
                    cpu.Cards.Add(new Planned($"core{i}", "Core " + i, core.Usage,
                        core.Usage + "%", string.Empty, Palette.AccentCpu, 100,
                        Percent, "100%", Small: true));
                }
            }
            cpu.Facts.Add(new DeviceFact { Name = _lang["cores"], Value = snap.Cores.Count.ToString() });
            if (snap.CpuTemp is int temp)
            {
                cpu.Facts.Add(new DeviceFact
                {
                    Name = _lang["temp_label"],
                    Value = Palette.TempText(temp, snap.CpuTempEstimated),
                });
            }
            wanted.Add(cpu);
        }

        if (_config.ShowRam)
        {
            var ram = new PlannedTab("ram", _lang["memory"], snap.Ram.Usage + "%");
            ram.Cards.Add(new Planned("ram", _lang["memory"], snap.Ram.Usage,
                snap.Ram.Usage + "%",
                $"{snap.Ram.UsedGb:F1} / {snap.Ram.TotalGb:F1} GB {_lang["in_use"]}",
                Palette.AccentRam, 100, Percent, "100%"));

            ram.Facts.Add(new DeviceFact
            {
                Name = _lang["total"],
                Value = $"{snap.Ram.TotalGb:F1} GB",
            });
            ram.Facts.Add(new DeviceFact
            {
                Name = _lang["in_use"],
                Value = $"{snap.Ram.UsedGb:F1} GB ({snap.Ram.Usage}%)",
            });
            foreach (Module module in snap.Modules)
            {
                ram.Facts.Add(new DeviceFact { Name = module.Title, Value = module.Detail });
            }
            wanted.Add(ram);
        }

        if (_config.ShowDisk)
        {
            // One tab per logical disk: a drive carries far more worth saying
            // than a core does, and sharing a tab left none of it room.
            foreach (Disk disk in snap.Disks)
            {
                var tab = new PlannedTab("disk" + disk.Letter, disk.Letter + ":",
                                         disk.Usage + "%");

                tab.Cards.Add(new Planned($"disk{disk.Letter}", _lang["in_use"], disk.Usage,
                    disk.Usage + "%",
                    $"{disk.UsedGb:F1} / {disk.TotalGb:F1} GB",
                    Palette.AccentDisk, 100, Percent, "100%"));

                // Throughput has no ceiling to measure against, so the graph
                // scales to its own peak and says what that peak is.
                tab.Cards.Add(new Planned($"diskio{disk.Letter}",
                    _lang["read"] + " / " + _lang["write"],
                    disk.ReadMb + disk.WriteMb,
                    Speed(disk.ReadMb + disk.WriteMb) + " MB/s",
                    SpeedText(disk.ReadMb, disk.WriteMb),
                    Palette.AccentDisk, 0, "MB/s", string.Empty));

                foreach (DeviceFact fact in DriveFacts(disk))
                {
                    tab.Facts.Add(fact);
                }
                wanted.Add(tab);
            }
        }

        if (_config.ShowNetwork)
        {
            foreach (Adapter adapter in snap.Adapters)
            {
                var tab = new PlannedTab("net" + adapter.Id, adapter.Name,
                                         Speed(adapter.DownMb + adapter.UpMb) + " MB/s");

                tab.Cards.Add(new Planned($"net{adapter.Id}", adapter.Name,
                    adapter.DownMb + adapter.UpMb,
                    Speed(adapter.DownMb + adapter.UpMb) + " MB/s",
                    NetText(adapter.DownMb, adapter.UpMb),
                    Palette.AccentNet, 0, "MB/s", string.Empty));

                tab.Facts.Add(new DeviceFact
                {
                    Name = _lang["network"],
                    Value = adapter.Wireless ? _lang["wireless"] : _lang["wired"],
                });
                tab.Facts.Add(new DeviceFact
                {
                    Name = "Link",
                    Value = adapter.SpeedMbps > 0
                        ? $"{adapter.SpeedMbps:F0} Mbps" : _lang["no_link"],
                });
                if (adapter.Description.Length > 0)
                {
                    tab.Facts.Add(new DeviceFact { Name = "Adapter", Value = adapter.Description });
                }
                wanted.Add(tab);
            }
        }

        Reconcile(wanted);
    }

    /// <summary>
    /// Everything worth stating about a drive: where it lives, how the
    /// partition sits on the disk, and what the disk itself is.
    /// </summary>
    private IEnumerable<DeviceFact> DriveFacts(Disk disk)
    {
        yield return new DeviceFact
        {
            Name = _lang["drive"],
            Value = disk.Letter + ":" + (disk.Label.Length > 0 ? $"  ({disk.Label})" : string.Empty),
        };
        yield return new DeviceFact
        {
            Name = _lang["in_use"],
            Value = $"{disk.UsedGb:F1} / {disk.TotalGb:F1} GB  ({disk.Usage}%)",
        };

        DriveDetail? detail = disk.Detail;
        if (detail is null)
        {
            yield break;
        }

        if (detail.FileSystem.Length > 0)
        {
            yield return new DeviceFact { Name = "File system", Value = detail.FileSystem };
        }
        if (detail.DiskNumber is int number)
        {
            yield return new DeviceFact
            {
                Name = _lang["partition"],
                Value = $"#{detail.PartitionNumber} {_lang["of_disk"]} {number}"
                        + (detail.IsBoot ? "  ·  boot" : string.Empty),
            };
            yield return new DeviceFact
            {
                Name = _lang["partition_size"],
                Value = detail.Disk is { Bytes: > 0 }
                    ? $"{detail.PartitionGb:F1} / {detail.Disk.Gb:F1} GB  ({detail.ShareOfDisk:F0}%)"
                    : $"{detail.PartitionGb:F1} GB",
            };
        }

        PhysicalDisk? physical = detail.Disk;
        if (physical is null)
        {
            yield break;
        }

        yield return new DeviceFact { Name = _lang["physical_disk"], Value = physical.Title };
        var hardware = new List<string>();
        if (physical.Media.Length > 0)
        {
            hardware.Add(physical.Media);
        }
        if (physical.Bus.Length > 0)
        {
            hardware.Add(physical.Bus);
        }
        if (physical.Rpm > 0)
        {
            hardware.Add($"{physical.Rpm} rpm");
        }
        if (physical.PartitionStyle.Length > 0)
        {
            hardware.Add(physical.PartitionStyle);
        }
        if (hardware.Count > 0)
        {
            yield return new DeviceFact { Name = "Hardware", Value = string.Join("  ·  ", hardware) };
        }
        if (physical.Serial.Length > 0)
        {
            yield return new DeviceFact { Name = "Serial", Value = physical.Serial };
        }
        if (physical.Firmware.Length > 0)
        {
            yield return new DeviceFact { Name = "Firmware", Value = physical.Firmware };
        }
        if (physical.PartitionCount > 0)
        {
            yield return new DeviceFact
            {
                Name = _lang["partitions"],
                Value = physical.PartitionCount.ToString(),
            };
        }
        if (disk.Temp is int temp)
        {
            yield return new DeviceFact
            {
                Name = _lang["temp_label"],
                Value = Palette.TempText(temp, disk.Estimated),
            };
        }
    }

    /// <summary>What a tab should hold this tick, before it exists.</summary>
    private sealed class PlannedTab
    {
        public PlannedTab(string key, string title, string summary)
        {
            Key = key;
            Title = title;
            Summary = summary;
        }

        public string Key { get; }
        public string Title { get; }
        public string Summary { get; }
        public List<Planned> Cards { get; } = new();
        public List<DeviceFact> Facts { get; } = new();
    }

    /// <summary>What a graph should look like this tick, before it exists.</summary>
    private readonly record struct Planned(string Key, string Title, double Sample,
        string Value, string Detail, Color Accent, double Max, string Unit,
        string Ceiling, bool Small = false);

    private string Percent => _lang["utilisation"];

    /// <summary>
    /// Bring the tabs into line with the plan, matching by key so a graph
    /// keeps its history when the list around it changes -- a drive that comes
    /// back finds its own graph again rather than starting from empty.
    /// </summary>
    private void Reconcile(List<PlannedTab> wanted)
    {
        foreach (DeviceTab stale in Tabs.Where(t => wanted.All(w => w.Key != t.Key)).ToList())
        {
            Tabs.Remove(stale);
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            PlannedTab plan = wanted[i];
            DeviceTab? tab = Tabs.FirstOrDefault(t => t.Key == plan.Key);
            if (tab is null)
            {
                tab = new DeviceTab { Key = plan.Key };
                Tabs.Insert(Math.Min(i, Tabs.Count), tab);
            }
            tab.Title = plan.Title;
            tab.Summary = plan.Summary;

            Fill(tab, plan.Cards);
            FillFacts(tab, plan.Facts);
        }

        // Something has to be selected, and the first tab is the CPU.
        if (Tabs.Count > 0 && !Tabs.Any(t => t.Selected))
        {
            Select(Tabs[0].Key);
        }
    }

    private void Fill(DeviceTab tab, List<Planned> cards)
    {
        foreach (ChartCard stale in tab.Cards.Where(c => cards.All(p => p.Key != c.Key)).ToList())
        {
            tab.Cards.Remove(stale);
        }

        for (int i = 0; i < cards.Count; i++)
        {
            Planned plan = cards[i];
            ChartCard? card = tab.Cards.FirstOrDefault(c => c.Key == plan.Key);
            if (card is null)
            {
                card = new ChartCard
                {
                    Key = plan.Key,
                    Group = tab.Key,
                    Small = plan.Small,
                    Unit = plan.Unit,
                };
                tab.Cards.Insert(Math.Min(i, tab.Cards.Count), card);
            }
            card.Title = plan.Title;
            card.Value = plan.Value;
            card.Detail = plan.Detail;
            card.Maximum = plan.Max;
            card.Accent = Palette.Brush(plan.Accent);
            card.Push(plan.Sample);
            // An unbounded graph says what its own peak is, since the vertical
            // scale moves with the data.
            card.Ceiling = plan.Max > 0 ? plan.Ceiling
                : Speed(card.Series.Max * 1.25) + " MB/s";
        }
    }

    /// <summary>
    /// Facts are rebuilt only when they change. They are strings on a panel
    /// nobody is watching change, and replacing the collection every couple of
    /// seconds would restart the layout for no reason.
    /// </summary>
    private static void FillFacts(DeviceTab tab, List<DeviceFact> facts)
    {
        bool same = tab.Facts.Count == facts.Count;
        for (int i = 0; same && i < facts.Count; i++)
        {
            same = tab.Facts[i].Name == facts[i].Name && tab.Facts[i].Value == facts[i].Value;
        }
        if (same)
        {
            return;
        }
        tab.Facts.Clear();
        foreach (DeviceFact fact in facts)
        {
            tab.Facts.Add(fact);
        }
    }

    /// <summary>Show one tab and put the others away.</summary>
    public void Select(string key)
    {
        foreach (DeviceTab tab in Tabs)
        {
            tab.Selected = tab.Key == key;
        }
        SelectedTab = Tabs.FirstOrDefault(t => t.Selected);
        OnPropertyChanged(nameof(SelectedTab));
    }

    public DeviceTab? SelectedTab { get; private set; }

    /// <summary>How many meters of this kind fit on one line.</summary>
    private int ColumnsFor(string kind) =>
        kind == "cpu" && _config.CpuMode == "separated" ? 2 : 1;

    private string TitleFor(string kind) => kind switch
    {
        "cpu" => _lang["cpu"],
        "ram" => _lang["memory"],
        "net" => _lang["network"],
        _ => _lang["disk"],
    };

    // -------------------------------------------------------------- helpers
    /// <summary>Grow or shrink a row list without discarding the rows that stay.</summary>
    private void Fill(ObservableCollection<MeterRow> rows, int count)
    {
        while (rows.Count > count)
        {
            rows.RemoveAt(rows.Count - 1);
        }
        while (rows.Count < count)
        {
            rows.Add(new MeterRow { Empty = BarEmptyBrush });
        }
    }

    private void Meter(MeterRow row, string title, double percent, Color accent,
                       string detail, int? temp, bool estimated, bool compact = false)
    {
        row.Title = title;
        row.Compact = compact;
        row.Percent = percent;
        row.ValueText = $"{percent:F0}%";
        row.Detail = detail;
        row.Empty = BarEmptyBrush;
        row.Accent = Palette.Brush(accent);
        row.Temp = temp;
        row.Estimated = estimated;
        ApplyTempColours(row);
    }

    private static (double Down, double Up, double Link) NetTotals(Snapshot snap)
    {
        if (snap.Adapters.Count == 0)
        {
            return (0, 0, 0);
        }
        return (snap.Adapters.Sum(a => a.DownMb),
                snap.Adapters.Sum(a => a.UpMb),
                snap.Adapters.Sum(a => a.SpeedMbps));
    }

    /// <summary>Combined throughput against combined link rate, as a percent.</summary>
    private static int LinkUsage(double down, double up, double linkMbps) =>
        linkMbps <= 0 ? 0
        : (int)Math.Clamp(Math.Round((down + up) * 8 / linkMbps * 100), 0, 100);

    private static string Suffix(string text) =>
        text.Length > 0 ? "  ·  " + text : string.Empty;

    private string SpeedText(double read, double write) =>
        $"{_lang["read"]} {Speed(read)} | {_lang["write"]} {Speed(write)} MB/s";

    private string NetText(double down, double up) =>
        $"↓ {Speed(down)} | ↑ {Speed(up)} MB/s";

    private static string Speed(double value) =>
        value >= 100 ? value.ToString("F0") : value.ToString("F1");

    private static (int Usage, int? Temp, double Read, double Write) DiskTotals(Snapshot snap)
    {
        if (snap.Disks.Count == 0)
        {
            return (0, null, 0, 0);
        }
        double used = snap.Disks.Sum(d => d.UsedGb);
        double total = snap.Disks.Sum(d => d.TotalGb);
        int usage = total > 0 ? (int)Math.Round(100.0 * used / total) : 0;
        int? temp = snap.Disks.Where(d => d.Temp is not null)
                              .Select(d => d.Temp)
                              .DefaultIfEmpty(null)
                              .Max();
        return (usage, temp, snap.Disks.Sum(d => d.ReadMb), snap.Disks.Sum(d => d.WriteMb));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(name);
        }
    }
}
