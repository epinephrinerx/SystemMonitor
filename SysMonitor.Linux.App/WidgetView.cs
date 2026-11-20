using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// The Widget (spec section 3): one page at a time, rotating every five
/// seconds. The content sits inside a host scaled by min(panel/270, panel/104);
/// the step buttons stay fixed size and the corner buttons live in the window's
/// shadow pad (a Border clips to its rounded corner).
/// </summary>
internal sealed class WidgetView : UserControl
{
    private readonly ShellModel _shell;
    private readonly LayoutTransformControl _scale;
    private double _factor = 1;

    public WidgetView(ShellModel shell)
    {
        _shell = shell;

        _scale = new LayoutTransformControl
        {
            // Spec section 1: 16 px all round. The old vertical budget applies
            // again now that every text block has a 1.36 em line box (see
            // Ui.LineHeightEm), so the amendment's temporary 8 px top and
            // bottom margin is gone.
            Margin = new Thickness(16),
            LayoutTransform = new ScaleTransform(1, 1),
        };

        var root = new Grid();
        root.Children.Add(_scale);

        Border prev = Ui.StepButton(
            IconName.ChevronLeft, shell.Palette, shell.Ramp.Small,
            () => _shell.PrevPage(), string.Empty);
        prev.HorizontalAlignment = HorizontalAlignment.Left;
        prev.VerticalAlignment = VerticalAlignment.Center;
        prev.Margin = new Thickness(-6, 0, 0, 0);
        root.Children.Add(prev);

        Border next = Ui.StepButton(
            IconName.ChevronRight, shell.Palette, shell.Ramp.Small,
            () => _shell.NextPage(), string.Empty);
        next.HorizontalAlignment = HorizontalAlignment.Right;
        next.VerticalAlignment = VerticalAlignment.Center;
        next.Margin = new Thickness(0, 0, -6, 0);
        root.Children.Add(next);

        Content = root;
        Render();
    }

    public void Render()
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;
        WidgetPage? page = _shell.CurrentPage;

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        if (page is null || page.Message.Length > 0)
        {
            var message = Ui.Text(
                page?.Message ?? _shell.Language["connecting"],
                ramp.Label,
                palette.Label,
                wrap: true,
                align: TextAlignment.Center);
            message.HorizontalAlignment = HorizontalAlignment.Center;
            message.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRowSpan(message, 3);
            content.Children.Add(message);
        }
        else
        {
            content.Children.Add(TitleRow(page, palette, ramp));

            var rows = new UniformGrid
            {
                Columns = Math.Max(1, page.Columns),
                ColumnSpacing = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2),
            };
            foreach (MeterRow row in page.Rows)
            {
                var view = Rows.Build(
                    row,
                    palette,
                    ramp,
                    compact: true,
                    lang: _shell.Language);
                view.HorizontalAlignment = HorizontalAlignment.Stretch;
                rows.Children.Add(view);
            }
            Grid.SetRow(rows, 1);
            content.Children.Add(rows);

            if (page.Detail.Length > 0)
            {
                bool alert = page.Alert is not null;
                Colour colour = page.Alert?.Colour ?? palette.Muted;
                TextBlock detail = Ui.Text(
                    Ui.Thai(page.Detail), ramp.Small, colour, ellipsis: true);
                detail.Margin = new Thickness(0, 4, 0, 0);
                detail.VerticalAlignment = VerticalAlignment.Bottom;
                Grid.SetRow(detail, 2);
                if (alert)
                {
                    detail.Cursor = new Cursor(StandardCursorType.Hand);
                    detail.Classes.Add("nodrag");
                    string drive = page.DriveKey ?? string.Empty;
                    detail.PointerPressed += (_, e) =>
                    {
                        if (!e.GetCurrentPoint(detail).Properties.IsLeftButtonPressed)
                        {
                            return;
                        }
                        e.Handled = true;
                        if (drive.Length > 0)
                        {
                            DiskTools.OpenDiskUsage(drive);
                            _shell.DismissAlert(drive);
                        }
                    };
                }
                content.Children.Add(detail);
            }
        }

        _scale.Child = content;
    }

    /// <summary>Content scale: min(panelW/270, panelH/104).</summary>
    public void SetScale(double scale)
    {
        _factor = Math.Max(0.01, scale);
        _scale.LayoutTransform = new ScaleTransform(_factor, _factor);
        InvalidateMeasure();
    }

    private Control TitleRow(WidgetPage page, Palette palette, TypeRamp ramp)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        var titleHost = new Grid();
        bool paused = _shell.Paused;
        if (paused)
        {
            TextBlock badge = Ui.Text(
                Ui.Thai(_shell.Language["paused"]), ramp.Label, palette.Label, FontWeight.SemiBold);
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            badge.VerticalAlignment = VerticalAlignment.Center;
            // The badge counter-scales so it stays the same size on screen.
            var counter = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(1 / _factor, 1 / _factor),
                Child = badge,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            titleHost.Children.Add(counter);
        }

        TextBlock title = Ui.Text(
            Ui.Thai(page.Title).ToUpperInvariant(),
            ramp.Label,
            palette.Label,
            FontWeight.SemiBold,
            ellipsis: true);
        // 72 screen px of room when the paused badge is shown.
        title.Margin = new Thickness(paused ? 72 / _factor : 0, 0, 8, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        titleHost.Children.Add(title);
        grid.Children.Add(titleHost);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (page.Badge is { } badgeModel)
        {
            Control badge = Ui.TemperatureBadge(badgeModel, ramp);
            badge.Margin = new Thickness(0, 0, 6, 0);
            right.Children.Add(badge);
        }
        right.Children.Add(Ui.Text(page.Value, ramp.Value, palette.Text, FontWeight.Bold));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return grid;
    }
}
