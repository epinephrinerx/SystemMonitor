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
    private string _note = string.Empty;

    public required string Title { get; init; }

    /// <summary>
    /// A figure that belongs to the group rather than to any one row, shown
    /// after the heading. The CPU package temperature lives here: it is one
    /// reading for the whole chip, and putting it on each core row stated it
    /// sixteen times as though sixteen sensors had been read.
    /// </summary>
    public string Note
    {
        get => _note;
        set
        {
            if (_note == value)
            {
                return;
            }
            _note = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Note)));
        }
    }
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
    public ObservableCollection<MeterRow> WidgetRows { get; } = new();
    public ObservableCollection<Section> Sections { get; } = new();

    /// <summary>The full view: one tab per device, each with its own graphs.</summary>
    public ObservableCollection<DeviceTab> Tabs { get; } = new();

    public string WidgetTitle
    {
        get => _miniTitle;
        private set => Set(ref _miniTitle, value);
    }

    public string WidgetValue
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
    public string WidgetDetail
    {
        get => _miniDetail;
        private set => Set(ref _miniDetail, value);
    }

    /// <summary>
    /// Carries only the temperature badge for the mini heading line, so the
    /// badge template is the same one the meter rows use.
    /// </summary>
    public MeterRow WidgetHeader { get; } = new();

    /// <summary>How many meters the mini view puts on one line.</summary>
    public int WidgetColumns
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

        ApplyTempColours(WidgetHeader);
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
        WidgetRows.Concat(Sections.SelectMany(s => s.Rows));

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
    public void UpdateWidget(Snapshot snap)
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
                WidgetColumns = 1;
                Fill(WidgetRows, 1);
                Meter(WidgetRows[0], string.Empty, snap.CpuTotal,
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
                WidgetColumns = count > 2 ? 2 : 1;
                Fill(WidgetRows, count);
                for (int i = 0; i < count; i++)
                {
                    Core core = snap.Cores[from.Value + i];
                    Meter(WidgetRows[i], "C" + (from.Value + i), core.Usage,
                          Palette.LoadColor(core.Usage), string.Empty,
                          null, false, compact: true);
                }
                break;
            }

            case "ram":
                Head(_lang["memory"], snap.Ram.Usage,
                     $"{snap.Ram.UsedGb:F1} / {snap.Ram.TotalGb:F1} GB {_lang["in_use"]}",
                     snap.Ram.Temp, snap.Ram.Estimated);
                WidgetColumns = 1;
                Fill(WidgetRows, 1);
                Meter(WidgetRows[0], string.Empty, snap.Ram.Usage, Palette.LoadColor(snap.Ram.Usage, Palette.AccentRam),
                      string.Empty, null, false, compact: true);
                break;

            case "disk" when from is null:
            {
                (int usage, int? temp, double read, double write) = DiskTotals(snap);
                Head($"{_lang["disk"]} ({_lang["all_drives"]})", usage,
                     SpeedText(read, write), temp, false);
                WidgetColumns = 1;
                Fill(WidgetRows, 1);
                Meter(WidgetRows[0], string.Empty, usage, Palette.LoadColor(usage, Palette.AccentDisk),
                      string.Empty, null, false, compact: true);
                break;
            }

            case "net" when from is null:
            {
                (double down, double up, double link) = NetTotals(snap);
                Head($"{_lang["network"]} ({_lang["all_adapters"]})",
                     LinkUsage(down, up, link), NetText(down, up), null, false);
                WidgetColumns = 1;
                Fill(WidgetRows, 1);
                Meter(WidgetRows[0], string.Empty, LinkUsage(down, up, link),
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
                WidgetColumns = 1;
                Fill(WidgetRows, 1);
                Meter(WidgetRows[0], string.Empty, adapter.Usage, Palette.LoadColor(adapter.Usage, Palette.AccentNet),
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
                WidgetColumns = 1;
                Fill(WidgetRows, 1);
                Meter(WidgetRows[0], string.Empty, disk.Usage, Palette.LoadColor(disk.Usage, Palette.AccentDisk),
                      string.Empty, null, false, compact: true);
                break;
            }
        }
    }

    /// <summary>Set the mini heading line and the badge that belongs to it.</summary>
    private void Head(string title, int percent, string detail, int? temp, bool estimated)
    {
        WidgetTitle = title.ToUpperInvariant();
        WidgetValue = percent + "%";
        WidgetDetail = detail;
        WidgetHeader.Temp = temp;
        WidgetHeader.Estimated = estimated;
        ApplyTempColours(WidgetHeader);
    }

    // -------------------------------------------------------- expanded view
    public void UpdateOverall(Snapshot snap)
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
        // Only when the driver publishes counters: a remote session or a very
        // old adapter has none, and an empty heading helps nobody.
        if (_config.ShowGpu && snap.Gpu.Present)
        {
            wanted.Add("gpu");
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
                    // One chip, one temperature. It goes on the heading so it
                    // is on screen in both modes without claiming to be a
                    // per-core reading in either.
                    section.Note = snap.CpuTemp is int package
                        ? Palette.TempText(package, snap.CpuTempEstimated)
                        : string.Empty;

                    if (_config.CpuMode == "separated")
                    {
                        Fill(section.Rows, snap.Cores.Count);
                        for (int c = 0; c < snap.Cores.Count; c++)
                        {
                            Core core = snap.Cores[c];
                            Meter(section.Rows[c], "C" + c, core.Usage,
                                  Palette.LoadColor(core.Usage), string.Empty,
                                  null, false, showTemp: false);
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

                case "gpu":
                {
                    Gpu gpu = snap.Gpu;
                    section.Note = gpu.Name;

                    // The overall figure, then one row per engine that is
                    // doing anything. An idle engine is left out rather than
                    // drawing a row of zero -- there are seven of them and on
                    // most machines six are always idle.
                    Fill(section.Rows, 1 + gpu.Engines.Count);
                    Meter(section.Rows[0], _lang["gpu"], gpu.Usage,
                          Palette.LoadColor(gpu.Usage), GpuMemory(gpu),
                          null, false, showTemp: false);

                    for (int e = 0; e < gpu.Engines.Count; e++)
                    {
                        GpuEngine engine = gpu.Engines[e];
                        Meter(section.Rows[e + 1], engine.Name, engine.Usage,
                              Palette.LoadColor(engine.Usage), string.Empty,
                              null, false, showTemp: false);
                    }
                    section.Columns = 1;
                    break;
                }

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
            CpuInfo chip = snap.CpuInfo;
            var cpu = new PlannedTab("cpu", _lang["cpu"],
                WithTemp(snap.CpuTotal + "%", snap.CpuTemp, snap.CpuTempEstimated),
                detail: CpuDetail(chip, snap.Cores.Count),
                hardware: chip.Name);
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
            foreach (DeviceFact fact in CpuFacts(chip))
            {
                cpu.Facts.Add(fact);
            }
            wanted.Add(cpu);
        }

        if (_config.ShowRam)
        {
            var ram = new PlannedTab("ram", _lang["memory"], snap.Ram.Usage + "%",
                detail: $"{snap.Ram.UsedGb:F1} GB / {snap.Ram.TotalGb:F1} GB",
                hardware: Slots(snap));
            ram.Cards.Add(new Planned("ram", _lang["memory"], snap.Ram.Usage,
                snap.Ram.Usage + "%",
                $"{snap.Ram.UsedGb:F1} / {snap.Ram.TotalGb:F1} GB {_lang["in_use"]}",
                Palette.AccentRam, 100, Percent, "100%"));

            // The size and what is used are on the rail and on the graph
            // below; what is left to say is which module sits in which slot.
            foreach (Module module in snap.Modules)
            {
                ram.Facts.Add(new DeviceFact { Name = module.Title, Value = module.Detail });
            }
            wanted.Add(ram);
        }

        if (_config.ShowDisk)
        {
            // One tab per physical disk, the way Disk Management and Task
            // Manager both group them. Seven letters were seven tabs; they are
            // three disks, and which letters share a spindle is exactly what a
            // person wants to know when one of them is busy.
            foreach (var group in snap.Disks
                         .Where(d => !d.IsNetwork && d.Detail?.DiskNumber is int)
                         .GroupBy(d => d.Detail!.DiskNumber!.Value)
                         .OrderBy(g => g.Key))
            {
                var drives = group.OrderBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
                                  .ToList();
                PhysicalDisk? physical = drives.Select(d => d.Detail?.Disk)
                                               .FirstOrDefault(p => p is not null);

                var tab = new PlannedTab("disk" + group.Key,
                                         $"Disk {group.Key}",
                                         physical?.Model ?? string.Empty,
                                         detail: DiskLine(physical, drives),
                                         hardware: physical?.Model ?? string.Empty);

                AddDriveCards(tab, drives);
                wanted.Add(tab);
            }

            // Letters with no disk behind them: a cloud filesystem, a mapped
            // share, a subst. They are real to the user and invisible to the
            // storage stack, so they share one heading rather than vanishing.
            var virtualDrives = snap.Disks
                .Where(d => !d.IsNetwork && d.Detail?.DiskNumber is null)
                .OrderBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
                .ToList();
            AddLetterGroup(wanted, "diskvirtual", _lang["virtual_drives"], virtualDrives);

            // Mapped shares get their own heading rather than sharing the
            // virtual one. Windows itself separates them -- This PC calls them
            // Network Locations -- and they behave differently enough to be
            // worth telling apart: a share can simply be gone.
            var networkDrives = snap.Disks
                .Where(d => d.IsNetwork)
                .OrderBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
                .ToList();
            AddLetterGroup(wanted, "disknetwork", _lang["network_drives"], networkDrives);
        }

        if (_config.ShowGpu && snap.Gpu.Present)
        {
            Gpu gpu = snap.Gpu;
            var tab = new PlannedTab("gpu", _lang["gpu"], gpu.Usage + "%",
                detail: GpuMemory(gpu),
                hardware: gpu.Name);

            tab.Cards.Add(new Planned("gpu", _lang["gpu"], gpu.Usage,
                gpu.Usage + "%",
                string.Join("  \u00b7  ", gpu.Engines.Select(e => $"{e.Name} {e.Usage}%")),
                Palette.AccentGpu, 100, Percent, "100%"));

            // One square per engine, the same shape the cores use: they are
            // the parts of the adapter, as cores are the parts of the chip.
            foreach (GpuEngine engine in gpu.Engines)
            {
                tab.Cards.Add(new Planned("gpu" + engine.Name, engine.Name, engine.Usage,
                    engine.Usage + "%", string.Empty, Palette.AccentGpu, 100,
                    Percent, "100%", Small: true));
            }

            if (gpu.Driver.Length > 0)
            {
                tab.Facts.Add(new DeviceFact { Name = "Driver", Value = gpu.Driver });
            }
            if (gpu.DedicatedGb > 0.01)
            {
                tab.Facts.Add(new DeviceFact
                {
                    Name = _lang["dedicated"],
                    Value = $"{gpu.DedicatedGb:F2} GB",
                });
            }
            if (gpu.SharedGb > 0.01)
            {
                tab.Facts.Add(new DeviceFact
                {
                    Name = _lang["shared"],
                    Value = $"{gpu.SharedGb:F2} GB",
                });
            }
            wanted.Add(tab);
        }

        if (_config.ShowNetwork)
        {
            foreach (Adapter adapter in snap.Adapters)
            {
                var tab = new PlannedTab("net" + adapter.Id, adapter.Name,
                                         Speed(adapter.DownMb + adapter.UpMb) + " MB/s",
                                         detail: adapter.Wireless
                                             ? _lang["wireless"] : _lang["wired"],
                                         hardware: adapter.Description);

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
    /// What the CPU is, as the rail says it in one line: the shape of the
    /// chip, because the model name is too long to fit beside a percentage.
    /// </summary>
    private static string CpuDetail(CpuInfo chip, int sampled)
    {
        int cores = chip.Cores > 0 ? chip.Cores : sampled;
        int logical = chip.Logical > 0 ? chip.Logical : sampled;

        var parts = new List<string>();
        if (cores > 0)
        {
            parts.Add(logical > cores ? $"{cores}C/{logical}T" : $"{cores}C");
        }
        if (chip.BaseMhz > 0)
        {
            // The clock speaks for itself; a label in front of it only takes
            // room the rail does not have.
            parts.Add($"{chip.BaseMhz / 1000.0:F2} GHz");
        }
        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// A reading with its temperature after it, where there is one. Drives
    /// without a sensor and machines without a thermal zone just get the
    /// reading, rather than a row of "n/a" that says nothing.
    /// </summary>
    private static string WithTemp(string value, int? temp, bool estimated) =>
        temp is int c ? $"{value}  ·  {Palette.TempText(c, estimated)}" : value;

    /// <summary>
    /// What is left to say about the processor once the rail has said the
    /// rest. The clock, the core counts and the temperature are on the rail
    /// and the model heads the panel, so repeating any of them here would
    /// fill the box with things already on screen.
    /// </summary>
    private IEnumerable<DeviceFact> CpuFacts(CpuInfo chip)
    {
        if (chip.Virtualization is bool virtualisation)
        {
            yield return new DeviceFact
            {
                Name = _lang["virtualisation"],
                Value = virtualisation ? _lang["enabled"] : _lang["disabled"],
            };
        }
        if (chip.L2Kb > 0)
        {
            yield return new DeviceFact { Name = "L2", Value = Cache(chip.L2Kb) };
        }
        if (chip.L3Kb > 0)
        {
            yield return new DeviceFact { Name = "L3", Value = Cache(chip.L3Kb) };
        }
    }

    /// <summary>Cache sizes arrive in KB; MB reads better past a megabyte.</summary>
    private static string Cache(int kb) =>
        kb >= 1024 ? $"{kb / 1024.0:0.#} MB" : $"{kb} KB";

    /// <summary>
    /// How many memory slots are in use: "2 จาก 4 สล็อต".
    ///
    /// Not "2 x 16 GB", which is what this used to say and which is simply
    /// wrong on a machine with unmatched modules -- the panel below lists each
    /// module anyway, so the only thing worth saying up here is how much room
    /// is left to add more.
    /// </summary>
    private string Slots(Snapshot snap)
    {
        int filled = snap.Modules.Count;
        if (filled == 0)
        {
            return string.Empty;
        }
        int total = Math.Max(snap.MemorySlots, filled);
        return $"{filled} {_lang["of_slots"]} {total} {_lang["slots"]}";
    }

    /// <summary>
    /// A heading for a set of drive letters that has no physical disk behind
    /// it. Added only when it has members: an empty heading is a row of
    /// nothing.
    /// </summary>
    private void AddLetterGroup(List<PlannedTab> wanted, string key, string title,
                                List<Disk> drives)
    {
        if (drives.Count == 0)
        {
            return;
        }
        var tab = new PlannedTab(key, title,
            string.Join("  \u00b7  ", drives.Select(d => d.Letter + ":")),
            detail: $"{drives.Count} {_lang["drives"]}");

        AddDriveCards(tab, drives);
        wanted.Add(tab);
    }

    /// <summary>
    /// One card per drive on a disk: what it is called, what is on it, and how
    /// hard it is being read and written.
    ///
    /// Space in use is stated rather than graphed -- it moves by a gigabyte a
    /// week, which is a flat line on a three-minute chart. Throughput is the
    /// part that actually moves, and it has no ceiling to measure against, so
    /// the graph scales to its own peak and says what that peak is.
    /// </summary>
    private void AddDriveCards(PlannedTab tab, List<Disk> drives)
    {
        foreach (Disk disk in drives)
        {
            tab.Cards.Add(new Planned($"diskio{disk.Letter}",
                DriveName(disk),
                disk.ReadMb + disk.WriteMb,
                Speed(disk.ReadMb + disk.WriteMb) + " MB/s",
                DriveLine(disk),
                Palette.AccentDisk, 0, "MB/s", string.Empty));
        }
    }

    /// <summary>
    /// The second rail line for a physical disk: what kind it is, how big,
    /// whether it is online, and how much of it is not in a partition.
    ///
    /// The unallocated figure appears only when there is some. A disk that is
    /// fully partitioned -- which is most of them -- says nothing about it
    /// rather than printing a zero.
    /// </summary>
    private string DiskLine(PhysicalDisk? physical, List<Disk> drives)
    {
        var parts = new List<string>();

        string media = physical?.Media.Length > 0
            ? physical.Media
            : drives.Select(d => d.Media).FirstOrDefault(m => m.Length > 0) ?? string.Empty;
        string bus = physical?.Bus.Length > 0
            ? physical.Bus
            : drives.Select(d => d.Bus).FirstOrDefault(b => b.Length > 0 && b != "Unknown")
              ?? string.Empty;
        string kind = bus.Length == 0 || bus == "Unknown"
            ? media
            : media.Length > 0 ? $"{media} ({bus})" : bus;
        if (kind.Length > 0)
        {
            parts.Add(kind);
        }

        if (physical is { Gb: > 0 })
        {
            parts.Add($"{physical.Gb:F1} GB");
        }
        parts.Add(physical is null || physical.Online ? _lang["online"] : _lang["offline"]);

        if (physical is { UnallocatedGb: > 0.05 })
        {
            parts.Add($"{physical.UnallocatedGb:F1} GB {_lang["unallocated"]}");
        }
        return string.Join("  \u00b7  ", parts);
    }

    /// <summary>
    /// What a single drive says under its name on a disk's panel: filesystem,
    /// how full it is, and the read and write rates behind the graph.
    /// </summary>
    private string DriveLine(Disk disk)
    {
        var parts = new List<string>();
        if (disk.Detail is { FileSystem.Length: > 0 })
        {
            parts.Add(disk.Detail.FileSystem);
        }
        parts.Add($"{disk.UsedGb:F1} GB / {disk.TotalGb:F1} GB  ({disk.Usage}%)");
        parts.Add(SpeedText(disk.ReadMb, disk.WriteMb));
        if (disk.Temp is int temp)
        {
            parts.Add(Palette.TempText(temp, disk.Estimated));
        }
        return string.Join("  \u00b7  ", parts);
    }

    /// <summary>
    /// What the adapter is using, in the one line the rail has for it.
    ///
    /// An integrated adapter has no dedicated memory of its own and borrows
    /// system memory instead, so whichever figure exists is the one shown and
    /// a machine with both gets both.
    /// </summary>
    private string GpuMemory(Gpu gpu)
    {
        var parts = new List<string>();
        if (gpu.DedicatedGb > 0.01)
        {
            parts.Add($"{gpu.DedicatedGb:F1} GB {_lang["dedicated"]}");
        }
        if (gpu.SharedGb > 0.01)
        {
            parts.Add($"{gpu.SharedGb:F1} GB {_lang["shared"]}");
        }
        return string.Join("  \u00b7  ", parts);
    }

    /// <summary>The drive as the rail names it: "C: (Windows)".</summary>
    private static string DriveName(Disk disk) =>
        disk.Letter + ":" + (disk.Label.Length > 0 ? $" ({disk.Label})" : string.Empty);

    /// <summary>
    /// What kind of drive this is, with its temperature where there is a
    /// sensor: "NTFS · SSD (NVMe) · 41°C".
    ///
    /// All of it on one rail line, because the panel beside it no longer has a
    /// facts box to put any of this in.
    /// </summary>
    private static string DriveType(Disk disk)
    {
        DriveDetail? detail = disk.Detail;
        PhysicalDisk? physical = detail?.Disk;

        var parts = new List<string>();
        if (detail is { FileSystem.Length: > 0 })
        {
            parts.Add(detail.FileSystem);
        }

        string media = physical?.Media.Length > 0 ? physical.Media : disk.Media;
        string bus = physical?.Bus.Length > 0 ? physical.Bus : disk.Bus;
        string kind = bus.Length == 0 || bus == "Unknown"
            ? media
            : media.Length > 0 ? $"{media} ({bus})" : bus;
        if (kind.Length > 0)
        {
            parts.Add(kind);
        }

        if (disk.Temp is int temp)
        {
            parts.Add(Palette.TempText(temp, disk.Estimated));
        }
        return string.Join("  ·  ", parts);
    }

    /// <summary>What a tab should hold this tick, before it exists.</summary>
    private sealed class PlannedTab
    {
        public PlannedTab(string key, string title, string summary,
                          string detail = "", string hardware = "")
        {
            Key = key;
            Title = title;
            Summary = summary;
            Detail = detail;
            Hardware = hardware;
        }

        public string Key { get; }
        public string Title { get; }
        public string Summary { get; }

        /// <summary>What the device is, shown on the rail under its name.</summary>
        public string Detail { get; }

        /// <summary>The make and model, shown beside the name on the panel.</summary>
        public string Hardware { get; }
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
            tab.Detail = plan.Detail;
            tab.Hardware = plan.Hardware;
            tab.Summary = plan.Summary;

            Fill(tab.Cards, tab.Key, plan.Cards.Where(c => !c.Small).ToList());
            Fill(tab.Cores, tab.Key, plan.Cards.Where(c => c.Small).ToList());
            FillFacts(tab, plan.Facts);
        }

        // Something has to be selected. The tab the user was last on wins if
        // it is still here: the view is often opened before the first sample
        // has landed, so there were no tabs to restore the choice onto at the
        // moment the view asked for it, and this is where they arrive.
        if (Tabs.Count > 0 && !Tabs.Any(t => t.Selected))
        {
            string remembered = _config.FullTab;
            Select(Tabs.Any(t => t.Key == remembered) ? remembered : Tabs[0].Key);
        }
    }

    private void Fill(ObservableCollection<ChartCard> target, string group,
                      List<Planned> cards)
    {
        foreach (ChartCard stale in target.Where(c => cards.All(p => p.Key != c.Key)).ToList())
        {
            target.Remove(stale);
        }

        for (int i = 0; i < cards.Count; i++)
        {
            Planned plan = cards[i];
            ChartCard? card = target.FirstOrDefault(c => c.Key == plan.Key);
            if (card is null)
            {
                card = new ChartCard
                {
                    Key = plan.Key,
                    Group = group,
                    Small = plan.Small,
                    Unit = plan.Unit,
                };
                target.Insert(Math.Min(i, target.Count), card);
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
        "gpu" => _lang["gpu"],
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
                       string detail, int? temp, bool estimated, bool compact = false,
                       bool showTemp = true)
    {
        row.Title = title;
        row.Compact = compact;
        row.ShowTemp = showTemp;
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
