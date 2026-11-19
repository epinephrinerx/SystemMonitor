using SysMonitor.Linux;

namespace SysMonitor.Linux.Tests;

/// <summary>
/// Disposable temp directory fixture representing a fake root (e.g. /proc and /sys tree).
/// </summary>
public sealed class FakeRoot : IDisposable
{
    public string Root { get; }

    public FakeRoot()
    {
        Root = Path.Combine(Path.GetTempPath(), "sysmon-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(Root, relativePath.TrimStart('/', '\\'));
        string? dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public string CreateDirectory(string relativePath)
    {
        string fullPath = Path.Combine(Root, relativePath.TrimStart('/', '\\'));
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public void CreateSymlink(string relativeLinkPath, string targetFullPathOrRelative)
    {
        string linkFull = Path.Combine(Root, relativeLinkPath.TrimStart('/', '\\'));
        string? dir = Path.GetDirectoryName(linkFull);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.CreateSymbolicLink(linkFull, targetFullPathOrRelative);
    }

    public void CreateDirectorySymlink(string relativeLinkPath, string targetFullPathOrRelative)
    {
        string linkFull = Path.Combine(Root, relativeLinkPath.TrimStart('/', '\\'));
        string? dir = Path.GetDirectoryName(linkFull);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        Directory.CreateSymbolicLink(linkFull, targetFullPathOrRelative);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception)
        {
            // Ignore best-effort cleanup errors in temp folder
        }
    }
}

/// <summary>
/// Fake command runner for external commands such as iw and nvidia-smi.
/// </summary>
public sealed class FakeCommandRunner : ICommandRunner
{
    public List<(string File, string Args, int TimeoutMs)> Executions { get; } = new();
    public Dictionary<string, string> CommandOutputs { get; } = new(StringComparer.Ordinal);
    public Func<string, string, int, string?>? CustomHandler { get; set; }

    public string? Run(string file, string args, int timeoutMs)
    {
        Executions.Add((file, args, timeoutMs));

        if (CustomHandler is not null)
        {
            return CustomHandler(file, args, timeoutMs);
        }

        string key = $"{file} {args}".Trim();
        if (CommandOutputs.TryGetValue(key, out string? output))
        {
            return output;
        }

        if (CommandOutputs.TryGetValue(file, out output))
        {
            return output;
        }

        return null;
    }
}

/// <summary>
/// Controllable clock for rate calculations.
/// </summary>
public sealed class FakeClock
{
    public long Ticks { get; set; } = 1000;

    public long Now() => Ticks;

    public void Advance(long milliseconds) => Ticks += milliseconds;
}
