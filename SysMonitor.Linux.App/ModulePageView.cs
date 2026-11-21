using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// One module page (R-030 §2b), top to bottom: the device name and module
/// label, the options bar, Usage, Sensors and Properties. Every card is keyed,
/// so a fresh sample updates its number and its graph in place; only a new page
/// or a new language rebuilds the controls.
///
/// The model is <see cref="FullTab"/>; this file draws and wires.
/// </summary>
internal sealed class ModulePageView : UserControl
{
    private readonly ShellModel _shell;
    private readonly Action<string> _dispatch;

    private readonly StackPanel _page;
    private readonly Dictionary<string, BigCardView> _big = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SmallCardView> _small = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BigCardView> _sensors = new(StringComparer.Ordinal);
    private readonly List<TextBlock> _properties = new();
    private readonly List<string> _propertyTitles = new();
    private readonly List<string> _sections = new();

    private FullTab? _tab;
    private string _shownKey = string.Empty;

    public ModulePageView(ShellModel shell, Action<string> dispatch)
    {
        _shell = shell;
        _dispatch = dispatch;

        _page = new StackPanel();
        Content = new ScrollViewer
        {
            Content = _page,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
        };
    }

    /// <summary>"Usage", "Sensors" and "Properties", as the page actually shows them.</summary>
    public IReadOnlyList<string> SectionTitles => _sections;

    /// <summary>A structural render: the page, its palette and its language.</summary>
    public void Render(FullTab? tab)
    {
        _tab = tab;
        Rebuild();
    }

    /// <summary>A data tick: numbers and graphs, in place.</summary>
    public void UpdateData(FullTab? tab)
    {
        if (tab is null)
        {
            if (_tab is not null)
            {
                _tab = null;
                Rebuild();
            }
            return;
        }

        if (_tab is null
            || !string.Equals(tab.Key, _shownKey, StringComparison.Ordinal)
            || !SameKeys(_big, tab.BigCards)
            || !SameKeys(_small, tab.SmallCards)
            || !SameSensors(tab)
            || !SameProperties(tab))
        {
            _tab = tab;
            Rebuild();
            return;
        }

        _tab = tab;
        foreach (FullCard card in tab.BigCards)
        {
            if (_big.TryGetValue(card.Key, out BigCardView? view))
            {
                view.Update(card, _shell);
            }
        }
        foreach (FullCard card in tab.SmallCards)
        {
            if (_small.TryGetValue(card.Key, out SmallCardView? view))
            {
                view.Update(card, _shell);
            }
        }
        foreach (FullCard card in tab.Sensors)
        {
            if (_sensors.TryGetValue(card.Key, out BigCardView? view))
            {
                view.Update(card, _shell);
            }
        }
        for (int i = 0; i < tab.Properties.Count && i < _properties.Count; i++)
        {
            PageParts.UpdateProperty(_properties[i], tab.Properties[i], _shell);
        }
    }

    // ------------------------------------------------------------- pieces

    private void Rebuild()
    {
        _page.Children.Clear();
        _big.Clear();
        _small.Clear();
        _sensors.Clear();
        _properties.Clear();
        _propertyTitles.Clear();
        _sections.Clear();
        _shownKey = _tab?.Key ?? string.Empty;

        FullTab? tab = _tab;
        if (tab is null)
        {
            _page.Children.Add(Ui.Text(
                _shell.Language["no_data"],
                _shell.Ramp.Body,
                _shell.Palette.Label,
                wrap: true));
            return;
        }

        _page.Children.Add(Header(tab));

        if (tab.Options.Count > 0)
        {
            _page.Children.Add(OptionsBar(tab));
        }

        _sections.Add(_shell.Language["usage"]);
        _page.Children.Add(PageParts.Title(_shell, _shell.Language["usage"]));

        foreach (FullCard card in tab.BigCards)
        {
            var view = new BigCardView(card, _shell);
            _big[card.Key] = view;
            _page.Children.Add(view.Control);
        }

        if (tab.SmallCards.Count > 0)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (FullCard card in tab.SmallCards)
            {
                var view = new SmallCardView(card, _shell);
                _small[card.Key] = view;
                wrap.Children.Add(view.Control);
            }
            _page.Children.Add(wrap);
        }

        if (tab.Sensors.Count > 0)
        {
            _sections.Add(_shell.Language["sensors"]);
            _page.Children.Add(PageParts.Title(_shell, _shell.Language["sensors"]));
            foreach (FullCard card in tab.Sensors)
            {
                var view = new BigCardView(card, _shell);
                _sensors[card.Key] = view;
                _page.Children.Add(view.Control);
            }
        }

        if (tab.Properties.Count > 0)
        {
            _sections.Add(_shell.Language["properties"]);
            _page.Children.Add(PageParts.Title(_shell, _shell.Language["properties"]));

            var list = new StackPanel();
            foreach (FullFact fact in tab.Properties)
            {
                Control row = PageParts.Property(fact, _shell, out TextBlock value);
                _properties.Add(value);
                _propertyTitles.Add(fact.Title);
                list.Children.Add(row);
            }
            _page.Children.Add(new Border
            {
                Background = Ui.Brush(_shell.Palette.Control),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Child = list,
            });
        }
    }

    /// <summary>The device name, the module label under it, and the disk tools.</summary>
    private Control Header(FullTab tab)
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        string device = tab.Hardware.Length > 0 ? tab.Hardware : tab.Title;
        TextBlock title = Ui.Text(
            Ui.Thai(device), ramp.Heading, palette.Text, FontWeight.Bold, ellipsis: true);
        TextBlock label = Ui.Text(Ui.Thai(tab.Label), ramp.Small, palette.Label, ellipsis: true);

        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        body.Children.Add(title);
        body.Children.Add(label);

        if (!tab.IsDisk)
        {
            return body;
        }

        // The old Full rail's behaviour stays on the page header: double-click
        // opens the volume (or the menu when a disk has several), right-click
        // opens the drive menu.
        body.Cursor = new Cursor(StandardCursorType.Hand);
        body.Classes.Add("nodrag");
        var menu = new ContextMenu();
        menu.Opening += (_, _) => FillDriveMenu(menu, tab);
        body.ContextMenu = menu;
        body.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            OpenDiskTab(tab, body, menu);
        };
        return body;
    }

    /// <summary>The §2 options bar: one compact checkbox per setting.</summary>
    private Control OptionsBar(FullTab tab)
    {
        Palette palette = _shell.Palette;
        TypeRamp ramp = _shell.Ramp;

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (ModuleOption option in tab.Options)
        {
            string key = option.ConfigKey;
            var check = new CheckBox
            {
                IsChecked = option.Value,
                Content = Ui.Text(
                    Ui.Thai(_shell.Language[option.LabelKey]), ramp.Small, palette.Muted),
                FontSize = ramp.Small,
                Foreground = Ui.Brush(palette.Muted),
                Margin = new Thickness(0, 2, 14, 2),
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            check.Classes.Add("nodrag");
            check.IsCheckedChanged += (_, _) =>
            {
                bool value = check.IsChecked == true;
                Ui.Act(() => _shell.SetOption(key, value));
            };
            bar.Children.Add(check);
        }
        return bar;
    }

    // ------------------------------------------------------------ disks

    private void OpenDiskTab(FullTab tab, Control anchor, ContextMenu menu)
    {
        if (tab.Mounts.Count == 1)
        {
            DiskTools.OpenMount(tab.Mounts[0]);
            return;
        }
        FillDriveMenu(menu, tab);
        menu.Open(anchor);
    }

    /// <summary>
    /// Build the drive actions into the menu that is about to open, so a
    /// right-click and the double-click path both use that one menu.
    /// </summary>
    private void FillDriveMenu(ContextMenu menu, FullTab tab)
    {
        menu.Items.Clear();
        foreach (string mount in tab.Mounts)
        {
            menu.Items.Add(MenuItemFor(
                _shell.Language.Get("disk_open_explorer", mount),
                () => DiskTools.OpenMount(mount)));
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
    }

    private static MenuItem MenuItemFor(string label, Action action)
    {
        var item = new MenuItem { Header = Ui.Thai(label) };
        item.Click += (_, _) => action();
        return item;
    }

    // --------------------------------------------------------- key checks

    private static bool SameKeys(
        Dictionary<string, BigCardView> views, IReadOnlyList<FullCard> cards)
    {
        if (views.Count != cards.Count)
        {
            return false;
        }
        foreach (FullCard card in cards)
        {
            if (!views.ContainsKey(card.Key))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SameKeys(
        Dictionary<string, SmallCardView> views, IReadOnlyList<FullCard> cards)
    {
        if (views.Count != cards.Count)
        {
            return false;
        }
        foreach (FullCard card in cards)
        {
            if (!views.ContainsKey(card.Key))
            {
                return false;
            }
        }
        return true;
    }

    private bool SameSensors(FullTab tab) => SameKeys(_sensors, tab.Sensors);

    /// <summary>
    /// Amendment 3, item 2: rows are keyed by position, so duplicate titles
    /// ("Mouse" twice) still update in place. Rebuild only when the titles, in
    /// order, are different.
    /// </summary>
    private bool SameProperties(FullTab tab)
    {
        if (_propertyTitles.Count != tab.Properties.Count)
        {
            return false;
        }
        for (int i = 0; i < _propertyTitles.Count; i++)
        {
            if (!string.Equals(_propertyTitles[i], tab.Properties[i].Title, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
