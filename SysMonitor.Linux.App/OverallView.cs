using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// Main's Overall page (R-030 §2): the section cards and the legend, without
/// the sidebar the old Overall view had. The sidebar (navigation, Settings and
/// the module list) now lives in <see cref="MainView"/>.
///
/// The content is laid out at the type ramp's size, like the module pages; the
/// window's font scale still applies to it, as it does everywhere else.
/// </summary>
internal sealed class OverallView : UserControl
{
    private readonly ShellModel _shell;
    private readonly Action<string> _dispatch;
    private readonly StackPanel _cards;
    private readonly TextBlock _legend;

    public OverallView(ShellModel shell, Action<string> dispatch)
    {
        _shell = shell;
        _dispatch = dispatch;

        _cards = new StackPanel();
        var cardScroll = new ScrollViewer
        {
            Content = _cards,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
        };

        _legend = Ui.Text(string.Empty, shell.Ramp.Small, shell.Palette.Label, wrap: true);
        _legend.Margin = new Thickness(0, 6, 0, 0);

        var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        content.Children.Add(cardScroll);
        Grid.SetRow(_legend, 1);
        content.Children.Add(_legend);
        Content = content;
    }

    /// <summary>Rebuild the cards and the legend. Called on every fresh snapshot.</summary>
    public void RenderCards()
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;
        _cards.Children.Clear();

        OverallModel model = _shell.Overall;
        if (model.Message.Length > 0)
        {
            _cards.Children.Add(new Border
            {
                Background = Ui.Brush(palette.Control),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 16, 12, 16),
                Margin = new Thickness(0, 0, 0, 10),
                Child = Ui.Text(
                    Ui.Thai(model.Message), ramp.Body, palette.Label,
                    wrap: true, align: TextAlignment.Center),
            });
        }

        foreach (SectionModel section in model.Sections)
        {
            _cards.Children.Add(Card(section, palette, ramp));
        }

        _legend.Text = Ui.Thai(model.Legend);
        _legend.Foreground = Ui.Brush(palette.Label);
        _legend.FontSize = ramp.Small;
    }

    private Control Card(SectionModel section, Palette palette, TypeRamp ramp)
    {
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        header.Children.Add(Ui.Text(
            Ui.Thai(section.Title), ramp.Label, palette.Label,
            FontWeight.SemiBold, wrap: true));
        if (section.Note.Length > 0)
        {
            TextBlock note = Ui.Text(Ui.Thai(section.Note), ramp.Small, palette.Label);
            note.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(note, 1);
            header.Children.Add(note);
        }

        var rows = new UniformGrid
        {
            Columns = Math.Max(1, section.Columns),
            ColumnSpacing = 14,
            Margin = new Thickness(0, 4, 0, 0),
        };
        foreach (MeterRow row in section.Rows)
        {
            rows.Children.Add(Rows.Build(
                row,
                palette,
                ramp,
                compact: false,
                lang: _shell.Language,
                openDrive: drive => DiskTools.OpenMount(drive),
                driveMenu: OpenDriveMenu,
                openCleanup: drive =>
                {
                    DiskTools.OpenDiskUsage(drive);
                    _shell.DismissAlert(drive);
                },
                dismissAlert: letter => _shell.DismissAlert(letter)));
        }

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(rows);

        return new Border
        {
            Background = Ui.Brush(palette.Control),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = body,
        };
    }

    /// <summary>The drive menu (spec 4b) with its Linux tools.</summary>
    private void OpenDriveMenu(string mount, ContextMenu menu)
    {
        menu.Items.Clear();
        menu.Items.Add(DriveItem(
            _shell.Language.Get("disk_open_explorer", mount),
            () => DiskTools.OpenMount(mount)));
        if (ToolFinder.DiskUsageTool() is not null)
        {
            menu.Items.Add(DriveItem(
                _shell.Language.Get("disk_cleanup", mount),
                () => DiskTools.OpenDiskUsage(mount)));
        }
        if (ToolFinder.DiskManagerTool() is not null)
        {
            menu.Items.Add(DriveItem(
                _shell.Language["disk_manager"],
                () => DiskTools.OpenDiskManager()));
        }
        menu.Items.Add(new Separator());
        foreach (UiRow row in _shell.ContextMenu)
        {
            menu.Items.Add(row is ActionRow action
                ? DriveItem(action.Label, () => _dispatch(action.Action))
                : new Separator());
        }
    }

    private static MenuItem DriveItem(string label, Action action)
    {
        var item = new MenuItem { Header = Ui.Thai(label) };
        item.Click += (_, _) => action();
        return item;
    }
}
