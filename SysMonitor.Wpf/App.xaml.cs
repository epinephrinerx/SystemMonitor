using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using SysMonitor.Extensions;
using SysMonitor.Native;
using SysMonitor.Sensors;

namespace SysMonitor;

public partial class App : Application
{
    /// <summary>Long enough for a Restart() hand-over, short enough to notice.</summary>
    private static readonly TimeSpan HandOverWait = TimeSpan.FromSeconds(5);

    private Sampler? _sampler;
    private SysMonitor.Extensions.ExtensionHost? _extensions;
    private SingleInstance? _instance;
    private bool _guarded = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The elevated copy the memory tab starts through UAC: one job, then
        // gone. It must not take the one-copy guard or open a window.
        if (e.Args.Contains(MemoryTools.ElevatedSwitch))
        {
            Shutdown(MemoryTools.RunElevated(e.Args));
            return;
        }

        // --allow-multiple is for checking a build next to the copy that is
        // already running; nobody launches it by accident.
        _guarded = !e.Args.Contains("--allow-multiple");
        _instance = _guarded
            ? SingleInstance.TryAcquire(SingleInstance.DefaultName, HandOverWait)
            : null;
        if (_guarded && _instance is null)
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

        Assembly appAssembly = typeof(App).Assembly;
        var extensionOptions = new ExtensionLoaderOptions
        {
            ExtensionsRoot = Path.Combine(AppContext.BaseDirectory, "extensions"),
            HostVersion = appAssembly.GetName().Version ?? new Version(1, 0, 0),
            HostIsElevated = Win32.IsElevated(),
            Language = config.Lang,
            FirstParty = FirstPartyAllowlist.FromAssembly(appAssembly, Diag.Write),
            Approvals = new ApprovalStore(ApprovalStore.DefaultPath, log: Diag.Write),
            Log = Diag.Write,
        };

        _extensions = new ExtensionHost(config, extensionOptions);
        _ = _extensions.StartAsync();

        _sampler = new Sampler(config, _extensions);
        _sampler.Start();

        var window = new MainWindow(config, _sampler, _extensions, e.Args);
        MainWindow = window;
        window.Show();

        Win32.TrimWorkingSet();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _sampler?.Dispose();
        }
        catch (Exception error)
        {
            Diag.ReportException("SamplerDispose", error);
        }

        try
        {
            if (_extensions is not null)
            {
                var disposeTask = Task.Run(() => _extensions.Dispose());
                if (!disposeTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    Diag.Write("Extension host dispose timed out on exit.");
                }
            }
        }
        catch (Exception error)
        {
            Diag.ReportException("ExtensionHostDispose", error);
        }

        if (_instance is not null || !_guarded)
        {
            Diag.Write("--- exit");
            _instance?.Dispose();
        }
        base.OnExit(e);
    }
}
