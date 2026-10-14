using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SysMonitor.Tests.Extensions;

internal static class TestJunctionHelper
{
    private const int FSCTL_SET_REPARSE_POINT = 0x000900A4;
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out int pBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.U1)] // BOOLEAN, one byte: read as a 4-byte BOOL it reports failure as success
    private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

    /// <summary>
    /// Attempts to create an NTFS directory junction without requiring administrator privileges.
    /// Returns true if successful, false otherwise.
    /// </summary>
    public static bool TryCreateJunction(string junctionPoint, string targetDir)
    {
        try
        {
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            if (!Directory.Exists(junctionPoint))
            {
                Directory.CreateDirectory(junctionPoint);
            }

            using SafeFileHandle handle = CreateFile(
                junctionPoint,
                GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                return false;
            }

            string nonInterpretedTarget = @"\??\" + Path.GetFullPath(targetDir);
            byte[] substituteNameBytes = Encoding.Unicode.GetBytes(nonInterpretedTarget);
            byte[] printNameBytes = Encoding.Unicode.GetBytes(Path.GetFullPath(targetDir));

            short substituteNameOffset = 0;
            short substituteNameLength = (short)substituteNameBytes.Length;
            short printNameOffset = (short)(substituteNameLength + 2);
            short printNameLength = (short)printNameBytes.Length;

            int dataLength = 8 + substituteNameLength + 2 + printNameLength + 2;
            byte[] buffer = new byte[8 + dataLength];

            BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(buffer, 0);
            BitConverter.GetBytes((short)dataLength).CopyTo(buffer, 4);
            BitConverter.GetBytes((short)0).CopyTo(buffer, 6);

            BitConverter.GetBytes(substituteNameOffset).CopyTo(buffer, 8);
            BitConverter.GetBytes(substituteNameLength).CopyTo(buffer, 10);
            BitConverter.GetBytes(printNameOffset).CopyTo(buffer, 12);
            BitConverter.GetBytes(printNameLength).CopyTo(buffer, 14);

            substituteNameBytes.CopyTo(buffer, 16 + substituteNameOffset);
            printNameBytes.CopyTo(buffer, 16 + printNameOffset);

            bool ok = DeviceIoControl(
                handle,
                FSCTL_SET_REPARSE_POINT,
                buffer,
                buffer.Length,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero);

            return ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to create a file symbolic link.
    /// Returns true if successful, false if privileges or developer mode are missing.
    /// </summary>
    /// <summary>
    /// Deletes a test folder that may contain junctions. A recursive delete fails on a junction
    /// to a folder (access denied), so remove each reparse point itself first.
    /// </summary>
    public static void DeleteTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string dir in Directory.GetDirectories(path))
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(dir, recursive: false);
            }
            else
            {
                DeleteTree(dir);
            }
        }

        Directory.Delete(path, recursive: true);
    }

    public static bool TryCreateFileSymlink(string linkPath, string targetPath)
    {
        try
        {
            // Flag 0x2 = SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE
            // Trust the file system, not the return value: the link must really be there.
            return (CreateSymbolicLink(linkPath, targetPath, 0x2) || CreateSymbolicLink(linkPath, targetPath, 0))
                   && File.Exists(linkPath)
                   && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }
}
