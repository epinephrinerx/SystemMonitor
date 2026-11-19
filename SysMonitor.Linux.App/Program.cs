using Avalonia;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        StartupOptions options = StartupOptions.Parse(args);

        // --probe-ui builds the view models from two real samples and prints
        // them. It never opens a window and needs no display.
        if (options.ProbeUi)
        {
            return Probe.Run();
        }

        SingleInstanceLock? guard = null;
        if (!options.AllowMultiple)
        {
            guard = SingleInstanceLock.Acquire(TimeSpan.FromSeconds(5));
            if (!guard.Acquired)
            {
                // Another copy owns the widget. Exit silently; the spec has no
                // activation signal.
                guard.Dispose();
                return 0;
            }
        }

        try
        {
            BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("sysmonitor: " + exception);
            new Diagnostics().Line("startup failed: " + exception);
            return 1;
        }
        finally
        {
            guard?.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp(StartupOptions options) =>
        AppBuilder.Configure(() => new App(options))
            .UsePlatformDetect()
            .LogToTrace();
}
