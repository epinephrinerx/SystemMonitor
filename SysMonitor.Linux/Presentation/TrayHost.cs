namespace SysMonitor.Linux.Presentation;

/// <summary>
/// Whether the desktop has a StatusNotifier host (Amendment 2, item 5). The
/// tray icon is a StatusNotifierItem, so with no watcher it cannot appear, and
/// hiding the window would leave no way back.
///
/// One read-only <c>busctl</c> call answers it. A missing program, a timeout
/// and a non-zero exit all mean "no host": only exit 0 counts as available.
/// It lives here rather than in the app so a fake
/// <see cref="ICommandRunner"/> can prove the argv and every failure branch.
/// </summary>
public static class TrayHost
{
    public const string Program = "busctl";
    public const string Service = "org.kde.StatusNotifierWatcher";
    public const int TimeoutMs = 2000;

    /// <summary><c>busctl --user --no-pager status org.kde.StatusNotifierWatcher</c>.</summary>
    public static IReadOnlyList<string> Arguments { get; } = new[]
    {
        "--user",
        "--no-pager",
        "status",
        Service,
    };

    public static bool IsAvailable(ICommandRunner runner) => IsAvailable(runner, TimeoutMs);

    public static bool IsAvailable(ICommandRunner runner, int timeoutMs)
    {
        try
        {
            CommandResult? result = runner.RunResult(Program, Arguments, timeoutMs);
            return result is { Ok: true };
        }
        catch (Exception)
        {
            // A runner that throws is as unavailable as one that says no.
            return false;
        }
    }
}
