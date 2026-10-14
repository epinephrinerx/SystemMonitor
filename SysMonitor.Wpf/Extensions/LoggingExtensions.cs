namespace SysMonitor.Extensions;

/// <summary>
/// Helper for safely invoking logging delegates without letting logger exceptions propagate.
/// </summary>
internal static class LoggingExtensions
{
    /// <summary>
    /// An exception's Message is extension code when the exception type is the extension's own,
    /// and it can throw. Never let that escape from a catch block.
    /// </summary>
    public static string Describe(this Exception ex)
    {
        try
        {
            return ex.Message;
        }
        catch
        {
            return ex.GetType().FullName ?? "exception";
        }
    }

    public static void SafeLog(this Action<string>? log, string message)
    {
        if (log == null)
        {
            return;
        }

        try
        {
            log(message);
        }
        catch
        {
            // Logging failures must never propagate or interrupt cleanup/execution flow
        }
    }
}
