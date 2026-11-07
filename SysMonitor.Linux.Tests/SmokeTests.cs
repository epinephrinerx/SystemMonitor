namespace SysMonitor.Linux.Tests;

[TestClass]
public class SmokeTests
{
    [TestMethod]
    public void CoreIsReachableFromLinuxProject()
    {
        var history = new SysMonitor.Model.History(2);
        history.Add(3);
        Assert.AreEqual(3, history.Latest);
    }
}
