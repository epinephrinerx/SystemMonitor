using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ServiceReaderTests
{
    [TestMethod]
    public void ListArguments_BuildsExactArgv_ForSystemAndUser()
    {
        IReadOnlyList<string> systemArgs = ServiceReader.ListArguments(user: false);
        CollectionAssert.AreEqual(
            new[] { "list-units", "--type=service", "--all", "--output=json", "--no-pager" },
            systemArgs.ToArray());

        IReadOnlyList<string> userArgs = ServiceReader.ListArguments(user: true);
        CollectionAssert.AreEqual(
            new[] { "list-units", "--type=service", "--all", "--output=json", "--no-pager", "--user" },
            userArgs.ToArray());
    }

    [TestMethod]
    public void ShowArguments_NormalizesUnitNames_AndAppendsFlags()
    {
        var unitNames = new[] { "cups", "bluetooth.service", "pipewire" };

        IReadOnlyList<string> systemArgs = ServiceReader.ShowArguments(unitNames, user: false);
        CollectionAssert.AreEqual(
            new[]
            {
                "show",
                "-p",
                ServiceReader.ShowProperties,
                "--no-pager",
                "--",
                "cups.service",
                "bluetooth.service",
                "pipewire.service",
            },
            systemArgs.ToArray());

        IReadOnlyList<string> userArgs = ServiceReader.ShowArguments(unitNames, user: true);
        CollectionAssert.AreEqual(
            new[]
            {
                "--user",
                "show",
                "-p",
                ServiceReader.ShowProperties,
                "--no-pager",
                "--",
                "cups.service",
                "bluetooth.service",
                "pipewire.service",
            },
            userArgs.ToArray());
    }

    [TestMethod]
    public void ShowArguments_UnitNameStartingWithDash_StaysAfterOptionTerminator()
    {
        var unitNames = new[] { "-dash-unit" };

        IReadOnlyList<string> args = ServiceReader.ShowArguments(unitNames, user: false);
        int terminatorIndex = args.ToList().IndexOf("--");
        Assert.IsTrue(terminatorIndex >= 0, "Must contain '--' option terminator");
        Assert.AreEqual(terminatorIndex + 1, args.ToList().IndexOf("-dash-unit.service"),
            "Unit starting with dash must stay after '--'");
    }

    [TestMethod]
    public void ShowArguments_OmitsInvalidUnitNames_WithSlashOrEmptyOrNul()
    {
        var unitNames = new[] { "", "valid", "bad/path.service", "bad\0char", "good.service" };

        IReadOnlyList<string> args = ServiceReader.ShowArguments(unitNames, user: false);

        // Only "valid.service" and "good.service" should appear after "--"
        int terminatorIndex = args.ToList().IndexOf("--");
        var unitsAfterTerminator = args.Skip(terminatorIndex + 1).ToList();

        CollectionAssert.AreEqual(
            new[] { "valid.service", "good.service" },
            unitsAfterTerminator);
    }

    [DataTestMethod]
    [DataRow("enabled", ServiceStart.Automatic)]
    [DataRow("enabled-runtime", ServiceStart.Automatic)]
    [DataRow("masked", ServiceStart.Disabled)]
    [DataRow("masked-runtime", ServiceStart.Disabled)]
    [DataRow("disabled", ServiceStart.Manual)]
    [DataRow("static", ServiceStart.Manual)]
    [DataRow("indirect", ServiceStart.Manual)]
    [DataRow("generated", ServiceStart.Manual)]
    [DataRow("transient", ServiceStart.Manual)]
    [DataRow(null, ServiceStart.Manual)]
    [DataRow("unknown_value", ServiceStart.Manual)]
    public void StartOf_MapsUnitFileState_ToServiceStart(string? state, ServiceStart expected)
    {
        Assert.AreEqual(expected, ServiceReader.StartOf(state));
    }

    [DataTestMethod]
    [DataRow("cups.service", "cups")]
    [DataRow("systemd-journald.service", "systemd-journald")]
    [DataRow("pipewire", "pipewire")]
    [DataRow("", "")]
    public void UnitName_StripsServiceSuffix(string input, string expected)
    {
        Assert.AreEqual(expected, ServiceReader.UnitName(input));
    }

    [TestMethod]
    public void ParseList_ParsesValidJsonArray()
    {
        const string json = """
            [
              {
                "unit": "NetworkManager.service",
                "load": "loaded",
                "active": "active",
                "sub": "running",
                "description": "Network Manager"
              },
              {
                "unit": "cron.service",
                "load": "loaded",
                "active": "inactive",
                "sub": "dead",
                "description": "Regular background program processing daemon"
              }
            ]
            """;

        IReadOnlyList<ServiceEntry> list = ServiceReader.ParseList(json, user: false);

        Assert.AreEqual(2, list.Count);

        Assert.AreEqual("NetworkManager", list[0].Name);
        Assert.AreEqual("Network Manager", list[0].Display);
        Assert.IsTrue(list[0].Running);
        Assert.AreEqual("active", list[0].State);
        Assert.IsFalse(list[0].UserUnit);

        Assert.AreEqual("cron", list[1].Name);
        Assert.AreEqual("Regular background program processing daemon", list[1].Display);
        Assert.IsFalse(list[1].Running);
        Assert.AreEqual("inactive", list[1].State);
        Assert.IsFalse(list[1].UserUnit);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("{ \"unit\": \"not an array\" }")]
    [DataRow("This is not valid json at all!")]
    public void ParseList_HandlesMalformedOrNonArrayJson_Gracefully(string malformedJson)
    {
        IReadOnlyList<ServiceEntry> list = ServiceReader.ParseList(malformedJson, user: true);
        Assert.AreEqual(0, list.Count);
    }

    [TestMethod]
    public void ParseList_SkipsElementsWithoutValidUnitName()
    {
        const string json = """
            [
              123,
              { "description": "Missing unit field" },
              { "unit": "", "description": "Empty unit string" },
              { "unit": "valid.service", "active": "active", "description": "Valid unit" }
            ]
            """;

        IReadOnlyList<ServiceEntry> list = ServiceReader.ParseList(json, user: true);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("valid", list[0].Name);
        Assert.IsTrue(list[0].UserUnit);
    }

    [TestMethod]
    public void ParseList_SkipsEntriesWithSlashOrNulInUnitName()
    {
        const string json = """
            [
              { "unit": "path/traversal.service", "active": "active", "description": "Slash unit" },
              { "unit": "/etc/systemd.service", "active": "active", "description": "Absolute path" },
              { "unit": "good.service", "active": "active", "description": "Good service" }
            ]
            """;

        IReadOnlyList<ServiceEntry> list = ServiceReader.ParseList(json, user: true);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("good", list[0].Name);
    }

    [TestMethod]
    public void ParseShow_ParsesKeyValues_SeparatedByBlankLines()
    {
        const string showText = """
            Id=cups.service
            MainPID=1420
            UnitFileState=enabled
            ActiveState=active
            SubState=running
            Description=CUPS Scheduler

            Id=pulseaudio.service
            MainPID=0
            UnitFileState=disabled
            ActiveState=inactive
            SubState=dead
            Description=PulseAudio Sound System
            """;

        Dictionary<string, ServiceReader.ShowState> states = ServiceReader.ParseShow(showText);

        Assert.AreEqual(2, states.Count);
        Assert.IsTrue(states.ContainsKey("cups"));
        Assert.IsTrue(states.ContainsKey("pulseaudio"));

        ServiceReader.ShowState cups = states["cups"];
        Assert.AreEqual("cups.service", cups.Id);
        Assert.AreEqual(1420, cups.MainPid);
        Assert.AreEqual("enabled", cups.UnitFileState);
        Assert.AreEqual("active", cups.ActiveState);
        Assert.AreEqual("running", cups.SubState);
        Assert.AreEqual("CUPS Scheduler", cups.Description);

        ServiceReader.ShowState pulse = states["pulseaudio"];
        Assert.AreEqual("pulseaudio.service", pulse.Id);
        Assert.AreEqual(0, pulse.MainPid);
        Assert.AreEqual("disabled", pulse.UnitFileState);
        Assert.AreEqual("inactive", pulse.ActiveState);
        Assert.AreEqual("dead", pulse.SubState);
        Assert.AreEqual("PulseAudio Sound System", pulse.Description);
    }

    [TestMethod]
    public void ParseShow_HandlesInvalidOrNegativePid_AsZero()
    {
        const string showText = """
            Id=test.service
            MainPID=-5
            UnitFileState=masked
            """;

        Dictionary<string, ServiceReader.ShowState> states = ServiceReader.ParseShow(showText);
        Assert.IsTrue(states.ContainsKey("test"));
        Assert.AreEqual(0, states["test"].MainPid);
    }

    [TestMethod]
    public void ReadScope_ReturnsEmpty_WhenCommandRunnerReturnsNull()
    {
        var runner = new FakeCommandRunner();
        var reader = new ServiceReader(runner);

        IReadOnlyList<ServiceEntry> services = reader.ReadScope(user: false);
        Assert.AreEqual(0, services.Count);
    }

    [TestMethod]
    public void ReadScope_CombinesListAndShowData_SettingPropertiesCorrectly()
    {
        var runner = new FakeCommandRunner
        {
            CustomHandler = (file, args, timeoutMs) =>
            {
                if (args.Contains("list-units"))
                {
                    return """
                        [
                          {
                            "unit": "gdm.service",
                            "active": "active",
                            "description": "Initial GDM Description"
                          }
                        ]
                        """;
                }

                if (args.Contains("show"))
                {
                    return """
                        Id=gdm.service
                        MainPID=980
                        UnitFileState=enabled
                        ActiveState=active
                        SubState=running
                        Description=GNOME Display Manager
                        """;
                }

                return null;
            }
        };

        var reader = new ServiceReader(runner);
        IReadOnlyList<ServiceEntry> services = reader.ReadScope(user: false);

        Assert.AreEqual(1, services.Count);
        ServiceEntry gdm = services[0];
        Assert.AreEqual("gdm", gdm.Name);
        Assert.AreEqual("GNOME Display Manager", gdm.Display); // Description updated from show
        Assert.IsTrue(gdm.Running);
        Assert.AreEqual("active", gdm.State);
        Assert.AreEqual(980, gdm.Pid);
        Assert.AreEqual(ServiceStart.Automatic, gdm.Start); // enabled -> Automatic
        Assert.IsFalse(gdm.UserUnit);
    }

    [TestMethod]
    public void Read_CombinesUserAndSystemScopes_ReturningUserUnitsFirst()
    {
        var runner = new FakeCommandRunner
        {
            CustomHandler = (file, args, timeoutMs) =>
            {
                bool isUser = args.Contains("--user");
                if (args.Contains("list-units"))
                {
                    if (isUser)
                    {
                        return """
                            [
                              { "unit": "pipewire.service", "active": "active", "description": "PipeWire Multimedia Service" }
                            ]
                            """;
                    }

                    return """
                        [
                          { "unit": "systemd-resolved.service", "active": "active", "description": "Network Name Resolution" }
                        ]
                        """;
                }

                if (args.Contains("show"))
                {
                    if (isUser)
                    {
                        return """
                            Id=pipewire.service
                            MainPID=2100
                            UnitFileState=enabled
                            ActiveState=active
                            SubState=running
                            Description=PipeWire Multimedia Service
                            """;
                    }

                    return """
                        Id=systemd-resolved.service
                        MainPID=450
                        UnitFileState=enabled
                        ActiveState=active
                        SubState=running
                        Description=Network Name Resolution
                        """;
                }

                return null;
            }
        };

        var reader = new ServiceReader(runner);
        IReadOnlyList<ServiceEntry> all = reader.Read();

        Assert.AreEqual(2, all.Count);

        // User units first
        Assert.AreEqual("pipewire", all[0].Name);
        Assert.IsTrue(all[0].UserUnit);
        Assert.AreEqual(2100, all[0].Pid);
        Assert.AreEqual(ServiceStart.Automatic, all[0].Start);

        // System units second
        Assert.AreEqual("systemd-resolved", all[1].Name);
        Assert.IsFalse(all[1].UserUnit);
        Assert.AreEqual(450, all[1].Pid);
        Assert.AreEqual(ServiceStart.Automatic, all[1].Start);
    }
}
