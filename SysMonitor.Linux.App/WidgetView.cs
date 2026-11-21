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
/// the step buttons stay fixed size just inside the panel's edges, and the
/// corner buttons (built by the window) sit in the panel's top-right corner,
/// with the title row keeping their room clear.
/// </summary>
internal sealed class WidgetView : UserControl
{
    /// <summary>The step chips sit this far inside the panel edge.</summary>
    internal const double ArrowInset = 2;

    /// <summary>The step chip's box (Ui.StepButton); it is not scaled.</summary>
    internal const double ArrowBox = 20;

    /// <summary>
    /// The content's left/right inset: a chip plus a 4 px gap, so a chip never
    /// covers a label, a bar or the title.
    /// </summary>
    internal const double ContentInset = ArrowInset + ArrowBox + 4;

    /// <summary>The corner buttons sit this far inside the panel edge.</summary>
    internal const double CornerInset = 6;

    /// <summary>The corner button's box; its hit area does not scale.</summary>
    internal const double CornerBox = 18;

    /// <summary>
    /// The gap between the two corner buttons, measured edge to edge: 18 px box
    /// plus 4 px.
    /// </summary>
    internal const double CornerStep = CornerBox + 4;

    /// <summary>
    /// The title row's right pad in panel pixels: the corner buttons' 46 px,
    /// less the content's own 26 px inset, plus a 4 px gap. The content is
    /// scaled, so the row's margin is this value divided by the scale.
    /// </summary>
    internal const double CornerReserve =
        CornerInset + CornerStep + CornerBox + 4 - ContentInset;

    /// <summary>The screen-space room the paused badge takes before the title.</summary>
    internal const double PausedBadgeRoom = 72;

    private readonly ShellModel _shell;
    private readonly LayoutTransformControl _scale;
    private double _factor = 1;

    // The title row is built once per render but the scale changes on every
    // resize, so the controls whose margins are measured in panel pixels are
    // kept and updated by ApplyTitleScale.
    private TextBlock? _title;
    private LayoutTransformControl? _pausedHost;
    private StackPanel? _titleRight;
    private bool _titlePaused;

    public WidgetView(ShellModel shell)
    {
        _shell = shell;

        _scale = new LayoutTransformControl
        {
            // Spec section 1: 16 px top and bottom. The sides give the step
            // chips their own strip so they stay inside the panel and cover no
            // content. The old vertical budget applies again now that every
            // text block has a 1.36 em line box (see Ui.LineHeightEm), so the
            // amendment's temporary 8 px top and bottom margin is gone.
            Margin = new Thickness(ContentInset, 16, ContentInset, 16),
            LayoutTransform = new ScaleTransform(1, 1),
        };

        var root = new Grid();
        root.Children.Add(_scale);

        Border prev = Ui.StepButton(
            IconName.ChevronLeft, shell.Palette, shell.Ramp.Small,
            () => _shell.PrevPage(), string.Empty);
        prev.HorizontalAlignment = HorizontalAlignment.Left;
        prev.VerticalAlignment = VerticalAlignment.Center;
        prev.Margin = new Thickness(ArrowInset, 0, 0, 0);
        root.Children.Add(prev);

        Border next = Ui.StepButton(
            IconName.ChevronRight, shell.Palette, shell.Ramp.Small,
            () => _shell.NextPage(), string.Empty);
        next.HorizontalAlignment = HorizontalAlignment.Right;
        next.VerticalAlignment = VerticalAlignment.Center;
        next.Margin = new Thickness(0, 0, ArrowInset, 0);
        root.Children.Add(next);

        Content = root;
        Render();
    }

    public void Render()
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;
        WidgetPage? page = _shell.CurrentPage;

        // A render replaces the content, so any reference to the previous
        // title row is dropped with it.
        _title = null;
        _pausedHost = null;
        _titleRight = null;

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
        // The title row's margins are panel pixels divided by the scale (the
        // corner buttons' room and the paused badge), so a scale change has to
        // rewrite them; the row itself is not rebuilt on every resize.
        ApplyTitleScale();
        InvalidateMeasure();
    }

    /// <summary>
    /// Re-apply the title row's scale-dependent margins and counter-scales: the
    /// corner buttons' reserve and the paused badge's fixed screen size.
    /// </summary>
    private void ApplyTitleScale()
    {
        double inverse = 1 / _factor;
        if (_pausedHost is not null)
        {
            _pausedHost.LayoutTransform = new ScaleTransform(inverse, inverse);
        }
        if (_title is not null)
        {
            _title.Margin = new Thickness(
                _titlePaused ? PausedBadgeRoom * inverse : 0, 0, 8, 0);
        }
        if (_titleRight is not null)
        {
            _titleRight.Margin = new Thickness(0, 0, CornerReserve * inverse, 0);
        }
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
        _titlePaused = paused;
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
            _pausedHost = counter;
            titleHost.Children.Add(counter);
        }

        TextBlock title = Ui.Text(
            Ui.Thai(page.Title).ToUpperInvariant(),
            ramp.Label,
            palette.Label,
            FontWeight.SemiBold,
            ellipsis: true);
        // The paused badge's room is a screen width, so it is divided by the
        // scale; ApplyTitleScale rewrites it when the scale changes.
        title.Margin = new Thickness(paused ? PausedBadgeRoom / _factor : 0, 0, 8, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        _title = title;
        titleHost.Children.Add(title);
        grid.Children.Add(titleHost);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            // See CornerReserve: panel pixels turned into the row's units, and
            // rewritten by ApplyTitleScale on a resize.
            Margin = new Thickness(0, 0, CornerReserve / _factor, 0),
        };
        _titleRight = right;
        if (page.Badge is { } badgeModel)
        {
            Control badge = Ui.Badge(badgeModel, ramp);
            badge.Margin = new Thickness(0, 0, 6, 0);
            right.Children.Add(badge);
        }
        right.Children.Add(Ui.Text(page.Value, ramp.Value, palette.Text, FontWeight.Bold));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return grid;
    }
}
