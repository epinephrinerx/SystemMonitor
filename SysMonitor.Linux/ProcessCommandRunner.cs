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
        try
        {
            var start = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

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

            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception)
        {
            // No binary, no permission, no process namespace: all the same to
            // the caller, which shows the field as unknown.
            return null;
        }
    }
}
