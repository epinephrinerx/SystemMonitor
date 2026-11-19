namespace SysMonitor.Linux;

/// <summary>
/// The only way the sampler starts another process. A missing binary, a
/// non-zero exit and a timeout are all reported the same way: null.
/// </summary>
public interface ICommandRunner
{
    /// <summary>
    /// Run <paramref name="file"/> with <paramref name="args"/> and return its
    /// standard output, or null when it could not be run, failed, or exceeded
    /// <paramref name="timeoutMs"/>.
    /// </summary>
    string? Run(string file, string args, int timeoutMs);
}
