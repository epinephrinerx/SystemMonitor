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

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// <summary>The graphs, one framed panel each, top to bottom.</summary>
    public ObservableCollection<ChartCard> Cards { get; } = new();

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
