using System.Xml.Linq;
using SysMonitor.Setup;

namespace SysMonitor.Tests;

/// <summary>
/// R-022: the Scheduled Task definition and the schtasks command lines. Built
/// and parsed only; nothing is registered with Windows.
/// </summary>
[TestClass]
public class ScheduledTasksTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private const string Exe = @"C:\Program Files\SystemMonitor\SystemMonitor.exe";
    private const string Dir = @"C:\Program Files\SystemMonitor";
    private const string User = @"WORKGROUP\tester";

    private static XElement Parse(string xml) => XDocument.Parse(xml).Root!;

    [TestMethod]
    public void The_task_starts_the_program_at_logon_of_that_user_only()
    {
        XElement task = Parse(ScheduledTasks.BuildXml(Exe, Dir, User));

        XElement trigger = task.Element(Ns + "Triggers")!.Elements().Single();
        Assert.AreEqual("LogonTrigger", trigger.Name.LocalName);
        Assert.AreEqual(User, trigger.Element(Ns + "UserId")!.Value,
            "a trigger with no user would start it for everyone who signs in");
    }

    [TestMethod]
    public void The_task_runs_as_that_user_with_the_highest_privileges_and_not_as_system()
    {
        XElement principal = Parse(ScheduledTasks.BuildXml(Exe, Dir, User))
            .Element(Ns + "Principals")!.Elements().Single();

        Assert.AreEqual(User, principal.Element(Ns + "UserId")!.Value);
        Assert.AreEqual("HighestAvailable", principal.Element(Ns + "RunLevel")!.Value);
        Assert.AreEqual("InteractiveToken", principal.Element(Ns + "LogonType")!.Value,
            "only while the user is signed in, so the widget has a desktop to draw on");
        StringAssert.DoesNotMatch(principal.ToString(), new System.Text.RegularExpressions.Regex("S-1-5-18|SYSTEM"));
    }

    [TestMethod]
    public void The_task_runs_exactly_the_installed_program_from_its_folder()
    {
        XElement exec = Parse(ScheduledTasks.BuildXml(Exe, Dir, User))
            .Element(Ns + "Actions")!.Elements().Single();

        Assert.AreEqual("Exec", exec.Name.LocalName);
        Assert.AreEqual(Exe, exec.Element(Ns + "Command")!.Value);
        Assert.AreEqual(Dir, exec.Element(Ns + "WorkingDirectory")!.Value);
        Assert.IsNull(exec.Element(Ns + "Arguments"), "nothing is passed on the command line");
    }

    [TestMethod]
    public void The_task_is_not_stopped_by_battery_time_limits_or_a_second_copy()
    {
        XElement settings = Parse(ScheduledTasks.BuildXml(Exe, Dir, User)).Element(Ns + "Settings")!;

        Assert.AreEqual("PT0S", settings.Element(Ns + "ExecutionTimeLimit")!.Value);
        Assert.AreEqual("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.AreEqual("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
        Assert.AreEqual("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
    }

    [TestMethod]
    public void The_definition_declares_utf16_as_schtasks_expects()
    {
        string xml = ScheduledTasks.BuildXml(Exe, Dir, User);

        StringAssert.StartsWith(xml, "<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
    }

    [TestMethod]
    public void A_path_with_xml_characters_cannot_break_out_of_its_element()
    {
        const string odd = @"C:\Tom & Jerry <x>\SystemMonitor.exe";

        XElement task = Parse(ScheduledTasks.BuildXml(odd, @"C:\Tom & Jerry <x>", @"DOM\a&b"));

        Assert.AreEqual(odd, task.Descendants(Ns + "Command").Single().Value);
        Assert.AreEqual(@"DOM\a&b", task.Descendants(Ns + "UserId").First().Value);
        Assert.AreEqual(1, task.Descendants(Ns + "Exec").Count(), "no extra action was injected");
    }

    [TestMethod]
    public void A_relative_path_or_a_missing_user_is_refused()
    {
        Assert.ThrowsException<ArgumentException>(() => ScheduledTasks.BuildXml(@"SystemMonitor.exe", Dir, User));
        Assert.ThrowsException<ArgumentException>(() => ScheduledTasks.BuildXml(Exe, @"..\x", User));
        Assert.ThrowsException<ArgumentException>(() => ScheduledTasks.BuildXml(Exe, Dir, ""));
        Assert.ThrowsException<ArgumentException>(() => ScheduledTasks.BuildXml(Exe, Dir, "a\nb"));
    }

    [TestMethod]
    public void A_task_is_built_only_for_the_machine_wide_layout()
    {
        var folders = new KnownFolders(@"C:\L", @"C:\R", @"C:\Program Files", @"C:\D", @"C:\P", @"C:\CD", @"C:\CP");

        string xml = ScheduledTasks.BuildXml(InstallLayout.Create(InstallScope.Admin, folders), User);

        StringAssert.Contains(xml, @"C:\Program Files\SystemMonitor\SystemMonitor.exe");
        Assert.ThrowsException<InvalidOperationException>(
            () => ScheduledTasks.BuildXml(InstallLayout.Create(InstallScope.PerUser, folders), User));
    }

    [TestMethod]
    public void A_task_the_app_switched_off_is_not_mistaken_for_one_that_is_on()
    {
        const string template = "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
            + "<Settings><Enabled>{0}</Enabled></Settings></Task>";

        Assert.AreEqual(true, ScheduledTasks.ParseEnabled(string.Format(template, "true")));
        Assert.AreEqual(false, ScheduledTasks.ParseEnabled(string.Format(template, "false")));
        Assert.AreEqual(true, ScheduledTasks.ParseEnabled(
            "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Settings/></Task>"));
        Assert.IsNull(ScheduledTasks.ParseEnabled("ERROR: The system cannot find the file specified."));
    }

    [TestMethod]
    public void The_schtasks_command_lines_are_argument_lists_with_a_plain_task_name()
    {
        CollectionAssert.AreEqual(
            new[] { "/Create", "/TN", "SystemMonitor", "/XML", @"C:\t\task.xml", "/F" },
            ScheduledTasks.CreateArguments("SystemMonitor", @"C:\t\task.xml").ToArray());
        CollectionAssert.AreEqual(
            new[] { "/Delete", "/TN", "SystemMonitor", "/F" },
            ScheduledTasks.DeleteArguments("SystemMonitor").ToArray());
        CollectionAssert.AreEqual(
            new[] { "/Query", "/TN", "SystemMonitor" },
            ScheduledTasks.QueryArguments("SystemMonitor").ToArray());

        foreach (string bad in new[] { "", " ", @"\Microsoft\Windows\Defrag", "a/b", "a\nb" })
        {
            Assert.ThrowsException<ArgumentException>(() => ScheduledTasks.DeleteArguments(bad),
                $"'{bad}' must not reach schtasks");
        }
    }
}
