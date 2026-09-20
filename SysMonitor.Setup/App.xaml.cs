using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SysMonitor.Setup;

/// <summary>
/// One executable, three jobs, chosen by where it was started from and what
/// it was given: install wizard, uninstall wizard, or the temporary copy that
/// finishes an uninstall after the real one has exited.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string[] args = e.Args;
        bool silent = args.Contains("/S", StringComparer.OrdinalIgnoreCase);

        // The temporary copy: wait for the installed uninstaller to let go of
        // its own file, then remove everything including it.
        int finishIndex = Array.FindIndex(args,
            a => a.Equals("--finish", StringComparison.OrdinalIgnoreCase));
        if (finishIndex >= 0 && finishIndex + 1 < args.Length)
        {
            Finish(args[finishIndex + 1],
                   args.Contains("--settings", StringComparer.OrdinalIgnoreCase));
            Shutdown();
            return;
        }

        bool uninstalling = string.Equals(
            Path.GetFileName(Environment.ProcessPath), Installer.UninstallName,
            StringComparison.OrdinalIgnoreCase);

        if (silent)
        {
            RunSilent(uninstalling);
            Shutdown();
            return;
        }

        var window = new SetupWindow(uninstalling);
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    private static void RunSilent(bool uninstalling)
    {
        try
        {
            if (uninstalling)
            {
                Relaunch.ToFinishUninstall(removeSettings: false);
            }
            else
            {
                Installer.Install(desktopShortcut: false, startMenu: true,
                                  autostart: Installer.AutostartEnabled());
            }
        }
        catch (Exception error)
        {
            // Silent means silent: a dialog here would block an unattended run.
            Trace.WriteLine(error);
            Environment.ExitCode = 1;
        }
    }

    private static void Finish(string parentPid, bool removeSettings)
    {
        if (int.TryParse(parentPid, out int pid))
        {
            try
            {
                using Process parent = Process.GetProcessById(pid);
                parent.WaitForExit(8000);
            }
            catch (Exception)
            {
                // Already gone, which is exactly what we were waiting for.
            }
        }
        Installer.Uninstall(removeSettings);
        Installer.DeleteSelfAtReboot(Environment.ProcessPath!);
        // The directory holding it goes at the same time; it is ours and it
        // holds nothing else.
        string? own = Path.GetDirectoryName(Environment.ProcessPath!);
        if (own is not null)
        {
            Installer.DeleteSelfAtReboot(own);
        }
    }
}

/// <summary>
/// An executable cannot delete itself while it is running. It copies itself
/// to the temp directory, hands over the work, and exits.
/// </summary>
internal static class Relaunch
{
    public static void ToFinishUninstall(bool removeSettings)
    {
        // A fresh directory nobody can have pre-created, rather than a
        // predictable name in a shared temp root.
        string directory = Path.Combine(Path.GetTempPath(),
            $"SysMonitor-uninstall-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, "uninstall.exe");

        using (var destination = new FileStream(temp, FileMode.CreateNew,
                                                FileAccess.Write, FileShare.None))
        {
            using var source = new FileStream(Environment.ProcessPath!, FileMode.Open,
                                              FileAccess.Read, FileShare.Read);
            source.CopyTo(destination);
            destination.Flush(flushToDisk: true);
        }

        // Held open for reading, which denies writing and deleting, until the
        // child has started. Copying and then launching by path leaves a window
        // in which the file can be swapped for another, and what runs is
        // whatever is there at the moment of launch -- ArgumentList protects
        // the command line, not the image.
        //
        // The handle has to be read-only: an image cannot be mapped for
        // execution while anyone holds it open for writing, so guarding it
        // with a write handle stops the uninstaller from running at all.
        using (new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var start = new ProcessStartInfo(temp) { UseShellExecute = false };
            // ArgumentList, never a command line we built by hand: no quoting
            // to get wrong and no shell to reinterpret a path.
            start.ArgumentList.Add("--finish");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            if (removeSettings)
            {
                start.ArgumentList.Add("--settings");
            }
            Process.Start(start);
        }
    }
}
