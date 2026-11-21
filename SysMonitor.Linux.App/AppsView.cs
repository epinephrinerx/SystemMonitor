using System.Globalization;
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
/// The Apps page (R-030 §3, phase C part 1): every desktop app the session's
/// <c>app.slice</c> knows, with its instances and their processes, sorted and
/// searched in place.
///
/// The rows are keyed by app id, by instance unit and by PID, so a 2 s read
/// moves numbers and keeps the expanded rows, the open search and the pointer
/// where they were. Close and restart run off the UI thread through
/// <see cref="ShellModel.AppActions"/>, and a result that needs force asks the
/// same question the Processes page asks.
/// </summary>
internal sealed class AppsView : UserControl
{
    private const double AppIconSize = 18;

    /// <summary>The six columns the header row offers, in page order.</summary>
    private static readonly GridColumn[] Sortable =
    {
        GridColumn.Name,
        GridColumn.Instances,
        GridColumn.Cpu,
        GridColumn.Memory,
        GridColumn.Gpu,
        GridColumn.GpuMemory,
    };

    private readonly ShellModel _shell;
    private readonly TextBlock _heading;
    private readonly TextBlock _count;
    private readonly TextBox _search;
    private readonly TextBlock _status;
    private readonly Grid _header;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _list;
    private readonly TextBlock _empty;
    private readonly Dictionary<GridColumn, HeaderCell> _headers = new();
    private readonly Dictionary<string, AppRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rowKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);

    /// <summary>The row keys in the order the list last showed them.</summary>
    private readonly List<string> _order = new();

    /// <summary>
    /// The order held still while the pointer is over the rows (Amendment 1
    /// item 2), or null when a new sample may sort again.
    /// </summary>
    private List<string>? _frozenOrder;

    private bool _pointerOverRows;
    private string _searchText = string.Empty;

    public AppsView(ShellModel shell)
    {
        _shell = shell;

        _heading = Ui.Text(
            string.Empty, shell.Ramp.Heading, shell.Palette.Text, FontWeight.Bold);
        _count = Ui.Text(string.Empty, shell.Ramp.Small, shell.Palette.Label);
        _count.VerticalAlignment = VerticalAlignment.Bottom;
        _count.Margin = new Thickness(0, 0, 0, 2);

        var headingRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(2, 2, 2, 8),
        };
        headingRow.Children.Add(_heading);
        headingRow.Children.Add(_count);

        // The same search box the Processes page uses, filtering by app name
        // (and id and instance unit) through ProcessGrid.Filter.
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
        _status.IsVisible = false;

        // Header row: the same columns as a data row, click to sort.
        _header = new Grid { Margin = new Thickness(2, 0, 4, 2) };
        AddColumns(_header);
        BuildHeaders();

        _list = new StackPanel();
        _scroll = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
            Margin = new Thickness(0, 2, 0, 0),
        };

        // Amendment 1 item 2: while the pointer is over the rows, keep the
        // order they arrived in. Sorting resumes when the pointer leaves.
        _list.PointerEntered += (_, _) =>
        {
            _pointerOverRows = true;
            _frozenOrder ??= _order.ToList();
        };
        _list.PointerExited += (_, _) =>
        {
            if (!_pointerOverRows)
            {
                return;
            }
            _pointerOverRows = false;
            _frozenOrder = null;
            RenderData();
        };

        // Amendment 1 item 5: a visible vertical scrollbar narrows the rows,
        // so the header has to reserve the same width to stay over them.
        _scroll.LayoutUpdated += (_, _) => AlignHeader();

        _empty = Ui.Text(
            string.Empty, shell.Ramp.Body, shell.Palette.Label, wrap: true);
        _empty.Margin = new Thickness(4, 8, 4, 4);
        _empty.HorizontalAlignment = HorizontalAlignment.Left;
        _empty.VerticalAlignment = VerticalAlignment.Top;
        _empty.IsVisible = false;

        var body = new Grid();
        body.Children.Add(_scroll);
        body.Children.Add(_empty);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*") };
        root.Children.Add(headingRow);
        Grid.SetRow(_search, 1);
        root.Children.Add(_search);
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);
        Grid.SetRow(_header, 3);
        root.Children.Add(_header);
        Grid.SetRow(body, 4);
        root.Children.Add(body);
        Content = root;

        Render();
    }

    /// <summary>Re-apply text, language and palette; the rows stay keyed.</summary>
    public void Render()
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        _heading.Text = Ui.Thai(_shell.Language["apps"]);
        _heading.Foreground = Ui.Brush(palette.Text);
        _heading.FontSize = ramp.Heading;

        _count.Foreground = Ui.Brush(palette.Label);
        _count.FontSize = ramp.Small;

        _search.PlaceholderText = Ui.Thai(_shell.Language["apps_search"]);
        Ui.Tip(_search, _shell.Language["apps_search"]);
        _search.Background = Ui.Brush(palette.Control);
        _search.Foreground = Ui.Brush(palette.Text);
        _search.CaretBrush = Ui.Brush(palette.Text);
        _search.PlaceholderForeground = Ui.Brush(palette.Label);
        _search.FontSize = ramp.Body;

        _status.Foreground = Ui.Brush(palette.Label);
        _status.FontSize = ramp.Small;

        _empty.Foreground = Ui.Brush(palette.Label);
        _empty.FontSize = ramp.Body;

        RenderData();
    }

    /// <summary>Update the grid after a new sample or an action, without rebuilding.</summary>
    public void RenderData()
    {
        IReadOnlyList<AppEntry> all = _shell.Snapshot?.Apps ?? Array.Empty<AppEntry>();

        // Keys come from the unfiltered list, so a filter change never gives the
        // same app a different key (and its expanded state) back.
        List<(AppEntry App, string Key)> keyed = Keyed(all);
        var keyOf = new Dictionary<AppEntry, string>(ReferenceEqualityComparer.Instance);
        _rowKeys.Clear();
        foreach ((AppEntry app, string key) in keyed)
        {
            keyOf[app] = key;
            _rowKeys.TryAdd(app.Id, key);
        }

        // Amendment 1 item 3: expansion belongs to the app, not to the current
        // search, so prune only the apps that no longer exist at all.
        var existing = new HashSet<string>(
            keyed.Select(item => item.Key), StringComparer.Ordinal);
        _expanded.RemoveWhere(key => !existing.Contains(key));

        IReadOnlyList<AppEntry> filtered = ProcessGrid.Filter(all, _searchText);
        GridColumn column = ProcessGrid.ParseColumn(_shell.Config.AppsSort, GridColumn.Memory);
        bool descending = _shell.Config.AppsSortDesc;
        IReadOnlyList<AppEntry> apps = ProcessGrid.Sort(filtered, column, descending);

        // Amendment 1 item 2: while the pointer is over the rows, keep the
        // order frozen (values still update below); re-sort when it leaves, a
        // header is clicked or the search text changes. New apps append at the
        // end, gone apps simply drop out.
        if (_frozenOrder is { Count: > 0 } frozen)
        {
            var frozenIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < frozen.Count; index++)
            {
                frozenIndex[frozen[index]] = index;
            }
            apps = apps
                .OrderBy(app => frozenIndex.TryGetValue(keyOf[app], out int index)
                    ? index
                    : int.MaxValue)
                .ToList();
        }

        _order.Clear();
        foreach (AppEntry app in apps)
        {
            _order.Add(keyOf[app]);
        }

        _count.Text = Ui.Thai(_shell.Language.Get("apps_count", apps.Count));
        _empty.Text = Ui.Thai(_shell.Language["apps_empty"]);
        _empty.IsVisible = apps.Count == 0;
        UpdateHeaders(column, descending);

        // Rows that are gone (or filtered out) leave first, so the inserts below
        // land at the right index.
        var live = new HashSet<string>(_order, StringComparer.Ordinal);
        foreach (string key in _rows.Keys.Where(key => !live.Contains(key)).ToList())
        {
            _list.Children.Remove(_rows[key].Control);
            _rows.Remove(key);
        }

        for (int index = 0; index < apps.Count; index++)
        {
            AppEntry app = apps[index];
            string key = keyOf[app];
            if (!_rows.TryGetValue(key, out AppRow? row))
            {
                row = new AppRow(this);
                _rows[key] = row;
            }
            row.App = app;
            row.Key = key;
            row.Update(_expanded.Contains(key));

            if (index < _list.Children.Count && ReferenceEquals(_list.Children[index], row.Control))
            {
                continue;
            }
            _list.Children.Remove(row.Control);
            if (index >= _list.Children.Count)
            {
                _list.Children.Add(row.Control);
            }
            else
            {
                _list.Children.Insert(index, row.Control);
            }
        }

        AlignHeader();
    }

    /// <summary>
    /// One key per app, in list order: the id, plus an occurrence suffix for
    /// two unresolved units that happen to share a name.
    /// </summary>
    private static List<(AppEntry App, string Key)> Keyed(IReadOnlyList<AppEntry> apps)
    {
        var keyed = new List<(AppEntry App, string Key)>(apps.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (AppEntry app in apps)
        {
            string key = app.Id;
            if (!used.Add(key))
            {
                key = app.Id + "\n" + keyed.Count.ToString(CultureInfo.InvariantCulture);
                used.Add(key);
            }
            keyed.Add((app, key));
        }
        return keyed;
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
    /// Amendment 1 item 5: the rows sit inside the ScrollViewer, which is
    /// narrower than the page while its vertical scrollbar is shown, so the
    /// header reserves the same width (plus the row border's own 2/4 px).
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
        var margin = new Thickness(2, 0, 4 + scrollbar, 2);
        if (_header.Margin != margin)
        {
            _header.Margin = margin;
        }
    }

    public bool IsBusy(string appId) => _busy.Contains(KeyOf(appId));

    private string KeyOf(string appId) =>
        _rowKeys.TryGetValue(appId, out string? key) ? key : appId;

    private void SetBusy(string appId, bool busy)
    {
        string key = KeyOf(appId);
        if (busy)
        {
            _busy.Add(key);
        }
        else
        {
            _busy.Remove(key);
        }
        if (_rows.TryGetValue(key, out AppRow? row))
        {
            row.Update(_expanded.Contains(key));
        }
    }

    private void ToggleExpanded(string appId)
    {
        if (!_expanded.Add(appId))
        {
            _expanded.Remove(appId);
        }
        RenderData();
    }

    private void SetStatus(string text)
    {
        _status.Text = Ui.Thai(text);
        _status.IsVisible = _status.Text.Length > 0;
    }

    // ------------------------------------------------------------- actions

    private async void CloseApp(AppEntry app)
    {
        if (IsBusy(app.Id))
        {
            return;
        }

        // Amendment 2 item 3: every awaited step — confirm, the worker, the
        // force prompt and the finish — is wrapped so the row is re-enabled
        // even when one of them throws.
        //
        // Amendment 3 item 2: the busy flag goes up before the confirm is
        // awaited, so a second click while the dialog is open does nothing.
        // The same finally clears it when the person declines.
        try
        {
            SetBusy(app.Id, true);
            if (!await Confirm(_shell.Language.Get("app_close_confirm", app.Name)))
            {
                SetStatus(_shell.Language["mem_cancelled"]);
                return;
            }

            SetStatus(_shell.Language.Get("app_closing", app.Name));
            ActionResult result = await Task.Run(() => _shell.AppActions.CloseApp(app));
            await Finish(app, result, restart: false);
        }
        catch (Exception error)
        {
            SetStatus(_shell.Language.Get("mem_failed", error.Message));
        }
        finally
        {
            SetBusy(app.Id, false);
            _shell.RequestMemoryRefresh();
        }
    }

    private async void RestartApp(AppEntry app)
    {
        if (IsBusy(app.Id) || DesktopEntry.ParseExec(app.Exec) is null)
        {
            return;
        }

        try
        {
            // Amendment 3 item 2: busy before the await, cleared in the finally.
            SetBusy(app.Id, true);
            if (!await Confirm(_shell.Language.Get("app_restart_confirm", app.Name)))
            {
                SetStatus(_shell.Language["mem_cancelled"]);
                return;
            }

            SetStatus(_shell.Language.Get("app_restarting", app.Name));
            ActionResult result = await Task.Run(() => _shell.AppActions.RestartApp(app));
            await Finish(app, result, restart: true);
        }
        catch (Exception error)
        {
            SetStatus(_shell.Language.Get("mem_failed", error.Message));
        }
        finally
        {
            SetBusy(app.Id, false);
            _shell.RequestMemoryRefresh();
        }
    }

    private async void CloseInstance(AppEntry app, AppInstance instance)
    {
        if (IsBusy(app.Id))
        {
            return;
        }

        try
        {
            // Amendment 3 item 2: busy before the await, cleared in the finally.
            SetBusy(app.Id, true);
            if (!await Confirm(_shell.Language.Get("app_close_confirm", app.Name)))
            {
                SetStatus(_shell.Language["mem_cancelled"]);
                return;
            }

            SetStatus(_shell.Language.Get("app_closing", app.Name));
            ActionResult result = await Task.Run(() => _shell.AppActions.CloseInstance(instance));
            await Finish(app, result, restart: false);
        }
        catch (Exception error)
        {
            SetStatus(_shell.Language.Get("mem_failed", error.Message));
        }
        finally
        {
            SetBusy(app.Id, false);
            _shell.RequestMemoryRefresh();
        }
    }

    /// <summary>
    /// The shared end of an action: force prompt for whatever survived, then
    /// one status line. The caller's finally clears the busy flag and asks for
    /// a fresh read, so a faulted step cannot leave the row disabled.
    ///
    /// A restart that needed force is not finished by the force kill alone
    /// (Amendment 1 item 1): once every member is really gone the app is
    /// launched once through <see cref="AppActions.CompleteRestart"/>. Only a
    /// force result that reports the process gone continues to the launch
    /// (Amendment 2 item 2): <see cref="KillState.Cancelled"/>,
    /// <see cref="KillState.Failed"/>, <see cref="KillState.NeedsForce"/> or
    /// anything else stops the restart with a status message and nothing is
    /// started. If the person declines the force, nothing is launched and the
    /// status says the app is still running.
    /// </summary>
    private async Task Finish(AppEntry app, ActionResult result, bool restart)
    {
        string? failure = result.Error;
        bool cancelled = false;

        // True only while every force the person confirmed actually ended its
        // process. One refusal or one non-Gone result blocks the launch.
        bool forceSucceeded = true;

        foreach (PendingKill pending in result.Stubborn)
        {
            string message = _shell.Language.Get(
                "mem_force_kill", pending.Target.Name, pending.Target.Pid);
            if (!await Confirm(message))
            {
                // Declined: this token's handle is closed here, exactly once.
                _shell.CancelKill(pending);
                cancelled = true;
                forceSucceeded = false;
                continue;
            }

            KillResult forced = await Task.Run(() => _shell.ForceKill(pending));
            if (forced.State != KillState.Gone)
            {
                forceSucceeded = false;
                if (forced.State == KillState.Failed
                    && failure is null
                    && !string.IsNullOrEmpty(forced.Error))
                {
                    failure = forced.Error;
                }
            }
        }

        bool stillRunning = false;
        if (restart && result.Outcome == ActionOutcome.NeedsForce)
        {
            if (forceSucceeded)
            {
                // The row is still busy, so the buttons stay disabled while the
                // launch waits for the last member to leave.
                SetStatus(_shell.Language.Get("app_restarting", app.Name));
                ActionResult completed = await Task.Run(
                    () => _shell.AppActions.CompleteRestart(app));
                failure = completed.Error;
            }
            else if (failure is null)
            {
                stillRunning = true;
            }
        }

        SetStatus(
            failure is not null
                ? _shell.Language.Get("mem_failed", failure)
                : stillRunning
                    ? _shell.Language["app_restart_still_running"]
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

    // ------------------------------------------------------------- header

    private void BuildHeaders()
    {
        foreach (GridColumn column in Sortable)
        {
            var cell = new HeaderCell(
                string.Empty,
                _shell.Ramp.Tiny,
                _shell.Palette.Label,
                right: column != GridColumn.Name,
                () => Ui.Act(() =>
                {
                    // A header click always sorts, even while the rows are
                    // frozen under the pointer (Amendment 1 item 2).
                    _frozenOrder = null;
                    _shell.SetAppsSort(column);
                    if (_pointerOverRows)
                    {
                        _frozenOrder = _order.ToList();
                    }
                }));
            _headers[column] = cell;
            Grid.SetColumn(cell.Control, ColumnOf(column));
            _header.Children.Add(cell.Control);
        }
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
    }

    private static string HeaderKey(GridColumn column) => column switch
    {
        GridColumn.Instances => "col_instances",
        GridColumn.Cpu => "col_cpu",
        GridColumn.Memory => "col_memory",
        GridColumn.Gpu => "col_gpu",
        GridColumn.GpuMemory => "col_gpu_memory",
        _ => "col_name",
    };

    /// <summary>The grid column a sortable header sits in (the expander is 0).</summary>
    private static int ColumnOf(GridColumn column) => column switch
    {
        GridColumn.Name => 1,
        GridColumn.Instances => 2,
        GridColumn.Cpu => 3,
        GridColumn.Memory => 4,
        GridColumn.Gpu => 5,
        GridColumn.GpuMemory => 6,
        _ => 1,
    };

    /// <summary>The shared column widths: expander, name, five figures, two actions.</summary>
    private static void AddColumns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition(28, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(76, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(76, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(92, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(76, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(92, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(30, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(30, GridUnitType.Pixel));
    }

    // ----------------------------------------------------------- formatting

    private static string Percent(double? value) =>
        value is double percent ? Format.Decimal(percent, 1) + "%" : "—";

    private static string Bytes(long bytes) => MemoryBoard.Size(bytes);

    private static string BytesOrDash(long? bytes) =>
        bytes is long value ? MemoryBoard.Size(value) : "—";

    private static TextBlock Value(TypeRamp ramp, Palette palette) => Value(
        Ui.Text(string.Empty, ramp.Body, palette.Text, align: TextAlignment.Right));

    private static TextBlock Value(TextBlock block)
    {
        block.VerticalAlignment = VerticalAlignment.Center;
        block.HorizontalAlignment = HorizontalAlignment.Stretch;
        return block;
    }

    // ------------------------------------------------------------ rows

    /// <summary>One app row, updated in place, with its instances beneath it.</summary>
    private sealed class AppRow
    {
        private readonly AppsView _view;
        private readonly Border _border;
        private readonly ContentControl _chevron;
        private readonly Avalonia.Controls.Shapes.Path _appIcon;
        private readonly TextBlock _name;
        private readonly TextBlock _autostart;
        private readonly TextBlock _instances;
        private readonly TextBlock _cpu;
        private readonly TextBlock _memory;
        private readonly TextBlock _gpu;
        private readonly TextBlock _gpuMemory;
        private readonly Border _close;
        private readonly Border _closeSlot;
        private readonly Border _restart;

        /// <summary>
        /// Wraps the restart button so a disabled button can still explain
        /// itself: Avalonia does not hit-test a disabled control, but the
        /// transparent container around it is still hovered (Amendment 1
        /// item 4).
        /// </summary>
        private readonly Border _restartSlot;
        private readonly StackPanel _children;
        private readonly Dictionary<string, InstanceRow> _instanceRows = new(StringComparer.Ordinal);

        public AppRow(AppsView view)
        {
            _view = view;
            TypeRamp ramp = view._shell.Ramp;
            Palette palette = view._shell.Palette;
            Lang lang = view._shell.Language;

            _chevron = new ContentControl
            {
                Width = 24,
                Height = 24,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            var expand = new Border
            {
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = _chevron,
                VerticalAlignment = VerticalAlignment.Center,
            };
            expand.Classes.Add("nodrag");
            expand.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(expand).Properties.IsLeftButtonPressed)
                {
                    return;
                }
                e.Handled = true;
                view.ToggleExpanded(Key);
            };

            _name = Ui.Text(
                string.Empty, ramp.Body, palette.Text, FontWeight.SemiBold, ellipsis: true);
            _autostart = Ui.Text(
                Ui.Thai(lang["apps_autostart"]), ramp.Tiny, palette.Label, ellipsis: true);
            _autostart.IsVisible = false;

            var nameStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 8, 1),
            };
            nameStack.Children.Add(_name);
            nameStack.Children.Add(_autostart);

            _appIcon = (Avalonia.Controls.Shapes.Path)Ui.Icon(
                IconName.App, AppIconSize, palette.Label);
            _appIcon.Margin = new Thickness(0, 0, 8, 0);
            _appIcon.VerticalAlignment = VerticalAlignment.Center;

            var nameCell = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                },
            };
            nameCell.Children.Add(_appIcon);
            Grid.SetColumn(nameStack, 1);
            nameCell.Children.Add(nameStack);

            _instances = Value(ramp, palette);
            _cpu = Value(ramp, palette);
            _memory = Value(ramp, palette);
            _gpu = Value(ramp, palette);
            _gpuMemory = Value(ramp, palette);

            _close = Ui.IconButton(
                IconName.Stop, 26, ramp.Body, Colour.FromHex("#ef4444"),
                () => view.CloseApp(App), lang["app_close"]);
            _restart = Ui.IconButton(
                IconName.Restart, 26, ramp.Body, palette.Label,
                () => view.RestartApp(App), lang["app_restart"]);
            _close.VerticalAlignment = VerticalAlignment.Center;
            _restart.VerticalAlignment = VerticalAlignment.Center;
            _closeSlot = new Border
            {
                Background = Brushes.Transparent,
                Child = _close,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _closeSlot.Classes.Add("nodrag");
            _restartSlot = new Border
            {
                Background = Brushes.Transparent,
                Child = _restart,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // The button's own class is only in the tree below the slot, and a
            // disabled child is not the press source, so mark the slot too.
            _restartSlot.Classes.Add("nodrag");

            var grid = new Grid();
            AddColumns(grid);
            grid.Children.Add(expand);
            Grid.SetColumn(nameCell, 1);
            grid.Children.Add(nameCell);
            Grid.SetColumn(_instances, 2);
            grid.Children.Add(_instances);
            Grid.SetColumn(_cpu, 3);
            grid.Children.Add(_cpu);
            Grid.SetColumn(_memory, 4);
            grid.Children.Add(_memory);
            Grid.SetColumn(_gpu, 5);
            grid.Children.Add(_gpu);
            Grid.SetColumn(_gpuMemory, 6);
            grid.Children.Add(_gpuMemory);
            Grid.SetColumn(_closeSlot, 7);
            grid.Children.Add(_closeSlot);
            Grid.SetColumn(_restartSlot, 8);
            grid.Children.Add(_restartSlot);

            _children = new StackPanel
            {
                IsVisible = false,
                Margin = new Thickness(34, 2, 0, 4),
            };

            var body = new StackPanel();
            body.Children.Add(grid);
            body.Children.Add(_children);

            _border = new Border
            {
                Padding = new Thickness(2, 3, 4, 3),
                BorderBrush = Ui.Brush(Colour.FromHex("#22808080")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = body,
            };
            Control = _border;
        }

        public AppEntry App { get; set; } = new()
        {
            Id = string.Empty,
            Name = string.Empty,
            Instances = Array.Empty<AppInstance>(),
        };

        /// <summary>The key the view stores this row under (usually <see cref="AppEntry.Id"/>).</summary>
        public string Key { get; set; } = string.Empty;

        public Control Control { get; }

        public void Update(bool expanded)
        {
            TypeRamp ramp = _view._shell.Ramp;
            Palette palette = _view._shell.Palette;
            Lang lang = _view._shell.Language;
            bool busy = _view.IsBusy(App.Id);

            _name.Text = App.Name;
            _name.FontSize = ramp.Body;
            _name.Foreground = Ui.Brush(palette.Text);

            _autostart.Text = Ui.Thai(lang["apps_autostart"]);
            _autostart.FontSize = ramp.Tiny;
            _autostart.Foreground = Ui.Brush(palette.Label);
            _autostart.IsVisible = App.Autostart;

            _appIcon.Stroke = Ui.Brush(palette.Label);

            _instances.Text = App.Instances.Count.ToString(CultureInfo.InvariantCulture);
            _cpu.Text = Percent(App.CpuPercent);
            _memory.Text = Bytes(App.MemoryBytes);
            _gpu.Text = Percent(App.GpuPercent);
            _gpuMemory.Text = BytesOrDash(App.GpuMemoryBytes);
            foreach (TextBlock value in new[] { _instances, _cpu, _memory, _gpu, _gpuMemory })
            {
                value.FontSize = ramp.Body;
                value.Foreground = Ui.Brush(palette.Text);
                value.VerticalAlignment = VerticalAlignment.Center;
            }

            bool canRestart = DesktopEntry.ParseExec(App.Exec) is not null;
            _close.IsEnabled = !busy;
            _close.Opacity = busy ? 0.45 : 1;
            // Amendment 1 item 4: restart is really disabled when the Exec is
            // unusable; the container still carries the reason as a tooltip.
            _restart.IsEnabled = !busy && canRestart;
            _restart.Opacity = !busy && canRestart ? 1 : 0.45;
            _restart.Cursor = new Cursor(
                !busy && canRestart ? StandardCursorType.Hand : StandardCursorType.Arrow);
            ToolTip.SetTip(
                _restartSlot,
                canRestart ? lang["app_restart"] : lang["app_restart_unavailable"]);

            _chevron.Content = Ui.Icon(
                expanded ? IconName.ChevronDown : IconName.ChevronRight,
                ramp.NavIcon,
                palette.Label);

            _children.IsVisible = expanded;
            if (expanded)
            {
                SyncInstances();
            }
        }

        /// <summary>Instance rows are keyed by unit, so a read does not rebuild them.</summary>
        private void SyncInstances()
        {
            var live = new HashSet<string>(
                App.Instances.Select(instance => instance.Unit), StringComparer.Ordinal);
            foreach (string unit in _instanceRows.Keys.Where(unit => !live.Contains(unit)).ToList())
            {
                _children.Children.Remove(_instanceRows[unit].Control);
                _instanceRows.Remove(unit);
            }

            for (int index = 0; index < App.Instances.Count; index++)
            {
                AppInstance instance = App.Instances[index];
                if (!_instanceRows.TryGetValue(instance.Unit, out InstanceRow? row))
                {
                    row = new InstanceRow(_view);
                    _instanceRows[instance.Unit] = row;
                }
                row.App = App;
                row.Instance = instance;
                row.Number = index + 1;
                row.Update();

                if (index < _children.Children.Count
                    && ReferenceEquals(_children.Children[index], row.Control))
                {
                    continue;
                }
                _children.Children.Remove(row.Control);
                if (index >= _children.Children.Count)
                {
                    _children.Children.Add(row.Control);
                }
                else
                {
                    _children.Children.Insert(index, row.Control);
                }
            }
        }
    }

    /// <summary>One instance: its label and totals, Close instance, and its processes.</summary>
    private sealed class InstanceRow
    {
        private readonly AppsView _view;
        private readonly StackPanel _control;
        private readonly TextBlock _label;
        private readonly TextBlock _totals;
        private readonly Border _close;
        private readonly StackPanel _processes;
        private readonly Dictionary<int, ProcessLine> _lines = new();

        public InstanceRow(AppsView view)
        {
            _view = view;
            TypeRamp ramp = view._shell.Ramp;
            Palette palette = view._shell.Palette;
            Lang lang = view._shell.Language;

            _label = Ui.Text(
                string.Empty, ramp.Small, palette.Text, FontWeight.SemiBold, ellipsis: true);
            _totals = Ui.Text(string.Empty, ramp.Tiny, palette.Label, ellipsis: true);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_label);
            text.Children.Add(_totals);

            _close = Ui.IconButton(
                IconName.Stop, 24, ramp.Small, Colour.FromHex("#ef4444"),
                () => view.CloseInstance(App, Instance), lang["app_close_instance"]);
            _close.VerticalAlignment = VerticalAlignment.Center;

            var header = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(26, GridUnitType.Pixel),
                },
            };
            // The whole instance header keeps its press (no window drag), which
            // also covers its Close button while that is disabled.
            header.Classes.Add("nodrag");
            header.Children.Add(text);
            Grid.SetColumn(_close, 1);
            header.Children.Add(_close);

            _processes = new StackPanel { Margin = new Thickness(18, 2, 0, 2) };

            _control = new StackPanel();
            _control.Children.Add(header);
            _control.Children.Add(_processes);
            Control = _control;
        }

        public AppEntry App { get; set; } = new()
        {
            Id = string.Empty,
            Name = string.Empty,
            Instances = Array.Empty<AppInstance>(),
        };

        public AppInstance Instance { get; set; } = new()
        {
            Unit = string.Empty,
            Processes = ProcessRow.Empty,
        };

        public int Number { get; set; }

        public Control Control { get; }

        public void Update()
        {
            TypeRamp ramp = _view._shell.Ramp;
            Palette palette = _view._shell.Palette;
            Lang lang = _view._shell.Language;

            _label.Text = Ui.Thai(lang.Get("app_instance", Number));
            _label.FontSize = ramp.Small;
            _label.Foreground = Ui.Brush(palette.Text);
            ToolTip.SetTip(_label, Instance.Unit);

            _totals.Text = Format.Join(
                $"{lang["col_cpu"]} {Percent(Instance.CpuPercent)}",
                $"{lang["col_memory"]} {Bytes(Instance.MemoryBytes)}",
                $"{lang["col_gpu"]} {Percent(Instance.GpuPercent)}");
            _totals.FontSize = ramp.Tiny;
            _totals.Foreground = Ui.Brush(palette.Label);

            // Amendment 1 item 4: an action on the app disables its Close
            // instance button too, so two clicks cannot race one instance.
            bool busy = _view.IsBusy(App.Id);
            _close.IsEnabled = !busy;
            _close.Opacity = busy ? 0.45 : 1;

            // Process lines are keyed by PID and reordered in place, so a 2 s
            // read does not replace the control under the pointer.
            var live = new HashSet<int>(Instance.Processes.Select(process => process.Pid));
            foreach (int pid in _lines.Keys.Where(pid => !live.Contains(pid)).ToList())
            {
                _processes.Children.Remove(_lines[pid].Control);
                _lines.Remove(pid);
            }

            for (int index = 0; index < Instance.Processes.Count; index++)
            {
                ProcessRow process = Instance.Processes[index];
                if (!_lines.TryGetValue(process.Pid, out ProcessLine? line))
                {
                    line = new ProcessLine(_view);
                    _lines[process.Pid] = line;
                }
                line.Process = process;
                line.Update();

                if (index < _processes.Children.Count
                    && ReferenceEquals(_processes.Children[index], line.Control))
                {
                    continue;
                }
                _processes.Children.Remove(line.Control);
                if (index >= _processes.Children.Count)
                {
                    _processes.Children.Add(line.Control);
                }
                else
                {
                    _processes.Children.Insert(index, line.Control);
                }
            }
        }
    }

    /// <summary>One process under an instance: name, PID, CPU, memory.</summary>
    private sealed class ProcessLine
    {
        private readonly AppsView _view;
        private readonly Grid _control;
        private readonly TextBlock _name;
        private readonly TextBlock _pid;
        private readonly TextBlock _cpu;
        private readonly TextBlock _memory;

        public ProcessLine(AppsView view)
        {
            _view = view;
            TypeRamp ramp = view._shell.Ramp;
            Palette palette = view._shell.Palette;

            _name = Ui.Text(
                string.Empty, ramp.Small, palette.Text, ellipsis: true);
            _name.VerticalAlignment = VerticalAlignment.Center;
            _pid = Value(ramp, palette);
            _cpu = Value(ramp, palette);
            _memory = Value(ramp, palette);
            foreach (TextBlock value in new[] { _pid, _cpu, _memory })
            {
                value.FontSize = ramp.Small;
            }

            _control = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(70, GridUnitType.Pixel),
                    new ColumnDefinition(64, GridUnitType.Pixel),
                    new ColumnDefinition(84, GridUnitType.Pixel),
                },
            };
            _control.Children.Add(_name);
            Grid.SetColumn(_pid, 1);
            _control.Children.Add(_pid);
            Grid.SetColumn(_cpu, 2);
            _control.Children.Add(_cpu);
            Grid.SetColumn(_memory, 3);
            _control.Children.Add(_memory);
            Control = _control;
        }

        public ProcessRow Process { get; set; } = new()
        {
            Pid = 0,
            Name = string.Empty,
        };

        public Control Control { get; }

        public void Update()
        {
            Palette palette = _view._shell.Palette;
            _name.Text = Process.Name;
            _name.Foreground = Ui.Brush(palette.Text);
            _pid.Text = Process.Pid.ToString(CultureInfo.InvariantCulture);
            _cpu.Text = Percent(Process.CpuPercent);
            _memory.Text = Bytes(Process.MemoryBytes);
            foreach (TextBlock value in new[] { _pid, _cpu, _memory })
            {
                value.Foreground = Ui.Brush(palette.Label);
            }
        }
    }

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
}
