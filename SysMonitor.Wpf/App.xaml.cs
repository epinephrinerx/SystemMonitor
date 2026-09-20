using System.Windows;
using System.Windows.Threading;
using SysMonitor.Native;
using SysMonitor.Sensors;

namespace SysMonitor;

public partial class App : Application
{
    private Sampler? _sampler;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A widget nobody is watching must not die silently: log first, then
        // let the normal crash path run.
        DispatcherUnhandledException += (_, args) =>
            Diag.ReportException("Dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error)
            {
                Diag.ReportException("Unhandled", error);
            }
        };

        AppConfig config = AppConfig.Load();
        Diag.Start(config.Diagnostics);

        _sampler = new Sampler(config);
        _sampler.Start();

        var window = new MainWindow(config, _sampler, e.Args);
        MainWindow = window;
        window.Show();

        Win32.TrimWorkingSet();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sampler?.Dispose();
        Diag.Write("--- exit");
        base.OnExit(e);
    }
}
