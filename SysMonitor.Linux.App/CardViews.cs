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

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
            Margin = new Thickness(0, 0, 0, 4),
        };
        _title = Ui.Text(string.Empty, ramp.Small, palette.Label, ellipsis: true);
        header.Children.Add(_title);
        _value = Ui.Text(string.Empty, ramp.Small, palette.Text, FontWeight.SemiBold, ellipsis: true);
        Grid.SetColumn(_value, 1);
        header.Children.Add(_value);

        _chart = new ChartControl(card.History, card.Accent, card.Maximum, palette)
        {
            Height = 60,
        };

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(_chart);

        _border = new Border
        {
            Width = 89,
            Height = 89,
            Background = Ui.Brush(palette.Control),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 5, 7, 6),
            Margin = new Thickness(0, 0, 8, 8),
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
        _value.Text = Ui.Thai(card.Value);
        _chart.SetSeries(card.History, card.Maximum);
    }
}
