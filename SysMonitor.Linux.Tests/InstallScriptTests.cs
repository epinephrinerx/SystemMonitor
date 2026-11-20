using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class InstallScriptTests
{
    private const int ScriptTimeoutSeconds = 30;

    /// <summary>
    /// Disposable sandbox providing a isolated fake root with HOME and an extracted archive.
    /// </summary>
    private sealed class InstallerSandbox : IDisposable
    {
        public string SandboxRoot { get; }
        public string Home { get; }
        public string ExtractDir { get; }
        public string DataHome => Path.Combine(Home, ".local", "share");
        public string ConfigHome => Path.Combine(Home, ".config");
        public string StateHome => Path.Combine(Home, ".local", "state");

        public InstallerSandbox(string? customRoot = null, string? customHomeDir = null)
        {
            SandboxRoot = customRoot ?? Path.Combine(Path.GetTempPath(), "sysmon-installer-test-" + Guid.NewGuid().ToString("N"));
            Home = customHomeDir ?? Path.Combine(SandboxRoot, "home");
            ExtractDir = Path.Combine(SandboxRoot, "extract");

            Directory.CreateDirectory(Home);
            Directory.CreateDirectory(ExtractDir);

            PopulateFakeArchive();
        }

        private void PopulateFakeArchive()
        {
            string repoRoot = FindRepoRoot();

            // app/sysmonitor: tiny shell script
            string appDir = Path.Combine(ExtractDir, "app");
            Directory.CreateDirectory(appDir);
            string binaryPath = Path.Combine(appDir, "sysmonitor");
            File.WriteAllText(binaryPath, "#!/bin/sh\nexit 0\n");
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(binaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            // icons/systemmonitor.png: arbitrary bytes
            string iconsDir = Path.Combine(ExtractDir, "icons");
            Directory.CreateDirectory(iconsDir);
            File.WriteAllBytes(Path.Combine(iconsDir, "systemmonitor.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 });

            // systemmonitor.desktop
            string repoDesktop = Path.Combine(repoRoot, "packaging", "linux", "systemmonitor.desktop");
            string extractDesktop = Path.Combine(ExtractDir, "systemmonitor.desktop");
            if (File.Exists(repoDesktop))
            {
                File.Copy(repoDesktop, extractDesktop, overwrite: true);
            }
            else
            {
                File.WriteAllText(extractDesktop,
                    "[Desktop Entry]\nType=Application\nName=System Monitor\nExec=@EXEC@\nIcon=systemmonitor\nTerminal=false\n");
            }

            // VERSION
            File.WriteAllText(Path.Combine(ExtractDir, "VERSION"), "4.3.0\n");

            // install.sh and uninstall.sh copied from packaging/linux/
            string installSrc = Path.Combine(repoRoot, "packaging", "linux", "install.sh");
            string installDst = Path.Combine(ExtractDir, "install.sh");
            File.Copy(installSrc, installDst, overwrite: true);

            string uninstallSrc = Path.Combine(repoRoot, "packaging", "linux", "uninstall.sh");
            string uninstallDst = Path.Combine(ExtractDir, "uninstall.sh");
            File.Copy(uninstallSrc, uninstallDst, overwrite: true);

            if (OperatingSystem.IsLinux())
            {
                var scriptMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                 UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                 UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
                File.SetUnixFileMode(installDst, scriptMode);
                File.SetUnixFileMode(uninstallDst, scriptMode);
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(SandboxRoot))
                {
                    Directory.Delete(SandboxRoot, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup in temp directory
            }
        }
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "packaging", "linux")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException($"Could not locate repository root containing packaging/linux/ from {AppContext.BaseDirectory}");
    }

    private static (int ExitCode, string StdOut, string StdErr) RunScript(
        string scriptPath,
        string[]? args,
        string home,
        string? dataHome = null,
        string? configHome = null,
        string? stateHome = null,
        IDictionary<string, string>? extraEnv = null,
        int timeoutSeconds = ScriptTimeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add(scriptPath);
        if (args != null)
        {
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        // Clear any inherited XDG_* and test hook variables to avoid leaking host environment
        var inheritedKeysToRemove = psi.Environment.Keys
            .Where(k => k.StartsWith("XDG_", StringComparison.OrdinalIgnoreCase) ||
                        k.StartsWith("SYSMONITOR_TEST_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in inheritedKeysToRemove)
        {
            psi.Environment.Remove(key);
        }

        // Environment holding HOME, XDG_DATA_HOME, XDG_CONFIG_HOME, XDG_STATE_HOME set to sandbox
        psi.Environment["HOME"] = home;
        psi.Environment["XDG_DATA_HOME"] = dataHome ?? Path.Combine(home, ".local", "share");
        psi.Environment["XDG_CONFIG_HOME"] = configHome ?? Path.Combine(home, ".config");
        psi.Environment["XDG_STATE_HOME"] = stateHome ?? Path.Combine(home, ".local", "state");
        psi.Environment["SYSMONITOR_NO_LAUNCH"] = "1";

        if (extraEnv != null)
        {
            foreach (var kvp in extraEnv)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort kill on timeout
            }
            throw new TimeoutException($"Script {scriptPath} timed out after {timeoutSeconds}s");
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunInstall(
        InstallerSandbox sandbox,
        string[]? args = null,
        string? dataHome = null,
        string? configHome = null,
        string? stateHome = null,
        IDictionary<string, string>? extraEnv = null)
    {
        string script = Path.Combine(sandbox.ExtractDir, "install.sh");
        return RunScript(
            scriptPath: script,
            args: args,
            home: sandbox.Home,
            dataHome: dataHome ?? sandbox.DataHome,
            configHome: configHome ?? sandbox.ConfigHome,
            stateHome: stateHome ?? sandbox.StateHome,
            extraEnv: extraEnv);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunUninstall(
        InstallerSandbox sandbox,
        string[]? args = null,
        string? scriptPath = null,
        string? dataHome = null,
        string? configHome = null,
        string? stateHome = null)
    {
        string script = scriptPath ?? Path.Combine(sandbox.ExtractDir, "uninstall.sh");
        return RunScript(
            scriptPath: script,
            args: args,
            home: sandbox.Home,
            dataHome: dataHome ?? sandbox.DataHome,
            configHome: configHome ?? sandbox.ConfigHome,
            stateHome: stateHome ?? sandbox.StateHome);
    }

    // ------------------------------------------------------------- Install tests

    [TestMethod]
    public void Install_CreatesExpectedTree_IncludingLauncherMenuIconAndUninstallerCopy()
    {
        using var sandbox = new InstallerSandbox();

        var result = RunInstall(sandbox);

        Assert.AreEqual(0, result.ExitCode, $"install.sh failed with stderr: {result.StdErr}");

        // 1. App binary
        string installedBinary = Path.Combine(sandbox.DataHome, "SystemMonitor", "app", "sysmonitor");
        Assert.IsTrue(File.Exists(installedBinary), $"App binary must exist at {installedBinary}");

        // 2. Launcher symlink pointing to app binary
        string launcherPath = Path.Combine(sandbox.Home, ".local", "bin", "sysmonitor");
        Assert.IsTrue(File.Exists(launcherPath), $"Launcher symlink must exist at {launcherPath}");
        var launcherInfo = new FileInfo(launcherPath);
        Assert.IsNotNull(launcherInfo.LinkTarget, "Launcher must be a symbolic link");
        Assert.AreEqual(installedBinary, launcherInfo.LinkTarget, "Launcher symlink target must point to installed app binary");

        // 3. Menu entry desktop file
        string menuFile = Path.Combine(sandbox.DataHome, "applications", "systemmonitor.desktop");
        Assert.IsTrue(File.Exists(menuFile), $"Menu file must exist at {menuFile}");

        // 4. Icon file
        string iconFile = Path.Combine(sandbox.DataHome, "icons", "hicolor", "256x256", "apps", "systemmonitor.png");
        Assert.IsTrue(File.Exists(iconFile), $"Icon must exist at {iconFile}");

        // 5. Uninstaller copy outside app/
        string uninstallerCopy = Path.Combine(sandbox.DataHome, "SystemMonitor", "uninstall.sh");
        Assert.IsTrue(File.Exists(uninstallerCopy), $"Uninstaller copy must exist at {uninstallerCopy}");
        if (OperatingSystem.IsLinux())
        {
            var uninstallerMode = File.GetUnixFileMode(uninstallerCopy);
            Assert.IsTrue((uninstallerMode & UnixFileMode.UserExecute) != 0, "Uninstaller copy must have user execute mode");
        }
    }

    [TestMethod]
    public void Install_EscapesDesktopExec_ForPathContainingSpaceAndDollar()
    {
        string specialRoot = Path.Combine(Path.GetTempPath(), "sysmon test $dir " + Guid.NewGuid().ToString("N"));
        string specialHome = Path.Combine(specialRoot, "home $pace");

        using var sandbox = new InstallerSandbox(customRoot: specialRoot, customHomeDir: specialHome);

        var result = RunInstall(sandbox);

        Assert.AreEqual(0, result.ExitCode, $"install.sh failed with stderr: {result.StdErr}");

        string menuFile = Path.Combine(sandbox.DataHome, "applications", "systemmonitor.desktop");
        Assert.IsTrue(File.Exists(menuFile), $"Desktop file must exist at {menuFile}");

        string installedBinary = Path.Combine(sandbox.DataHome, "SystemMonitor", "app", "sysmonitor");
        string expectedExecLine = $"Exec={DesktopEntry.Quote(installedBinary)}";

        string[] lines = File.ReadAllLines(menuFile);
        string? actualExecLine = lines.FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal));

        Assert.IsNotNull(actualExecLine, "systemmonitor.desktop must contain an Exec line");
        Assert.AreEqual(expectedExecLine, actualExecLine, "Exec line must match two-stage quoted desktop entry specification");
        Assert.IsTrue(actualExecLine.Contains(@"\\$"), "Exec line must escape '$' as '\\\\$' for desktop entry key files");
    }

    [TestMethod]
    public void Install_InstalledBinary_IsExecutable()
    {
        using var sandbox = new InstallerSandbox();

        var result = RunInstall(sandbox);

        Assert.AreEqual(0, result.ExitCode, result.StdErr);

        string installedBinary = Path.Combine(sandbox.DataHome, "SystemMonitor", "app", "sysmonitor");
        if (OperatingSystem.IsLinux())
        {
            var mode = File.GetUnixFileMode(installedBinary);
            Assert.IsTrue((mode & UnixFileMode.UserExecute) != 0, "sysmonitor binary must be user-executable");
        }
    }

    [TestMethod]
    public void Install_Reinstall_ReplacesApp_AndLeavesNoNewOrOldDirectories()
    {
        using var sandbox = new InstallerSandbox();

        // Initial install
        var firstResult = RunInstall(sandbox);
        Assert.AreEqual(0, firstResult.ExitCode, firstResult.StdErr);

        // Stage a new file in the fake archive
        string v2Marker = Path.Combine(sandbox.ExtractDir, "app", "version2.marker");
        File.WriteAllText(v2Marker, "version 2 payload\n");

        // Reinstall
        var secondResult = RunInstall(sandbox);
        Assert.AreEqual(0, secondResult.ExitCode, secondResult.StdErr);

        // Verify replacement
        string installedMarker = Path.Combine(sandbox.DataHome, "SystemMonitor", "app", "version2.marker");
        Assert.IsTrue(File.Exists(installedMarker), "Re-install must update files from fake archive");

        // Verify no leftover .new or .old directories
        string appNew = Path.Combine(sandbox.DataHome, "SystemMonitor", "app.new");
        string appOld = Path.Combine(sandbox.DataHome, "SystemMonitor", "app.old");
        Assert.IsFalse(Directory.Exists(appNew), "app.new must not remain after re-install");
        Assert.IsFalse(Directory.Exists(appOld), "app.old must not remain after re-install");
    }

    // ------------------------------------------------------------- Refusals

    [TestMethod]
    public void Install_Refuses_XdgDataHomeOutsideHome()
    {
        using var sandbox = new InstallerSandbox();

        string outsideDataHome = Path.Combine(sandbox.SandboxRoot, "outside-home", "share");
        Directory.CreateDirectory(outsideDataHome);

        var result = RunInstall(sandbox, dataHome: outsideDataHome);

        Assert.AreNotEqual(0, result.ExitCode, "install.sh must exit non-zero when XDG_DATA_HOME is outside HOME");
    }

    [TestMethod]
    public void Install_Refuses_RelativeXdgDataHome()
    {
        using var sandbox = new InstallerSandbox();

        var result = RunInstall(sandbox, dataHome: "relative/path/share");

        Assert.AreNotEqual(0, result.ExitCode, "install.sh must exit non-zero when XDG_DATA_HOME is a relative path");
        StringAssert.Contains(result.StdErr, "must be an absolute path");
    }

    [TestMethod]
    public void Install_Refuses_NonNumericWaitPid()
    {
        using var sandbox = new InstallerSandbox();

        var result = RunInstall(sandbox, args: new[] { "--update", "--wait-pid", "12ab" });

        Assert.AreNotEqual(0, result.ExitCode, "install.sh must exit non-zero when --wait-pid is non-numeric");
        StringAssert.Contains(result.StdErr, "needs a number");
    }

    // ------------------------------------------------------------- Update with wait-pid

    [TestMethod]
    public void Install_UpdateWithDeadPid_InstallsSuccessfully()
    {
        using var sandbox = new InstallerSandbox();

        // Start a short-lived process and wait for it to exit to obtain a guaranteed dead PID
        int deadPid;
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { "-c", "exit 0" },
            UseShellExecute = false,
            CreateNoWindow = true
        })!)
        {
            Assert.IsTrue(process.WaitForExit(ScriptTimeoutSeconds * 1000), "Dummy process failed to exit within timeout");
            deadPid = process.Id;
        }

        var result = RunInstall(sandbox, args: new[] { "--update", "--wait-pid", deadPid.ToString() });

        Assert.AreEqual(0, result.ExitCode, $"install.sh --update --wait-pid failed with stderr: {result.StdErr}");

        string installedBinary = Path.Combine(sandbox.DataHome, "SystemMonitor", "app", "sysmonitor");
        Assert.IsTrue(File.Exists(installedBinary), "App must be installed when --wait-pid receives an already-dead process id");
    }

    // ------------------------------------------------------------- Uninstall tests

    [TestMethod]
    public void Uninstall_KeepsSettings_ByDefault()
    {
        using var sandbox = new InstallerSandbox();

        var installResult = RunInstall(sandbox);
        Assert.AreEqual(0, installResult.ExitCode, installResult.StdErr);

        // Create user settings
        string settingsDir = Path.Combine(sandbox.ConfigHome, "SystemMonitor");
        Directory.CreateDirectory(settingsDir);
        string settingsFile = Path.Combine(settingsDir, "settings.json");
        File.WriteAllText(settingsFile, "{\"LastView\":\"overall\"}\n");

        var uninstallResult = RunUninstall(sandbox);
        Assert.AreEqual(0, uninstallResult.ExitCode, uninstallResult.StdErr);

        // App dir is gone
        string appDir = Path.Combine(sandbox.DataHome, "SystemMonitor", "app");
        Assert.IsFalse(Directory.Exists(appDir), "App directory must be removed by uninstaller");

        // Settings are kept
        Assert.IsTrue(File.Exists(settingsFile), "Settings file must be preserved by uninstaller without --purge");
    }

    [TestMethod]
    public void Uninstall_Purge_RemovesSettingsAndStateDirectories()
    {
        using var sandbox = new InstallerSandbox();

        var installResult = RunInstall(sandbox);
        Assert.AreEqual(0, installResult.ExitCode, installResult.StdErr);

        // Create settings and log files
        string settingsDir = Path.Combine(sandbox.ConfigHome, "SystemMonitor");
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(Path.Combine(settingsDir, "settings.json"), "{\"LastView\":\"widget\"}\n");

        string stateDir = Path.Combine(sandbox.StateHome, "SystemMonitor");
        Directory.CreateDirectory(stateDir);
        File.WriteAllText(Path.Combine(stateDir, "systemmonitor.log"), "sample log output\n");

        var uninstallResult = RunUninstall(sandbox, args: new[] { "--purge" });
        Assert.AreEqual(0, uninstallResult.ExitCode, uninstallResult.StdErr);

        Assert.IsFalse(Directory.Exists(settingsDir), "Settings directory must be removed when --purge is given");
        Assert.IsFalse(Directory.Exists(stateDir), "Log/state directory must be removed when --purge is given");
    }

    [TestMethod]
    public void Uninstall_RemovesOnlyOurAutostartFile_LeavesOtherDesktopFiles()
    {
        using var sandbox = new InstallerSandbox();

        var installResult = RunInstall(sandbox);
        Assert.AreEqual(0, installResult.ExitCode, installResult.StdErr);

        string autostartDir = Path.Combine(sandbox.ConfigHome, "autostart");
        Directory.CreateDirectory(autostartDir);

        // Our autostart file pointing to the installed app binary
        string installedBinary = Path.Combine(sandbox.DataHome, "SystemMonitor", "app", "sysmonitor");
        string ourAutostartFile = Path.Combine(autostartDir, "systemmonitor.desktop");
        File.WriteAllText(ourAutostartFile, DesktopEntry.Build(installedBinary));

        // Another application's desktop file
        string otherDesktopFile = Path.Combine(autostartDir, "otherapp.desktop");
        File.WriteAllText(otherDesktopFile, "[Desktop Entry]\nType=Application\nName=OtherApp\nExec=/usr/bin/other\n");

        var uninstallResult = RunUninstall(sandbox);
        Assert.AreEqual(0, uninstallResult.ExitCode, uninstallResult.StdErr);

        Assert.IsFalse(File.Exists(ourAutostartFile), "Our autostart desktop file must be removed");
        Assert.IsTrue(File.Exists(otherDesktopFile), "Other application autostart desktop file must be preserved");
    }

    [TestMethod]
    public void Uninstall_LeavesLauncher_WhenNotOurSymlink()
    {
        using var sandbox = new InstallerSandbox();

        var installResult = RunInstall(sandbox);
        Assert.AreEqual(0, installResult.ExitCode, installResult.StdErr);

        string launcherPath = Path.Combine(sandbox.Home, ".local", "bin", "sysmonitor");
        Assert.IsTrue(File.Exists(launcherPath), "Launcher must exist after install");

        // Replace launcher symlink with a symlink pointing to an unrelated binary
        File.Delete(launcherPath);
        File.CreateSymbolicLink(launcherPath, "/bin/sh");

        var uninstallResult = RunUninstall(sandbox);
        Assert.AreEqual(0, uninstallResult.ExitCode, uninstallResult.StdErr);

        Assert.IsTrue(File.Exists(launcherPath), "Launcher pointing elsewhere must be left in place");
    }

    [TestMethod]
    public void Uninstall_RunFromInstalledCopy_RemovesItselfAndSystemMonitorDirectory()
    {
        using var sandbox = new InstallerSandbox();

        var installResult = RunInstall(sandbox);
        Assert.AreEqual(0, installResult.ExitCode, installResult.StdErr);

        string installedUninstaller = Path.Combine(sandbox.DataHome, "SystemMonitor", "uninstall.sh");
        Assert.IsTrue(File.Exists(installedUninstaller), $"Installed copy of uninstaller must exist at {installedUninstaller}");

        // Execute the uninstaller from its installed location
        var uninstallResult = RunUninstall(sandbox, scriptPath: installedUninstaller);
        Assert.AreEqual(0, uninstallResult.ExitCode, uninstallResult.StdErr);

        Assert.IsFalse(File.Exists(installedUninstaller), "Uninstaller must have removed itself");
        Assert.IsFalse(Directory.Exists(Path.Combine(sandbox.DataHome, "SystemMonitor")), "SystemMonitor directory must be removed");
    }

    // ------------------------------------------------------------- Round 2 tests

    [TestMethod]
    public void Install_Refuses_XdgDataHomeWithDotDotOutsideHome_AndWritesNothingOutside()
    {
        using var sandbox = new InstallerSandbox();

        string outsideDir = Path.Combine(sandbox.SandboxRoot, "outside");
        Directory.CreateDirectory(outsideDir);
        string canaryFile = Path.Combine(outsideDir, "canary.txt");
        File.WriteAllText(canaryFile, "canary-install");

        string dotDotDataHome = sandbox.Home + "/../outside";

        var result = RunInstall(sandbox, dataHome: dotDotDataHome);

        Assert.AreNotEqual(0, result.ExitCode, "install.sh must exit non-zero when XDG_DATA_HOME contains '..' climbing outside HOME");
        Assert.IsTrue(File.Exists(canaryFile), "Pre-existing canary file outside HOME must not be disturbed");
        Assert.AreEqual("canary-install", File.ReadAllText(canaryFile));
        Assert.IsFalse(Directory.Exists(Path.Combine(outsideDir, "SystemMonitor")), "Nothing must be written to outside directory");
    }

    [TestMethod]
    public void Uninstall_Refuses_XdgDataHomeWithDotDotOutsideHome_AndLeavesOutsideUntouched()
    {
        using var sandbox = new InstallerSandbox();

        string outsideDir = Path.Combine(sandbox.SandboxRoot, "outside");
        Directory.CreateDirectory(outsideDir);
        string canaryFile = Path.Combine(outsideDir, "canary.txt");
        File.WriteAllText(canaryFile, "canary-uninstall");

        string dotDotDataHome = sandbox.Home + "/../outside";

        var result = RunUninstall(sandbox, dataHome: dotDotDataHome);

        Assert.AreNotEqual(0, result.ExitCode, "uninstall.sh must exit non-zero when XDG_DATA_HOME contains '..' climbing outside HOME");
        Assert.IsTrue(File.Exists(canaryFile), "Pre-existing canary file outside HOME must not be disturbed");
        Assert.AreEqual("canary-uninstall", File.ReadAllText(canaryFile));
        Assert.IsFalse(Directory.Exists(Path.Combine(outsideDir, "SystemMonitor")), "Nothing must be written or removed in outside directory");
    }

    [TestMethod]
    public void Install_Refuses_XdgDataHomeSymlinkedOutsideHome()
    {
        using var sandbox = new InstallerSandbox();

        string siblingDir = Path.Combine(sandbox.SandboxRoot, "sibling-outside");
        Directory.CreateDirectory(siblingDir);
        string canaryFile = Path.Combine(siblingDir, "canary.txt");
        File.WriteAllText(canaryFile, "canary-sibling");

        // Symlink inside HOME pointing to sibling outside HOME
        string symlinkedParent = Path.Combine(sandbox.Home, "symlink-parent");
        Directory.CreateSymbolicLink(symlinkedParent, siblingDir);

        string dataHome = Path.Combine(symlinkedParent, "share");

        var result = RunInstall(sandbox, dataHome: dataHome);

        Assert.AreNotEqual(0, result.ExitCode, "install.sh must exit non-zero when XDG_DATA_HOME physical path is outside HOME");
        Assert.IsTrue(File.Exists(canaryFile), "Canary file in sibling directory must remain intact");
        Assert.AreEqual("canary-sibling", File.ReadAllText(canaryFile));
        Assert.IsFalse(Directory.Exists(Path.Combine(siblingDir, "share")), "No directory must be created in sibling directory outside HOME");
    }

    [TestMethod]
    public void Uninstall_Refuses_XdgDataHomeSymlinkedOutsideHome()
    {
        using var sandbox = new InstallerSandbox();

        string siblingDir = Path.Combine(sandbox.SandboxRoot, "sibling-outside");
        Directory.CreateDirectory(siblingDir);
        string canaryFile = Path.Combine(siblingDir, "canary.txt");
        File.WriteAllText(canaryFile, "canary-sibling-uninstall");

        // Symlink inside HOME pointing to sibling outside HOME
        string symlinkedParent = Path.Combine(sandbox.Home, "symlink-parent");
        Directory.CreateSymbolicLink(symlinkedParent, siblingDir);

        string dataHome = Path.Combine(symlinkedParent, "share");

        var result = RunUninstall(sandbox, dataHome: dataHome);

        Assert.AreNotEqual(0, result.ExitCode, "uninstall.sh must exit non-zero when XDG_DATA_HOME physical path is outside HOME");
        Assert.IsTrue(File.Exists(canaryFile), "Canary file in sibling directory must remain intact");
        Assert.AreEqual("canary-sibling-uninstall", File.ReadAllText(canaryFile));
    }

    [TestMethod]
    public void Install_Refuses_WhenAppNewIsSymlink_AndLeavesTargetUntouched()
    {
        using var sandbox = new InstallerSandbox();

        string smDir = Path.Combine(sandbox.DataHome, "SystemMonitor");
        Directory.CreateDirectory(smDir);

        string decoyTarget = Path.Combine(sandbox.SandboxRoot, "decoy-app-new-target");
        Directory.CreateDirectory(decoyTarget);
        string canaryFile = Path.Combine(decoyTarget, "canary.txt");
        File.WriteAllText(canaryFile, "decoy-content");

        string appNew = Path.Combine(smDir, "app.new");
        Directory.CreateSymbolicLink(appNew, decoyTarget);

        var result = RunInstall(sandbox);

        Assert.AreNotEqual(0, result.ExitCode, "install.sh must refuse when app.new is a symbolic link");
        StringAssert.Contains(result.StdErr, "symbolic link");
        Assert.IsTrue(File.Exists(canaryFile), "Canary file in symlink target must remain intact");
        Assert.AreEqual("decoy-content", File.ReadAllText(canaryFile), "Symlink target content must be untouched");
        Assert.IsFalse(File.Exists(Path.Combine(decoyTarget, "sysmonitor")), "Archive files must not be copied into symlink target");
    }

    [TestMethod]
    public void Install_Recovers_WhenAppMissingAndAppOldPresent()
    {
        using var sandbox = new InstallerSandbox();

        string smDir = Path.Combine(sandbox.DataHome, "SystemMonitor");
        Directory.CreateDirectory(smDir);

        string appDir = Path.Combine(smDir, "app");
        string appOld = Path.Combine(smDir, "app.old");
        string appNew = Path.Combine(smDir, "app.new");

        // Simulate interrupted state: app/ missing, app.old/ present with old content
        Directory.CreateDirectory(appOld);
        File.WriteAllText(Path.Combine(appOld, "old-version.marker"), "previous-version");

        // Also simulate leftover stale app.new/
        Directory.CreateDirectory(appNew);
        File.WriteAllText(Path.Combine(appNew, "stale.marker"), "stale-version");

        Assert.IsFalse(Directory.Exists(appDir), "Precondition: app directory must be missing");

        var result = RunInstall(sandbox);

        Assert.AreEqual(0, result.ExitCode, $"install.sh failed recovery with stderr: {result.StdErr}");

        // Ends with working app/
        string installedBinary = Path.Combine(appDir, "sysmonitor");
        Assert.IsTrue(File.Exists(installedBinary), "Recovered install must have sysmonitor binary");
        if (OperatingSystem.IsLinux())
        {
            var mode = File.GetUnixFileMode(installedBinary);
            Assert.IsTrue((mode & UnixFileMode.UserExecute) != 0, "sysmonitor binary must be user-executable");
        }

        // And no app.old or app.new
        Assert.IsFalse(Directory.Exists(appOld), "app.old must not exist after recovery and install");
        Assert.IsFalse(Directory.Exists(appNew), "app.new must not exist after recovery and install");
    }

    [TestMethod]
    public void Uninstall_NeverRemovesTargetOfLauncherSymlink()
    {
        using var sandbox = new InstallerSandbox();

        var installResult = RunInstall(sandbox);
        Assert.AreEqual(0, installResult.ExitCode, installResult.StdErr);

        string launcherPath = Path.Combine(sandbox.Home, ".local", "bin", "sysmonitor");
        Assert.IsTrue(File.Exists(launcherPath), "Launcher must exist after install");

        string externalTargetDir = Path.Combine(sandbox.SandboxRoot, "external-bin");
        Directory.CreateDirectory(externalTargetDir);
        string externalTargetFile = Path.Combine(externalTargetDir, "custom-launcher-target");
        File.WriteAllText(externalTargetFile, "#!/bin/sh\necho custom\n");

        // Point launcher symlink to external target
        File.Delete(launcherPath);
        File.CreateSymbolicLink(launcherPath, externalTargetFile);

        var uninstallResult = RunUninstall(sandbox, args: new[] { "--purge" });
        Assert.AreEqual(0, uninstallResult.ExitCode, uninstallResult.StdErr);

        Assert.IsTrue(File.Exists(externalTargetFile), "The target of the launcher symlink must not be removed by uninstaller");
        Assert.AreEqual("#!/bin/sh\necho custom\n", File.ReadAllText(externalTargetFile), "The target file content must remain untouched");
    }

    // ------------------------------------------------------------- Round 3 tests

    private static void AssertStagedDestinationRejectsPlantedSymlinks(
        Func<InstallerSandbox, string> getDestDir,
        string fileName,
        string oldStagingName)
    {
        using var sandbox = new InstallerSandbox();

        string destDir = getDestDir(sandbox);
        Directory.CreateDirectory(destDir);

        // Sentinel file outside fake HOME, but inside sandbox
        string sentinelFile = Path.Combine(sandbox.SandboxRoot, $"sentinel-{fileName}.txt");
        const string sentinelContent = "sentinel-content-untouched";
        File.WriteAllText(sentinelFile, sentinelContent);

        // Outside target file for final name symlink
        string outsideTargetFile = Path.Combine(sandbox.SandboxRoot, $"outside-{fileName}-target.txt");
        const string outsideTargetContent = "outside-target-content-untouched";
        File.WriteAllText(outsideTargetFile, outsideTargetContent);

        // Pre-plant 1: symlink at old fixed staging name pointing to sentinel
        string stagingSymlink = Path.Combine(destDir, oldStagingName);
        File.CreateSymbolicLink(stagingSymlink, sentinelFile);

        // Pre-plant 2: symlink at final name pointing outside
        string finalSymlink = Path.Combine(destDir, fileName);
        File.CreateSymbolicLink(finalSymlink, outsideTargetFile);

        var result = RunInstall(sandbox);

        // Assert: for the final-name symlink, install refuses (non-zero exit)
        Assert.AreNotEqual(0, result.ExitCode,
            $"install.sh must exit non-zero when {fileName} is a symbolic link. StdErr: {result.StdErr}");
        StringAssert.Contains(result.StdErr, "symbolic link");

        // Assert: the sentinel's content is unchanged
        Assert.IsTrue(File.Exists(sentinelFile), $"Sentinel file {sentinelFile} must exist");
        Assert.AreEqual(sentinelContent, File.ReadAllText(sentinelFile),
            $"Sentinel content for {fileName} must remain unchanged");

        // Outside target is also unchanged
        Assert.IsTrue(File.Exists(outsideTargetFile), $"Outside target file {outsideTargetFile} must exist");
        Assert.AreEqual(outsideTargetContent, File.ReadAllText(outsideTargetFile),
            $"Outside target content for {fileName} must remain unchanged");

        // Assert: no .<name>.* temp files are left behind
        var leftoverTempFiles = Directory.EnumerateFileSystemEntries(destDir)
            .Select(Path.GetFileName)
            .Where(name => name != null && name.StartsWith($".{fileName}") && name != oldStagingName)
            .ToList();
        Assert.AreEqual(0, leftoverTempFiles.Count,
            $"No .{fileName}.* temp files must be left behind in {destDir}. Found: {string.Join(", ", leftoverTempFiles)}");
    }

    [TestMethod]
    public void Install_StagedDestination_UninstallSh_RefusesPlantedFinalSymlink_PreservesSentinel_AndCleansUpStaging()
    {
        AssertStagedDestinationRejectsPlantedSymlinks(
            sandbox => Path.Combine(sandbox.DataHome, "SystemMonitor"),
            fileName: "uninstall.sh",
            oldStagingName: ".uninstall.sh.new");
    }

    [TestMethod]
    public void Install_StagedDestination_MenuDesktop_RefusesPlantedFinalSymlink_PreservesSentinel_AndCleansUpStaging()
    {
        AssertStagedDestinationRejectsPlantedSymlinks(
            sandbox => Path.Combine(sandbox.DataHome, "applications"),
            fileName: "systemmonitor.desktop",
            oldStagingName: ".systemmonitor.desktop.new");
    }

    [TestMethod]
    public void Install_StagedDestination_Icon_RefusesPlantedFinalSymlink_PreservesSentinel_AndCleansUpStaging()
    {
        AssertStagedDestinationRejectsPlantedSymlinks(
            sandbox => Path.Combine(sandbox.DataHome, "icons", "hicolor", "256x256", "apps"),
            fileName: "systemmonitor.png",
            oldStagingName: ".systemmonitor.png.new");
    }

    [TestMethod]
    public void Install_StagedDestinations_PlantedOldFixedStagingSymlinks_AreIgnored_AndSentinelsUntouchedOnSuccess()
    {
        using var sandbox = new InstallerSandbox();

        string smDir = Path.Combine(sandbox.DataHome, "SystemMonitor");
        string menuDir = Path.Combine(sandbox.DataHome, "applications");
        string iconDir = Path.Combine(sandbox.DataHome, "icons", "hicolor", "256x256", "apps");

        Directory.CreateDirectory(smDir);
        Directory.CreateDirectory(menuDir);
        Directory.CreateDirectory(iconDir);

        string sentinelUninstall = Path.Combine(sandbox.SandboxRoot, "sentinel-uninstall.txt");
        string sentinelDesktop = Path.Combine(sandbox.SandboxRoot, "sentinel-desktop.txt");
        string sentinelIcon = Path.Combine(sandbox.SandboxRoot, "sentinel-icon.txt");

        File.WriteAllText(sentinelUninstall, "sentinel-uninstall-content");
        File.WriteAllText(sentinelDesktop, "sentinel-desktop-content");
        File.WriteAllText(sentinelIcon, "sentinel-icon-content");

        File.CreateSymbolicLink(Path.Combine(smDir, ".uninstall.sh.new"), sentinelUninstall);
        File.CreateSymbolicLink(Path.Combine(menuDir, ".systemmonitor.desktop.new"), sentinelDesktop);
        File.CreateSymbolicLink(Path.Combine(iconDir, ".systemmonitor.png.new"), sentinelIcon);

        var result = RunInstall(sandbox);

        Assert.AreEqual(0, result.ExitCode, $"install.sh failed with stderr: {result.StdErr}");

        Assert.AreEqual("sentinel-uninstall-content", File.ReadAllText(sentinelUninstall), "Uninstall sentinel must be untouched");
        Assert.AreEqual("sentinel-desktop-content", File.ReadAllText(sentinelDesktop), "Desktop sentinel must be untouched");
        Assert.AreEqual("sentinel-icon-content", File.ReadAllText(sentinelIcon), "Icon sentinel must be untouched");

        // The final destinations must exist as regular files
        string installedUninstall = Path.Combine(smDir, "uninstall.sh");
        string installedDesktop = Path.Combine(menuDir, "systemmonitor.desktop");
        string installedIcon = Path.Combine(iconDir, "systemmonitor.png");

        Assert.IsTrue(File.Exists(installedUninstall) && new FileInfo(installedUninstall).LinkTarget == null, "uninstall.sh must be installed as a regular file");
        Assert.IsTrue(File.Exists(installedDesktop) && new FileInfo(installedDesktop).LinkTarget == null, "systemmonitor.desktop must be installed as a regular file");
        Assert.IsTrue(File.Exists(installedIcon) && new FileInfo(installedIcon).LinkTarget == null, "systemmonitor.png must be installed as a regular file");

        // Verify no leftover mktemp files
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(smDir).Select(Path.GetFileName).Count(name => name != null && name.StartsWith(".uninstall.sh") && name != ".uninstall.sh.new"));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(menuDir).Select(Path.GetFileName).Count(name => name != null && name.StartsWith(".systemmonitor.desktop") && name != ".systemmonitor.desktop.new"));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(iconDir).Select(Path.GetFileName).Count(name => name != null && name.StartsWith(".systemmonitor.png") && name != ".systemmonitor.png.new"));
    }

    // ------------------------------------------------------------- Round 4 tests

    [TestMethod]
    public void Install_Reinstall_WhenSwapFails_RollsBackToPreviousVersion_AndLeavesNoAppOld()
    {
        using var sandbox = new InstallerSandbox();

        // Start from an existing install
        var initialResult = RunInstall(sandbox);
        Assert.AreEqual(0, initialResult.ExitCode, $"Initial install failed with stderr: {initialResult.StdErr}");

        string appDir = Path.Combine(sandbox.DataHome, "SystemMonitor", "app");
        string appOld = Path.Combine(sandbox.DataHome, "SystemMonitor", "app.old");

        // Put a marker file in previous version
        string markerPath = Path.Combine(appDir, "previous-version.marker");
        const string markerContent = "previous-version-content-v1";
        File.WriteAllText(markerPath, markerContent);

        // Stage a new version payload in the fake archive
        string newVersionMarker = Path.Combine(sandbox.ExtractDir, "app", "new-version.marker");
        File.WriteAllText(newVersionMarker, "new-version-payload-v2");

        // Reinstall with SYSMONITOR_TEST_FAIL_SWAP=1
        var env = new Dictionary<string, string>
        {
            ["SYSMONITOR_TEST_FAIL_SWAP"] = "1"
        };
        var result = RunInstall(sandbox, extraEnv: env);

        // Exit is non-zero
        Assert.AreNotEqual(0, result.ExitCode, "install.sh must exit non-zero when swap fails");

        // app/ is the previous version (marker file is present)
        Assert.IsTrue(Directory.Exists(appDir), "app directory must exist after rollback");
        Assert.IsTrue(File.Exists(markerPath), "Previous version marker file must exist in app/ after rollback");
        Assert.AreEqual(markerContent, File.ReadAllText(markerPath), "Previous version marker content must remain intact");
        Assert.IsFalse(File.Exists(Path.Combine(appDir, "new-version.marker")), "New version payload must not be in app/");

        // No app.old is left
        Assert.IsFalse(Directory.Exists(appOld), "No app.old directory must be left after rollback");

        // The output says the previous version was restored
        string combinedOutput = result.StdOut + "\n" + result.StdErr;
        StringAssert.Contains(combinedOutput, "restored the previous version");
    }

    [TestMethod]
    public void Install_Reinstall_WhenSwapAndRestoreFail_LeavesPreviousVersionInAppOld_ReportsFailedRestore_AndNeverSaysRestored()
    {
        using var sandbox = new InstallerSandbox();

        // Start from an existing install
        var initialResult = RunInstall(sandbox);
        Assert.AreEqual(0, initialResult.ExitCode, $"Initial install failed with stderr: {initialResult.StdErr}");

        string smDir = Path.Combine(sandbox.DataHome, "SystemMonitor");
        string appDir = Path.Combine(smDir, "app");
        string appOld = Path.Combine(smDir, "app.old");

        // Put a marker file in previous version
        string markerFileName = "previous-version.marker";
        string markerPath = Path.Combine(appDir, markerFileName);
        const string markerContent = "previous-version-content-v1";
        File.WriteAllText(markerPath, markerContent);

        // Stage a new version payload in the fake archive
        string newVersionMarker = Path.Combine(sandbox.ExtractDir, "app", "new-version.marker");
        File.WriteAllText(newVersionMarker, "new-version-payload-v2");

        // Reinstall with SYSMONITOR_TEST_FAIL_SWAP=1 and SYSMONITOR_TEST_FAIL_RESTORE=1
        var env = new Dictionary<string, string>
        {
            ["SYSMONITOR_TEST_FAIL_SWAP"] = "1",
            ["SYSMONITOR_TEST_FAIL_RESTORE"] = "1"
        };
        var result = RunInstall(sandbox, extraEnv: env);

        // Exit is non-zero
        Assert.AreNotEqual(0, result.ExitCode, "install.sh must exit non-zero when both swap and restore fail");

        // app.old still holds the previous version (check the marker)
        Assert.IsTrue(Directory.Exists(appOld), "app.old directory must still exist when restore fails");
        string oldMarkerPath = Path.Combine(appOld, markerFileName);
        Assert.IsTrue(File.Exists(oldMarkerPath), "Previous version marker file must exist in app.old");
        Assert.AreEqual(markerContent, File.ReadAllText(oldMarkerPath), "Previous version marker content in app.old must remain intact");

        // The output says the restore failed and names app.old
        string combinedOutput = result.StdOut + "\n" + result.StdErr;
        StringAssert.Contains(combinedOutput, "the restore failed");
        StringAssert.Contains(combinedOutput, "app.old");

        // The output never says "restored"
        Assert.IsFalse(combinedOutput.Contains("restored", StringComparison.OrdinalIgnoreCase),
            $"Output must never say 'restored'. Actual output was:\n{combinedOutput}");
    }
}
