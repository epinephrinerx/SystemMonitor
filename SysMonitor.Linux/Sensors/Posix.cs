using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// The two libc calls the app needs and no package provides: whose user we are,
/// and sending a signal. Nothing here raises; a failed call comes back as the
/// error text the status line shows.
/// </summary>
public static class Posix
{
    public const int SigTerm = 15;
    public const int SigKill = 9;

    private const int Esrch = 3;

    /// <summary>The syscall does not exist on this kernel (older than 5.3).</summary>
    public const int Enosys = 38;

    // syscall numbers on the generic table used by x86-64, arm64 and riscv64.
    private const long SysPidfdSendSignal = 424;
    private const long SysPidfdOpen = 434;

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    /// <summary>
    /// libc's variadic syscall(2). Four fixed long slots cover the integer
    /// arguments used here, which is how the variadic call passes them on the
    /// platforms this app targets (x86-64, arm64); unused slots are zero.
    /// </summary>
    [DllImport("libc", SetLastError = true)]
    private static extern long syscall(long number, long arg1, long arg2, long arg3, long arg4);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc")]
    private static extern uint getuid();

    /// <summary>The real UID of this process.</summary>
    public static int Uid => unchecked((int)getuid());

    /// <summary>
    /// Send a signal. Null means it was delivered or the process was already
    /// gone (ESRCH); anything else is the system's own error text.
    /// </summary>
    public static string? Signal(int pid, int signal)
    {
        if (kill(pid, signal) == 0)
        {
            return null;
        }

        int errno = Marshal.GetLastPInvokeError();
        if (errno == Esrch)
        {
            return null;
        }
        return ErrorText(errno);
    }

    /// <summary>
    /// pidfd_open(2) through libc's syscall(2), so no package and no newer
    /// glibc is required. Returns the descriptor, or -1 with errno in
    /// <paramref name="errno"/>; ENOSYS means the kernel has no pidfd support.
    /// </summary>
    public static int PidfdOpen(int pid, out int errno)
    {
        long fd = syscall(SysPidfdOpen, pid, 0, 0, 0);
        if (fd < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            return -1;
        }
        errno = 0;
        return (int)fd;
    }

    /// <summary>
    /// pidfd_send_signal(2) on a descriptor that pins the process. Null means
    /// delivered or already gone; anything else is the system's error text.
    /// </summary>
    public static string? PidfdSendSignal(int pidfd, int signal)
    {
        if (syscall(SysPidfdSendSignal, pidfd, signal, 0, 0) == 0)
        {
            return null;
        }

        int errno = Marshal.GetLastPInvokeError();
        return errno == Esrch ? null : ErrorText(errno);
    }

    /// <summary>close(2): a pidfd is an ordinary descriptor.</summary>
    public static void CloseHandle(int pidfd)
    {
        try
        {
            _ = close(pidfd);
        }
        catch (Exception)
        {
            // The descriptor is gone either way; closing must not end the flow.
        }
    }

    public static string ErrorText(int errno)
    {
        try
        {
            return new Win32Exception(errno).Message;
        }
        catch (Exception)
        {
            return "errno " + errno;
        }
    }
}
