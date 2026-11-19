using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

/// <summary>
/// The Settings, Manual and About windows (spec section 10): normal OS chrome,
/// owned by the main window, centred on it, one instance per page, Esc closes.
/// The content is a row list, so the three pages share one window class.
/// </summary>
internal sealed class PanelWindow : Window
{
    private readonly ShellModel _shell;
    private readonly Func<ShellModel, IReadOnlyList<UiRow>> _rows;
    private readonly Action<string> _dispatch;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _body;
    private readonly List<DialView> _dials = new();

    public PanelWindow(
        ShellModel shell,
        string id,
        string title,
        Size size,
        bool resizable,
        Func<ShellModel, IReadOnlyList<UiRow>> rows,
        Action<string> dispatch)
    {
        Id = id;
        _shell = shell;
        _rows = rows;
        _dispatch = dispatch;

        Title = title;
        Width = size.Width;
        Height = size.Height;
        MinWidth = 280;
        MinHeight = 240;
        CanResize = resizable;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = Ui.Family;

        _body = new StackPanel();
        _scroll = new ScrollViewer
        {
            Content = _body,
            Padding = new Thickness(18),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
        };
        Content = _scroll;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };

        Render();
    }

    public string Id { get; }

    /// <summary>Rebuild the page, keeping the scroll position (spec section 10).</summary>
    public void Render()
    {
        double offset = _scroll.Offset.Y;
        _dials.Clear();
        _body.Children.Clear();

        Title = Id switch
        {
            WindowIds.Settings => _shell.Language["settings"],
            WindowIds.Manual => _shell.Language["manual"],
            _ => _shell.Language["about"],
        };
        Background = Ui.Brush(_shell.Palette.Panel);
        RequestedThemeVariant = App.ThemeFor(_shell.Config.Theme);
        RowRenderer.Fill(_body, _rows(_shell), _shell, _dispatch, _dials);

        if (offset > 0)
        {
            _scroll.Offset = new Vector(0, offset);
        }
    }

    /// <summary>Move the dials to the model after the other window changed one.</summary>
    public void SyncDials()
    {
        foreach (DialView dial in _dials)
        {
            dial.SetValue(_shell.DialValue(dial.Key));
        }
    }

    /// <summary>The page sizes from spec section 10.</summary>
    public static Size SizeFor(string id) => id switch
    {
        WindowIds.Settings => new Size(360, 600),
        WindowIds.Manual => new Size(460, 560),
        WindowIds.About => new Size(380, 440),
        _ => new Size(360, 480),
    };

    public static bool IsResizable(string id) => id != WindowIds.About;
}
