namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The two windows R-030 keeps. The Widget stays a small window of its own; the
/// old Overall and Full views become pages inside one Main window, selected in
/// <see cref="MainState"/>.
/// </summary>
public enum ViewMode
{
    Widget,
    Main,

    /// <summary>
    /// Removed in R-030: the old Overall view is Main's "overall" page. Kept as
    /// a value nothing produces, so code and tests written against the
    /// three-view build still compile.
    /// </summary>
    Overall = 100,

    /// <summary>Removed in R-030: the old Full view is a page per module now.</summary>
    Full = 101,
}

public static class ViewModeExtensions
{
    /// <summary>The config key: "widget" or "main". Legacy values read as main.</summary>
    public static string ToKey(this ViewMode mode) => mode == ViewMode.Widget ? "widget" : "main";

    /// <summary>
    /// "main" is the new key; "overall" and "full" are the two views it
    /// replaced, and both mean Main now.
    /// </summary>
    public static ViewMode Parse(string? value, ViewMode fallback) => value switch
    {
        "main" => ViewMode.Main,
        "overall" => ViewMode.Main,
        "full" => ViewMode.Main,
        "widget" => ViewMode.Widget,
        _ => fallback,
    };
}

/// <summary>
/// Which input leads where, kept apart from the window so every switching rule
/// can be exercised without a pointer.
///
/// R-030: Double-click is Widget &lt;-&gt; Main, F11 is full screen (from the
/// Widget it opens Main full screen first), Esc leaves full screen and then
/// steps back to the Widget, and Esc in the Widget does nothing.
/// </summary>
public sealed class ViewState
{
    public ViewState(ViewMode mode)
    {
        Mode = mode == ViewMode.Main ? ViewMode.Main
            : mode == ViewMode.Widget ? ViewMode.Widget
            : mode;
    }

    public ViewMode Mode { get; private set; }

    /// <summary>True while Main covers the screen (F11). Separate from the mode.</summary>
    public bool MainFullScreen { get; private set; }

    /// <summary>Double-click anywhere the child did not handle it.</summary>
    public bool DoubleClick() => Mode == ViewMode.Widget ? Move(ViewMode.Main) : Move(ViewMode.Widget);

    /// <summary>
    /// From the Widget: Main, full screen. In Main: toggle full screen. A
    /// legacy mode is treated as Main.
    /// </summary>
    public bool F11()
    {
        if (Mode == ViewMode.Widget)
        {
            SetFullScreen(true);
            return Move(ViewMode.Main);
        }
        SetFullScreen(!MainFullScreen);
        return true;
    }

    /// <summary>Esc steps back; in Widget it is a no-op.</summary>
    public bool Escape()
    {
        if (Mode == ViewMode.Widget)
        {
            return false;
        }
        if (MainFullScreen)
        {
            SetFullScreen(false);
            return true;
        }
        return Move(ViewMode.Widget);
    }

    /// <summary>The context menu's "expand"/"collapse": Widget &lt;-&gt; Main.</summary>
    public bool ContextExpand() => Mode == ViewMode.Widget ? Move(ViewMode.Main) : Move(ViewMode.Widget);

    /// <summary>The context menu's full-screen item.</summary>
    public bool ContextFullScreen() => F11();

    /// <summary>The header's full-screen button.</summary>
    public bool ToggleFullScreen() => F11();

    // ------------------------------------------------------------- legacy
    // The Overall and Full views are replaced in phase B2. These stand-ins keep
    // the old buttons compiling until then; each one lands on the view that
    // still exists.

    public bool OverallHeaderFull() => Move(ViewMode.Main);
    public bool OverallHeaderWidget() => Move(ViewMode.Widget);
    public bool SidebarFullData() => Move(ViewMode.Main);
    public bool SidebarOpenWidget() => Move(ViewMode.Widget);
    public bool FullHeaderOverall() => Move(ViewMode.Main);
    public bool FullHeaderWidget() => Move(ViewMode.Widget);
    public bool FullRailOverall() => Move(ViewMode.Main);
    public bool FullRailWidget() => Move(ViewMode.Widget);

    /// <summary>True when the mode actually changed.</summary>
    public bool Move(ViewMode mode)
    {
        if (mode == ViewMode.Widget && Mode != ViewMode.Widget)
        {
            SetFullScreen(false);
        }
        if (mode == Mode)
        {
            return false;
        }
        Mode = mode;
        return true;
    }

    public bool IsWidget => Mode == ViewMode.Widget;

    /// <summary>What the main context menu calls the first item.</summary>
    public string FirstMenuItemKey => Mode == ViewMode.Main ? "collapse" : "expand_hint";

    public string FullScreenItemKey => MainFullScreen ? "exit_fullscreen" : "fullscreen";

    private void SetFullScreen(bool full)
    {
        if (MainFullScreen != full)
        {
            MainFullScreen = full;
        }
    }
}

/// <summary>
/// Widget page rotation: five seconds, checked on the 400 ms UI tick.
/// Pausing is in-memory only, stepping keeps the pause, and resuming resets
/// the timer.
/// </summary>
public sealed class RotationState
{
    public const int RotateEveryMs = 5000;

    private IReadOnlyList<WidgetPage> _pages = Array.Empty<WidgetPage>();
    private int _index;
    private long _lastRotateMs;

    public bool Paused { get; private set; }
    public int Index => _index;
    public IReadOnlyList<WidgetPage> Pages => _pages;
    public long LastRotateMs => _lastRotateMs;

    public WidgetPage? Page => _pages.Count == 0 ? null : _pages[Math.Clamp(_index, 0, _pages.Count - 1)];

    /// <summary>
    /// Install a new page list. The current page is kept when it is still
    /// there, otherwise the old index is clamped to the smaller list. The
    /// five-second timer is left alone: a new snapshot must not postpone the
    /// rotation the user is waiting for.
    /// </summary>
    public void SetPages(IReadOnlyList<WidgetPage> pages)
    {
        string? currentKey = Page?.Key;
        int previous = _index;
        _pages = pages;
        _index = Math.Clamp(previous, 0, Math.Max(0, pages.Count - 1));
        if (currentKey is not null)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                if (string.Equals(pages[i].Key, currentKey, StringComparison.Ordinal))
                {
                    _index = i;
                    break;
                }
            }
        }
        if (_pages.Count <= 1)
        {
            _index = 0;
        }
    }

    /// <summary>Start the five seconds again, e.g. when entering the Widget.</summary>
    public void ResetTimer(long nowMs) => _lastRotateMs = nowMs;

    /// <summary>Step one page, wrapping. Always resets the five-second timer.</summary>
    public void Next(long nowMs)
    {
        if (_pages.Count > 1)
        {
            _index = (_index + 1) % _pages.Count;
        }
        _lastRotateMs = nowMs;
    }

    public void Prev(long nowMs)
    {
        if (_pages.Count > 1)
        {
            _index = (_index - 1 + _pages.Count) % _pages.Count;
        }
        _lastRotateMs = nowMs;
    }

    /// <summary>Pausing keeps the page; resuming starts a fresh five seconds.</summary>
    public void TogglePause(long nowMs)
    {
        Paused = !Paused;
        _lastRotateMs = nowMs;
    }

    /// <summary>
    /// Called on the 400 ms tick. Returns true when the page changed, so the
    /// caller only redraws when there is something new.
    /// </summary>
    public bool Tick(long nowMs, bool isWidgetView)
    {
        if (Paused || !isWidgetView || _pages.Count <= 1)
        {
            return false;
        }
        if (nowMs - _lastRotateMs < RotateEveryMs)
        {
            return false;
        }
        _index = (_index + 1) % _pages.Count;
        _lastRotateMs = nowMs;
        return true;
    }

    /// <summary>True when the timer is due -- used to decide whether to redraw.</summary>
    public bool IsDue(long nowMs, bool isWidgetView) =>
        !Paused && isWidgetView && _pages.Count > 1 && nowMs - _lastRotateMs >= RotateEveryMs;
}
