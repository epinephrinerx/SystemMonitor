namespace SysMonitor.Linux.Presentation;

/// <summary>The three stacked views. Only one is visible at a time.</summary>
public enum ViewMode
{
    Widget,
    Overall,
    Full,
}

public static class ViewModeExtensions
{
    public static string ToKey(this ViewMode mode) => mode switch
    {
        ViewMode.Overall => "overall",
        ViewMode.Full => "full",
        _ => "widget",
    };

    public static ViewMode Parse(string? value, ViewMode fallback) => value switch
    {
        "overall" => ViewMode.Overall,
        "full" => ViewMode.Full,
        "widget" => ViewMode.Widget,
        _ => fallback,
    };
}

/// <summary>
/// Which input leads where. Kept apart from the window so every row of the
/// spec's switching table can be exercised without a pointer.
/// </summary>
public sealed class ViewState
{
    public ViewState(ViewMode mode)
    {
        Mode = mode;
    }

    public ViewMode Mode { get; private set; }

    /// <summary>Double-click anywhere the child did not handle it.</summary>
    public bool DoubleClick() => Move(Mode switch
    {
        ViewMode.Widget => ViewMode.Overall,
        ViewMode.Overall => ViewMode.Full,
        _ => ViewMode.Widget,
    });

    public bool F11() => Move(Mode switch
    {
        ViewMode.Full => ViewMode.Overall,
        _ => ViewMode.Full,
    });

    /// <summary>Esc steps back; in Widget it is a no-op.</summary>
    public bool Escape() => Mode == ViewMode.Widget ? false : Move(Mode == ViewMode.Full ? ViewMode.Overall : ViewMode.Widget);

    /// <summary>The context menu's "collapse" in Overall, "expand" elsewhere.</summary>
    public bool ContextExpand() => Move(Mode switch
    {
        ViewMode.Widget => ViewMode.Overall,
        ViewMode.Overall => ViewMode.Widget,
        _ => ViewMode.Overall,
    });

    /// <summary>The context menu's full-screen item.</summary>
    public bool ContextFullScreen() => Move(Mode == ViewMode.Full ? ViewMode.Overall : ViewMode.Full);

    // Overall header: Full (E740), Widget (E745) and close, which is separate.
    public bool OverallHeaderFull() => Move(ViewMode.Full);
    public bool OverallHeaderWidget() => Move(ViewMode.Widget);

    // Overall sidebar navigation.
    public bool SidebarFullData() => Move(ViewMode.Full);
    public bool SidebarOpenWidget() => Move(ViewMode.Widget);

    // Full header: Overall (E80F), Widget (E745) and close.
    public bool FullHeaderOverall() => Move(ViewMode.Overall);
    public bool FullHeaderWidget() => Move(ViewMode.Widget);
    public bool FullRailOverall() => Move(ViewMode.Overall);
    public bool FullRailWidget() => Move(ViewMode.Widget);

    /// <summary>True when the mode actually changed.</summary>
    public bool Move(ViewMode mode)
    {
        if (mode == Mode)
        {
            return false;
        }
        Mode = mode;
        return true;
    }

    public bool IsWidget => Mode == ViewMode.Widget;

    /// <summary>
    /// What the main context menu calls the first item: collapse in Overall,
    /// the expand hint everywhere else.
    /// </summary>
    public string FirstMenuItemKey => Mode == ViewMode.Overall ? "collapse" : "expand_hint";

    public string FullScreenItemKey => Mode == ViewMode.Full ? "exit_fullscreen" : "fullscreen";
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
