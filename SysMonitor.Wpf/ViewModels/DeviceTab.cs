using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SysMonitor.ViewModels;

/// <summary>
/// One tab of the full view: a device kind, or a single drive.
///
/// The view used to put every graph on one scrolling page, which meant sixteen
/// cores, seven drives and two adapters competing for the same screen. A tab
/// shows one group at a time and gives it the room to be read.
/// </summary>
public sealed class DeviceTab : INotifyPropertyChanged
{
    private bool _selected;
    private string _title = string.Empty;
    private string _summary = string.Empty;
    private string _detail = string.Empty;
    private string _hardware = string.Empty;

    /// <summary>Stable across rebuilds, so the selection survives a refresh.</summary>
    public required string Key { get; init; }

    /// <summary>What the tab button says: "CPU", "Memory", "C:", "Wi-Fi".</summary>
    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    /// <summary>The headline figure on the tab button, so the unselected tabs still report.</summary>
    public string Summary
    {
        get => _summary;
        set => Set(ref _summary, value);
    }

    /// <summary>
    /// What the device is, on the rail between the name and the figure:
    /// "SSD (NVMe)", "31.6 GB", "Wi-Fi". Task Manager puts it here and it is
    /// the right place -- a name alone does not tell you which drive is which,
    /// and repeating it inside the panel only says it twice.
    /// </summary>
    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    /// <summary>The model line that heads the panel, to the right of the name.</summary>
    public string Hardware
    {
        get => _hardware;
        set => Set(ref _hardware, value);
    }

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// <summary>The headline graphs, one framed panel each, top to bottom.</summary>
    public ObservableCollection<ChartCard> Cards { get; } = new();

    /// <summary>
    /// The per-core squares, which flow into as many columns as the window is
    /// wide. Kept apart from <see cref="Cards"/> because the two are laid out
    /// differently: one stacks, the other wraps.
    /// </summary>
    public ObservableCollection<ChartCard> Cores { get; } = new();

    /// <summary>
    /// Facts rather than readings: what the hardware is. Empty for tabs that
    /// have nothing static worth stating.
    /// </summary>
    public ObservableCollection<DeviceFact> Facts { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>One labelled fact about a device: "Model", "WDS250G3X0C-00SJG0".</summary>
public sealed class DeviceFact
{
    public required string Name { get; init; }
    public required string Value { get; init; }
}
