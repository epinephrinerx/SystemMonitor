using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// <see cref="IProcessSignals"/> over /proc and libc. A signal is sent through
/// a pidfd so the process that was checked is the process that is signalled,
/// even if its PID is reused in between. The name and start time are read the
/// same way <see cref="ProcessReader"/> reads them, so the re-check compares
/// like with like.
/// </summary>
public sealed class ProcessSignals : IProcessSignals
{
    private readonly string _root;

    public ProcessSignals(string root = "/")
    {
        _root = string.IsNullOrEmpty(root) ? "/" : root;
    }

    /// <summary>Everything the kill rules check, read fresh from /proc.</summary>
    public ProcessFacts? Read(int pid) => ProcessReader.FactsOf(_root, pid);

    /// <summary>
    /// pidfd_open(pid, 0). ENOSYS is reported as unsupported so the flow can
    /// fall back to kill(2); any other failure carries the errno text.
    /// </summary>
    public PinnedResult Pin(int pid)
    {
        int fd = Posix.PidfdOpen(pid, out int errno);
        if (fd >= 0)
        {
            return PinnedResult.Pinned(new PinnedProcess(_root, pid, fd));
        }
        return errno == Posix.Enosys
            ? PinnedResult.Unsupported
            : PinnedResult.Failed(Posix.ErrorText(errno));
    }

    /// <summary>
    /// kill(2), the fallback for a kernel without pidfds. The normal path goes
    /// through <see cref="PinnedProcess.Signal"/>.
    /// </summary>
    public string? Signal(int pid, bool force) =>
        Posix.Signal(pid, force ? Posix.SigKill : Posix.SigTerm);

    /// <summary>One open pidfd. Read and Signal both go through it.</summary>
    private sealed class PinnedProcess : IPinnedProcess
    {
        private readonly string _root;
        private readonly int _pid;
        private int _fd;
        private bool _closed;

        public PinnedProcess(string root, int pid, int fd)
        {
            _root = root;
            _pid = pid;
            _fd = fd;
        }

        public ProcessFacts? Read() => ProcessReader.FactsOf(_root, _pid);

        public string? Signal(bool force) =>
            _closed
                ? "pidfd is closed"
                : Posix.PidfdSendSignal(_fd, force ? Posix.SigKill : Posix.SigTerm);

        public void Dispose()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;
            Posix.CloseHandle(_fd);
        }
    }
}
