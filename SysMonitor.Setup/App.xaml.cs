using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SysMonitor.Setup;

/// <summary>
/// One executable, three jobs, chosen by where it was started from and what
/// it was given: install wizard, uninstall wizard, or the temporary copy that
/// finishes an uninstall after the real one has exited.
///
/// A machine-wide install (R-022) needs administrator rights. The setup does
/// not ask for them in its manifest, because the per-user install must never
/// prompt; it asks, once, by starting itself again elevated when the work in
/// front of it is machine-wide.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SetupArguments arguments = SetupArguments.Parse(e.Args);

        // The temporary copy: wait for the installed uninstaller to let go of
        // its own file, then remove everything including it.
        if (arguments.FinishPid is int parentPid)
        {
            Finish(parentPid, arguments.RemoveSettings,
                   arguments.Admin ? InstallScope.Admin : InstallScope.PerUser);
            Shutdown();
            return;
        }

        bool uninstalling = string.Equals(
            Path.GetFileName(Environment.ProcessPath), Installer.UninstallName,
            StringComparison.OrdinalIgnoreCase);
        InstallScope scope = ChooseScope(arguments, uninstalling);

        if (scope == InstallScope.Admin && !Elevation.IsElevated())
        {
            // One UAC prompt, here at the start. The elevated copy gets the
            // same arguments, and --admin so it makes the same choice.
            IReadOnlyList<string> handover = (arguments with { Admin = true }).ToArguments();
            if (arguments.Silent)
            {
                // An unattended caller is told what really happened, not just
                // that the elevated copy started.
                int? code = Elevation.RelaunchAndWait(handover);
                Environment.ExitCode = code ?? 1;
                if (code == 0 && !uninstalling)
                {
                    // Back without administrator rights, which is what removing
                    // the old per-user files needs (see MigrateFromPerUser).
                    TryMigrate();
                }
            }
            else if (!Elevation.Relaunch(handover))
            {
                Environment.ExitCode = 1;
            }
            Shutdown();
            return;
        }

        if (arguments.Silent)
        {
            RunSilent(arguments, scope, uninstalling);
            Shutdown();
            return;
        }

        var window = new SetupWindow(uninstalling, scope, arguments);
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    /// <summary>
    /// What this run is about. An uninstaller belongs to the install it sits in;
    /// the setup follows the install already on the machine, and only
    /// <c>--admin</c> (or the wizard's checkbox) starts a machine-wide one.
    /// </summary>
    internal static InstallScope ChooseScope(SetupArguments arguments, bool uninstalling)
    {
        // Null when Windows will not name the machine-wide folders; a per-user
        // run must carry on without them.
        InstallLayout? admin = InstallLayout.TryCreate(InstallScope.Admin);
        if (uninstalling)
        {
            string? own = Path.GetDirectoryName(Environment.ProcessPath);
            bool inProgramFiles = own is not null && admin is not null
                && string.Equals(Path.GetFullPath(own), Path.GetFullPath(admin.InstallDir),
                                 StringComparison.OrdinalIgnoreCase);
            return inProgramFiles || arguments.Admin ? InstallScope.Admin : InstallScope.PerUser;
        }
        if (arguments.Admin)
        {
            return InstallScope.Admin;
        }
        return Installer.DetectInstalledScope(KnownFolders.Current()) ?? InstallScope.PerUser;
    }

    private static void RunSilent(SetupArguments arguments, InstallScope scope, bool uninstalling)
    {
        try
        {
            if (uninstalling)
            {
                Relaunch.ToFinishUninstall(arguments.RemoveSettings, scope);
                return;
            }

            // What an unattended run keeps from before: the start-up choice
            // already made, a Start menu entry, no desktop icon unless asked.
            InstallLayout layout = InstallLayout.Create(scope);
            InstallLayout? perUser = InstallLayout.TryCreate(InstallScope.PerUser);
            bool autostart = arguments.Autostart
                ?? (WindowsInstallHost.Instance.IsAutostartEnabled(layout)
                    || (scope == InstallScope.Admin && perUser is not null && File.Exists(perUser.ExePath)
                        && WindowsInstallHost.Instance.IsAutostartEnabled(perUser)));
            Installer.InstallScoped(scope, new InstallOptions(
                arguments.Desktop ?? false, arguments.StartMenu ?? true, autostart));
        }
        catch (Exception error)
        {
            // Silent means silent: a dialog here would block an unattended run.
            Trace.WriteLine(error);
            Environment.ExitCode = 1;
        }
    }

    /// <summary>Retire the per-user install a machine-wide one replaced; a failure here is not an install failure.</summary>
    internal static void TryMigrate()
    {
        try
        {
            Installer.MigrateFromPerUser();
        }
        catch (Exception error)
        {
            Trace.WriteLine(error);
        }
    }

    private static void Finish(int parentPid, bool removeSettings, InstallScope scope)
    {
        try
        {
            using Process parent = Process.GetProcessById(parentPid);
            parent.WaitForExit(8000);
        }
        catch (Exception)
        {
            // Already gone, which is exactly what we were waiting for.
        }

        try
        {
            Installer.Uninstall(InstallLayout.Create(scope), removeSettings,
                                WindowsInstallHost.Instance, Installer.ReadExtensionPayload());
        }
        catch (Exception error)
        {
            Trace.WriteLine(error);
            Environment.ExitCode = 1;
            return;
        }
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
    public static void ToFinishUninstall(bool removeSettings, InstallScope scope = InstallScope.PerUser)
    {
        // A fresh directory nobody can have pre-created, rather than a
        // predictable name in a shared temp root.
        //
        // For the machine-wide uninstall this copy runs elevated, so it must
        // not sit in %TEMP%: a user-level process could swap the image before
        // it starts, plant a DLL beside it, or re-point its folder before the
        // delete-at-reboot. It goes in a directory only administrators can
        // enter, created with that ACL in the same call.
        string directory;
        if (scope == InstallScope.Admin)
        {
            directory = PrivateDirectory.Create(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SysMonitor-uninstall-");
        }
        else
        {
            directory = Path.Combine(Path.GetTempPath(), $"SysMonitor-uninstall-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
        }
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
            if (scope == InstallScope.Admin)
            {
                start.ArgumentList.Add("--admin");
            }
            Process.Start(start);
        }
    }
}
