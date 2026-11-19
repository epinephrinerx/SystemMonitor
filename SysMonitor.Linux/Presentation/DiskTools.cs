using System.Diagnostics;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The Linux mapping of the Windows drive menu (spec section 4b): drive letters
/// become mount points, File Explorer becomes <c>xdg-open</c>, Disk Cleanup
/// becomes a disk-usage tool and Disk Management becomes the partition editor.
/// An item whose tool is not installed is left out rather than shown dead.
/// </summary>
public static class DiskTools
{
    /// <summary>Open a mount point in the desktop's file manager.</summary>
    public static bool OpenMount(string mount)
    {
        if (string.IsNullOrWhiteSpace(mount))
        {
            return false;
        }
        return Launch("xdg-open", mount);
    }

    /// <summary>filelight or baobab with the mount point: where the space went.</summary>
    public static bool OpenDiskUsage(string mount, string? path = null)
    {
        string? tool = ToolFinder.DiskUsageTool(path);
        return tool is not null && Launch(tool, mount);
    }

    /// <summary>partitionmanager or gnome-disks.</summary>
    public static bool OpenDiskManager(string? path = null)
    {
        string? tool = ToolFinder.DiskManagerTool(path);
        return tool is not null && Launch(tool, string.Empty);
    }

    public static bool HasFileManager(string? path = null) =>
        ToolFinder.FirstOnPath(new[] { "xdg-open" }, path) is not null;

    /// <summary>
    /// Start a detached program. The app is a widget: a tool that fails to
    /// start must not take it down, so the result is a bool and never a throw.
    /// </summary>
    public static bool Launch(string file, string argument)
    {
        try
        {
            var start = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrWhiteSpace(argument))
            {
                start.ArgumentList.Add(argument);
            }
            using Process? process = Process.Start(start);
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
