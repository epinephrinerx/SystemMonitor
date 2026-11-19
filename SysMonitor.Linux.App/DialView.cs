using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// The spec's dial (section 4): an uppercase caption with a "NN%" readout, an
/// 18 px hit area, a 4 px track, a blue filled part and a white 14 px thumb
/// with a blue ring. Click anywhere to jump; drag to follow.
///
/// It lives in the app project because it is a control, but its value rules
/// (limits, rounding, readout) stay in <see cref="ShellModel"/>.
/// </summary>
internal sealed class DialView : Grid
{
    private readonly double _minimum;
    private readonly double _maximum;
    private readonly TextBlock _readout;
    private readonly Border _fill;
    private readonly Border _thumb;
    private readonly Grid _track;
    private double _value;
    private bool _dragging;

    public DialView(
        string settingKey,
        string caption,
        double value,
        double minimum,
        double maximum,
        Palette palette,
        TypeRamp ramp)
    {
        _minimum = minimum;
        _maximum = maximum;
        _value = Math.Clamp(value, minimum, maximum);
        Key = settingKey;

        RowDefinitions = new RowDefinitions("Auto,Auto");
        Classes.Add("nodrag");

        // Header row: caption on the left, readout on the right.
        var header = new Grid
        {
            Margin = new Thickness(0, 10, 0, 4),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        header.Children.Add(Ui.Text(
            Ui.Thai(caption).ToUpperInvariant(),
            ramp.Label,
            palette.Label,
            FontWeight.SemiBold,
            wrap: true));
        _readout = Ui.Text(ShellModel.Readout(_value), ramp.Small, palette.Muted);
        _readout.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_readout, 1);
        header.Children.Add(_readout);
        Children.Add(header);

        // Track row.
        _fill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Ui.Brush(Colour.FromHex("#3b82f6")),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _thumb = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.White,
            BorderBrush = Ui.Brush(Colour.FromHex("#3b82f6")),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _track = new Grid
        {
            Height = 18,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        _track.Children.Add(new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Ui.Brush(Colour.FromHex("#44808080")),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _track.Children.Add(_fill);
        _track.Children.Add(_thumb);
        Grid.SetRow(_track, 1);
        Children.Add(_track);

        _track.SizeChanged += (_, _) => Place();
        _track.PointerPressed += OnPressed;
        _track.PointerMoved += OnMoved;
        _track.PointerReleased += OnReleased;
        Place();
    }

    public string Key { get; }

    public double Value => _value;

    /// <summary>Live value while the pointer is down, before the committed one.</summary>
    public event Action<string, double>? ValueChanged;

    /// <summary>The value on release (or on a click): this is the one to save.</summary>
    public event Action<string, double>? ValueCommitted;

    /// <summary>Move the control to a value without raising either event.</summary>
    public void SetValue(double value)
    {
        _value = Math.Clamp(value, _minimum, _maximum);
        Sync();
    }

    private void Sync()
    {
        _readout.Text = ShellModel.Readout(_value);
        Place();
    }

    private void Place()
    {
        double width = _track.Bounds.Width;
        if (width <= 0)
        {
            return;
        }
        double fraction = _maximum > _minimum ? (_value - _minimum) / (_maximum - _minimum) : 0;
        fraction = Math.Clamp(fraction, 0, 1);
        _fill.Width = fraction * width;
        _thumb.Margin = new Thickness(fraction * Math.Max(0, width - 14), 0, 0, 0);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_track).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _dragging = true;
        e.Pointer.Capture(_track);
        e.Handled = true;
        Apply(FromPointer(e));
        ValueChanged?.Invoke(Key, _value);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        Apply(FromPointer(e));
        ValueChanged?.Invoke(Key, _value);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        e.Pointer.Capture(null);
        Apply(FromPointer(e));
        ValueCommitted?.Invoke(Key, _value);
        e.Handled = true;
    }

    private double FromPointer(PointerEventArgs e)
    {
        double width = Math.Max(1, _track.Bounds.Width - 14);
        double x = e.GetPosition(_track).X - 7;
        double fraction = Math.Clamp(x / width, 0, 1);
        return _minimum + fraction * (_maximum - _minimum);
    }

    private void Apply(double value)
    {
        // Font size steps to 0.1, opacity to whole percent: snapping here keeps
        // the thumb and the readout from disagreeing.
        double snapped = Key == SettingKeys.FontSize
            ? Math.Round(value, 1)
            : Math.Round(value, 2);
        _value = Math.Clamp(snapped, _minimum, _maximum);
        Sync();
    }
}
