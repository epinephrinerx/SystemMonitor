using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;
using Path = Avalonia.Controls.Shapes.Path;

namespace SysMonitor.Linux.App;

/// <summary>
/// A Yes/No dialog with the warning icon the spec asks for, default No. Used by
/// the memory tab: process kills, service actions and startup changes.
/// </summary>
internal sealed class ConfirmWindow : Window
{
    public ConfirmWindow(ShellModel shell, string message, string yes, string no)
    {
        Palette palette = shell.Palette;
        TypeRamp ramp = shell.Ramp;

        Title = shell.Language["title"];
        Width = 430;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = Ui.Family;
        Background = Ui.Brush(palette.Panel);
        RequestedThemeVariant = App.ThemeFor(shell.Config.Theme);

        var warning = new Path
        {
            Data = Geometry.Parse("M12,3 L22,20 H2 Z M12,9 V14 M12,17 V17.6"),
            Width = 26,
            Height = 26,
            Stretch = Stretch.Uniform,
            Stroke = Ui.Brush(Colour.FromHex("#eab308")),
            StrokeThickness = 1.6,
            StrokeJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 12, 0),
        };

        var text = Ui.Text(Ui.Thai(message), ramp.Body, palette.Text, wrap: true);
        text.VerticalAlignment = VerticalAlignment.Center;

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        body.Children.Add(warning);
        Grid.SetColumn(text, 1);
        body.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var yesButton = new Button { Content = yes, MinWidth = 84, IsDefault = false };
        var noButton = new Button { Content = no, MinWidth = 84, IsDefault = true };
        yesButton.Click += (_, _) => Close(true);
        noButton.Click += (_, _) => Close(false);
        buttons.Children.Add(yesButton);
        buttons.Children.Add(noButton);

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(body);
        content.Children.Add(buttons);
        Content = content;

        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                e.Handled = true;
                Close(false);
            }
        };
    }

    /// <summary>Yes and No in the current language; the table owns both words.</summary>
    public static (string Yes, string No) Labels(ShellModel shell) =>
        (shell.Language["yes"], shell.Language["no"]);
}
