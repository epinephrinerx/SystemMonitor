using System.Windows;
using System.Windows.Threading;
using SysMonitor.Native;
using SysMonitor.Sensors;

namespace SysMonitor;

public partial class App : Application
{
    /// <summary>Long enough for a Restart() hand-over, short enough to notice.</summary>
    private static readonly TimeSpan HandOverWait = TimeSpan.FromSeconds(5);

    private Sampler? _sampler;
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = SingleInstance.TryAcquire(SingleInstance.DefaultName, HandOverWait);
        if (_instance is null)
        {
            // Already running: this copy was started twice. Leave the first one alone.
            Shutdown();
            return;
        }

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
        if (_instance is not null)
        {
            Diag.Write("--- exit");
            _instance.Dispose();
        }
        base.OnExit(e);
    }
}
