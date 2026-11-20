using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace SysMonitor.Linux.App;

/// <summary>
/// Amendment 1: the check and radio marks are 14 px and sit on the first line
/// of their label. An absolute top margin cannot do that at every font scale,
/// so the mark's top offset is half the difference between one line box
/// (1.36 em, see <see cref="Ui.LineHeightEm"/>) and the 14 px mark. That keeps
/// the mark centred on the line at any size the type ramp can produce.
/// </summary>
public sealed class LineCentreConverter : IValueConverter
{
    /// <summary>The check box and the radio ring are both this square.</summary>
    public const double MarkSize = 14;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double size = value is double d ? d : 0;
        // Centre the 14 px mark on one 1.36 em line box. At 9 px the line is
        // 12.24, shorter than the mark, so the top margin is negative and the
        // mark overhangs the line symmetrically; the mark's own clip is off.
        double offset = (size * Ui.LineHeightEm - MarkSize) / 2;
        return new Thickness(0, Math.Round(offset, 2), 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
