using SysMonitor.Setup;

namespace SysMonitor.Tests;

/// <summary>
/// R-022: the setup's command line. The elevated copy that a UAC prompt
/// starts must receive exactly what the first copy was given.
/// </summary>
[TestClass]
public class SetupArgumentsTests
{
    private static SetupArguments Parse(params string[] args) => SetupArguments.Parse(args);

    [TestMethod]
    public void No_arguments_means_a_per_user_wizard_with_nothing_chosen()
    {
        SetupArguments none = Parse();

        Assert.IsFalse(none.Silent);
        Assert.IsFalse(none.Admin, "a machine-wide install is never the default");
        Assert.IsNull(none.Desktop);
        Assert.IsNull(none.StartMenu);
        Assert.IsNull(none.Autostart);
        Assert.IsFalse(none.Run);
        Assert.IsNull(none.FinishPid);
    }

    [TestMethod]
    public void Both_spellings_of_the_machine_wide_switch_work_and_case_does_not_matter()
    {
        Assert.IsTrue(Parse("/admin").Admin);
        Assert.IsTrue(Parse("--ADMIN").Admin);
        Assert.IsTrue(Parse("/s").Silent);
    }

    [TestMethod]
    public void A_pair_of_switches_is_unset_on_or_off_and_the_last_one_wins()
    {
        Assert.IsNull(Parse().Autostart);
        Assert.AreEqual(true, Parse("--autostart").Autostart);
        Assert.AreEqual(false, Parse("--no-autostart").Autostart);
        Assert.AreEqual(false, Parse("--autostart", "--no-autostart").Autostart);
        Assert.AreEqual(true, Parse("--no-desktop", "--desktop").Desktop);
    }

    [TestMethod]
    public void The_finish_pid_is_read_only_when_it_is_a_number()
    {
        Assert.AreEqual(1234, Parse("--finish", "1234", "--admin").FinishPid);
        Assert.IsNull(Parse("--finish", "abc").FinishPid);
        Assert.IsNull(Parse("--finish").FinishPid, "a switch with nothing after it is ignored");
    }

    [TestMethod]
    public void What_the_elevated_copy_is_handed_parses_back_to_the_same_choices()
    {
        var original = new SetupArguments(
            Silent: false, Admin: true, Desktop: true, StartMenu: false, Autostart: true,
            Launch: true, Run: true, RemoveSettings: false, FinishPid: null);

        SetupArguments again = SetupArguments.Parse(original.ToArguments().ToArray());

        Assert.AreEqual(original, again);
    }

    [TestMethod]
    public void Unset_choices_stay_unset_through_the_hand_over()
    {
        SetupArguments silent = Parse("/S", "--admin");

        SetupArguments again = SetupArguments.Parse(silent.ToArguments().ToArray());

        Assert.AreEqual(silent, again);
        Assert.IsNull(again.Autostart, "an unattended run still follows the choice already on the machine");
    }

    [TestMethod]
    public void Unknown_arguments_are_ignored_not_executed()
    {
        SetupArguments parsed = Parse("--run-this", "calc.exe", "--admin");

        Assert.IsTrue(parsed.Admin);
        CollectionAssert.DoesNotContain(parsed.ToArguments().ToArray(), "calc.exe");
    }
}
