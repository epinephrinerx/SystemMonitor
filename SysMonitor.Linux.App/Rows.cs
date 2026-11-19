using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// One meter row, shared by the Overall cards and the compact Widget pages
/// (spec section 4 "MeterRow"). Compact rows are the Widget's: label and bar,
/// nothing else.
/// </summary>
internal static class Rows
{
    public static Control Build(
        MeterRow row,
        Palette palette,
        TypeRamp ramp,
        bool compact,
        Lang lang,
        Action<string>? openDrive = null,
        Action<string, ContextMenu>? driveMenu = null,
        Action<string>? openCleanup = null,
        Action<string>? dismissAlert = null)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var grid = new Grid
        {
            Margin = compact ? new Thickness(0, 1, 0, 1) : new Thickness(0, 2, 6, 2),
        };
        if (compact)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            if (row.Title.Length > 0)
            {
                var label = Ui.Text(row.Title, ramp.Small, palette.Muted, ellipsis: true);
                label.Margin = new Thickness(0, 0, 6, 0);
                label.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(label);
            }
            Control bar = Ui.Bar(row.Percent, row.BarColour, palette.BarEmpty);
            Grid.SetColumn(bar, 1);
            bar.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(bar);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var title = Ui.Text(row.Title, ramp.Body, palette.Muted, ellipsis: true);
            title.MaxWidth = 150;
            title.Margin = new Thickness(0, 0, 8, 0);
            title.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(title);

            Control bar = Ui.Bar(row.Percent, row.BarColour, palette.BarEmpty);
            Grid.SetColumn(bar, 1);
            bar.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(bar);

            if (row.ShowValue)
            {
                string readout = string.IsNullOrEmpty(row.ValueText)
                    ? Format.Percent(row.Percent)
                    : row.ValueText!;
                var value = Ui.Text(readout, ramp.Body, palette.Text);
                value.MinWidth = 34;
                value.TextAlignment = TextAlignment.Right;
                value.Margin = new Thickness(8, 0, 0, 0);
                value.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(value, 2);
                grid.Children.Add(value);
            }

            if (row.Badge is { } badge)
            {
                Control badgeView = Ui.TemperatureBadge(badge, ramp);
                badgeView.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(badgeView, 3);
                grid.Children.Add(badgeView);
            }
        }
        stack.Children.Add(grid);

        if (!compact && row.Detail.Length > 0)
        {
            TextBlock detail = Ui.Text(
                Ui.Thai(row.Detail), ramp.Small, palette.Label, ellipsis: true);
            detail.Margin = new Thickness(0, 1, 0, 3);
            stack.Children.Add(detail);
        }

        if (!compact && row.Alert is { } alert)
        {
            stack.Children.Add(AlertBanner(alert, lang, palette, ramp, openCleanup, dismissAlert));
        }

        if (row.DriveKey is { } drive)
        {
            // A drive row answers to the pointer: double-click opens it,
            // right-click opens the drive menu (spec section 4b).
            stack.Classes.Add("nodrag");
            stack.Cursor = new Cursor(StandardCursorType.Arrow);
            stack.DoubleTapped += (_, e) =>
            {
                e.Handled = true;
                openDrive?.Invoke(drive);
            };
            if (driveMenu is not null)
            {
                var menu = new ContextMenu();
                menu.Opening += (_, _) => driveMenu(drive, menu);
                stack.ContextMenu = menu;
            }
        }

        return stack;
    }

    /// <summary>The amber/red warning bar under a drive row (spec section 4).</summary>
    private static Control AlertBanner(
        DiskAlert alert,
        Lang lang,
        Palette palette,
        TypeRamp ramp,
        Action<string>? openCleanup,
        Action<string>? dismissAlert)
    {
        Colour colour = alert.Colour;
        var text = Ui.Text(Ui.Thai(alert.Text(lang)), ramp.Small, palette.Text, wrap: true);
        var dismiss = Ui.IconButton(
            IconName.Dismiss,
            18,
            ramp.Tiny,
            palette.Muted,
            () => dismissAlert?.Invoke(alert.Letter),
            tooltip: string.Empty);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Children.Add(text);
        Grid.SetColumn(dismiss, 1);
        grid.Children.Add(dismiss);

        var banner = new Border
        {
            Background = Ui.Brush(colour.WithAlpha(0x30)),
            BorderBrush = Ui.Brush(colour),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 6, 4),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid,
        };
        banner.Classes.Add("nodrag");
        banner.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(banner).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            openCleanup?.Invoke(alert.Letter);
        };
        return banner;
    }
}
