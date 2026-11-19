using System.Diagnostics;

namespace SysMonitor.Linux;

/// <summary>
/// Runs a command with <see cref="Process"/> and gives up after the timeout.
/// Output only: the callers here ask for a few lines of text.
/// </summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public string? Run(string file, string args, int timeoutMs)
    {
        var start = Start(file);
        if (args.Length > 0)
        {
            start.Arguments = args;
        }
        return Execute(start, timeoutMs) is { Ok: true } result ? result.Output : null;
    }

    /// <summary>
    /// The same run, with the arguments handed to the program one by one. A
    /// unit name that contains a space or a quote cannot become two arguments
    /// or a shell command.
    /// </summary>
    public string? Run(string file, IReadOnlyList<string> args, int timeoutMs) =>
        Execute(Start(file, args), timeoutMs) is { Ok: true } result ? result.Output : null;

    /// <summary>
    /// The same run, keeping a non-zero exit and its stderr: a service action
    /// has to say why systemd refused it. Only a program that never ran (or
    /// overran the timeout) is null.
    /// </summary>
    public CommandResult? RunResult(string file, IReadOnlyList<string> args, int timeoutMs) =>
        Execute(Start(file, args), timeoutMs);

    private static ProcessStartInfo Start(string file) => new(file)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    private static ProcessStartInfo Start(string file, IReadOnlyList<string> args)
    {
        ProcessStartInfo start = Start(file);
        foreach (string argument in args)
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }

    private static CommandResult? Execute(ProcessStartInfo start, int timeoutMs)
    {
        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            // Read before waiting: a child that fills the output pipe would
            // otherwise block forever while WaitForExit waits for it.
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(Math.Max(1, timeoutMs)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // The process may have exited between the timeout and kill.
                }
                return null;
            }

            // Drain the pipes; the process is gone, so this is bounded.
            output.Wait(Math.Max(1, timeoutMs));
            error.Wait(1);

            return new CommandResult(process.ExitCode, output.Result, error.Result);
        }
        catch (Exception)
        {
            // No binary, no permission, no process namespace: all the same to
            // the caller, which shows the field as unknown.
            return null;
        }
    }
}
