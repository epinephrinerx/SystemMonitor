using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// Turns the sidebar, Settings, Manual and About row models into controls. All
/// four surfaces use the same row kinds, so this is the only place a check row
/// or a dial is built.
/// </summary>
internal static class RowRenderer
{
    public static void Fill(
        StackPanel target,
        IReadOnlyList<UiRow> rows,
        ShellModel shell,
        Action<string> dispatch,
        List<DialView>? dials = null)
    {
        Palette palette = shell.Palette;
        TypeRamp ramp = shell.Ramp;

        foreach (UiRow row in rows)
        {
            switch (row)
            {
                case HeadingRow heading:
                    target.Children.Add(Ui.Heading(heading.Text, palette, ramp));
                    break;

                case TitleRow title:
                    target.Children.Add(Ui.Text(
                        title.Text, ramp.Heading, palette.Text, FontWeight.Bold));
                    break;

                case CheckRow checkRow:
                    target.Children.Add(Check(checkRow, shell, palette, ramp));
                    break;

                case DialRow dialRow:
                {
                    var dial = new DialView(
                        dialRow.Key,
                        dialRow.Caption,
                        dialRow.Value,
                        dialRow.Minimum,
                        dialRow.Maximum,
                        palette,
                        ramp);
                    dial.ValueChanged += (key, value) => shell.SetDial(key, value, save: false);
                    dial.ValueCommitted += (key, value) => shell.SetDial(key, value, save: true);
                    dials?.Add(dial);
                    target.Children.Add(dial);
                    break;
                }

                case RadioRow radioRow:
                    target.Children.Add(Radios(radioRow, shell, palette, ramp));
                    break;

                case NavRow nav:
                {
                    Border button = Ui.NavRow(
                        nav.Icon,
                        nav.Label,
                        palette,
                        ramp,
                        () => Ui.Act(() => dispatch(nav.Action)),
                        nav.Tooltip);
                    if (nav.TopMargin > 0)
                    {
                        button.Margin = new Thickness(0, nav.TopMargin, 0, 0);
                    }
                    target.Children.Add(button);
                    break;
                }

                case ActionRow action:
                    target.Children.Add(Ui.SidebarButton(
                        action.Label,
                        palette,
                        ramp.Body,
                        () => Ui.Act(() => dispatch(action.Action)),
                        action.Enabled));
                    break;

                case TextRow text:
                    target.Children.Add(Muted(text, palette, ramp));
                    break;

                case SeparatorRow:
                    target.Children.Add(Ui.Separator(palette));
                    break;
            }
        }
    }

    private static Control Check(CheckRow row, ShellModel shell, Palette palette, TypeRamp ramp)
    {
        var check = new CheckBox
        {
            IsChecked = row.IsChecked,
            Content = Ui.Text(Ui.Thai(row.Label), ramp.Body, palette.Muted, wrap: true),
            FontSize = ramp.Body,
            Foreground = Ui.Brush(palette.Muted),
            Margin = new Thickness(row.Indented ? 20 : 0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        check.Classes.Add("nodrag");
        Ui.Tip(check, row.Tooltip);
        check.IsCheckedChanged += (_, _) =>
        {
            bool value = check.IsChecked == true;
            Ui.Act(() => shell.SetBool(row.Key, value));
        };
        return check;
    }

    private static Control Radios(RadioRow row, ShellModel shell, Palette palette, TypeRamp ramp)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        foreach (RadioOption option in row.Options)
        {
            var radio = new RadioButton
            {
                GroupName = row.Key,
                IsChecked = option.Selected,
                Content = Ui.Text(option.Label, ramp.Body, palette.Muted),
                FontSize = ramp.Body,
                Foreground = Ui.Brush(palette.Muted),
                Margin = new Thickness(0, 0, 10, 0),
            };
            radio.Classes.Add("nodrag");
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                {
                    Ui.Act(() => shell.SetChoice(row.Key, option.Value));
                }
            };
            panel.Children.Add(radio);
        }
        return panel;
    }

    private static TextBlock Muted(TextRow row, Palette palette, TypeRamp ramp)
    {
        TextBlock block = Ui.Paragraph(row.Text, palette, ramp, row.Muted);
        Ui.Tip(block, row.Tooltip);
        return block;
    }
}
