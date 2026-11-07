using Avalonia;
using Avalonia.Headless;
using SysMonitor.Linux.App;

[assembly: AvaloniaTestApplication(typeof(SysMonitor.Linux.App.Tests.TestAppBuilder))]

namespace SysMonitor.Linux.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Runs code on the Avalonia UI thread of a headless session.</summary>
public static class Ui
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    public static T Run<T>(Func<T> f) => Session.Dispatch(f, CancellationToken.None).GetAwaiter().GetResult();
    public static void Run(Action a) => Session.Dispatch(() => { a(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();
}

[TestClass]
public class HeadlessSmokeTests
{
    [TestMethod]
    public void MainWindow_CanBeCreatedAndRendered()
    {
        int width = Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            var frame = w.CaptureRenderedFrame();
            w.Close();
            return frame?.PixelSize.Width ?? 0;
        });
        Assert.IsTrue(width > 0);
    }
}
