using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.App;

/// <summary>
/// The main window (R-030 §2): a sidebar on the left and the selected page on
/// the right. The sidebar holds the three fixed pages, the Restart, Manual and
/// About actions (R-030 fix 5), the Settings expander (today's Overall sidebar
/// rows) and one item per module with its value, its device name and a
/// sparkline. The right frame shows the Overall cards, a module page, the
/// Processes page or the Apps page.
///
/// The window supplies placement and input; this file draws and wires, and
/// keeps a control per key so a data tick moves numbers without rebuilding.
/// </summary>
internal sealed class MainView : UserControl
{
    private const double SplitterWidth = 4;

    /// <summary>The sidebar rows' hover tint: 13 % grey over the panel.</summary>
    private static readonly Colour RowHover = Colour.FromHex("#22808080");

    private readonly ShellModel _shell;
    private readonly Action<string> _dispatch;

    private readonly ColumnDefinition _sidebarColumn =
        new(MainState.DefaultSidebarWidth, GridUnitType.Pixel);
    private readonly Border _sidebarPanel;
    private readonly StackPanel _sidebarList;
    private readonly Border _splitter;
    private readonly StackPanel _headerButtons;
    private readonly TextBlock _title;

    private readonly OverallView _overallPage;
    private readonly ModulePageView _modulePage;
    private readonly ProcessesView _processesPage;
    private readonly AppsView _appsView;
    private readonly Grid _pageHost;

    private readonly Dictionary<string, ModuleItem> _modules = new(StringComparer.Ordinal);
    private readonly List<DialView> _dials = new();
    private readonly List<Border> _navItems = new();

    private string _renderedPage = string.Empty;
    private string? _renderedModuleKey;
    private bool _draggingSplitter;
    private double _dragStartX;
    private double _dragStartWidth;

    public MainView(ShellModel shell, Action<string> dispatch)
    {
        _shell = shell;
        _dispatch = dispatch;

        // Sidebar: its own scroll, the same look the old Overall sidebar had.
        _sidebarList = new StackPanel { Margin = new Thickness(10, 12, 10, 12) };
        var sidebarScroll = new ScrollViewer
        {
            Content = _sidebarList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
        };
        _sidebarPanel = new Border
        {
            Background = Ui.Brush(shell.Palette.SidebarBrush(shell.Config.Opacity)),
            Child = sidebarScroll,
        };

        // Splitter: drag to resize, 200-320 DIPs, saved on release.
        _splitter = new Border
        {
            Width = SplitterWidth,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        _splitter.Classes.Add("nodrag");
        _splitter.PointerEntered += (_, _) => _splitter.Background = Ui.Brush(Palette.Hover);
        _splitter.PointerExited += (_, _) => _splitter.Background = Brushes.Transparent;
        _splitter.PointerPressed += OnSplitterPressed;
        _splitter.PointerMoved += OnSplitterMoved;
        _splitter.PointerReleased += OnSplitterReleased;

        // Right frame: header, then the page for the selection.
        _title = Ui.Text(string.Empty, shell.Ramp.Heading, shell.Palette.Text, FontWeight.Bold);
        _title.VerticalAlignment = VerticalAlignment.Center;
        _headerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        header.Children.Add(_title);
        Grid.SetColumn(_headerButtons, 1);
        header.Children.Add(_headerButtons);

        _overallPage = new OverallView(shell, dispatch);
        _modulePage = new ModulePageView(shell, dispatch);
        _processesPage = new ProcessesView(shell);
        _appsView = new AppsView(shell);

        _pageHost = new Grid();
        _pageHost.Children.Add(_overallPage);
        _pageHost.Children.Add(_modulePage);
        _pageHost.Children.Add(_processesPage);
        _pageHost.Children.Add(_appsView);

        var frame = new Grid
        {
            Margin = new Thickness(14, 12, 14, 12),
            RowDefinitions = new RowDefinitions("Auto,*"),
        };
        frame.Children.Add(header);
        Grid.SetRow(_pageHost, 1);
        frame.Children.Add(_pageHost);

        var root = new Grid();
        root.ColumnDefinitions.Add(_sidebarColumn);
        root.ColumnDefinitions.Add(new ColumnDefinition(SplitterWidth, GridUnitType.Pixel));
        root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        root.Children.Add(_sidebarPanel);
        Grid.SetColumn(_splitter, 1);
        root.Children.Add(_splitter);
        Grid.SetColumn(frame, 2);
        root.Children.Add(frame);
        Content = root;

        Render();
    }

    /// <summary>How many nav rows the sidebar shows (Overall, Apps, Processes).</summary>
    public int NavCount => _navItems.Count;

    /// <summary>How many module items the sidebar shows.</summary>
    public int ModuleItemCount => _modules.Count;

    /// <summary>The Settings expander's state, as the view drew it.</summary>
    public bool SettingsOpen => _shell.Main.SettingsOpen;

    /// <summary>The dials inside the Settings expander, for the appearance sync.</summary>
    public IReadOnlyList<DialView> Dials => _dials;

    /// <summary>A structural render: language, palette, settings or selection.</summary>
    public void Render()
    {
        _sidebarColumn.Width = new GridLength(_shell.Main.SidebarWidth);
        _sidebarPanel.Background = Ui.Brush(
            _shell.Palette.SidebarBrush(_shell.Config.Opacity));
        _title.Text = Ui.Thai(_shell.Language["title"]);

        RenderHeader();
        RenderSidebar();
        ShowPage();
    }

    /// <summary>A data tick: values and sparklines in place, keyed by page.</summary>
    public void RenderData()
    {
        if (!StructureMatches())
        {
            Render();
            return;
        }

        foreach (SidebarItem item in _shell.MainSidebar)
        {
            if (_modules.TryGetValue(item.Key, out ModuleItem? control))
            {
                control.Update(item, _shell);
            }
        }

        string page = _shell.Main.Page;
        if (string.Equals(page, PageKeys.Overall, StringComparison.Ordinal))
        {
            _overallPage.RenderCards();
        }
        else if (string.Equals(page, PageKeys.Processes, StringComparison.Ordinal))
        {
            _processesPage.RenderData();
        }
        else if (string.Equals(page, PageKeys.Apps, StringComparison.Ordinal))
        {
            _appsView.RenderData();
        }
        else
        {
            _modulePage.UpdateData(_shell.Full.Selected);
        }
    }

    /// <summary>Opacity only changes the sidebar's background (spec section 1).</summary>
    public void SetOpacity(double opacity) =>
        _sidebarPanel.Background = Ui.Brush(_shell.Palette.SidebarBrush(opacity));

    /// <summary>
    /// Amendment 1: the font dial fires AppearanceChanged without a rebuild,
    /// so the current module page's small cards re-size in place to match.
    /// </summary>
    public void RescaleCards() => _modulePage.RescaleCards();

    // ------------------------------------------------------------- header

    private void RenderHeader()
    {
        _headerButtons.Children.Clear();
        _headerButtons.Children.Add(Ui.IconButton(
            IconName.Widget, 26, _shell.Ramp.Body, Palette.IconButton,
            () => _shell.ShowWidget(), _shell.Language["open_widget"]));
        _headerButtons.Children.Add(Ui.IconButton(
            IconName.FullScreen, 26, _shell.Ramp.Body, Palette.IconButton,
            () => _shell.ToggleFullScreen(), _shell.Language[_shell.View.FullScreenItemKey]));
        _headerButtons.Children.Add(Ui.IconButton(
            IconName.Close, 26, _shell.Ramp.Body, Palette.IconButton,
            () => _shell.Close(), _shell.Language["close"]));
    }

    // ------------------------------------------------------------ sidebar

    private void RenderSidebar()
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;
        string page = _shell.Main.Page;

        _sidebarList.Children.Clear();
        _dials.Clear();
        _modules.Clear();
        _navItems.Clear();

        AddNav(IconName.Home, _shell.Language["overall"], PageKeys.Overall, page);
        AddNav(IconName.Widget, _shell.Language["apps"], PageKeys.Apps, page);
        AddNav(IconName.FullData, _shell.Language["processes"], PageKeys.Processes, page);

        // Restart, Manual and About are actions, not pages: they sit in the nav
        // list (the user's order: under Processes, before Settings) but never
        // take the selected highlight and never change Main's page (fix 5).
        AddAction(IconName.Restart, _shell.Language["restart"], ActionKeys.Restart);
        AddAction(IconName.Manual, _shell.Language["manual"], ActionKeys.OpenManual);
        AddAction(IconName.About, _shell.Language["about"], ActionKeys.OpenAbout);

        _sidebarList.Children.Add(new Border { Height = 6 });
        _sidebarList.Children.Add(SettingsHeader(palette, ramp));

        if (_shell.Main.SettingsOpen)
        {
            var body = new StackPanel { Margin = new Thickness(2, 2, 0, 6) };
            RowRenderer.Fill(body, _shell.Sidebar, _shell, _dispatch, _dials);
            _sidebarList.Children.Add(body);
        }

        _sidebarList.Children.Add(Ui.Separator(palette));

        foreach (SidebarItem item in _shell.MainSidebar)
        {
            var control = new ModuleItem(_shell, item, () => _shell.SelectMainPage(item.Key));
            _modules[item.Key] = control;
            _sidebarList.Children.Add(control.Control);
        }
    }

    private void AddNav(IconName icon, string label, string key, string page)
    {
        bool selected = string.Equals(key, page, StringComparison.Ordinal);
        Border control = NavRowControl(icon, label, selected);

        // Hover and selection are read from the model when the pointer moves,
        // never from the value captured at build time: a row that became the
        // selected one after the sidebar was built must not keep a hover tint,
        // and one that stopped being selected must not keep the selection blue.
        control.PointerEntered += (_, _) =>
        {
            if (!IsSelected(key))
            {
                control.Background = Ui.Brush(RowHover);
            }
        };
        control.PointerExited += (_, _) =>
            control.Background = IsSelected(key)
                ? Ui.Brush(Palette.Selection)
                : Brushes.Transparent;
        control.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            Ui.Act(() => _shell.SelectMainPage(key));
        };

        _navItems.Add(control);
        _sidebarList.Children.Add(control);
    }

    /// <summary>
    /// One action row in the nav list: same look as a page row, but no
    /// selection — it runs the same action the Settings expander used to
    /// (R-030 fix 5). Restart, Manual and About are the three callers.
    /// </summary>
    private void AddAction(IconName icon, string label, string action)
    {
        Border control = NavRowControl(icon, label, selected: false);
        control.PointerEntered += (_, _) => control.Background = Ui.Brush(RowHover);
        control.PointerExited += (_, _) => control.Background = Brushes.Transparent;
        control.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            Ui.Act(() => _dispatch(action));
        };

        _sidebarList.Children.Add(control);
    }

    /// <summary>The shared nav row: icon plus label, tinted when selected.</summary>
    private Border NavRowControl(IconName icon, string label, bool selected)
    {
        var control = new Border
        {
            Padding = new Thickness(6, 5, 6, 5),
            CornerRadius = new CornerRadius(7),
            Background = selected ? Ui.Brush(Palette.Selection) : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        control.Classes.Add("nodrag");

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        Colour foreground = selected ? Colour.FromHex("#ffffff") : _shell.Palette.Text;
        Control glyph = Ui.Icon(icon, _shell.Ramp.NavIcon, foreground);
        glyph.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(glyph);
        TextBlock text = Ui.Text(
            Ui.Thai(label), _shell.Ramp.Nav, foreground, FontWeight.SemiBold, ellipsis: true);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        control.Child = row;
        return control;
    }

    /// <summary>True while <paramref name="key"/> is the page Main is showing.</summary>
    private bool IsSelected(string key) =>
        string.Equals(key, _shell.Main.Page, StringComparison.Ordinal);

    /// <summary>The Settings expander: a row with a chevron and the remembered state.</summary>
    private Control SettingsHeader(Palette palette, TypeRamp ramp)
    {
        bool open = _shell.Main.SettingsOpen;
        var control = new Border
        {
            Padding = new Thickness(6, 5, 6, 5),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        control.Classes.Add("nodrag");

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        Control glyph = Ui.Icon(IconName.Settings, ramp.NavIcon, palette.Label);
        glyph.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(glyph);

        TextBlock text = Ui.Text(
            Ui.Thai(_shell.Language["settings"]), ramp.Nav, palette.Text,
            FontWeight.SemiBold, ellipsis: true);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        Control chevron = Ui.Icon(
            open ? IconName.ChevronDown : IconName.ChevronRight,
            ramp.Tiny,
            palette.Label);
        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);
        control.Child = row;

        control.PointerEntered += (_, _) =>
            control.Background = Ui.Brush(RowHover);
        control.PointerExited += (_, _) => control.Background = Brushes.Transparent;
        control.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            Ui.Act(() => _shell.SetSettingsOpen(!_shell.Main.SettingsOpen));
        };
        return control;
    }

    // ------------------------------------------------------------- pages

    private void ShowPage()
    {
        string page = _shell.Main.Page;
        bool overall = string.Equals(page, PageKeys.Overall, StringComparison.Ordinal);
        bool apps = string.Equals(page, PageKeys.Apps, StringComparison.Ordinal);
        bool processes = string.Equals(page, PageKeys.Processes, StringComparison.Ordinal);
        FullTab? tab = overall || apps || processes ? null : _shell.Full.Selected;

        _overallPage.IsVisible = overall;
        _appsView.IsVisible = apps;
        _processesPage.IsVisible = processes;
        _modulePage.IsVisible = !overall && !apps && !processes;

        if (overall)
        {
            _overallPage.RenderCards();
        }
        else if (apps)
        {
            _appsView.Render();
        }
        else if (processes)
        {
            _processesPage.Render();
        }
        else
        {
            _modulePage.Render(tab);
        }

        _renderedPage = page;
        _renderedModuleKey = tab?.Key;
    }

    /// <summary>True while the sidebar and the page still match the model.</summary>
    private bool StructureMatches()
    {
        if (!string.Equals(_shell.Main.Page, _renderedPage, StringComparison.Ordinal))
        {
            return false;
        }

        if (_modules.Count != _shell.MainSidebar.Count)
        {
            return false;
        }
        foreach (SidebarItem item in _shell.MainSidebar)
        {
            if (!_modules.ContainsKey(item.Key))
            {
                return false;
            }
        }

        string page = _shell.Main.Page;
        bool fixedPage = string.Equals(page, PageKeys.Overall, StringComparison.Ordinal)
                         || string.Equals(page, PageKeys.Apps, StringComparison.Ordinal)
                         || string.Equals(page, PageKeys.Processes, StringComparison.Ordinal);
        string? moduleKey = fixedPage ? null : _shell.Full.Selected?.Key;
        return string.Equals(moduleKey, _renderedModuleKey, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------- splitter

    private void OnSplitterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_splitter).Properties.IsLeftButtonPressed)
        {
            return;
        }
        e.Handled = true;
        _draggingSplitter = true;
        _dragStartX = e.GetPosition(this).X;
        _dragStartWidth = _sidebarColumn.Width.Value;
        e.Pointer.Capture(_splitter);
    }

    private void OnSplitterMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingSplitter)
        {
            return;
        }
        e.Handled = true;
        double width = _dragStartWidth + (e.GetPosition(this).X - _dragStartX);
        _sidebarColumn.Width = new GridLength(
            Math.Clamp(width, MainState.MinSidebarWidth, MainState.MaxSidebarWidth));
    }

    private void OnSplitterReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_draggingSplitter)
        {
            return;
        }
        _draggingSplitter = false;
        e.Pointer.Capture(null);
        // Saved once, on release: a live drag must not rebuild the sidebar.
        _shell.SetSidebarWidth((int)Math.Round(_sidebarColumn.Width.Value));
    }

    // ------------------------------------------------------- module item

    /// <summary>
    /// One sidebar module: title and value on the first line, the device name
    /// under it and its sparkline. Updated in place by key.
    /// </summary>
    private sealed class ModuleItem
    {
        private readonly TextBlock _title;
        private readonly TextBlock _value;
        private readonly TextBlock _device;
        private readonly Sparkline? _sparkline;
        private readonly Border _border;
        private readonly Border _badgeSlot;
        private bool _selected;

        public ModuleItem(ShellModel shell, SidebarItem item, Action onClick)
        {
            TypeRamp ramp = shell.Ramp;
            Palette palette = shell.Palette;

            var top = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            _title = Ui.Text(
                Ui.Thai(item.Title), ramp.Small, palette.Text, FontWeight.SemiBold, ellipsis: true);
            _title.Margin = new Thickness(0, 0, 8, 0);
            top.Children.Add(_title);
            _value = Ui.Text(
                item.Value, ramp.Small, palette.Text, FontWeight.SemiBold, ellipsis: true);
            // The value and its badge share the right-hand column, so the
            // battery's power icon sits beside "100%" (R-030 fix 4).
            var valueRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            valueRow.Children.Add(_value);
            _badgeSlot = new Border { VerticalAlignment = VerticalAlignment.Center };
            valueRow.Children.Add(_badgeSlot);
            Grid.SetColumn(valueRow, 1);
            top.Children.Add(valueRow);

            _device = Ui.Text(Ui.Thai(item.Device), ramp.Tiny, palette.Label, ellipsis: true);
            _sparkline = item.ShowSparkline
                ? new Sparkline(item.Primary, item.Secondary, item.Accent, palette)
                {
                    Height = 28,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 3, 0, 0),
                }
                : null;

            var body = new StackPanel();
            body.Children.Add(top);
            body.Children.Add(_device);
            if (_sparkline is not null)
            {
                body.Children.Add(_sparkline);
            }

            _border = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(6, 5, 6, 4),
                Margin = new Thickness(0, 0, 0, 3),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = body,
            };
            _border.Classes.Add("nodrag");
            _border.PointerEntered += (_, _) =>
            {
                if (!_selected)
                {
                    _border.Background = Ui.Brush(RowHover);
                }
            };
            _border.PointerExited += (_, _) =>
                _border.Background = _selected
                    ? Ui.Brush(Palette.Selection)
                    : Brushes.Transparent;
            _border.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(_border).Properties.IsLeftButtonPressed)
                {
                    return;
                }
                e.Handled = true;
                onClick();
            };

            Update(item, shell);
        }

        public Control Control => _border;

        public void Update(SidebarItem item, ShellModel shell)
        {
            bool selected = string.Equals(item.Key, shell.Main.Page, StringComparison.Ordinal);
            Palette palette = shell.Palette;

            _title.Text = Ui.Thai(item.Title);
            _value.Text = Ui.Thai(item.Value);
            _device.Text = Ui.Thai(item.Device);
            Ui.FillBadge(_badgeSlot, item.Badge, shell.Ramp);
            _sparkline?.SetSeries(item.Primary, item.Secondary);

            // Only a change of selection repaints the row: a data tick must not
            // wipe the hover tint while the pointer is still on the item.
            if (selected != _selected)
            {
                _selected = selected;
                Colour foreground = selected ? Colour.FromHex("#ffffff") : palette.Text;
                _title.Foreground = Ui.Brush(foreground);
                _value.Foreground = Ui.Brush(foreground);
                _device.Foreground = Ui.Brush(selected ? Colour.FromHex("#e2e8f0") : palette.Label);
                _border.Background = selected ? Ui.Brush(Palette.Selection) : Brushes.Transparent;
            }
        }
    }

    /// <summary>
    /// The 72-point sparkline: the module's primary series filled, the optional
    /// second series (swap under RAM, upload under download) drawn lighter on
    /// the same plot. No grid and no frame, as §2 asks; the geometry comes from
    /// <see cref="ChartMath"/>.
    /// </summary>
    private sealed class Sparkline : Control
    {
        private readonly double[] _values = new double[ChartMath.Points];
        private readonly double[] _secondaryValues = new double[ChartMath.Points];
        private readonly IPen _linePen;
        private readonly IPen _secondaryPen;
        private readonly IBrush _fillBrush;

        private History _primary;
        private History? _secondary;

        public Sparkline(History primary, History? secondary, Colour accent, Palette palette)
        {
            _primary = primary;
            _secondary = secondary;
            _linePen = new Pen(Ui.Brush(accent), ChartMath.StrokeThickness)
            {
                LineJoin = PenLineJoin.Miter,
            };
            _secondaryPen = new Pen(Ui.Brush(accent.WithAlpha(0x88)), ChartMath.StrokeThickness)
            {
                LineJoin = PenLineJoin.Miter,
            };
            _fillBrush = Ui.Brush(ChartMath.FillColour(accent, palette.Plot));
        }

        public void SetSeries(History primary, History? secondary)
        {
            _primary = primary;
            _secondary = secondary;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            double width = Bounds.Width;
            double height = Bounds.Height;
            if (width < 2 || height < 2)
            {
                return;
            }

            var plot = new ChartRect(0, 0, width, height);
            if (_secondary is { } secondary)
            {
                DrawSeries(context, secondary, _secondaryValues, plot, filled: false, _secondaryPen);
            }
            DrawSeries(context, _primary, _values, plot, filled: true, _linePen);
        }

        private void DrawSeries(
            DrawingContext context,
            History history,
            double[] buffer,
            ChartRect plot,
            bool filled,
            IPen pen)
        {
            int count = history.CopyTo(buffer);
            if (count < 2)
            {
                return;
            }

            double scale = ChartMath.Scale(history.Max, 0);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext path = geometry.Open())
            {
                if (filled)
                {
                    path.BeginFigure(new Point(plot.X, plot.Bottom), isFilled: true);
                }
                else
                {
                    ChartPoint first = ChartMath.Point(0, count, buffer[0], scale, plot);
                    path.BeginFigure(new Point(first.X, first.Y), isFilled: false);
                }

                for (int i = filled ? 0 : 1; i < count; i++)
                {
                    ChartPoint point = ChartMath.Point(i, count, buffer[i], scale, plot);
                    path.LineTo(new Point(point.X, point.Y));
                }

                if (filled)
                {
                    path.LineTo(new Point(plot.Right, plot.Bottom));
                }
                path.EndFigure(isClosed: filled);
            }
            context.DrawGeometry(filled ? _fillBrush : null, pen, geometry);
        }
    }
}
