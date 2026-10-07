using System.Diagnostics;
using SysMonitor.Model;
using SysMonitor.Native;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>Grouping processes, counting who shares a host, and the size text.</summary>
[TestClass]
public class MemoryBoardTests
{
    private const long Mb = 1048576;

    private static ProcessEntry P(int pid, string name, long mb, bool readable = true) =>
        new() { Pid = pid, Name = name, WorkingSet = mb * Mb, PrivateBytes = mb * Mb / 2, Readable = readable };

    [TestMethod]
    public void Processes_of_one_name_become_one_group_biggest_first()
    {
        var groups = MemoryBoard.Group(new[]
        {
            P(1, "chrome", 300), P(2, "code", 500), P(3, "Chrome", 400), P(4, "tiny", 1),
        });

        CollectionAssert.AreEqual(new[] { "chrome", "code", "tiny" }, groups.Select(g => g.Name).ToList());
        Assert.AreEqual(2, groups[0].Count);
        Assert.AreEqual(700 * Mb, groups[0].WorkingSet);
        CollectionAssert.AreEqual(new[] { 3, 1 }, groups[0].Members.Select(m => m.Pid).ToList());
    }

    [TestMethod]
    public void A_service_gets_its_host_process_memory_and_a_count_of_who_shares_it()
    {
        var processes = new[] { P(10, "svchost", 80), P(20, "own", 30) };
        var services = new[]
        {
            new ServiceEntry { Name = "A", Pid = 10, Running = true },
            new ServiceEntry { Name = "B", Pid = 10, Running = true },
            new ServiceEntry { Name = "C", Pid = 20, Running = true },
            new ServiceEntry { Name = "D", Pid = 0 },
        };

        var attached = MemoryBoard.Attach(services, processes).ToDictionary(s => s.Name);

        Assert.AreEqual(80 * Mb, attached["A"].WorkingSet);
        Assert.AreEqual(2, attached["A"].SharedWith);
        Assert.AreEqual(2, attached["B"].SharedWith);
        Assert.AreEqual(1, attached["C"].SharedWith);
        Assert.AreEqual(0, attached["D"].WorkingSet);
    }

    [TestMethod]
    public void A_stopped_service_has_no_memory_and_shares_with_nobody()
    {
        var stopped = new ServiceEntry { Name = "S", Pid = 0 };
        var attached = MemoryBoard.Attach(new[] { stopped }, new[] { P(0, "idle", 10) }).Single();

        Assert.AreEqual(0, attached.WorkingSet);
        Assert.AreEqual(1, attached.SharedWith);
    }

    [TestMethod]
    [DataRow(0L, "0.0 MB")]
    [DataRow(5L * Mb, "5.0 MB")]
    [DataRow(340L * Mb, "340 MB")]
    [DataRow(2048L * Mb, "2.0 GB")]
    public void Sizes_use_the_unit_that_keeps_the_number_short(long bytes, string text) =>
        Assert.AreEqual(text, MemoryBoard.Size(bytes));
}

/// <summary>What may be ended, and what the elevated copy is told to do.</summary>
[TestClass]
public class MemoryToolsTests
{
    private static ProcessEntry P(int pid, string name, bool readable = true) =>
        new() { Pid = pid, Name = name, Readable = readable };

    [TestMethod]
    [DataRow("csrss")]
    [DataRow("LSASS")]
    [DataRow("wininit")]
    [DataRow("Memory Compression")]
    public void Processes_windows_depends_on_cannot_be_ended(string name) =>
        Assert.IsFalse(MemoryTools.CanKill(P(1234, name)));

    [TestMethod]
    public void The_program_itself_and_the_system_pid_cannot_be_ended()
    {
        Assert.IsFalse(MemoryTools.CanKill(P(Environment.ProcessId, "anything")));
        Assert.IsFalse(MemoryTools.CanKill(P(4, "whatever")));
        Assert.IsFalse(MemoryTools.CanKill(P(0, "whatever")));
    }

    [TestMethod]
    public void An_unreadable_process_is_not_offered_for_ending() =>
        Assert.IsFalse(MemoryTools.CanKill(P(500, "mystery", readable: false)));

    [TestMethod]
    public void An_ordinary_process_can_be_ended() =>
        Assert.IsTrue(MemoryTools.CanKill(P(500, "notepad")));

    [TestMethod]
    public void The_elevated_copy_is_told_which_pid_and_name_so_a_reused_pid_is_not_hit()
    {
        var args = MemoryTools.KillArguments(new[] { P(11, "a"), P(22, "b c") });

        CollectionAssert.AreEqual(
            new[] { MemoryTools.ElevatedSwitch, "kill", "11:a", "22:b c" }, args);
    }

    [TestMethod]
    public void The_elevated_launch_asks_Windows_for_administrator_and_runs_hidden()
    {
        ProcessStartInfo start = MemoryTools.ElevatedStart(@"C:\x\SystemMonitor.exe",
            MemoryTools.ServiceArguments(ServiceOp.SetStart, "Spooler", ServiceStart.Disabled));

        Assert.AreEqual("runas", start.Verb);
        Assert.IsTrue(start.UseShellExecute);
        CollectionAssert.AreEqual(
            new[] { MemoryTools.ElevatedSwitch, "service", "setstart", "Spooler", "disabled" },
            start.ArgumentList.ToList());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("bad\"name")]
    [DataRow("line\nbreak")]
    public void A_name_that_could_break_out_of_a_command_line_is_refused(string name) =>
        Assert.IsFalse(MemoryTools.ValidName(name));

    [TestMethod]
    public void The_elevated_copy_refuses_jobs_it_does_not_understand()
    {
        string sw = MemoryTools.ElevatedSwitch;
        Assert.AreEqual(1, MemoryTools.RunElevated(Array.Empty<string>()));
        Assert.AreEqual(1, MemoryTools.RunElevated(new[] { sw }));
        Assert.AreEqual(1, MemoryTools.RunElevated(new[] { sw, "format", "C:" }));
        Assert.AreEqual(1, MemoryTools.RunElevated(new[] { sw, "kill", "notapid:x" }));
        Assert.AreEqual(1, MemoryTools.RunElevated(new[] { sw, "kill", "5" }));
        Assert.AreEqual(1, MemoryTools.RunElevated(new[] { sw, "service", "explode", "Spooler" }));
        Assert.AreEqual(1, MemoryTools.RunElevated(new[] { sw, "service", "setstart", "Spooler" }));
    }

    [TestMethod]
    public void The_elevated_copy_will_not_end_a_protected_process()
    {
        Assert.AreEqual(1, MemoryTools.RunElevated(
            new[] { MemoryTools.ElevatedSwitch, "kill", $"{Environment.ProcessId}:anything" }));
        Assert.AreEqual(1, MemoryTools.RunElevated(
            new[] { MemoryTools.ElevatedSwitch, "kill", "700:csrss" }));
    }

    [TestMethod]
    public void A_process_that_is_already_gone_counts_as_done() =>
        Assert.AreEqual(0, MemoryTools.KillLocal(0x7FFFFFF0, "ghost"));

    private static Process Sleeper() =>
        Process.Start(new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

    [TestMethod]
    public async Task Ending_a_process_we_started_ends_it_and_a_wrong_name_leaves_it_alone()
    {
        using Process victim = Sleeper();
        try
        {
            // Same pid, a different name: not the process we were told about.
            Assert.AreEqual(0, MemoryTools.KillLocal(victim.Id, "someone-else"));
            Assert.IsFalse(victim.HasExited);

            var entry = new ProcessEntry { Pid = victim.Id, Name = victim.ProcessName };
            OpResult result = await MemoryTools.KillAsync(new[] { entry });

            Assert.IsTrue(result.Ok, result.Message);
            Assert.IsTrue(victim.WaitForExit(5000));
        }
        finally
        {
            if (!victim.HasExited)
            {
                victim.Kill();
            }
        }
    }

    [TestMethod]
    public async Task Ending_only_protected_processes_does_nothing_and_says_so()
    {
        OpResult result = await MemoryTools.KillAsync(new[] { P(Environment.ProcessId, "x") });

        Assert.IsFalse(result.Ok);
        Assert.IsFalse(result.Cancelled);
    }
}

/// <summary>The service table, read without elevation.</summary>
[TestClass]
public class ServiceControlTests
{
    [TestMethod]
    public void The_service_table_lists_running_services_with_a_process_each()
    {
        List<ServiceEntry> services = ServiceControl.List();

        Assert.IsTrue(services.Count > 20, "a Windows machine has dozens of services");
        ServiceEntry? running = services.FirstOrDefault(s => s.Running && s.Pid > 0);
        Assert.IsNotNull(running);
        Assert.AreEqual("Running", running.State);
        Assert.IsTrue(services.All(s => s.Name.Length > 0 && s.Display.Length > 0));
    }

    [TestMethod]
    public void The_start_type_comes_from_the_registry_values()
    {
        Assert.AreEqual(ServiceStart.Automatic, ServiceControl.StartFromRegistry(2));
        Assert.AreEqual(ServiceStart.Manual, ServiceControl.StartFromRegistry(3));
        Assert.AreEqual(ServiceStart.Disabled, ServiceControl.StartFromRegistry(4));
        Assert.AreEqual(ServiceStart.Other, ServiceControl.StartFromRegistry(0));
        Assert.AreEqual(ServiceStart.Other, ServiceControl.StartFromRegistry(null));
    }

    [TestMethod]
    public void Setting_the_start_type_to_something_that_is_not_one_of_the_three_is_refused()
    {
        Assert.IsFalse(ServiceControl.SetStart("Spooler", ServiceStart.Other).Ok);
    }

    [TestMethod]
    public void A_service_that_does_not_exist_is_an_error_not_a_crash()
    {
        Assert.IsFalse(ServiceControl.Stop("NoSuchServiceXyz").Ok);
        Assert.IsFalse(ServiceControl.Start("NoSuchServiceXyz").Ok);
    }
}

/// <summary>The lists on the memory tab, from the model.</summary>
[TestClass]
public class MemoryViewModelTests
{
    private const long Mb = 1048576;

    private static ProcessEntry P(int pid, string name, long mb, bool readable = true) =>
        new() { Pid = pid, Name = name, WorkingSet = mb * Mb, PrivateBytes = mb * Mb / 2, Readable = readable };

    private static Snapshot Snap(IEnumerable<ProcessEntry> processes,
                                 IEnumerable<ServiceEntry>? services = null) => new()
    {
        Ready = true,
        CpuTotal = 10,
        Cores = new[] { new Core { Usage = 5 } },
        Ram = new Ram { Usage = 50, UsedGb = 8, TotalGb = 16 },
        Processes = processes.ToList(),
        Services = MemoryBoard.Attach(services ?? Array.Empty<ServiceEntry>(), processes),
        MemoryStamp = 1,
    };

    private static WidgetViewModel Model() => new(new AppConfig { Lang = "en" });

    [TestMethod]
    public void The_tab_lists_each_group_with_its_count_and_size()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(new[] { P(1, "chrome", 300), P(2, "chrome", 200), P(3, "code", 100) }));

        var groups = model.Memory.Groups;
        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual("chrome", groups[0].Name);
        Assert.AreEqual("2 processes", groups[0].Count);
        Assert.AreEqual("500 MB", groups[0].Size);
        Assert.AreEqual(string.Empty, groups[1].Count);
        Assert.AreEqual(2, groups[0].Members.Count);
    }

    [TestMethod]
    public void A_group_that_was_opened_stays_open_and_keeps_its_row_when_the_numbers_change()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(new[] { P(1, "chrome", 300), P(3, "code", 100) }));
        ProcessGroupVm row = model.Memory.Groups[0];
        row.Expanded = true;

        model.PushHistory(Snap(new[] { P(1, "chrome", 90), P(3, "code", 400) }));

        Assert.AreEqual("code", model.Memory.Groups[0].Name);          // order follows size
        ProcessGroupVm same = model.Memory.Groups.Single(g => g.Name == "chrome");
        Assert.AreSame(row, same);                                     // the very same row
        Assert.IsTrue(same.Expanded);
        Assert.AreEqual("90.0 MB", same.Size);
    }

    [TestMethod]
    public void A_process_that_ends_leaves_the_list()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(new[] { P(1, "a", 10), P(2, "b", 20) }));
        model.PushHistory(Snap(new[] { P(2, "b", 20) }));

        CollectionAssert.AreEqual(new[] { "b" }, model.Memory.Groups.Select(g => g.Name).ToList());
    }

    [TestMethod]
    public void The_search_box_filters_processes_and_services_by_name()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(
            new[] { P(1, "chrome", 10), P(2, "code", 20) },
            new[]
            {
                new ServiceEntry { Name = "Spooler", Display = "Print Spooler", Running = true, Pid = 1 },
                new ServiceEntry { Name = "Winmgmt", Display = "WMI", Running = true, Pid = 2 },
            }));

        model.Memory.Filter = "SPOOL";

        Assert.AreEqual(0, model.Memory.Groups.Count);
        Assert.AreEqual("Spooler", model.Memory.Services.Single().Name);

        model.Memory.Filter = "ch";
        Assert.AreEqual("chrome", model.Memory.Groups.Single().Name);

        model.Memory.Filter = string.Empty;
        Assert.AreEqual(2, model.Memory.Groups.Count);
        Assert.AreEqual(2, model.Memory.Services.Count);
    }

    [TestMethod]
    public void A_long_list_is_cut_and_says_how_much_was_left_out_until_you_search()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(Enumerable.Range(1, 100).Select(i => P(i, "proc" + i, i))));

        Assert.AreEqual(MemoryViewModel.GroupCap, model.Memory.Groups.Count);
        StringAssert.Contains(model.Memory.GroupNote, "100");

        model.Memory.Filter = "proc";
        Assert.AreEqual(100, model.Memory.Groups.Count);
        Assert.AreEqual(string.Empty, model.Memory.GroupNote);
    }

    [TestMethod]
    public void A_process_windows_will_not_describe_is_listed_as_unreadable_and_cannot_be_ended()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(new[] { P(77, "secret", 0, readable: false) }));

        ProcessVm member = model.Memory.Groups.Single().Members.Single();
        Assert.AreEqual("no access", member.Size);
        Assert.IsFalse(member.CanKill);
    }

    [TestMethod]
    public void System_processes_are_listed_but_have_no_end_button()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(new[] { P(600, "csrss", 5), P(601, "notepad", 5) }));

        Assert.IsFalse(model.Memory.Groups.Single(g => g.Name == "csrss").CanKill);
        Assert.IsTrue(model.Memory.Groups.Single(g => g.Name == "notepad").CanKill);
    }

    [TestMethod]
    public void A_service_row_says_its_state_start_type_and_who_it_shares_a_process_with()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(
            new[] { P(10, "svchost", 50) },
            new[]
            {
                new ServiceEntry { Name = "A", Display = "Alpha", Running = true, State = "Running",
                                   Pid = 10, Start = ServiceStart.Automatic },
                new ServiceEntry { Name = "B", Display = "Beta", Running = true, State = "Running",
                                   Pid = 10, Start = ServiceStart.Manual },
                new ServiceEntry { Name = "C", Display = "Gamma", State = "Stopped",
                                   Start = ServiceStart.Disabled },
            }));

        ServiceVm alpha = model.Memory.Services.Single(s => s.Name == "A");
        StringAssert.Contains(alpha.Memory, "PID 10");
        StringAssert.Contains(alpha.Memory, "50.0 MB");
        StringAssert.Contains(alpha.Shared, "2");
        Assert.AreEqual("Automatic", alpha.Startup);
        Assert.IsTrue(alpha.Running);

        ServiceVm gamma = model.Memory.Services.Single(s => s.Name == "C");
        Assert.AreEqual(string.Empty, gamma.Memory);
        Assert.IsFalse(gamma.CanStart);                 // disabled: it would not start
        Assert.AreEqual(2, model.Memory.ServicesOn(10));
    }

    [TestMethod]
    public void Running_services_come_first()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(
            new[] { P(10, "x", 5) },
            new[]
            {
                new ServiceEntry { Name = "Off", Display = "Off", State = "Stopped" },
                new ServiceEntry { Name = "On", Display = "On", Running = true, State = "Running", Pid = 10 },
            }));

        Assert.AreEqual("On", model.Memory.Services[0].Name);
    }

    [TestMethod]
    public void Picking_a_group_puts_its_graph_on_the_memory_tab_with_the_past_it_already_has()
    {
        WidgetViewModel model = Model();
        for (int i = 1; i <= 5; i++)
        {
            model.PushHistory(Snap(new[] { P(1, "chrome", 100 * i), P(2, "code", 50) }));
        }

        model.Memory.Toggle("chrome");
        model.PushHistory(Snap(new[] { P(1, "chrome", 600), P(2, "code", 50) }));

        DeviceTab ram = model.Tabs.Single(t => t.Key == "ram");
        ChartCard card = ram.Cards.Single(c => c.Key == "proc:chrome");
        Assert.AreEqual("chrome", card.Title);
        Assert.IsTrue(card.Series.Count >= 6, "the earlier readings came with it");
        Assert.AreEqual(600, card.Series.Latest, 0.01);
        StringAssert.EndsWith(card.Ceiling, " MB");
        Assert.IsTrue(model.Memory.Groups.Single(g => g.Name == "chrome").Selected);

        // Picking it again takes the graph away.
        model.Memory.Toggle("chrome");
        model.PushHistory(Snap(new[] { P(1, "chrome", 600) }));
        Assert.IsFalse(ram.Cards.Any(c => c.Key.StartsWith("proc:")));
    }

    [TestMethod]
    public void A_picked_group_that_stops_running_drops_its_graph()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(new[] { P(1, "chrome", 100), P(2, "code", 50) }));
        model.Memory.Toggle("chrome");
        model.PushHistory(Snap(new[] { P(1, "chrome", 100), P(2, "code", 50) }));

        model.PushHistory(Snap(new[] { P(2, "code", 50) }));

        Assert.IsNull(model.Memory.SelectedGroup);
        Assert.IsFalse(model.Tabs.Single(t => t.Key == "ram").Cards.Any(c => c.Key.StartsWith("proc:")));
    }

    [TestMethod]
    public void The_overall_memory_heading_names_the_biggest_process_and_so_does_the_widget_page()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Snap(new[] { P(1, "chrome", 1500), P(2, "chrome", 700), P(3, "code", 100) });
        model.UpdateOverall(snap);

        Section ram = model.Sections.Single(s => s.Title.Length > 0 && s.Rows.Count == 1
                                                  && s.Note.StartsWith("Top RAM"));
        StringAssert.Contains(ram.Note, "chrome ×2");
        StringAssert.Contains(ram.Note, "2.1 GB");

        model.RebuildViews(snap);
        for (int i = 0; i < model.ViewCount; i++)
        {
            model.ViewIndex = i;
            model.UpdateWidget(snap);
            if (model.WidgetDetail.Contains("chrome"))
            {
                return;
            }
        }
        Assert.Fail("no widget page carries the biggest process");
    }

    [TestMethod]
    public void The_words_on_the_tab_follow_the_language()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(new[] { P(1, "a", 1) }));
        Assert.AreEqual("Processes in memory", model.Memory.Text["mem_processes"]);

        model.SetLanguage("th");
        model.PushHistory(Snap(new[] { P(1, "a", 1) }));

        Assert.AreNotEqual("Processes in memory", model.Memory.Text["mem_processes"]);
    }

    [TestMethod]
    public void A_service_row_that_changes_its_name_text_tells_the_view()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Snap(new[] { P(1, "x", 1) },
            new[] { new ServiceEntry { Name = "S", Display = "Old", State = "Stopped" } }));
        ServiceVm row = model.Memory.Services.Single();
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.PushHistory(Snap(new[] { P(1, "x", 1) },
            new[] { new ServiceEntry { Name = "S", Display = "New", State = "Stopped" } }));

        Assert.AreEqual("New", row.Display);
        CollectionAssert.Contains(changed, nameof(ServiceVm.Display));
    }
}
