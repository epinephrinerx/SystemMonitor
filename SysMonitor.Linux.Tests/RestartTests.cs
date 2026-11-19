using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class RestartTests
{
    [TestMethod]
    public void ProcessRestarter_DefaultExecutable_IsProcessPath()
    {
        var restarter = new ProcessRestarter();

        string expected = Environment.ProcessPath ?? "sysmonitor";
        Assert.AreEqual(expected, restarter.Executable);
        Assert.AreEqual(0, restarter.Arguments.Count);
    }

    [TestMethod]
    public void ProcessRestarter_CustomExecutableAndArguments_AreStored()
    {
        string[] args = { "--full", "--snap", "--heartbeat", "300" };
        var restarter = new ProcessRestarter("/usr/bin/sysmonitor", args);

        Assert.AreEqual("/usr/bin/sysmonitor", restarter.Executable);
        CollectionAssert.AreEqual(args, restarter.Arguments.ToArray());
    }

    [TestMethod]
    public void ProcessRestarter_Restart_InvokesStartFunctionWithExactExecutableAndArguments()
    {
        string? capturedFile = null;
        IReadOnlyList<string>? capturedArgs = null;

        string[] args = { "--expanded", "path with spaces", "\"quoted\"" };
        var restarter = new ProcessRestarter(
            executable: "/opt/sysmonitor/bin/sysmonitor",
            arguments: args,
            start: (file, arguments) =>
            {
                capturedFile = file;
                capturedArgs = arguments;
                return true;
            });

        bool result = restarter.Restart();

        Assert.IsTrue(result);
        Assert.AreEqual("/opt/sysmonitor/bin/sysmonitor", capturedFile);
        Assert.IsNotNull(capturedArgs);
        CollectionAssert.AreEqual(args, capturedArgs.ToArray());
    }

    [TestMethod]
    public void ProcessRestarter_Restart_PropagatesFalseFromStartFunction()
    {
        var restarter = new ProcessRestarter(
            executable: "/usr/bin/sysmonitor",
            arguments: Array.Empty<string>(),
            start: (_, _) => false);

        Assert.IsFalse(restarter.Restart());
    }

    [TestMethod]
    public void ProcessRestarter_StartInfo_UsesArgumentListWithoutShell()
    {
        string exe = "/usr/bin/sysmonitor";
        string[] args = { "--full", "--heartbeat", "60", "path with spaces" };

        ProcessStartInfo info = Restart.BuildStartInfo(exe, args);

        Assert.AreEqual(exe, info.FileName);
        Assert.IsFalse(info.UseShellExecute, "UseShellExecute must be false so no shell is involved.");
        Assert.IsFalse(info.CreateNoWindow);
        Assert.AreEqual(4, info.ArgumentList.Count);
        CollectionAssert.AreEqual(args, info.ArgumentList.ToArray());
    }

    [TestMethod]
    public void Restart_BuildStartInfo_PreservesOriginalArgumentsUnchanged()
    {
        string exe = "/opt/sysmonitor/sysmonitor";
        string[] args = { "--snap", "argument with spaces", "\"already-quoted\"", "$VARIABLE", "`cmd`" };

        ProcessStartInfo info = Restart.BuildStartInfo(exe, args);

        Assert.AreEqual(exe, info.FileName);
        Assert.IsFalse(info.UseShellExecute);
        Assert.IsFalse(info.CreateNoWindow);
        CollectionAssert.AreEqual(args, info.ArgumentList.ToArray());
    }

    [TestMethod]
    public void StartupOptions_Parse_PreservesOriginalArguments()
    {
        string[] args = { "--expanded", "--heartbeat", "120", "--allow-multiple" };
        StartupOptions options = StartupOptions.Parse(args);

        Assert.IsTrue(options.Expanded);
        Assert.IsFalse(options.Full);
        Assert.IsTrue(options.AllowMultiple);
        Assert.AreEqual(120, options.HeartbeatSeconds);
        CollectionAssert.AreEqual(args, options.Arguments.ToArray());
    }

    [TestMethod]
    public void StartupOptions_StartView_FullWinsOverExpanded()
    {
        var config = new AppConfig { LastView = "widget" };

        StartupOptions fullAndExpanded = StartupOptions.Parse(new[] { "--full", "--expanded" });
        Assert.AreEqual(ViewMode.Full, fullAndExpanded.StartView(config));

        StartupOptions expandedOnly = StartupOptions.Parse(new[] { "--expanded" });
        Assert.AreEqual(ViewMode.Overall, expandedOnly.StartView(config));

        StartupOptions none = StartupOptions.Parse(Array.Empty<string>());
        Assert.AreEqual(ViewMode.Widget, none.StartView(config));
    }

    [TestMethod]
    public void NoopRestarter_Restart_ReturnsFalse()
    {
        var noop = new NoopRestarter();
        Assert.IsFalse(noop.Restart());
    }
}
