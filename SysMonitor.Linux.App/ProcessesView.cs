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
/// The Processes page (R-030 §2): two tabs, "Processes" and "Services". The
/// process groups and service rows are the ones the old Full view's memory tab
/// drew, moved here unchanged — keyed, updated in place, with the same search,
/// kill/force/confirm flow and status line. Phase C replaces the process list
/// with the new grid; this file keeps today's behaviour until then.
/// </summary>
internal sealed class ProcessesView : UserControl
{
    private const string GraphBackground = "#223b82f6";

    private readonly ShellModel _shell;

    private readonly Border _processesTab;
    private readonly TextBlock _processesTabText;
    private readonly Border _servicesTab;
    private readonly TextBlock _servicesTabText;
    private readonly ScrollViewer _processesPage;
    private readonly ScrollViewer _servicesPage;

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
    private readonly Dictionary<string, GroupRow> _groupRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServiceRow> _userRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ServiceRow> _systemRows = new(StringComparer.Ordinal);

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

        // Processes tab.
        _processHeading = Ui.Text(
            string.Empty, shell.Ramp.Heading, shell.Palette.Text, FontWeight.SemiBold, wrap: true);
        _processHeading.Margin = new Thickness(2, 2, 2, 8);

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
        _status.Margin = new Thickness(2, 0, 2, 4);

        _groupNote = Ui.Text(string.Empty, shell.Ramp.Small, shell.Palette.Label, wrap: true);
        _groupNote.Margin = new Thickness(2, 6, 2, 2);

        _processList = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        var processesBody = new StackPanel();
        processesBody.Children.Add(_processHeading);
        processesBody.Children.Add(_search);
        processesBody.Children.Add(_groupNote);
        processesBody.Children.Add(_processList);
        _processesPage = Page(processesBody);

        // Services tab.
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

        RenderMemory();
    }

    /// <summary>Update the tables after a new sample or a kill.</summary>
    public void RenderData() => RenderMemory();

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
            RenderMemory();
        });

    // ------------------------------------------------------------ tables

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

    // ------------------------------------------------------- process rows

    /// <summary>One process group row, updated in place.</summary>
    private sealed class GroupRow
    {
        private readonly ProcessesView _view;
        private readonly Border _border;
        private readonly ContentControl _expandGlyph;
        private readonly TextBlock _name;
        private readonly TextBlock _count;
        private readonly TextBlock _size;
        private readonly Border _killGroup;
        private readonly StackPanel _members;

        public GroupRow(ProcessesView view)
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
