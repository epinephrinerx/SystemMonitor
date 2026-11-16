using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SysMonitor.Tests;

/// <summary>
/// What the window's drag and resize treat as "on a control" (the crash in the
/// full view's RAM list: the pointer over a Run inside a TextBlock).
/// </summary>
[TestClass]
public class HitSourceTests
{
    /// <summary>WPF objects belong to the thread that made them.</summary>
    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new AssertFailedException("threw " + failure.GetType().Name + ": " + failure.Message, failure);
        }
        return result;
    }

    [TestMethod]
    public void A_run_inside_a_text_block_does_not_throw_and_is_not_a_control()
    {
        bool over = OnSta(() =>
        {
            var run = new Run("firefox");
            var text = new TextBlock();
            text.Inlines.Add(run);
            return HitSource.IsControl(run);
        });

        Assert.IsFalse(over);
    }

    [TestMethod]
    public void A_run_inside_a_button_counts_as_the_button()
    {
        bool over = OnSta(() =>
        {
            var run = new Run("stop");
            var text = new TextBlock();
            text.Inlines.Add(run);
            var button = new Button { Content = text };
            return HitSource.IsControl(run);
        });

        Assert.IsTrue(over);
    }

    [TestMethod]
    public void A_plain_element_a_button_and_nothing_are_told_apart()
    {
        (bool plain, bool button, bool none) = OnSta(() =>
            (HitSource.IsControl(new Border()), HitSource.IsControl(new Button()), HitSource.IsControl(null)));

        Assert.IsFalse(plain);
        Assert.IsTrue(button);
        Assert.IsFalse(none);
    }
}
