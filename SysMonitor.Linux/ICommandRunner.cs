namespace SysMonitor.Linux;

/// <summary>
/// The exit code and both output streams of one finished command. A command
/// that could not be started or that timed out is null instead, because it has
/// no exit code to report.
/// </summary>
public readonly record struct CommandResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// The only way the sampler starts another process. <see cref="Run"/> reports
/// a missing binary, a non-zero exit and a timeout the same way: null. Callers
/// that have to show why a command failed use <see cref="RunResult"/>.
/// </summary>
public interface ICommandRunner
{
    /// <summary>
    /// Run <paramref name="file"/> with <paramref name="args"/> and return its
    /// standard output, or null when it could not be run, failed, or exceeded
    /// <paramref name="timeoutMs"/>.
    /// </summary>
    string? Run(string file, string args, int timeoutMs);

    /// <summary>
    /// Run with an argument list. Unit names and paths reach the program as
    /// separate argv entries, so nothing is split, quoted or interpreted by a
    /// shell. The default joins the list for callers that only implement the
    /// string form; <see cref="ProcessCommandRunner"/> passes it through as-is.
    /// </summary>
    string? Run(string file, IReadOnlyList<string> args, int timeoutMs) =>
        Run(file, JoinArguments(args), timeoutMs);

    /// <summary>
    /// Run with an argument list and report the exit code, stdout and stderr.
    /// Null means the program could not be started or ran past the timeout.
    /// The default suits a test double that only implements <see cref="Run"/>:
    /// its success becomes exit 0 with no stderr.
    /// </summary>
    CommandResult? RunResult(string file, IReadOnlyList<string> args, int timeoutMs) =>
        Run(file, args, timeoutMs) is { } output
            ? new CommandResult(0, output, string.Empty)
            : new CommandResult(-1, string.Empty, string.Empty);

    /// <summary>Quote an argument list for the string form, for test doubles.</summary>
    static string JoinArguments(IReadOnlyList<string> args) =>
        args.Count == 0
            ? string.Empty
            : string.Join(' ', args.Select(QuoteArgument));

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0
            && argument.IndexOfAny(new[] { ' ', '\t', '"', '\'', '\\', '$', '`' }) < 0)
        {
            return argument;
        }
        return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
