namespace SysMonitor.Linux.Presentation;

/// <summary>
/// When the RAM tab's two tables are read (spec section 5c): processes at most
/// every 3 s, services at most every 10 s, and nothing at all while the tab is
/// not showing. The Apps and Processes grids have their own 2 s cadence and are
/// read only while one of those two pages is showing. A forced read (after a
/// kill, a service action or entering a page) ignores the intervals once.
/// </summary>
public static class MemoryCadence
{
    public const int ProcessMs = 3000;
    public const int ServiceMs = 10000;

    /// <summary>The Apps and Processes grids: 2 s, as R-030 phase C asks.</summary>
    public const int GridMs = 2000;

    /// <summary>What one sample should read.</summary>
    public readonly record struct Plan(bool Processes, bool Services, bool Grid);

    public static Plan Next(
        long now,
        long lastProcessMs,
        long lastServiceMs,
        bool onRamTab,
        bool forced = false,
        bool onGridPage = false,
        long lastGridMs = 0,
        bool gridForced = false)
    {
        bool grid = onGridPage && (gridForced || Due(now, lastGridMs, GridMs));

        if (!onRamTab)
        {
            return new Plan(false, false, grid);
        }

        bool processes = forced || Due(now, lastProcessMs, ProcessMs);
        bool services = forced || Due(now, lastServiceMs, ServiceMs);
        return new Plan(processes, services, grid);
    }

    /// <summary>
    /// True when the interval has passed. A stamp of zero (never read) is due;
    /// a stamp in the future (the clock went backwards) is due too.
    /// </summary>
    public static bool Due(long now, long lastMs, int intervalMs) =>
        lastMs <= 0 || now < lastMs || now - lastMs >= intervalMs;
}
