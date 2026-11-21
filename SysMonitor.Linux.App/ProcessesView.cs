using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.App;

/// <summary>
/// The Processes page (R-030 §4, phase C part 2): two tabs, "Processes" and
/// "Services". The Processes tab is a grid fed by <see cref="Snapshot.ProcessRows"/>
/// — every user's processes, sorted and searched in place, one Close and one
/// optional service Restart per row. The Services tab is the one the memory tab
/// drew, moved here unchanged.
///
/// The rows live in a virtualising <see cref="ItemsControl"/>: the item
/// collection holds one keyed <see cref="RowItem"/> per (PID, start time), so a
/// 2 s read moves numbers without rebuilding the list, and only the rows in the
/// viewport exist as controls. The order is held still while the pointer is
/// over the rows, exactly like the Apps page.
/// </summary>
internal sealed class ProcessesView : UserControl
{
    /// <summary>The eight columns the header row offers; Command is not sortable.</summary>
    private static readonly GridColumn[] Sortable =
    {
        GridColumn.Name,
        GridColumn.Pid,
        GridColumn.User,
        GridColumn.Cpu,
        GridColumn.Memory,
        GridColumn.Gpu,
        GridColumn.GpuMemory,
    };

    /// <summary>
    /// The narrowest the grid may get before it scrolls sideways. The numeric
    /// columns keep a readable width at 640 px; the window can be widened to
    /// make the name and command columns grow.
    /// </summary>
    private const double MinGridWidth = 700;

    private readonly ShellModel _shell;

    private readonly Border _processesTab;
    private readonly TextBlock _processesTabText;
    private readonly Border _servicesTab;
    private readonly TextBlock _servicesTabText;
    private readonly Control _processesPage;
    private readonly ScrollViewer _servicesPage;

    private readonly TextBlock _processHeading;
    private readonly TextBlock _processCount;
    private readonly TextBox _search;
    private readonly TextBlock _status;
    private readonly TextBlock _empty;
    private readonly Grid _header;
    private readonly TextBlock _commandHeader;
    private readonly ScrollViewer _headerScroll;
    private readonly ScrollViewer _scroll;
    private readonly ItemsControl _list;
    private readonly TextBlock _serviceHeading;
    private readonly TextBlock _serviceNote;
    private readonly TextBlock _userHeading;
    private readonly StackPanel _userList;
    private readonly TextBlock _systemHeading;
    private readonly StackPanel _systemList;

    private readonly ObservableCollection<RowItem> _items = new();
    private readonly Dictionary<string, RowItem> _itemsByKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly Dictionary<GridColumn, HeaderCell> _headers = new();
    private readonly Dictionary<string, ServiceRow> _userRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ServiceRow> _systemRows = new(StringComparer.Ordinal);

    /// <summary>
    /// The synthetic keys of the rows whose start time is unknown, by PID. The
    /// key carries the number of the render the PID was first seen in, so a
    /// reused PID never inherits the earlier process's row (Amendment 1 item 2).
    /// </summary>
    private readonly Dictionary<int, string> _unverified = new();

    /// <summary>The row keys in the order the list last showed them.</summary>
    private readonly List<string> _order = new();

    /// <summary>
    /// The order held still while the pointer is over the rows, or null when a
    /// new sample may sort again (the Apps page rule, reused here).
    /// </summary>
    private List<string>? _frozenOrder;

    /// <summary>Counts renders, so an unverified key is unique to one sighting.</summary>
    private int _sample;

    private bool _pointerOverRows;
    private string _searchText = string.Empty;
    private bool _services;

    public ProcessesView(ShellModel shell)
    {
        _shell = shell;

        _processesTabText = Ui.Text(string.Empty, shell.Ramp.Body, shell.Palette.Text, FontWeight.SemiBold);
        _processesTab = Tab(_processesTabText, () => ShowServices(false));
        _servicesTabText = Ui.Text(string.Empty, shell.Ramp.Body, shell.Palette.Text, FontWeight.SemiBold);
        _servicesTab = Tab(_servicesTabText, () => ShowServices(true));

        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 8),
        };
        tabs.Children.Add(_processesTab);
        tabs.Children.Add(_servicesTab);

        // Processes tab: heading, search, a fixed header row and the grid.
        _processHeading = Ui.Text(
            string.Empty, shell.Ramp.Heading, shell.Palette.Text, FontWeight.SemiBold);
        _processCount = Ui.Text(string.Empty, shell.Ramp.Small, shell.Palette.Label);
        _processCount.VerticalAlignment = VerticalAlignment.Bottom;
        _processCount.Margin = new Thickness(0, 0, 0, 2);

        var headingRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(2, 2, 2, 8),
        };
        headingRow.Children.Add(_processHeading);
        headingRow.Children.Add(_processCount);

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
            Margin = new Thickness(2, 0, 2, 6),
        };
        _search.TextChanged += (_, _) =>
        {
            string text = _search.Text ?? string.Empty;
            if (!string.Equals(text, _searchText, StringComparison.Ordinal))
            {
                _searchText = text;
                Resort();
            }
        };

        _status = Ui.Text(string.Empty, shell.Ramp.Small, shell.Palette.Label, wrap: true);
        _status.Margin = new Thickness(2, 0, 2, 4);

        // Header row: the same columns as a data row, click to sort. Command and
        // the two action columns have no header of their own.
        _header = new Grid
        {
            Margin = new Thickness(2, 0, 4, 2),
            // The row border's own 2 + 4 px padding is outside its inner grid,
            // so the header's minimum is that much smaller to keep the columns
            // over the rows when the page is narrower than the grid.
            MinWidth = MinGridWidth - 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AddColumns(_header);
        _commandHeader = Ui.Text(
            string.Empty, shell.Ramp.Tiny, shell.Palette.Label, FontWeight.SemiBold, ellipsis: true);
        _commandHeader.VerticalAlignment = VerticalAlignment.Center;
        _commandHeader.Margin = new Thickness(0, 3, 0, 3);
        BuildHeaders();

        _headerScroll = new ScrollViewer
        {
            Content = _header,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            AllowAutoHide = false,
        };

        _list = new ItemsControl
        {
            ItemsSource = _items,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<RowItem>(
                (item, _) => new GridRow(this, item).Control),
            MinWidth = MinGridWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // The generated ContentPresenters must fill the row width, or the
        // columns would shrink to the text instead of lining up with the header.
        _list.Styles.Add(new Style(x => x.OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(
                    ContentPresenter.HorizontalContentAlignmentProperty,
                    HorizontalAlignment.Stretch),
                new Setter(ContentPresenter.PaddingProperty, new Thickness(0)),
            },
        });

        _scroll = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
            Margin = new Thickness(0, 2, 0, 0),
        };

        // The rows freeze under the pointer; sorting resumes when it leaves.
        _scroll.PointerEntered += (_, _) =>
        {
            _pointerOverRows = true;
            _frozenOrder ??= _order.ToList();
        };
        _scroll.PointerExited += (_, _) =>
        {
            if (!_pointerOverRows)
            {
                return;
            }
            _pointerOverRows = false;
            _frozenOrder = null;
            RenderData();
        };

        // A visible vertical scrollbar narrows the rows, so the header reserves
        // the same width (the Apps page rule). The header also follows the
        // rows' horizontal offset when the grid is narrower than its columns.
        _scroll.LayoutUpdated += (_, _) => AlignHeader();
        _scroll.ScrollChanged += (_, _) =>
        {
            Vector offset = _scroll.Offset;
            if (Math.Abs(_headerScroll.Offset.X - offset.X) > 0.5)
            {
                _headerScroll.Offset = new Vector(offset.X, 0);
            }
            AlignHeader();
        };

        _empty = Ui.Text(
            string.Empty, shell.Ramp.Body, shell.Palette.Label, wrap: true);
        _empty.Margin = new Thickness(4, 8, 4, 4);
        _empty.HorizontalAlignment = HorizontalAlignment.Left;
        _empty.VerticalAlignment = VerticalAlignment.Top;
        _empty.IsVisible = false;

        var gridBody = new Grid();
        gridBody.Children.Add(_scroll);
        gridBody.Children.Add(_empty);

        var processesBody = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
        processesBody.Children.Add(headingRow);
        Grid.SetRow(_search, 1);
        processesBody.Children.Add(_search);
        Grid.SetRow(_headerScroll, 2);
        processesBody.Children.Add(_headerScroll);
        Grid.SetRow(gridBody, 3);
        processesBody.Children.Add(gridBody);
        _processesPage = processesBody;

        // Services tab, unchanged from the old memory tab.
        _serviceHeading = Ui.Text(
            string.Empty, shell.Ramp.Heading, shell.Palette.Text, FontWeight.SemiBold, wrap: true);
        _serviceHeading.Margin = new Thickness(2, 2, 2, 8);

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

        var servicesBody = new StackPanel();
        servicesBody.Children.Add(_serviceHeading);
        servicesBody.Children.Add(_serviceNote);
        servicesBody.Children.Add(_userHeading);
        servicesBody.Children.Add(_userList);
        servicesBody.Children.Add(_systemHeading);
        servicesBody.Children.Add(_systemList);
        _servicesPage = Page(servicesBody);

        var body = new Grid();
        body.Children.Add(_processesPage);
        body.Children.Add(_servicesPage);
        _servicesPage.IsVisible = false;

        // The status line belongs to both tabs: a failed service action must be
        // visible on the Services tab, where it happens.
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        root.Children.Add(tabs);
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        Content = root;

        shell.MemoryChanged += OnMemoryChanged;
        Render();
    }

    /// <summary>Re-apply text, language and palette; the rows stay keyed.</summary>
    public void Render()
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        _processesTabText.Text = Ui.Thai(_shell.Language["processes"]);
        _servicesTabText.Text = Ui.Thai(_shell.Language["mem_services"]);
        UpdateTabs();

        _processHeading.Text = Ui.Thai(_shell.Language["processes"]);
        _processHeading.Foreground = Ui.Brush(palette.Text);
        _processHeading.FontSize = ramp.Heading;

        _processCount.Foreground = Ui.Brush(palette.Label);
        _processCount.FontSize = ramp.Small;

        _search.PlaceholderText = Ui.Thai(_shell.Language["procs_search"]);
        Ui.Tip(_search, _shell.Language["procs_search"]);
        _search.Background = Ui.Brush(palette.Control);
        _search.Foreground = Ui.Brush(palette.Text);
        _search.CaretBrush = Ui.Brush(palette.Text);
        _search.PlaceholderForeground = Ui.Brush(palette.Label);
        _search.FontSize = ramp.Body;

        _status.Foreground = Ui.Brush(palette.Label);
        _status.FontSize = ramp.Small;
        _status.Text = Ui.Thai(_shell.MemoryStatus);
        _status.IsVisible = _status.Text.Length > 0;

        _empty.Foreground = Ui.Brush(palette.Label);
        _empty.FontSize = ramp.Body;

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

        RenderData();
    }

    /// <summary>
    /// Update the grid after a new sample or an action. The Services tab is
    /// refreshed only while it is the visible tab: its rows are not virtualised,
    /// and keeping a hidden tab's text current would cost every tick for
    /// nothing. Switching to it reads them once.
    /// </summary>
    public void RenderData()
    {
        RenderProcesses();
        if (_services)
        {
            RenderServices();
        }
    }

    private static ScrollViewer Page(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        AllowAutoHide = false,
    };

    private Border Tab(TextBlock text, Action onClick)
    {
        var tab = new Border
        {
            Padding = new Thickness(12, 5, 12, 5),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = text,
        };
        tab.Classes.Add("nodrag");
        tab.PointerEntered += (_, _) =>
        {
            if (tab.Background is SolidColorBrush { Color.A: 0 })
            {
                tab.Background = Ui.Brush(Colour.FromHex("#22808080"));
            }
        };
        tab.PointerExited += (_, _) => UpdateTabs();
        tab.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;
            onClick();
        };
        return tab;
    }

    private void ShowServices(bool services)
    {
        if (_services == services)
        {
            return;
        }
        _services = services;
        _processesPage.IsVisible = !services;
        _servicesPage.IsVisible = services;
        UpdateTabs();
        RenderData();
    }

    private void UpdateTabs()
    {
        Palette palette = _shell.Palette;
        Colour selected = Colour.FromHex("#ffffff");
        SetTab(_processesTab, _processesTabText, selected, palette, !_services);
        SetTab(_servicesTab, _servicesTabText, selected, palette, _services);
    }

    private static void SetTab(
        Border tab, TextBlock text, Colour selected, Palette palette, bool active)
    {
        tab.Background = active
            ? Ui.Brush(Palette.Selection)
            : Brushes.Transparent;
        text.Foreground = Ui.Brush(active ? selected : palette.Text);
    }

    private void OnMemoryChanged() =>
        Dispatcher.UIThread.Post(() =>
        {
            _status.Text = Ui.Thai(_shell.MemoryStatus);
            _status.IsVisible = _status.Text.Length > 0;
            RenderData();
        });

    // ------------------------------------------------------------ processes

    private void RenderProcesses()
    {
        _sample++;
        IReadOnlyList<ProcessRow> all = _shell.Snapshot?.ProcessRows ?? ProcessRow.Empty;
        IReadOnlyList<ProcessRow> filtered = ProcessGrid.Filter(all, _searchText);
        GridColumn column = ProcessGrid.ParseColumn(_shell.Config.ProcsSort, GridColumn.Memory);
        bool descending = _shell.Config.ProcsSortDesc;
        IReadOnlyList<ProcessRow> rows = ProcessGrid.Sort(filtered, column, descending);

        // A synthetic key lives only for the sighting that produced it: when the
        // PID leaves the snapshot, or turns out to have a start time after all,
        // its key is dropped so a later sighting cannot inherit it
        // (Amendment 1 item 2).
        var present = new HashSet<int>(all.Select(row => row.Pid));
        var identified = new HashSet<int>(
            all.Where(row => row.StartTime is not null).Select(row => row.Pid));
        foreach (int pid in _unverified.Keys.ToList())
        {
            if (!present.Contains(pid) || identified.Contains(pid))
            {
                _unverified.Remove(pid);
            }
        }

        // One item per process, keyed by PID and start time, so a recycled PID
        // never inherits the earlier row's state. Keys are kept for every
        // process the snapshot knows, not only the filtered ones, so a running
        // action's busy state survives a search that hides its row.
        var live = new HashSet<string>(all.Select(KeyOf), StringComparer.Ordinal);
        foreach (string key in _itemsByKey.Keys.Where(key => !live.Contains(key)).ToList())
        {
            // Drop the item and its busy flag together: a row whose identity
            // cannot be verified must never show another process's action
            // state (Amendment 1 item 2).
            _itemsByKey.Remove(key);
            _busy.Remove(key);
        }

        var wanted = new List<RowItem>(rows.Count);
        foreach (ProcessRow row in rows)
        {
            string key = KeyOf(row);
            if (!_itemsByKey.TryGetValue(key, out RowItem? item))
            {
                item = new RowItem(key);
                _itemsByKey[key] = item;
            }
            item.Set(row);
            wanted.Add(item);
        }

        // While the pointer is over the rows, keep the order frozen; values
        // still update below. New processes append, gone ones drop out.
        if (_frozenOrder is { } frozen)
        {
            var frozenIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < frozen.Count; index++)
            {
                frozenIndex[frozen[index]] = index;
            }
            wanted = wanted
                .OrderBy(item => frozenIndex.TryGetValue(item.Key, out int index)
                    ? index
                    : int.MaxValue)
                .ToList();

            // A key that appeared during the freeze joins the frozen order at
            // the end, so the next sample keeps it there instead of sorting it
            // again under the pointer (Amendment 1 item 1). Keys that are gone
            // are pruned, so the list stays the size of the grid.
            frozen.Clear();
            foreach (RowItem item in wanted)
            {
                frozen.Add(item.Key);
            }
        }

        _order.Clear();
        foreach (RowItem item in wanted)
        {
            _order.Add(item.Key);
        }

        ReconcileItems(wanted);

        _processCount.Text = Ui.Thai(_shell.Language.Get("procs_count", wanted.Count));
        _empty.Text = Ui.Thai(_shell.Language["procs_empty"]);
        _empty.IsVisible = wanted.Count == 0;
        UpdateHeaders(column, descending);
    }

    /// <summary>
    /// Bring the virtualising collection to <paramref name="wanted"/> with the
    /// fewest changes: move a live row instead of removing and re-adding it, so
    /// its container, hover and buttons stay put whenever the order is stable.
    /// </summary>
    private void ReconcileItems(List<RowItem> wanted)
    {
        var live = new HashSet<RowItem>(wanted);
        for (int index = _items.Count - 1; index >= 0; index--)
        {
            if (!live.Contains(_items[index]))
            {
                _items.RemoveAt(index);
            }
        }

        for (int index = 0; index < wanted.Count; index++)
        {
            RowItem item = wanted[index];
            if (index < _items.Count && ReferenceEquals(_items[index], item))
            {
                continue;
            }
            int at = _items.IndexOf(item);
            if (at >= 0)
            {
                _items.Move(at, index);
            }
            else
            {
                _items.Insert(index, item);
            }
        }

        while (_items.Count > wanted.Count)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }

    /// <summary>Sort again now, even with the pointer over the rows.</summary>
    private void Resort()
    {
        _frozenOrder = null;
        RenderData();
        if (_pointerOverRows)
        {
            _frozenOrder = _order.ToList();
        }
    }

    /// <summary>
    /// A row's identity. PID plus start time is the verified identity; with no
    /// start time there is none, so the key also carries the sample the PID was
    /// first seen in and is dropped when the PID leaves the snapshot. A reused
    /// PID therefore gets a new key and never inherits the old row, its busy
    /// flag or its action state (Amendment 1 item 2).
    /// </summary>
    private string KeyOf(ProcessRow row)
    {
        string pid = row.Pid.ToString(CultureInfo.InvariantCulture);
        if (row.StartTime is { } start)
        {
            return pid + ":" + start.ToString(CultureInfo.InvariantCulture);
        }

        if (!_unverified.TryGetValue(row.Pid, out string? key))
        {
            key = pid + ":?:" + _sample.ToString(CultureInfo.InvariantCulture);
            _unverified[row.Pid] = key;
        }
        return key;
    }

    public bool IsBusy(string key) => _busy.Contains(key);

    private void SetBusy(string key, bool busy)
    {
        if (busy)
        {
            // A row whose start time is unknown has no verified identity, so it
            // never becomes busy and can never show another process's action
            // state (Part 2 Amendment 2).
            if (_itemsByKey.TryGetValue(key, out RowItem? pending)
                && pending.Row.StartTime is null)
            {
                return;
            }
            _busy.Add(key);
        }
        else
        {
            _busy.Remove(key);
        }
        if (_itemsByKey.TryGetValue(key, out RowItem? item))
        {
            item.Notify();
        }
    }

    // ------------------------------------------------------------- actions

    private async void CloseProcess(RowItem item)
    {
        string key = item.Key;
        if (IsBusy(key))
        {
            return;
        }

        // The row as the person saw it. The kill flow re-reads the process and
        // applies the protection rules again before it signals anything.
        // A row without a start time has no verified identity, so it has no
        // actions at all (Part 2 Amendment 2).
        ProcessRow row = item.Row;
        if (row.StartTime is null)
        {
            return;
        }
        try
        {
            // Busy goes up before the confirm is awaited, so a second click
            // while the dialog is open does nothing (the Apps page rule).
            SetBusy(key, true);
            string message = _shell.Language.Get("mem_confirm_kill", row.Name, row.Pid)
                             + HostWarning(new[] { row.Pid });
            if (!await Confirm(message))
            {
                _shell.SetMemoryStatus(_shell.Language["mem_cancelled"]);
                return;
            }

            await RunKill(new[] { new ProcessTarget(row.Pid, row.StartTime, row.Name) });
        }
        catch (Exception error)
        {
            _shell.SetMemoryStatus(_shell.Language.Get("mem_failed", error.Message));
        }
        finally
        {
            SetBusy(key, false);
            _shell.RequestMemoryRefresh();
        }
    }

    private async void RestartProcess(RowItem item)
    {
        string key = item.Key;
        if (IsBusy(key))
        {
            return;
        }

        ProcessRow row = item.Row;
        string? unit = row.ServiceUnit;
        if (unit is null)
        {
            return;
        }
        if (row.StartTime is null)
        {
            // No verified identity, no actions (Part 2 Amendment 2).
            return;
        }

        try
        {
            SetBusy(key, true);
            if (!await Confirm(
                    _shell.Language.Get("proc_restart_confirm", unit, row.Pid)))
            {
                _shell.SetMemoryStatus(_shell.Language["mem_cancelled"]);
                return;
            }

            _shell.SetMemoryStatus(_shell.Language.Get("proc_restarting", unit));
            ActionResult result = await Task.Run(() => _shell.AppActions.RestartService(row));
            if (result.Outcome == ActionOutcome.Failed)
            {
                _shell.SetMemoryStatus(
                    _shell.Language.Get("mem_failed", result.Error ?? string.Empty));
            }
            else
            {
                _shell.SetMemoryStatus(string.Empty);
            }
        }
        catch (Exception error)
        {
            _shell.SetMemoryStatus(_shell.Language.Get("mem_failed", error.Message));
        }
        finally
        {
            SetBusy(key, false);
            _shell.RequestMemoryRefresh();
        }
    }

    /// <summary>The spec's extra paragraph when services run in the target.</summary>
    private string HostWarning(IEnumerable<int> pids)
    {
        int count = _shell.Memory.HostedServices(pids);
        return count > 0
            ? "\n\n" + _shell.Language.Get("mem_confirm_host", count)
            : string.Empty;
    }

    /// <summary>
    /// SIGTERM the target off the UI thread, then ask about a survivor with
    /// mem_force_kill. The flow itself lives in the presentation layer.
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

    // ---------------------------------------------------------- services

    private void RenderServices()
    {
        MemoryTabModel model = _shell.Memory;
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        _serviceNote.Text = Ui.Thai(model.ServiceNote);
        _serviceNote.IsVisible = model.ServiceNote.Length > 0;
        _serviceNote.Foreground = Ui.Brush(palette.Label);
        _serviceNote.FontSize = ramp.Small;

        SyncServiceRows(_userList, model.UserServices, _userRows);
        SyncServiceRows(_systemList, model.SystemServices, _systemRows);
        _userHeading.IsVisible = model.UserServices.Count > 0;
        _systemHeading.IsVisible = model.SystemServices.Count > 0;
    }

    /// <summary>
    /// Service rows are keyed by unit name and updated in place; a read every
    /// 10 s must not close an open startup menu or take the hover away.
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

    // -------------------------------------------------------------- header

    private void BuildHeaders()
    {
        foreach (GridColumn column in Sortable)
        {
            var cell = new HeaderCell(
                string.Empty,
                _shell.Ramp.Tiny,
                _shell.Palette.Label,
                right: column != GridColumn.Name && column != GridColumn.User,
                () => Ui.Act(() =>
                {
                    // A header click always sorts, even while the rows are
                    // frozen under the pointer.
                    _frozenOrder = null;
                    _shell.SetProcsSort(column);
                    if (_pointerOverRows)
                    {
                        _frozenOrder = _order.ToList();
                    }
                }));
            _headers[column] = cell;
            Grid.SetColumn(cell.Control, ColumnOf(column));
            _header.Children.Add(cell.Control);
        }

        // Command has no sort key in ProcessGrid, so it is a plain label.
        Grid.SetColumn(_commandHeader, 7);
        _header.Children.Add(_commandHeader);
    }

    private void UpdateHeaders(GridColumn active, bool descending)
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;
        foreach ((GridColumn column, HeaderCell cell) in _headers)
        {
            cell.Update(
                _shell.Language[HeaderKey(column)],
                ramp.Tiny,
                palette.Label,
                active: column == active,
                descending);
        }

        _commandHeader.Text = Ui.Thai(_shell.Language["col_command"]);
        _commandHeader.Foreground = Ui.Brush(palette.Label);
        _commandHeader.FontSize = ramp.Tiny;
    }

    private static string HeaderKey(GridColumn column) => column switch
    {
        GridColumn.Pid => "col_pid",
        GridColumn.User => "col_user",
        GridColumn.Cpu => "col_cpu",
        GridColumn.Memory => "col_memory",
        GridColumn.Gpu => "col_gpu",
        GridColumn.GpuMemory => "col_gpu_memory",
        _ => "col_name",
    };

    /// <summary>The grid column a sortable header sits in (Name is first).</summary>
    private static int ColumnOf(GridColumn column) => column switch
    {
        GridColumn.Name => 0,
        GridColumn.Pid => 1,
        GridColumn.User => 2,
        GridColumn.Cpu => 3,
        GridColumn.Memory => 4,
        GridColumn.Gpu => 5,
        GridColumn.GpuMemory => 6,
        _ => 0,
    };

    /// <summary>
    /// The shared columns: name, PID, user, CPU, memory, GPU, GPU memory,
    /// command, then Close and Restart. The two name-like columns are the only
    /// ones that grow with the window.
    /// </summary>
    private static void AddColumns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(54, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(82, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(56, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(82, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(56, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(82, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(28, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(28, GridUnitType.Pixel));
    }

    /// <summary>
    /// The rows sit inside the ScrollViewer, which is narrower than the page
    /// while its vertical scrollbar is shown, so the header reserves the same
    /// width. Its horizontal offset follows the rows in the ScrollChanged hook.
    /// </summary>
    private void AlignHeader()
    {
        double width = _scroll.Bounds.Width;
        double viewport = _scroll.Viewport.Width;
        if (!double.IsFinite(width) || !double.IsFinite(viewport) || viewport <= 0)
        {
            // Before the first layout pass there is nothing to measure yet.
            return;
        }

        double scrollbar = Math.Max(0, width - viewport);

        // Both the rows and the header get the same explicit width, so their
        // star columns divide the same space however the ScrollViewer chooses
        // to arrange its content. The row border's 2 + 4 px padding is the
        // header's margin instead.
        double content = Math.Max(MinGridWidth, viewport);
        double headerWidth = Math.Max(0, content - 6);
        if (!AreClose(_list.Width, content))
        {
            _list.Width = content;
        }
        if (!AreClose(_header.Width, headerWidth))
        {
            _header.Width = headerWidth;
        }

        var margin = new Thickness(0, 0, scrollbar, 0);
        if (_headerScroll.Margin != margin)
        {
            _headerScroll.Margin = margin;
        }
    }

    private static bool AreClose(double value, double other) =>
        double.IsFinite(value) && Math.Abs(value - other) < 0.5;

    // ----------------------------------------------------------- formatting

    private static string Percent(double? value) =>
        value is double percent ? Format.Decimal(percent, 1) + "%" : "—";

    private static string BytesOrDash(long? bytes) =>
        bytes is long value ? MemoryBoard.Size(value) : "—";

    /// <summary>Assign only when the text changed: a tick touches few glyphs.</summary>
    private static void Set(TextBlock block, string text)
    {
        if (!string.Equals(block.Text, text, StringComparison.Ordinal))
        {
            block.Text = text;
        }
    }

    private static TextBlock Value(TypeRamp ramp, Palette palette) => Value(
        Ui.Text(string.Empty, ramp.Body, palette.Text, align: TextAlignment.Right));

    private static TextBlock Value(TextBlock block)
    {
        block.VerticalAlignment = VerticalAlignment.Center;
        block.HorizontalAlignment = HorizontalAlignment.Stretch;
        return block;
    }

    // ------------------------------------------------------------- row item

    /// <summary>
    /// One process row's identity and newest values. It survives a sample, so
    /// the virtualising list never has to replace the item under the pointer;
    /// only the realised row control listens to <see cref="Changed"/>.
    /// </summary>
    private sealed class RowItem
    {
        public RowItem(string key) => Key = key;

        public string Key { get; }

        public ProcessRow Row { get; private set; } = new()
        {
            Pid = 0,
            Name = string.Empty,
        };

        public event Action? Changed;

        public void Set(ProcessRow row)
        {
            Row = row;
            Changed?.Invoke();
        }

        /// <summary>Re-read the same values (after a busy flag changed).</summary>
        public void Notify() => Changed?.Invoke();
    }

    // ---------------------------------------------------------------- rows

    /// <summary>One process row, filled from its <see cref="RowItem"/>.</summary>
    private sealed class GridRow
    {
        private readonly ProcessesView _view;
        private readonly RowItem _item;
        private readonly Border _border;
        private readonly TextBlock _name;
        private readonly TextBlock _pid;
        private readonly TextBlock _user;
        private readonly TextBlock _cpu;
        private readonly TextBlock _memory;
        private readonly TextBlock _gpu;
        private readonly TextBlock _gpuMemory;
        private readonly TextBlock _command;
        private readonly Border _closeSlot;
        private readonly Border _close;
        private readonly Border _lock;
        private readonly Border _restartSlot;
        private readonly Border _restart;

        public GridRow(ProcessesView view, RowItem item)
        {
            _view = view;
            _item = item;
            TypeRamp ramp = view._shell.Ramp;
            Palette palette = view._shell.Palette;
            Lang lang = view._shell.Language;

            _name = Ui.Text(string.Empty, ramp.Body, palette.Text, FontWeight.SemiBold, ellipsis: true);
            _pid = Value(ramp, palette);
            _user = Ui.Text(string.Empty, ramp.Small, palette.Label, ellipsis: true);
            _user.VerticalAlignment = VerticalAlignment.Center;
            _cpu = Value(ramp, palette);
            _memory = Value(ramp, palette);
            _gpu = Value(ramp, palette);
            _gpuMemory = Value(ramp, palette);
            _command = Ui.Text(string.Empty, ramp.Small, palette.Label, ellipsis: true);
            _command.VerticalAlignment = VerticalAlignment.Center;

            _close = Ui.IconButton(
                IconName.Stop, 24, ramp.Small, Colour.FromHex("#ef4444"),
                () => view.CloseProcess(item), lang["mem_kill"]);
            _close.VerticalAlignment = VerticalAlignment.Center;
            _closeSlot = new Border
            {
                Background = Brushes.Transparent,
                Child = _close,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _closeSlot.Classes.Add("nodrag");

            var lockIcon = Ui.Icon(IconName.Lock, ramp.NavIcon, palette.Label);
            _lock = new Border
            {
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Child = lockIcon,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
            };
            Ui.Tip(_lock, lang["proc_protected_tip"]);

            _restart = Ui.IconButton(
                IconName.Restart, 24, ramp.Small, palette.Label,
                () => view.RestartProcess(item), lang["proc_restart_service"]);
            _restart.VerticalAlignment = VerticalAlignment.Center;
            _restartSlot = new Border
            {
                Background = Brushes.Transparent,
                Child = _restart,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
            };
            _restartSlot.Classes.Add("nodrag");

            var grid = new Grid();
            AddColumns(grid);
            grid.Children.Add(_name);
            Grid.SetColumn(_pid, 1);
            grid.Children.Add(_pid);
            Grid.SetColumn(_user, 2);
            grid.Children.Add(_user);
            Grid.SetColumn(_cpu, 3);
            grid.Children.Add(_cpu);
            Grid.SetColumn(_memory, 4);
            grid.Children.Add(_memory);
            Grid.SetColumn(_gpu, 5);
            grid.Children.Add(_gpu);
            Grid.SetColumn(_gpuMemory, 6);
            grid.Children.Add(_gpuMemory);
            Grid.SetColumn(_command, 7);
            grid.Children.Add(_command);
            Grid.SetColumn(_closeSlot, 8);
            grid.Children.Add(_closeSlot);
            Grid.SetColumn(_lock, 8);
            grid.Children.Add(_lock);
            Grid.SetColumn(_restartSlot, 9);
            grid.Children.Add(_restartSlot);

            _border = new Border
            {
                Padding = new Thickness(2, 3, 4, 3),
                BorderBrush = Ui.Brush(Colour.FromHex("#22808080")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _border.Classes.Add("nodrag");

            // Only realised rows listen: scrolling through 300 processes must
            // not leave a chain of discarded controls updating every tick.
            _border.AttachedToVisualTree += (_, _) => _item.Changed += Update;
            _border.DetachedFromVisualTree += (_, _) => _item.Changed -= Update;

            Control = _border;
            Update();
        }

        public Control Control { get; }

        public void Update()
        {
            TypeRamp ramp = _view._shell.Ramp;
            Palette palette = _view._shell.Palette;
            Lang lang = _view._shell.Language;
            ProcessRow row = _item.Row;
            // A row whose start time is unknown has no verified identity, so it
            // has no actions and never shows a busy state (Part 2 Amendment 2).
            bool verified = row.StartTime is not null;
            bool busy = verified && _view.IsBusy(_item.Key);
            bool protectedRow = !_view._shell.CanEndProcess(row);

            Set(_name, row.Name);
            Set(_pid, row.Pid.ToString(CultureInfo.InvariantCulture));
            Set(_user, row.User.Length > 0 ? row.User : "—");
            Set(_cpu, Percent(row.CpuPercent));
            Set(_memory, row.Readable ? MemoryBoard.Size(row.MemoryBytes) : "—");
            Set(_gpu, Percent(row.GpuPercent));
            Set(_gpuMemory, BytesOrDash(row.GpuMemoryBytes));
            Set(_command, row.Command.Length > 0 ? row.Command : "—");
            ToolTip.SetTip(_command, row.Command.Length > 0 ? row.Command : null);

            _name.FontSize = ramp.Body;
            _name.Foreground = Ui.Brush(palette.Text);
            _pid.FontSize = ramp.Body;
            _pid.Foreground = Ui.Brush(palette.Text);
            _memory.FontSize = ramp.Body;
            _memory.Foreground = Ui.Brush(palette.Text);
            _user.FontSize = ramp.Small;
            _user.Foreground = Ui.Brush(palette.Label);
            _cpu.FontSize = ramp.Body;
            _cpu.Foreground = Ui.Brush(palette.Text);
            _gpu.FontSize = ramp.Body;
            _gpu.Foreground = Ui.Brush(palette.Text);
            _gpuMemory.FontSize = ramp.Body;
            _gpuMemory.Foreground = Ui.Brush(palette.Text);
            _command.FontSize = ramp.Small;
            _command.Foreground = Ui.Brush(palette.Label);

            // Existing protection rules: a row that may not be ended shows a
            // lock with the reason instead of a Close button.
            _lock.IsVisible = protectedRow;
            _closeSlot.IsVisible = !protectedRow;

            // Close stays disabled while an action runs, and on a row whose
            // start time is unknown: with no verified identity there is nothing
            // safe to signal (Part 2 Amendment 2). The tooltip sits on the slot
            // so it still shows while the button is disabled.
            bool canClose = verified && !busy;
            _close.IsEnabled = canClose;
            _close.Opacity = canClose ? 1 : 0.45;
            _close.Cursor = new Cursor(
                canClose ? StandardCursorType.Hand : StandardCursorType.Arrow);
            string closeTip = verified ? lang["mem_kill"] : lang["proc_unverified_tip"];
            ToolTip.SetTip(_close, closeTip);
            ToolTip.SetTip(_closeSlot, closeTip);
            ToolTip.SetTip(_lock, lang["proc_protected_tip"]);

            // Restart only for a systemd user unit whose MainPID this row is.
            // A protected unit still shows the button, disabled with its reason;
            // the action re-reads the process and refuses it either way. An
            // unverified row is refused too, with its own reason.
            bool hasUnit = !string.IsNullOrEmpty(row.ServiceUnit);
            _restartSlot.IsVisible = hasUnit;
            if (hasUnit)
            {
                bool canRestart = verified && !protectedRow;
                _restart.IsEnabled = !busy && canRestart;
                _restart.Opacity = !busy && canRestart ? 1 : 0.45;
                _restart.Cursor = new Cursor(
                    !busy && canRestart ? StandardCursorType.Hand : StandardCursorType.Arrow);
                string restartTip = !verified
                    ? lang["proc_unverified_tip"]
                    : canRestart
                        ? lang["proc_restart_service"]
                        : lang["proc_protected_tip"];
                ToolTip.SetTip(_restart, restartTip);
                ToolTip.SetTip(_restartSlot, restartTip);
            }
        }
    }

    // -------------------------------------------------------------- header

    /// <summary>A sortable column header with a drawn arrow for the active column.</summary>
    private sealed class HeaderCell
    {
        private readonly Border _control;
        private readonly TextBlock _text;
        private readonly ContentControl _arrow;

        public HeaderCell(string text, double size, Colour colour, bool right, Action click)
        {
            _text = Ui.Text(text, size, colour, FontWeight.SemiBold, ellipsis: true);
            _arrow = new ContentControl
            {
                Width = 12,
                Height = 12,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            row.Children.Add(_text);
            row.Children.Add(_arrow);

            _control = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(0, 3, 0, 3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _control.Classes.Add("nodrag");
            _control.PointerEntered += (_, _) => _control.Background = Ui.Brush(Palette.Hover);
            _control.PointerExited += (_, _) => _control.Background = Brushes.Transparent;
            _control.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(_control).Properties.IsLeftButtonPressed)
                {
                    return;
                }
                e.Handled = true;
                click();
            };
        }

        public Control Control => _control;

        public void Update(string text, double size, Colour colour, bool active, bool descending)
        {
            _text.Text = Ui.Thai(text);
            _text.FontSize = size;
            _text.Foreground = Ui.Brush(colour);
            _arrow.Content = active
                ? Ui.Icon(
                    descending ? IconName.ChevronDown : IconName.ChevronUp,
                    10,
                    colour)
                : null;
        }
    }

    // ------------------------------------------------------- service rows

    /// <summary>One systemd unit row, updated in place.</summary>
    private sealed class ServiceRow
    {
        private readonly ProcessesView _view;
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

        public ServiceRow(ProcessesView view)
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
}
