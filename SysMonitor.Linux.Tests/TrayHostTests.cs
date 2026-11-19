using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class TrayHostTests
{
    private sealed class FakeCommandRunner : ICommandRunner
    {
        public string? CapturedFile { get; private set; }
        public IReadOnlyList<string>? CapturedArgs { get; private set; }
        public int CapturedTimeoutMs { get; private set; }

        public Func<string, IReadOnlyList<string>, int, CommandResult?>? ResultHandler { get; set; }

        public string? Run(string file, string args, int timeoutMs) => null;

        public CommandResult? RunResult(string file, IReadOnlyList<string> args, int timeoutMs)
        {
            CapturedFile = file;
            CapturedArgs = args;
            CapturedTimeoutMs = timeoutMs;
            if (ResultHandler != null)
            {
                return ResultHandler(file, args, timeoutMs);
            }
            return new CommandResult(0, "active", string.Empty);
        }
    }

    [TestMethod]
    public void TrayHost_ConstantsAndArguments_MatchStatusNotifierWatcherSpecification()
    {
        Assert.AreEqual("busctl", TrayHost.Program);
        Assert.AreEqual("org.kde.StatusNotifierWatcher", TrayHost.Service);
        Assert.AreEqual(2000, TrayHost.TimeoutMs);

        CollectionAssert.AreEqual(
            new[] { "--user", "--no-pager", "status", "org.kde.StatusNotifierWatcher" },
            TrayHost.Arguments.ToArray());
    }

    [TestMethod]
    public void TrayHost_IsAvailable_ExitZero_ReturnsTrue_AndPassesExpectedArgv()
    {
        var runner = new FakeCommandRunner
        {
            ResultHandler = (file, args, timeoutMs) => new CommandResult(0, "Type=dbus", string.Empty)
        };

        bool available = TrayHost.IsAvailable(runner);

        Assert.IsTrue(available);
        Assert.AreEqual("busctl", runner.CapturedFile);
        Assert.AreEqual(TrayHost.TimeoutMs, runner.CapturedTimeoutMs);
        Assert.IsNotNull(runner.CapturedArgs);
        CollectionAssert.AreEqual(TrayHost.Arguments.ToArray(), runner.CapturedArgs.ToArray());
    }

    [TestMethod]
    public void TrayHost_IsAvailable_NonZeroExit_ReturnsFalse()
    {
        var runner = new FakeCommandRunner
        {
            ResultHandler = (file, args, timeoutMs) => new CommandResult(1, string.Empty, "Unit org.kde.StatusNotifierWatcher not found.")
        };

        bool available = TrayHost.IsAvailable(runner);

        Assert.IsFalse(available);
    }

    [TestMethod]
    public void TrayHost_IsAvailable_NullResult_ReturnsFalse()
    {
        // Null result represents process execution timeout or failure to start
        var runner = new FakeCommandRunner
        {
            ResultHandler = (file, args, timeoutMs) => null
        };

        bool available = TrayHost.IsAvailable(runner);

        Assert.IsFalse(available);
    }

    [TestMethod]
    public void TrayHost_IsAvailable_ThrownException_ReturnsFalse()
    {
        var runner = new FakeCommandRunner
        {
            ResultHandler = (file, args, timeoutMs) => throw new InvalidOperationException("Command runner crashed")
        };

        bool available = TrayHost.IsAvailable(runner);

        Assert.IsFalse(available);
    }

    [TestMethod]
    public void TrayHost_IsAvailable_CustomTimeout_PassesTimeoutToRunner()
    {
        var runner = new FakeCommandRunner
        {
            ResultHandler = (file, args, timeoutMs) => new CommandResult(0, string.Empty, string.Empty)
        };

        bool available = TrayHost.IsAvailable(runner, 4500);

        Assert.IsTrue(available);
        Assert.AreEqual(4500, runner.CapturedTimeoutMs);
    }
}
