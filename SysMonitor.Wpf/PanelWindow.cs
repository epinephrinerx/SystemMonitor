using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SysMonitor;

/// <summary>
/// A small window of its own for settings, the manual and the about page.
///
/// Ordinary window chrome rather than the widget's borderless panel: these are
/// pages to read and close, not part of the instrument. The body is built by
/// the main window, which owns the brushes and the helpers that keep it
/// looking like the sidebar, so this class only hosts it.
/// </summary>
internal sealed class PanelWindow : Window
{
    private readonly ScrollViewer _scroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    public PanelWindow(Window owner, string title, double width, double height,
                       Brush background, bool resizable)
    {
        Owner = owner;
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 280;
        MinHeight = 240;
        Background = background;
        ShowInTaskbar = false;
        ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        Content = _scroll;

        // Escape closes it, the way every small Windows dialog does.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>Replace the page, keeping the scroll position where it was.</summary>
    public void SetBody(UIElement body, string title)
    {
        Title = title;
        double offset = _scroll.VerticalOffset;
        _scroll.Content = new Border { Padding = new Thickness(18), Child = body };
        _scroll.ScrollToVerticalOffset(offset);
    }
}
