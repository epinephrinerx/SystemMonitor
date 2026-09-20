using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace SysMonitor.ViewModels;

/// <summary>
/// One labelled bar: the shape both the mini rotation and the expanded view
/// are built from.
///
/// The bar fill is expressed as star column widths rather than a pixel width,
/// so the layout system does the arithmetic and a resized window needs no
/// recalculation of its own.
/// </summary>
public sealed class MeterRow : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _detail = string.Empty;
    private string _valueText = string.Empty;
    private double _percent;
    private int? _temp;
    private bool _estimated;
    private Brush _accent = Brushes.Transparent;
    private Brush _empty = Brushes.Transparent;
    private Brush _tempFore = Brushes.Transparent;
    private Brush _tempBack = Brushes.Transparent;
    private bool _hot;
    private bool _compact;
    private bool _infoOnly;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public string ValueText
    {
        get => _valueText;
        set => Set(ref _valueText, value);
    }

    public double Percent
    {
        get => _percent;
        set
        {
            if (Set(ref _percent, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(FillStar));
                OnPropertyChanged(nameof(RestStar));
            }
        }
    }

    public GridLength FillStar => new(Math.Max(_percent, 0.0001), GridUnitType.Star);

    public GridLength RestStar => new(Math.Max(100 - _percent, 0.0001), GridUnitType.Star);

    public Brush Accent
    {
        get => _accent;
        set => Set(ref _accent, value);
    }

    public Brush Empty
    {
        get => _empty;
        set => Set(ref _empty, value);
    }

    public int? Temp
    {
        get => _temp;
        set
        {
            if (Set(ref _temp, value))
            {
                OnPropertyChanged(nameof(TempText));
            }
        }
    }

    public bool Estimated
    {
        get => _estimated;
        set
        {
            if (Set(ref _estimated, value))
            {
                OnPropertyChanged(nameof(TempText));
            }
        }
    }

    /// <summary>"n/a" where no sensor exists; "~" marks a modelled value.</summary>
    public string TempText => Palette.TempText(_temp, _estimated);

    public Brush TempFore
    {
        get => _tempFore;
        set => Set(ref _tempFore, value);
    }

    public Brush TempBack
    {
        get => _tempBack;
        set => Set(ref _tempBack, value);
    }

    /// <summary>At or above the hot threshold; the badge blinks while true.</summary>
    public bool Hot
    {
        get => _hot;
        set => Set(ref _hot, value);
    }

    /// <summary>
    /// A fact, not a measurement: the row shows its title and detail and no
    /// bar at all. Installed memory modules use this, because Windows reports
    /// no per-module usage and a filled bar would be inventing one.
    /// </summary>
    public bool InfoOnly
    {
        get => _infoOnly;
        set => Set(ref _infoOnly, value);
    }

    /// <summary>
    /// Label and bar only.  The mini view's per-core rows sit two to a line,
    /// where a percentage and a badge would leave the bar too narrow to read.
    /// </summary>
    public bool Compact
    {
        get => _compact;
        set => Set(ref _compact, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
