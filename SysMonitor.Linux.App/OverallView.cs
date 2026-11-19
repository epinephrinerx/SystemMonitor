using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// The Overall view (spec section 4): a 196 px sidebar of settings and a
/// scrolling column of section cards. The whole view is scaled by
/// min(panel/630, panel/480); the font scale is applied on top by the window.
/// </summary>
internal sealed class OverallView : UserControl
{
    private readonly ShellModel _shell;
    private readonly Action<string> _dispatch;
    private readonly Grid _content;
    private readonly Border _sidebarPanel;
    private readonly StackPanel _sidebar;
    private readonly StackPanel _cards;
    private readonly TextBlock _legend;
    private readonly Shape _legendGlyph;
    private readonly LayoutTransformControl _scale;
    private readonly List<DialView> _dials = new();
    private double _factor = 1;

    public OverallView(ShellModel shell, Action<string> dispatch)
    {
        _shell = shell;
        _dispatch = dispatch;

        _sidebar = new StackPanel { Margin = new Thickness(16) };
        var sidebarScroll = new ScrollViewer
        {
            Content = _sidebar,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
        };
        _sidebarPanel = new Border
        {
            CornerRadius = new CornerRadius(14, 0, 0, 14),
            Child = sidebarScroll,
        };

        _cards = new StackPanel();
        var cardScroll = new ScrollViewer
        {
            Content = _cards,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
        };

        _legend = Ui.Text(string.Empty, shell.Ramp.Small, shell.Palette.Label, wrap: true);
        _legendGlyph = Ui.Thermometer(9, 11, shell.Palette.Label);
        _legendGlyph.Margin = new Thickness(0, 2, 4, 0);
        _legendGlyph.VerticalAlignment = VerticalAlignment.Top;

        // The WPF legend line opens with the same thermometer the badges use;
        // the glyph sits outside the string so the text keeps its leading "=".
        var legendRow = new Grid
        {
            Margin = new Thickness(0, 6, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        legendRow.Children.Add(_legendGlyph);
        Grid.SetColumn(_legend, 1);
        legendRow.Children.Add(_legend);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        header.Children.Add(Ui.IconButton(
            IconName.FullScreen, 26, shell.Ramp.Body, Palette.IconButton,
            () => _shell.OverallHeaderFull(), shell.Language["full_data"]));
        header.Children.Add(Ui.IconButton(
            IconName.Widget, 26, shell.Ramp.Body, Palette.IconButton,
            () => _shell.OverallHeaderWidget(), shell.Language["open_widget"]));
        header.Children.Add(Ui.IconButton(
            IconName.Close, 26, shell.Ramp.Body, Palette.IconButton,
            () => _shell.Close(), shell.Language["close"]));

        var main = new Grid
        {
            Margin = new Thickness(16, 14, 14, 12),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        main.Children.Add(header);

        Grid.SetRow(cardScroll, 1);
        cardScroll.Margin = new Thickness(0, 8, 0, 0);
        main.Children.Add(cardScroll);

        Grid.SetRow(legendRow, 2);
        main.Children.Add(legendRow);

        var contentGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(196, GridUnitType.Pixel),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        contentGrid.Children.Add(_sidebarPanel);
        Grid.SetColumn(main, 1);
        contentGrid.Children.Add(main);
        _content = contentGrid;

        _scale = new LayoutTransformControl
        {
            Child = contentGrid,
            LayoutTransform = new ScaleTransform(1, 1),
        };
        Content = _scale;
    }

    /// <summary>Rebuild the sidebar. Called when the language or a setting changed.</summary>
    public void RenderSidebar()
    {
        _sidebar.Children.Clear();
        _dials.Clear();
        RowRenderer.Fill(_sidebar, _shell.Sidebar, _shell, _dispatch, _dials);
        _sidebarPanel.Background = Ui.Brush(
            _shell.Palette.SidebarBrush(_shell.Config.Opacity));
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
        _legendGlyph.Fill = Ui.Brush(palette.Label);
    }

    /// <summary>Update the sidebar background after an opacity change.</summary>
    public void SetOpacity(double opacity) =>
        _sidebarPanel.Background = Ui.Brush(_shell.Palette.SidebarBrush(opacity));

    /// <summary>Content scale: min(panelW/630, panelH/480).</summary>
    public void SetScale(double scale)
    {
        _factor = Math.Max(0.01, scale);
        _scale.LayoutTransform = new ScaleTransform(_factor, _factor);
        InvalidateMeasure();
    }

    public IReadOnlyList<DialView> Dials => _dials;

    /// <summary>
    /// The natural size of the view at scale 1, for the auto-fit on first entry
    /// to Overall. Width is measured against the work area so an unconstrained
    /// measure cannot ask for a window wider than the screen.
    /// </summary>
    public Size MeasureNatural(double maxWidth, double maxHeight)
    {
        _content.Measure(new Size(maxWidth, maxHeight));
        Size desired = _content.DesiredSize;
        _content.InvalidateMeasure();
        return desired;
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
