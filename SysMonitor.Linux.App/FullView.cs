using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.App;

/// <summary>
/// The Full view (spec sections 5, 5a, 5b, 5c): the header, the tab rail, the
/// detail pane with facts and chart cards, and on the RAM tab the process and
/// service lists.
///
/// Every value comes from <see cref="ShellModel"/>; this file draws and wires.
/// The tab buttons are reconciled by key, and the memory section is a permanent
/// control whose group rows are updated in place, so a chart, an expanded group
/// or the search box is not thrown away by the 2.5 s redraw.
/// </summary>
internal sealed class FullView : UserControl
{
    private const string GraphBackground = "#223b82f6";

    private readonly ShellModel _shell;
    private readonly Action<string> _dispatch;

    private readonly Grid _root;
    private readonly Grid _header;
    private readonly Grid _rail;
    private readonly Grid _nav;
    private readonly ScrollViewer _railScroll;
    private readonly StackPanel _tabList;
    private readonly Dictionary<string, TabButton> _tabButtons = new();
    private readonly ScrollViewer _detailScroll;
    private readonly StackPanel _detail;
    private readonly TextBlock _detailTitle;
    private readonly TextBlock _detailHardware;
    private readonly Border _factsCard;
    private readonly WrapPanel _facts;
    private readonly StackPanel _bigCards;
    private readonly WrapPanel _smallCards;

    private readonly StackPanel _memorySection;
    private readonly TextBlock _processHeading;
    private readonly TextBox _search;
    private readonly TextBlock _status;
    private readonly TextBlock _groupNote;
    private readonly StackPanel _processList;
    private readonly TextBlock _serviceHeading;
    private readonly TextBlock _serviceNote;
    private readonly TextBlock _userHeading;
    private readonly StackPanel _userList;
    private readonly TextBlock _systemHeading;
    private readonly StackPanel _systemList;
    private readonly Dictionary<string, GroupRow> _groupRows = new();
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServiceRow> _userRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ServiceRow> _systemRows = new(StringComparer.Ordinal);

    private string _searchText = string.Empty;

    public FullView(ShellModel shell, Action<string> dispatch)
    {
        _shell = shell;
        _dispatch = dispatch;

        // Header.
        _header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        // Rail.
        _nav = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
            Margin = new Thickness(0, 0, 0, 8),
        };

        _tabList = new StackPanel { MinWidth = 128 };
        _railScroll = new ScrollViewer
        {
            Content = _tabList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
        };
        _rail = new Grid
        {
            Margin = new Thickness(0, 0, 12, 0),
            RowDefinitions = new RowDefinitions("Auto,*"),
        };
        _rail.Children.Add(_nav);
        Grid.SetRow(_railScroll, 1);
        _rail.Children.Add(_railScroll);

        // Detail heading.
        _detailTitle = Ui.Text(string.Empty, shell.Ramp.Heading, shell.Palette.Text, FontWeight.Bold);
        _detailHardware = Ui.Text(
            string.Empty, shell.Ramp.Body, shell.Palette.Label, ellipsis: true);
        _detailHardware.MaxWidth = 360;
        _detailHardware.HorizontalAlignment = HorizontalAlignment.Right;
        _detailHardware.VerticalAlignment = VerticalAlignment.Bottom;

        var headingRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
            Margin = new Thickness(0, 0, 0, 8),
        };
        headingRow.Children.Add(_detailTitle);
        Grid.SetColumn(_detailHardware, 1);
        headingRow.Children.Add(_detailHardware);

        // Facts card.
        _facts = new WrapPanel();
        _factsCard = new Border
        {
            Background = Ui.Brush(shell.Palette.Control),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 4),
            Margin = new Thickness(0, 0, 0, 10),
            Child = _facts,
            IsVisible = false,
        };

        _bigCards = new StackPanel();
        _smallCards = new WrapPanel();

        _detail = new StackPanel();
        _detail.Children.Add(headingRow);
        _detail.Children.Add(_factsCard);
        _detail.Children.Add(_bigCards);
        _detail.Children.Add(_smallCards);

        // RAM tab: processes and services, built once and kept alive.
        _processHeading = Ui.Text(
            string.Empty, shell.Ramp.Heading, shell.Palette.Text, FontWeight.SemiBold, wrap: true);
        _processHeading.Margin = new Thickness(2, 6, 2, 8);

        _search = new TextBox
        {
            Padding = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(0),
            Background = Ui.Brush(shell.Palette.Control),
            Foreground = Ui.Brush(shell.Palette.Text),
            CaretBrush = Ui.Brush(shell.Palette.Text),
            PlaceholderForeground = Ui.Brush(shell.Palette.Label),
            FontFamily = Ui.Family,
            FontSize = shell.Ramp.Body,
        };
        _search.TextChanged += (_, _) =>
        {
            string text = _search.Text ?? string.Empty;
            if (!string.Equals(text, _searchText, StringComparison.Ordinal))
            {
                _searchText = text;
                _shell.SetMemoryQuery(text);
            }
        };

        _status = Ui.Text(string.Empty, shell.Ramp.Small, shell.Palette.Label, wrap: true);
        _status.Margin = new Thickness(2, 4, 2, 0);

        _groupNote = Ui.Text(string.Empty, shell.Ramp.Small, shell.Palette.Label, wrap: true);
        _groupNote.Margin = new Thickness(2, 6, 2, 2);

        _processList = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        _serviceHeading = Ui.Text(
            string.Empty, shell.Ramp.Heading, shell.Palette.Text, FontWeight.SemiBold, wrap: true);
        _serviceHeading.Margin = new Thickness(2, 16, 2, 8);

        _serviceNote = Ui.Text(string.Empty, shell.Ramp.Small, shell.Palette.Label, wrap: true);
        _serviceNote.Margin = new Thickness(2, 0, 2, 4);

        _userHeading = Ui.Text(
            string.Empty, shell.Ramp.Small, shell.Palette.Label, FontWeight.SemiBold, wrap: true);
        _userHeading.Margin = new Thickness(2, 4, 2, 4);
        _userList = new StackPanel();

        _systemHeading = Ui.Text(
            string.Empty, shell.Ramp.Small, shell.Palette.Label, FontWeight.SemiBold, wrap: true);
        _systemHeading.Margin = new Thickness(2, 12, 2, 4);
        _systemList = new StackPanel();

        _memorySection = new StackPanel();
        _memorySection.Children.Add(_processHeading);
        _memorySection.Children.Add(_search);
        _memorySection.Children.Add(_status);
        _memorySection.Children.Add(_groupNote);
        _memorySection.Children.Add(_processList);
        _memorySection.Children.Add(_serviceHeading);
        _memorySection.Children.Add(_serviceNote);
        _memorySection.Children.Add(_userHeading);
        _memorySection.Children.Add(_userList);
        _memorySection.Children.Add(_systemHeading);
        _memorySection.Children.Add(_systemList);

        _detailScroll = new ScrollViewer
        {
            Content = _detail,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
        };

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        body.Children.Add(_rail);
        Grid.SetColumn(_detailScroll, 1);
        body.Children.Add(_detailScroll);

        _root = new Grid
        {
            Margin = new Thickness(14, 12, 14, 12),
            RowDefinitions = new RowDefinitions("Auto,*"),
        };
        _root.Children.Add(_header);
        Grid.SetRow(body, 1);
        _root.Children.Add(body);
        Content = _root;

        shell.MemoryChanged += OnMemoryChanged;
        Render();
    }

    /// <summary>Rebuild the frame in the current language and palette.</summary>
    public void Render()
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        _root.Margin = new Thickness(14, 12, 14, 12);

        BuildHeader(palette, ramp);
        BuildNav(palette, ramp);
        ApplyTextStyles(palette, ramp);
        SyncTabs();
        RenderDetail();
    }

    /// <summary>Update the rail figures and the detail content after a snapshot.</summary>
    public void RenderData()
    {
        SyncTabs();
        RenderDetail();
    }

    private void OnMemoryChanged() =>
        Dispatcher.UIThread.Post(() =>
        {
            _status.Text = Ui.Thai(_shell.MemoryStatus);
            _status.IsVisible = _status.Text.Length > 0;
            RenderMemory();
        });

    // ------------------------------------------------------------- header

    private void BuildHeader(Palette palette, TypeRamp ramp)
    {
        _header.Children.Clear();
        _header.Children.Add(Ui.Text(
            _shell.FullTitle, ramp.Heading, palette.Text, FontWeight.Bold));

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
        _header.Children.Add(buttons);
    }

    /// <summary>The nav pair pinned above the tab list (spec section 5).</summary>
    private void BuildNav(Palette palette, TypeRamp ramp)
    {
        _nav.Children.Clear();
        Border overall = Ui.IconButton(
            IconName.Home, 26, ramp.Body, Palette.IconButton,
            () => _shell.FullRailOverall(), _shell.Language["go_overall"]);
        overall.HorizontalAlignment = HorizontalAlignment.Center;
        _nav.Children.Add(overall);

        Border widget = Ui.IconButton(
            IconName.Widget, 26, ramp.Body, Palette.IconButton,
            () => _shell.FullRailWidget(), _shell.Language["open_widget"]);
        widget.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(widget, 1);
        _nav.Children.Add(widget);
    }

    private void ApplyTextStyles(Palette palette, TypeRamp ramp)
    {
        _detailTitle.Foreground = Ui.Brush(palette.Text);
        _detailTitle.FontSize = ramp.Heading;
        _detailHardware.Foreground = Ui.Brush(palette.Label);
        _detailHardware.FontSize = ramp.Body;
        _factsCard.Background = Ui.Brush(palette.Control);

        _processHeading.Text = Ui.Thai(_shell.Language["mem_processes"]);
        _processHeading.Foreground = Ui.Brush(palette.Text);
        _processHeading.FontSize = ramp.Heading;

        _search.PlaceholderText = _shell.Language["mem_search"];
        Ui.Tip(_search, _shell.Language["mem_search"]);
        _search.Background = Ui.Brush(palette.Control);
        _search.Foreground = Ui.Brush(palette.Text);
        _search.CaretBrush = Ui.Brush(palette.Text);
        _search.PlaceholderForeground = Ui.Brush(palette.Label);
        _search.FontSize = ramp.Body;

        _status.Foreground = Ui.Brush(palette.Label);
        _status.FontSize = ramp.Small;
        _status.Text = Ui.Thai(_shell.MemoryStatus);
        _status.IsVisible = _status.Text.Length > 0;

        _groupNote.Foreground = Ui.Brush(palette.Label);
        _groupNote.FontSize = ramp.Small;

        _serviceHeading.Text = Ui.Thai(_shell.Language["mem_services"]);
        _serviceHeading.Foreground = Ui.Brush(palette.Text);
        _serviceHeading.FontSize = ramp.Heading;

        _serviceNote.Foreground = Ui.Brush(palette.Label);
        _serviceNote.FontSize = ramp.Small;

        _userHeading.Text = Ui.Thai(_shell.Language["mem_user_services"]);
        _userHeading.Foreground = Ui.Brush(palette.Label);
        _userHeading.FontSize = ramp.Small;

        _systemHeading.Text = Ui.Thai(_shell.Language["mem_system_services"]);
        _systemHeading.Foreground = Ui.Brush(palette.Label);
        _systemHeading.FontSize = ramp.Small;
    }

    // ---------------------------------------------------------------- rail

    private void SyncTabs()
    {
        FullModel model = _shell.Full;
        var live = new HashSet<string>(model.Tabs.Select(t => t.Key), StringComparer.Ordinal);

        foreach (string key in _tabButtons.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _tabList.Children.Remove(_tabButtons[key].Control);
            _tabButtons.Remove(key);
        }

        for (int index = 0; index < model.Tabs.Count; index++)
        {
            FullTab tab = model.Tabs[index];
            if (!_tabButtons.TryGetValue(tab.Key, out TabButton? button))
            {
                button = new TabButton(tab.Key, this);
                _tabButtons[tab.Key] = button;
            }
            button.Tab = tab;
            button.Update(tab, string.Equals(tab.Key, model.SelectedKey, StringComparison.Ordinal));

            if (index < _tabList.Children.Count && ReferenceEquals(_tabList.Children[index], button.Control))
            {
                continue;
            }
            _tabList.Children.Remove(button.Control);
            if (index >= _tabList.Children.Count)
            {
                _tabList.Children.Add(button.Control);
            }
            else
            {
                _tabList.Children.Insert(index, button.Control);
            }
        }
    }

    private void SelectTab(string key) => _shell.SelectFullTab(key);

    /// <summary>Double-click a disk tab: open it, or the menu when it has several mounts.</summary>
    private void OpenDiskTab(FullTab tab, Control anchor)
    {
        if (tab.Mounts.Count == 1)
        {
            DiskTools.OpenMount(tab.Mounts[0]);
            return;
        }
        ShowDriveMenu(tab, anchor);
    }

    private void ShowDriveMenu(FullTab tab, Control anchor)
    {
        var menu = new ContextMenu();
        foreach (string mount in tab.Mounts)
        {
            MenuItem open = MenuItemFor(
                _shell.Language.Get("disk_open_explorer", mount),
                () => DiskTools.OpenMount(mount));
            menu.Items.Add(open);
        }
        if (tab.Mounts.Count > 0 && ToolFinder.DiskUsageTool() is not null)
        {
            menu.Items.Add(MenuItemFor(
                _shell.Language.Get("disk_cleanup", tab.Mounts[0]),
                () => DiskTools.OpenDiskUsage(tab.Mounts[0])));
        }
        if (ToolFinder.DiskManagerTool() is not null)
        {
            menu.Items.Add(MenuItemFor(
                _shell.Language["disk_manager"], () => DiskTools.OpenDiskManager()));
        }
        menu.Items.Add(new Separator());
        foreach (UiRow row in _shell.ContextMenu)
        {
            menu.Items.Add(row is ActionRow action
                ? MenuItemFor(action.Label, () => _dispatch(action.Action))
                : new Separator());
        }
        menu.Open(anchor);
    }

    private static MenuItem MenuItemFor(string label, Action action)
    {
        var item = new MenuItem { Header = Ui.Thai(label) };
        item.Click += (_, _) => action();
        return item;
    }

    // -------------------------------------------------------------- detail

    private void RenderDetail()
    {
        FullTab? tab = _shell.Full.Selected;

        _detailTitle.Text = tab is null ? string.Empty : Ui.Thai(tab.Title);
        _detailHardware.Text = tab is null ? string.Empty : Ui.Thai(tab.Hardware);

        _facts.Children.Clear();
        _factsCard.IsVisible = tab is not null && tab.Facts.Count > 0;
        if (tab is not null)
        {
            foreach (FullFact fact in tab.Facts)
            {
                _facts.Children.Add(FactBlock(fact));
            }
        }

        _bigCards.Children.Clear();
        if (tab is not null)
        {
            foreach (FullCard card in tab.BigCards)
            {
                _bigCards.Children.Add(BigCard(card));
            }
        }

        _smallCards.Children.Clear();
        _smallCards.IsVisible = tab is not null && tab.SmallCards.Count > 0;
        if (tab is not null)
        {
            foreach (FullCard card in tab.SmallCards)
            {
                _smallCards.Children.Add(SmallCard(card));
            }
        }

        bool ram = string.Equals(tab?.Key, "ram", StringComparison.Ordinal);
        _memorySection.IsVisible = ram;
        if (ram)
        {
            RenderMemory();
        }
    }

    private Control FactBlock(FullFact fact)
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        var block = new StackPanel
        {
            MaxWidth = 280,
            Margin = new Thickness(0, 0, 24, 8),
        };
        block.Children.Add(Ui.Text(Ui.Thai(fact.Title), ramp.Label, palette.Label));
        TextBlock value = Ui.Text(Ui.Thai(fact.Value), ramp.Body, palette.Text, wrap: true);
        value.MaxWidth = 280;
        block.Children.Add(value);
        return block;
    }

    private Control BigCard(FullCard card)
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        var titleRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        TextBlock title = Ui.Text(
            Ui.Thai(card.Title), ramp.Label, palette.Text, ellipsis: true);
        title.Margin = new Thickness(0, 0, 66, 0);
        titleRow.Children.Add(title);
        TextBlock value = Ui.Text(
            Ui.Thai(card.Value), ramp.Body, palette.Text, FontWeight.SemiBold);
        value.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(value, 1);
        titleRow.Children.Add(value);

        TextBlock detail = Ui.Text(
            Ui.Thai(card.Detail), ramp.Small, palette.Label, ellipsis: true);

        var axis = new Grid
        {
            Margin = new Thickness(0, 6, 0, 2),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        axis.Children.Add(Ui.Text(
            Ui.Thai(card.Unit), ramp.Tiny, palette.Label, ellipsis: true));
        TextBlock ceiling = Ui.Text(Ui.Thai(card.Ceiling), ramp.Tiny, palette.Label);
        Grid.SetColumn(ceiling, 1);
        axis.Children.Add(ceiling);

        var chart = new ChartControl(card.History, card.Accent, card.Maximum, palette)
        {
            Height = 96,
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var body = new StackPanel();
        body.Children.Add(titleRow);
        body.Children.Add(detail);
        body.Children.Add(axis);
        body.Children.Add(chart);

        return new Border
        {
            Background = Ui.Brush(palette.Control),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 11),
            Margin = new Thickness(0, 0, 0, 10),
            MinHeight = 178,
            Child = body,
        };
    }

    private Control SmallCard(FullCard card)
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
            Margin = new Thickness(0, 0, 0, 4),
        };
        header.Children.Add(Ui.Text(
            Ui.Thai(card.Title), ramp.Small, palette.Label, ellipsis: true));
        TextBlock value = Ui.Text(
            Ui.Thai(card.Value), ramp.Small, palette.Text, FontWeight.SemiBold, ellipsis: true);
        Grid.SetColumn(value, 1);
        header.Children.Add(value);

        var chart = new ChartControl(card.History, card.Accent, card.Maximum, palette)
        {
            Height = 60,
        };

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(chart);

        return new Border
        {
            Width = 89,
            Height = 89,
            Background = Ui.Brush(palette.Control),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 5, 7, 6),
            Margin = new Thickness(0, 0, 8, 8),
            Child = body,
        };
    }

    // -------------------------------------------------------------- memory

    private void RenderMemory()
    {
        MemoryTabModel model = _shell.Memory;
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        _groupNote.Text = Ui.Thai(model.GroupNote);
        _groupNote.IsVisible = model.GroupNote.Length > 0;
        _serviceNote.Text = Ui.Thai(model.ServiceNote);
        _serviceNote.IsVisible = model.ServiceNote.Length > 0;

        // Process groups are keyed by name and updated in place, so an
        // expanded group and the control under the pointer survive a read.
        var live = new HashSet<string>(
            model.Groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        foreach (string name in _groupRows.Keys.Where(n => !live.Contains(n)).ToList())
        {
            _processList.Children.Remove(_groupRows[name].Control);
            _groupRows.Remove(name);
            _expanded.Remove(name);
        }

        for (int index = 0; index < model.Groups.Count; index++)
        {
            MemProcessGroup group = model.Groups[index];
            if (!_groupRows.TryGetValue(group.Name, out GroupRow? row))
            {
                row = new GroupRow(this);
                _groupRows[group.Name] = row;
            }
            row.Group = group;
            row.Update(group, _expanded.Contains(group.Name));

            if (index < _processList.Children.Count
                && ReferenceEquals(_processList.Children[index], row.Control))
            {
                continue;
            }
            _processList.Children.Remove(row.Control);
            if (index >= _processList.Children.Count)
            {
                _processList.Children.Add(row.Control);
            }
            else
            {
                _processList.Children.Insert(index, row.Control);
            }
        }

        _serviceNote.Foreground = Ui.Brush(palette.Label);
        _serviceNote.FontSize = ramp.Small;

        SyncServiceRows(_userList, model.UserServices, _userRows);
        SyncServiceRows(_systemList, model.SystemServices, _systemRows);
        _userHeading.IsVisible = model.UserServices.Count > 0;
        _systemHeading.IsVisible = model.SystemServices.Count > 0;
    }

    private void ToggleExpanded(string name)
    {
        if (!_expanded.Add(name))
        {
            _expanded.Remove(name);
        }
        RenderMemory();
    }

    private void ToggleGraph(string name) => _shell.ToggleProcessGraph(name);

    /// <summary>
    /// Service rows are keyed by unit name and updated in place, exactly like
    /// process groups. A read every 10 s must not close an open startup menu
    /// or take the hover away from a button under the pointer.
    /// </summary>
    private void SyncServiceRows(
        StackPanel list,
        IReadOnlyList<MemService> services,
        Dictionary<string, ServiceRow> rows)
    {
        var live = new HashSet<string>(services.Select(s => s.Name), StringComparer.Ordinal);
        foreach (string name in rows.Keys.Where(n => !live.Contains(n)).ToList())
        {
            list.Children.Remove(rows[name].Control);
            rows.Remove(name);
        }

        for (int index = 0; index < services.Count; index++)
        {
            MemService service = services[index];
            if (!rows.TryGetValue(service.Name, out ServiceRow? row))
            {
                row = new ServiceRow(this);
                rows[service.Name] = row;
            }
            row.Service = service;
            row.Update();

            if (index < list.Children.Count && ReferenceEquals(list.Children[index], row.Control))
            {
                continue;
            }
            list.Children.Remove(row.Control);
            if (index >= list.Children.Count)
            {
                list.Children.Add(row.Control);
            }
            else
            {
                list.Children.Insert(index, row.Control);
            }
        }
    }

    // --------------------------------------------------------------- kills

    private async void KillProcess(MemProcess process)
    {
        string message = _shell.Language.Get("mem_confirm_kill", process.Name, process.Pid)
                         + HostWarning(new[] { process.Pid });
        if (!await Confirm(message))
        {
            _shell.SetMemoryStatus(_shell.Language["mem_cancelled"]);
            return;
        }

        await RunKill(new[] { KillTarget(process) });
    }

    private async void KillGroup(MemProcessGroup group)
    {
        var targets = group.Members
            .Where(m => m.CanKill)
            .Select(KillTarget)
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        string message = _shell.Language.Get(
            "mem_confirm_kill_group", group.Name, group.KillableCount)
            + HostWarning(targets.Select(t => t.Pid));
        if (!await Confirm(message))
        {
            _shell.SetMemoryStatus(_shell.Language["mem_cancelled"]);
            return;
        }

        await RunKill(targets);
    }

    /// <summary>The row's identity: PID, start time and name, not just the PID.</summary>
    private static ProcessTarget KillTarget(MemProcess process) =>
        new(process.Pid, process.StartTime, process.Name);

    /// <summary>The spec's extra paragraph when services run in the target.</summary>
    private string HostWarning(IEnumerable<int> pids)
    {
        int count = _shell.Memory.HostedServices(pids);
        return count > 0
            ? "\n\n" + _shell.Language.Get("mem_confirm_host", count)
            : string.Empty;
    }

    /// <summary>
    /// SIGTERM every target off the UI thread, then ask about the stubborn ones
    /// with mem_force_kill. Each answer acts on the token its own question was
    /// opened for (Amendment 4); the flow itself lives in the presentation layer.
    /// </summary>
    private async Task RunKill(IReadOnlyList<ProcessTarget> targets)
    {
        (List<PendingKill> Stubborn, string? Error) first = await Task.Run(() =>
        {
            var alive = new List<PendingKill>();
            string? error = null;
            foreach (ProcessTarget target in targets)
            {
                KillResult result = _shell.KillProcess(target);
                if (result.State == KillState.NeedsForce && result.Pending is { } pending)
                {
                    alive.Add(pending);
                }
                else if (result.State == KillState.Failed && error is null)
                {
                    error = result.Error;
                }
            }
            return (alive, error);
        });

        string? failure = first.Error;
        bool cancelled = false;
        foreach (PendingKill pending in first.Stubborn)
        {
            string message = _shell.Language.Get(
                "mem_force_kill", pending.Target.Name, pending.Target.Pid);
            if (!await Confirm(message))
            {
                // Declined: this token's handle is closed here, exactly once.
                _shell.CancelKill(pending);
                cancelled = true;
                continue;
            }
            KillResult forced = await Task.Run(() => _shell.ForceKill(pending));
            if (forced.State == KillState.Failed && failure is null)
            {
                failure = forced.Error;
            }
        }

        _shell.SetMemoryStatus(
            failure is not null
                ? _shell.Language.Get("mem_failed", failure)
                : cancelled
                    ? _shell.Language["mem_cancelled"]
                    : string.Empty);
    }

    // ------------------------------------------------------------ services

    private async void ServiceAction(MemService service, string action, string confirmKey)
    {
        string message = _shell.Language.Get(confirmKey, service.Display);
        if (!await Confirm(message))
        {
            _shell.SetMemoryStatus(_shell.Language["mem_cancelled"]);
            return;
        }
        await Task.Run(() => _shell.RunService(action, service));
    }

    private async Task ConfirmThenStartup(MemService service, bool automatic, string label)
    {
        string message = _shell.Language.Get("mem_confirm_startup", service.Display, label);
        if (!await Confirm(message))
        {
            _shell.SetMemoryStatus(_shell.Language["mem_cancelled"]);
            return;
        }
        await Task.Run(() => _shell.SetServiceStartup(service, automatic));
    }

    private async Task<bool> Confirm(string message)
    {
        (string yes, string no) = ConfirmWindow.Labels(_shell);
        var window = new ConfirmWindow(_shell, message, yes, no);
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            return await window.ShowDialog<bool>(owner);
        }
        return false;
    }

    // ----------------------------------------------------------- tab button

    /// <summary>One rail button: title, detail and summary, reconciled by key.</summary>
    private sealed class TabButton
    {
        private readonly FullView _view;
        private readonly TextBlock _title;
        private readonly TextBlock _detail;
        private readonly TextBlock _summary;
        private bool _selected;

        public TabButton(string key, FullView view)
        {
            _view = view;
            Key = key;

            _title = Ui.Text(string.Empty, view._shell.Ramp.Label, Palette.TabText, FontWeight.SemiBold, ellipsis: true);
            _detail = Ui.Text(string.Empty, view._shell.Ramp.Small, Palette.TabText, ellipsis: true);
            _detail.Opacity = 0.7;
            _summary = Ui.Text(string.Empty, view._shell.Ramp.Small, Palette.TabText, ellipsis: true);
            _summary.Opacity = 0.85;

            var body = new StackPanel();
            body.Children.Add(_title);
            body.Children.Add(_detail);
            body.Children.Add(_summary);

            Control = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(9, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 3),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = body,
            };
            Control.Classes.Add("nodrag");
            Control.PointerEntered += (_, _) =>
            {
                if (!_selected)
                {
                    Control.Background = Ui.Brush(Colour.FromHex("#22808080"));
                }
            };
            Control.PointerExited += (_, _) =>
            {
                if (!_selected)
                {
                    Control.Background = Brushes.Transparent;
                }
            };
            Control.PointerPressed += (_, e) =>
            {
                PointerPoint point = e.GetCurrentPoint(Control);
                if (point.Properties.IsRightButtonPressed && Tab.IsDisk)
                {
                    e.Handled = true;
                    _view.ShowDriveMenu(Tab, Control);
                    return;
                }
                if (!point.Properties.IsLeftButtonPressed)
                {
                    return;
                }
                e.Handled = true;
                if (e.ClickCount >= 2 && Tab.IsDisk)
                {
                    _view.OpenDiskTab(Tab, Control);
                    return;
                }
                _view.SelectTab(Key);
            };
        }

        public string Key { get; }

        public FullTab Tab { get; set; } = new() { Key = string.Empty, Title = string.Empty };

        public Border Control { get; }

        public void Update(FullTab tab, bool selected)
        {
            _selected = selected;
            TypeRamp ramp = _view._shell.Ramp;
            _title.Text = Ui.Thai(tab.Title);
            _title.FontSize = ramp.Label;
            _detail.Text = Ui.Thai(tab.Detail);
            _detail.FontSize = ramp.Small;
            _detail.IsVisible = tab.Detail.Length > 0;
            _summary.Text = Ui.Thai(tab.Summary);
            _summary.FontSize = ramp.Small;
            _summary.IsVisible = tab.Summary.Length > 0;

            Colour foreground = selected ? Colour.FromHex("#ffffff") : Palette.TabText;
            _title.Foreground = Ui.Brush(foreground);
            _detail.Foreground = Ui.Brush(foreground);
            _summary.Foreground = Ui.Brush(foreground);

            Control.Background = selected ? Ui.Brush(Palette.Selection) : Brushes.Transparent;
            Control.BorderBrush = selected ? Ui.Brush(Palette.Selection) : Brushes.Transparent;
        }
    }

    // ------------------------------------------------------- service rows

    /// <summary>One systemd unit row, updated in place.</summary>
    private sealed class ServiceRow
    {
        private readonly FullView _view;
        private readonly Border _border;
        private readonly TextBlock _title;
        private readonly TextBlock _line;
        private readonly TextBlock _shared;
        private readonly Border _startupButton;
        private readonly TextBlock _startupText;
        private readonly TextBlock _startupReadOnly;
        private readonly Border _start;
        private readonly Border _restart;
        private readonly Border _stop;

        public ServiceRow(FullView view)
        {
            _view = view;
            TypeRamp ramp = view._shell.Ramp;
            Palette palette = view._shell.Palette;
            Lang lang = view._shell.Language;

            _title = Ui.Text(string.Empty, ramp.Body, palette.Text, ellipsis: true);
            _line = Ui.Text(string.Empty, ramp.Small, palette.Label, ellipsis: true);
            _shared = Ui.Text(string.Empty, ramp.Small, palette.Label, wrap: true);
            _shared.IsVisible = false;

            var left = new StackPanel();
            left.Children.Add(_title);
            left.Children.Add(_line);
            left.Children.Add(_shared);

            _startupText = Ui.Text(string.Empty, ramp.Small, palette.Label, ellipsis: true);
            _startupButton = new Border
            {
                Padding = new Thickness(6, 3, 6, 3),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = _startupText,
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _startupButton.Classes.Add("nodrag");
            _startupButton.PointerEntered += (_, _) =>
                _startupButton.Background = Ui.Brush(Palette.Hover);
            _startupButton.PointerExited += (_, _) =>
                _startupButton.Background = Brushes.Transparent;
            _startupButton.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(_startupButton).Properties.IsLeftButtonPressed)
                {
                    return;
                }
                e.Handled = true;
                OpenStartupMenu();
            };

            _startupReadOnly = Ui.Text(
                string.Empty, ramp.Small, palette.Label, ellipsis: true);
            _startupReadOnly.Margin = new Thickness(4, 0, 4, 0);
            _startupReadOnly.VerticalAlignment = VerticalAlignment.Center;

            _start = Ui.IconButton(
                IconName.Play, 26, ramp.Body, Palette.Start,
                () => view.ServiceAction(Service, ServiceControl.Start, "mem_confirm_start"),
                lang["mem_start"]);
            _restart = Ui.IconButton(
                IconName.Restart, 26, ramp.Body, palette.Label,
                () => view.ServiceAction(Service, ServiceControl.Restart, "mem_confirm_restart"),
                lang["mem_restart"]);
            _stop = Ui.IconButton(
                IconName.Stop, 26, ramp.Body, Palette.Stop,
                () => view.ServiceAction(Service, ServiceControl.Stop, "mem_confirm_stop"),
                lang["mem_stop"]);

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            row.Children.Add(left);
            Grid.SetColumn(_startupButton, 1);
            row.Children.Add(_startupButton);
            Grid.SetColumn(_startupReadOnly, 1);
            row.Children.Add(_startupReadOnly);
            Grid.SetColumn(_start, 2);
            row.Children.Add(_start);
            Grid.SetColumn(_restart, 3);
            row.Children.Add(_restart);
            Grid.SetColumn(_stop, 4);
            row.Children.Add(_stop);

            _border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = Ui.Brush(Colour.FromHex("#22808080")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row,
            };

            Control = _border;
        }

        public MemService Service { get; set; } = new()
        {
            Entry = new ServiceEntry { Name = string.Empty },
        };

        public Control Control { get; }

        public void Update()
        {
            TypeRamp ramp = _view._shell.Ramp;
            Palette palette = _view._shell.Palette;
            Lang lang = _view._shell.Language;
            MemService service = Service;

            _title.Text = Ui.Thai(service.Display);
            _title.FontSize = ramp.Body;
            _title.Foreground = Ui.Brush(palette.Text);

            string line = $"{service.Name} · {service.StateText(lang)}";
            if (service.Running && service.Pid > 0)
            {
                line += $" · PID {service.Pid} · {service.SizeText}";
            }
            _line.Text = line;
            _line.FontSize = ramp.Small;
            _line.Foreground = Ui.Brush(palette.Label);

            _shared.Text = Ui.Thai(lang.Get("mem_shared", service.SharedWith));
            _shared.IsVisible = service.SharedWith > 1;
            _shared.FontSize = ramp.Small;
            _shared.Foreground = Ui.Brush(palette.Label);

            bool user = service.UserUnit;
            _startupButton.IsVisible = user;
            _startupReadOnly.IsVisible = !user;
            _startupText.Text = Ui.Thai(service.StartupText(lang)) + " ▾";
            _startupReadOnly.Text = Ui.Thai(service.StartupText(lang));
            _startupText.FontSize = ramp.Small;
            _startupText.Foreground = Ui.Brush(palette.Label);
            _startupReadOnly.FontSize = ramp.Small;
            _startupReadOnly.Foreground = Ui.Brush(palette.Label);
            Ui.Tip(_startupButton, lang["mem_startup"]);

            _start.IsVisible = user && service.CanStart;
            _restart.IsVisible = user && service.Running;
            _stop.IsVisible = user && service.Running;
            Ui.Tip(_start, lang["mem_start"]);
            Ui.Tip(_restart, lang["mem_restart"]);
            Ui.Tip(_stop, lang["mem_stop"]);

            ToolTip.SetTip(_border, user ? null : lang["mem_system_readonly"]);
        }

        /// <summary>The startup menu: Automatic or Manual, the current one checked.</summary>
        private void OpenStartupMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(StartupItem(automatic: true, Service.Start == ServiceStart.Automatic));
            menu.Items.Add(StartupItem(automatic: false, Service.Start == ServiceStart.Manual));
            menu.Open(_startupButton);
        }

        private MenuItem StartupItem(bool automatic, bool current)
        {
            string label = automatic
                ? _view._shell.Language["mem_auto"]
                : _view._shell.Language["mem_manual"];
            var item = new MenuItem
            {
                Header = (current ? "✓ " : "   ") + Ui.Thai(label),
            };
            item.Click += (_, _) =>
            {
                if (!current)
                {
                    _ = _view.ConfirmThenStartup(Service, automatic, label);
                }
            };
            return item;
        }
    }

    // ------------------------------------------------------- process rows

    /// <summary>One process group row, updated in place.</summary>
    private sealed class GroupRow
    {
        private readonly FullView _view;
        private readonly Border _border;
        private readonly ContentControl _expandGlyph;
        private readonly TextBlock _name;
        private readonly TextBlock _count;
        private readonly TextBlock _size;
        private readonly Border _killGroup;
        private readonly StackPanel _members;

        public GroupRow(FullView view)
        {
            _view = view;

            _expandGlyph = new ContentControl
            {
                Width = 26,
                Height = 26,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            _name = Ui.Text(string.Empty, view._shell.Ramp.Body, view._shell.Palette.Text, ellipsis: true);
            _count = Ui.Text(
                string.Empty, view._shell.Ramp.Small, view._shell.Palette.Label);
            _count.Margin = new Thickness(8, 0, 0, 0);
            _count.VerticalAlignment = VerticalAlignment.Center;

            var nameBody = new StackPanel { Orientation = Orientation.Horizontal };
            nameBody.Children.Add(_name);
            nameBody.Children.Add(_count);
            var nameButton = new Border
            {
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = nameBody,
                VerticalAlignment = VerticalAlignment.Center,
            };
            nameButton.Classes.Add("nodrag");
            Ui.Tip(nameButton, view._shell.Language["mem_graph"]);
            nameButton.PointerEntered += (_, _) => nameButton.Background = Ui.Brush(Palette.Hover);
            nameButton.PointerExited += (_, _) => nameButton.Background = Brushes.Transparent;
            nameButton.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(nameButton).Properties.IsLeftButtonPressed)
                {
                    return;
                }
                e.Handled = true;
                view.ToggleGraph(Group.Name);
            };

            _size = Ui.Text(
                string.Empty, view._shell.Ramp.Body, view._shell.Palette.Text, FontWeight.SemiBold);
            _size.Margin = new Thickness(8, 0, 0, 0);
            _size.VerticalAlignment = VerticalAlignment.Center;

            _killGroup = Ui.IconButton(
                IconName.Stop, 26, view._shell.Ramp.Body, Colour.FromHex("#ef4444"),
                () => view.KillGroup(Group),
                view._shell.Language["mem_kill_group"]);
            _killGroup.HorizontalAlignment = HorizontalAlignment.Right;

            var top = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(28, GridUnitType.Pixel),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(30, GridUnitType.Pixel),
                },
            };
            var expandButton = new Border
            {
                Width = 26,
                Height = 26,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = _expandGlyph,
            };
            expandButton.Classes.Add("nodrag");
            expandButton.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(expandButton).Properties.IsLeftButtonPressed)
                {
                    return;
                }
                e.Handled = true;
                view.ToggleExpanded(Group.Name);
            };
            top.Children.Add(expandButton);

            Grid.SetColumn(nameButton, 1);
            top.Children.Add(nameButton);
            Grid.SetColumn(_size, 2);
            top.Children.Add(_size);
            Grid.SetColumn(_killGroup, 3);
            top.Children.Add(_killGroup);

            _members = new StackPanel { IsVisible = false };

            var body = new StackPanel();
            body.Children.Add(top);
            body.Children.Add(_members);

            _border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2, 2, 4, 2),
                Background = Brushes.Transparent,
                Child = body,
            };

            Control = _border;
        }

        public MemProcessGroup Group { get; set; } = new()
        {
            Name = string.Empty,
            Members = Array.Empty<MemProcess>(),
        };

        public Control Control { get; }

        public void Update(MemProcessGroup group, bool expanded)
        {
            TypeRamp ramp = _view._shell.Ramp;
            Palette palette = _view._shell.Palette;

            _name.Text = group.Name;
            _name.FontSize = ramp.Body;
            _name.Foreground = Ui.Brush(palette.Text);
            _count.Text = group.Count > 1
                ? Ui.Thai(_view._shell.Language.Get("mem_count", group.Count))
                : string.Empty;
            _count.FontSize = ramp.Small;
            _count.Foreground = Ui.Brush(palette.Label);
            _count.IsVisible = _count.Text.Length > 0;
            _size.Text = group.SizeText;
            _size.FontSize = ramp.Body;
            _size.Foreground = Ui.Brush(palette.Text);
            _killGroup.IsVisible = group.CanKill;
            _border.Background = group.Graphed
                ? Ui.Brush(Colour.FromHex(GraphBackground))
                : Brushes.Transparent;

            _expandGlyph.Content = Ui.Icon(
                expanded ? IconName.ChevronDown : IconName.ChevronRight,
                _view._shell.Ramp.NavIcon,
                _view._shell.Palette.Label);

            _members.IsVisible = expanded;
            _members.Children.Clear();
            if (!expanded)
            {
                return;
            }

            foreach (MemProcess member in group.Members)
            {
                _members.Children.Add(MemberRow(member));
            }
        }

        private Control MemberRow(MemProcess member)
        {
            TextBlock pid = Ui.Text(
                "PID " + member.Pid, _view._shell.Ramp.Small, _view._shell.Palette.Label);
            TextBlock privateText = Ui.Text(
                member.PrivateText(_view._shell.Language),
                _view._shell.Ramp.Small,
                _view._shell.Palette.Label);
            privateText.Margin = new Thickness(8, 0, 14, 0);

            TextBlock size = Ui.Text(
                member.Readable ? member.SizeText : _view._shell.Language["mem_no_access"],
                _view._shell.Ramp.Small,
                _view._shell.Palette.Text);

            Control action;
            if (member.CanKill)
            {
                action = Ui.IconButton(
                    IconName.Stop, 26, _view._shell.Ramp.Body, Colour.FromHex("#ef4444"),
                    () => _view.KillProcess(member),
                    _view._shell.Language["mem_kill"]);
            }
            else
            {
                var lockIcon = Ui.Icon(
                    IconName.Lock, _view._shell.Ramp.NavIcon, _view._shell.Palette.Label);
                var holder = new Border
                {
                    Width = 26,
                    Height = 26,
                    Child = lockIcon,
                };
                Ui.Tip(holder, _view._shell.Language["mem_protected"]);
                action = holder;
            }
            action.HorizontalAlignment = HorizontalAlignment.Right;

            var row = new Grid
            {
                Margin = new Thickness(30, 1, 0, 1),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(30, GridUnitType.Pixel),
                },
            };
            row.Children.Add(pid);
            Grid.SetColumn(privateText, 1);
            row.Children.Add(privateText);
            size.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(size, 2);
            row.Children.Add(size);
            Grid.SetColumn(action, 3);
            row.Children.Add(action);
            return row;
        }
    }
}
