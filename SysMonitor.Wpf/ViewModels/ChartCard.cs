using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SysMonitor.Model;

namespace SysMonitor.ViewModels;

/// <summary>
/// One graph in the full-screen view: a device, its current reading, and the
/// last few minutes of it.
/// </summary>
public sealed class ChartCard : INotifyPropertyChanged
{
    /// <summary>
    /// Three minutes at the balanced cadence, a little over one at the fast
    /// one. Long enough to see a spike in context, short enough that the
    /// buffer costs nothing.
    /// </summary>
    public const int Points = 72;

    private string _title = string.Empty;
    private string _value = string.Empty;
    private string _detail = string.Empty;
    private int _revision;
    private double _maximum = 100;
    private Brush _accent = Brushes.Gray;
    private string _ceiling = string.Empty;

    public required string Key { get; init; }

    /// <summary>Which heading this graph sits under: CPU, Memory, Disk, Network.</summary>
    public required string Group { get; init; }

    /// <summary>
    /// A headline device gets a full card; the per-core graphs get a quarter
    /// of one, the way Task Manager shows the package large and the logical
    /// processors as a grid of small boxes.
    /// </summary>
    public bool Small { get; init; }

    /// <summary>
    /// How tall a headline card stands. Small cards are half of it and square,
    /// the way Task Manager draws the package large and the logical processors
    /// as a grid of little boxes beneath it.
    /// </summary>
    public const double FullHeight = 178;
    public const double SmallSide = FullHeight / 2;

    public double CardWidth => Small ? SmallSide : double.NaN;
    public double CardHeight => Small ? SmallSide : FullHeight;

    /// <summary>"% ใช้งาน" or "MB/s": what the vertical axis is counting.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>The top of the scale, written out: "100%" or the peak so far.</summary>
    public string Ceiling
    {
        get => _ceiling;
        set => Set(ref _ceiling, value);
    }

    public History Series { get; } = new(Points);

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    /// <summary>The headline figure: a percentage, or a rate with its unit.</summary>
    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public Brush Accent
    {
        get => _accent;
        set => Set(ref _accent, value);
    }

    /// <summary>100 for a percentage; 0 lets the graph scale to its own peak.</summary>
    public double Maximum
    {
        get => _maximum;
        set => Set(ref _maximum, value);
    }

    /// <summary>
    /// The history object keeps its identity as it fills, so the chart needs
    /// something that actually changes to know the picture is stale.
    /// </summary>
    public int Revision
    {
        get => _revision;
        private set => Set(ref _revision, value);
    }

    public void Push(double sample)
    {
        Series.Add(sample);
        Revision = Series.Revision;
    }

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
