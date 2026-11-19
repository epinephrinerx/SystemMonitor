using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// The Full view is phase 2b. For now it is the real frame — the
/// <c>full_data</c> title, the three header buttons and the 0 px panel radius —
/// with an empty body, so the switching rules and the headers can be exercised.
/// </summary>
internal sealed class FullView : UserControl
{
    private readonly ShellModel _shell;

    public FullView(ShellModel shell, Action<string> dispatch)
    {
        _shell = shell;
        _ = dispatch;
        Render();
    }

    /// <summary>Rebuild the header in the current language and palette.</summary>
    public void Render()
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        header.Children.Add(Ui.Text(
            _shell.FullTitle, ramp.Heading, palette.Text, Avalonia.Media.FontWeight.Bold));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttons.Children.Add(Ui.IconButton(
            IconName.Home, 26, ramp.Body, Palette.IconButton,
            () => _shell.FullHeaderOverall(), _shell.Language["go_overall"]));
        buttons.Children.Add(Ui.IconButton(
            IconName.Widget, 26, ramp.Body, Palette.IconButton,
            () => _shell.FullHeaderWidget(), _shell.Language["open_widget"]));
        buttons.Children.Add(Ui.IconButton(
            IconName.Close, 26, ramp.Body, Palette.IconButton,
            () => _shell.Close(), _shell.Language["close"]));
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);

        var root = new Grid
        {
            Margin = new Thickness(14, 12, 14, 12),
            RowDefinitions = new RowDefinitions("Auto,*"),
        };
        root.Children.Add(header);

        Content = root;
    }
}
