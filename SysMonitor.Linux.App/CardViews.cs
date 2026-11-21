using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// The pieces a module page is made of (R-030 §2b), shared by the Main window's
/// page and the probe: one usage card with its chart, one small card, one
/// Properties row and the section headings. They are updated in place by key,
/// so a fresh sample moves numbers and graphs without rebuilding a control.
/// </summary>
internal static class PageParts
{
    public const string ValueSeparator = "  ·  ";

    /// <summary>"Usage", "Sensors", "Properties": the §2b section order.</summary>
    public static TextBlock Title(ShellModel shell, string text)
    {
        TextBlock block = Ui.Heading(text, shell.Palette, shell.Ramp);
        block.Margin = new Thickness(0, 2, 0, 8);
        return block;
    }

    /// <summary>The value line under a card's title: "14% · Highest: 42%".</summary>
    public static string ValueLine(FullCard card) =>
        card.Peak.Length > 0
            ? Ui.Thai(card.Value) + ValueSeparator + Ui.Thai(card.Peak)
            : Ui.Thai(card.Value);

    /// <summary>One label/value row of the Properties list.</summary>
    public static Control Property(FullFact fact, ShellModel shell, out TextBlock valueBlock)
    {
        Palette palette = shell.Palette;

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(150, GridUnitType.Pixel)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
            Margin = new Thickness(0, 1, 0, 1),
        };

        TextBlock label = Ui.Text(
            Ui.Thai(fact.Title), shell.Ramp.Small, palette.Label, ellipsis: true);
        label.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(label);

        TextBlock value = Ui.Text(
            Ui.Thai(fact.Value),
            shell.Ramp.Body,
            string.Equals(fact.Value, shell.Language["na"], StringComparison.Ordinal)
                ? palette.Label
                : palette.Text,
            wrap: true,
            align: TextAlignment.Right);
        value.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(value, 1);
        row.Children.Add(value);

        valueBlock = value;
        return row;
    }

    /// <summary>Refresh a Properties row in place on a data tick.</summary>
    public static void UpdateProperty(TextBlock value, FullFact fact, ShellModel shell)
    {
        value.Text = Ui.Thai(fact.Value);
        value.Foreground = Ui.Brush(
            string.Equals(fact.Value, shell.Language["na"], StringComparison.Ordinal)
                ? shell.Palette.Label
                : shell.Palette.Text);
    }
}

/// <summary>One Usage or Sensors card, updated in place.</summary>
internal sealed class BigCardView
{
    private readonly TextBlock _title;
    private readonly TextBlock _value;
    private readonly TextBlock _detail;
    private readonly TextBlock _unit;
    private readonly TextBlock _ceiling;
    private readonly ChartControl _chart;
    private readonly Border _border;

    public BigCardView(FullCard card, ShellModel shell)
    {
        Palette palette = shell.Palette;
        TypeRamp ramp = shell.Ramp;

        var titleRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        _title = Ui.Text(string.Empty, ramp.Label, palette.Text, ellipsis: true);
        _title.Margin = new Thickness(0, 0, 66, 0);
        titleRow.Children.Add(_title);

        _value = Ui.Text(string.Empty, ramp.Body, palette.Text, FontWeight.SemiBold);
        _value.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(_value, 1);
        titleRow.Children.Add(_value);

        _detail = Ui.Text(string.Empty, ramp.Small, palette.Label, ellipsis: true);

        var axis = new Grid
        {
            Margin = new Thickness(0, 6, 0, 2),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        _unit = Ui.Text(string.Empty, ramp.Tiny, palette.Label, ellipsis: true);
        axis.Children.Add(_unit);
        _ceiling = Ui.Text(string.Empty, ramp.Tiny, palette.Label);
        Grid.SetColumn(_ceiling, 1);
        axis.Children.Add(_ceiling);

        _chart = new ChartControl(card.History, card.Accent, card.Maximum, palette)
        {
            Height = 96,
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var body = new StackPanel();
        body.Children.Add(titleRow);
        body.Children.Add(_detail);
        body.Children.Add(axis);
        body.Children.Add(_chart);

        _border = new Border
        {
            Background = Ui.Brush(palette.Control),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 11),
            Margin = new Thickness(0, 0, 0, 10),
            MinHeight = 178,
            Child = body,
        };

        Update(card, shell);
    }

    public string Key { get; private set; } = string.Empty;

    public Control Control => _border;

    public void Update(FullCard card, ShellModel shell)
    {
        Key = card.Key;
        _title.Text = Ui.Thai(card.Title);
        _value.Text = PageParts.ValueLine(card);
        _detail.Text = Ui.Thai(card.Detail);
        _detail.IsVisible = _detail.Text.Length > 0;
        _unit.Text = Ui.Thai(card.Unit);
        _ceiling.Text = Ui.Thai(card.Ceiling);
        _chart.SetSeries(card.History, card.Maximum);
    }
}

/// <summary>One per-core, per-engine or per-volume card, updated in place.</summary>
internal sealed class SmallCardView
{
    private readonly TextBlock _title;
    private readonly TextBlock _value;
    private readonly ChartControl _chart;
    private readonly Border _border;

    public SmallCardView(FullCard card, ShellModel shell)
    {
        Palette palette = shell.Palette;
        TypeRamp ramp = shell.Ramp;

        // R-030 fix 6: the title keeps the first line and the value takes its
        // own, because "C7" beside "100% · 105°C · 1.2 GHz" stopped fitting on
        // one line once the font scale reached 110 % (fix 4 measured that line
        // at 100 % only). Both lines are left-aligned and single-line, with the
        // ellipsis kept as the last resort for a long disk label.
        _title = Ui.Text(string.Empty, ramp.Small, palette.Label, ellipsis: true);
        _value = Ui.Text(string.Empty, ramp.Small, palette.Text, FontWeight.SemiBold, ellipsis: true);

        _chart = new ChartControl(card.History, card.Accent, card.Maximum, palette)
        {
            Height = 60,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var body = new StackPanel();
        body.Children.Add(_title);
        body.Children.Add(_value);
        body.Children.Add(_chart);

        _border = new Border
        {
            Background = Ui.Brush(palette.Control),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 5, 7, 6),
            Margin = new Thickness(0, 0, 8, 8),
            Child = body,
        };

        // R-030 fix 6 + Amendment 1: size the card from the scale it was built
        // at, and again from the live scale when the font dial moves.
        Rescale(ramp);

        Update(card, shell);
    }

    public string Key { get; private set; } = string.Empty;

    public Control Control => _border;

    /// <summary>
    /// Amendment 1: follow a live font-size change. The page is not rebuilt
    /// for a tick of the font dial, so the card re-derives its own box from
    /// the current ramp instead of keeping the constructor's size. The two
    /// text lines grow with the type, the 60 px graph does not, and the
    /// surrounding wrap panel reflows to fewer columns on its own.
    /// </summary>
    public void Rescale(TypeRamp ramp)
    {
        // The 89 px of fix 4 held one text line (1.36 em), the 4 px gap and the
        // 60 px graph between the 11 px of padding; the second line adds its
        // own height, which grows with the scale like the type. Rounding up
        // leaves the graph clear of the bottom padding at every scale.
        double lineHeight = Math.Round(ramp.Small * Ui.LineHeightEm, 2);

        // Amendment 2: the height below counts these two text lines, so their
        // line boxes must follow the live scale too. Ui.Text set the height at
        // construction and ScaleText rewrites only FontSize, which left a card
        // built at 160 % with 160 % line boxes inside a 100 % card.
        _title.LineHeight = lineHeight;
        _value.LineHeight = lineHeight;

        // R-030 fix 6: 146 px at the 100 % font size, growing with the font
        // scale so the value line keeps the slack fix 4 measured (132 px
        // inner width) — the wrap panel reflows to fewer columns instead.
        _border.Width = Math.Ceiling(146 * ramp.Scale);
        _border.Height = Math.Ceiling(5 + (2 * lineHeight) + 4 + 60 + 6);
    }

    public void Update(FullCard card, ShellModel shell)
    {
        Key = card.Key;
        _title.Text = Ui.Thai(card.Title);
        _value.Text = Ui.Thai(card.Value);
        _chart.SetSeries(card.History, card.Maximum);
    }
}
