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

    // ---------------------------------------------------------------- state
    public ObservableCollection<MeterRow> MiniRows { get; } = new();
    public ObservableCollection<Section> Sections { get; } = new();

    /// <summary>The full-screen view: one graph per device.</summary>
    public ObservableCollection<ChartCard> Cards { get; } = new();

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
        if (_config.ShowRam && snap.Modules.Count > 0)
        {
            wanted.Add("modules");
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
                    Meter(section.Rows[0], _lang["memory"], snap.Ram.Usage,
                          Palette.LoadColor(snap.Ram.Usage, Palette.AccentRam),
                          $"{snap.Ram.UsedGb:F1} / {snap.Ram.TotalGb:F1} GB  {_lang["in_use"]}",
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

                case "modules":
                    // No bar: Windows reports no per-module usage, so a filled
                    // meter here would be inventing a number.
                    Fill(section.Rows, snap.Modules.Count);
                    for (int m = 0; m < snap.Modules.Count; m++)
                    {
                        Module module = snap.Modules[m];
                        MeterRow row = section.Rows[m];
                        row.Title = module.Title;
                        row.Detail = module.Detail;
                        row.ValueText = string.Empty;
                        row.Percent = 0;
                        row.Compact = true;
                        row.InfoOnly = true;
                        row.Temp = null;
                        ApplyTempColours(row);
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

            // One member stays full width; several go two to a line.
            section.Columns = section.Rows.Count > 1 ? 2 : 1;
        }
    }

    // -------------------------------------------------------- full view
    /// <summary>
    /// Append this sample to every graph, adding and removing cards as
    /// hardware appears and disappears.
    ///
    /// Called on every snapshot regardless of which view is on screen, so the
    /// graphs already have history behind them the moment the full view opens
    /// rather than starting from an empty box.
    /// </summary>
    public void PushHistory(Snapshot snap)
    {
        if (!snap.Ready)
        {
            return;
        }

        var wanted = new List<(string Key, string Title, double Sample, string Value,
                               string Detail, Color Accent, double Max)>();

        if (_config.ShowCpu)
        {
            wanted.Add(("cpu", _lang["cpu"], snap.CpuTotal, snap.CpuTotal + "%",
                        $"{snap.Cores.Count} {_lang["cores"]}"
                        + (snap.CpuTemp is int t
                           ? "  ·  " + Palette.TempText(t, snap.CpuTempEstimated)
                           : string.Empty),
                        Palette.AccentCpu, 100));

            if (_config.CpuMode == "separated")
            {
                for (int i = 0; i < snap.Cores.Count; i++)
                {
                    Core core = snap.Cores[i];
                    wanted.Add(($"core{i}", "C" + i, core.Usage, core.Usage + "%",
                                string.Empty, Palette.AccentCpu, 100));
                }
            }
        }

        if (_config.ShowRam)
        {
            wanted.Add(("ram", _lang["memory"], snap.Ram.Usage, snap.Ram.Usage + "%",
                        $"{snap.Ram.UsedGb:F1} / {snap.Ram.TotalGb:F1} GB",
                        Palette.LoadColor(snap.Ram.Usage, Palette.AccentRam), 100));
        }

        if (_config.ShowDisk)
        {
            foreach (Disk disk in snap.Disks)
            {
                wanted.Add(($"disk{disk.Letter}", disk.Title, disk.Usage, disk.Usage + "%",
                            $"{disk.UsedGb:F0}/{disk.TotalGb:F0} GB · "
                            + SpeedText(disk.ReadMb, disk.WriteMb),
                            Palette.LoadColor(disk.Usage, Palette.AccentDisk), 100));

                // A drive's throughput is the interesting series, and it has no
                // ceiling to scale against, so the graph scales to its own peak.
                wanted.Add(($"diskio{disk.Letter}", disk.Letter + ": I/O",
                            disk.ReadMb + disk.WriteMb,
                            Speed(disk.ReadMb + disk.WriteMb) + " MB/s",
                            SpeedText(disk.ReadMb, disk.WriteMb),
                            Palette.AccentDisk, 0));
            }
        }

        if (_config.ShowNetwork)
        {
            foreach (Adapter adapter in snap.Adapters)
            {
                wanted.Add(($"net{adapter.Id}", adapter.Name,
                            adapter.DownMb + adapter.UpMb,
                            Speed(adapter.DownMb + adapter.UpMb) + " MB/s",
                            (adapter.Wireless ? _lang["wireless"] : _lang["wired"])
                            + "  ·  " + NetText(adapter.DownMb, adapter.UpMb),
                            Palette.AccentNet, 0));
            }
        }

        // Match by key so a card keeps its history when the list around it
        // changes; a drive that comes back finds its own graph again.
        var byKey = Cards.ToDictionary(c => c.Key);
        foreach (string stale in byKey.Keys.Where(k => wanted.All(w => w.Key != k)).ToList())
        {
            Cards.Remove(byKey[stale]);
            byKey.Remove(stale);
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            var item = wanted[i];
            if (!byKey.TryGetValue(item.Key, out ChartCard? card))
            {
                card = new ChartCard { Key = item.Key };
                byKey[item.Key] = card;
                Cards.Insert(Math.Min(i, Cards.Count), card);
            }
            card.Title = item.Title;
            card.Value = item.Value;
            card.Detail = item.Detail;
            card.Maximum = item.Max;
            card.Accent = Palette.Brush(item.Accent);
            card.Push(item.Sample);
        }
    }

    private string TitleFor(string kind) => kind switch
    {
        "cpu" => _lang["cpu"],
        "ram" => _lang["memory"],
        "net" => _lang["network"],
        "modules" => _lang["modules"] + "  —  " + _lang["no_module_usage"],
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
