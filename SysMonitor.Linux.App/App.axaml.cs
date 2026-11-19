using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App;

public sealed class App : Application
{
    private readonly StartupOptions _options;
    private ShellModel? _shell;
    private Diagnostics? _diagnostics;

    /// <summary>Used by the XAML loader and the designer; the app passes its own.</summary>
    public App()
        : this(StartupOptions.Parse(Array.Empty<string>()))
    {
    }

    public App(StartupOptions options)
    {
        _options = options;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppConfig config = AppConfig.Load();
        ViewMode start = _options.StartView(config);

        // The start view is decided by the switches first, then by last_view.
        // Writing it here means the window's first layout already matches, with
        // no switch event before the window exists.
        config.LastView = start.ToKey();

        _diagnostics = new Diagnostics(enabled: config.Diagnostics);
        _shell = new ShellModel(
            config,
            version: typeof(App).Assembly.GetName().Version?.ToString(3) ?? "4.3.0");

        RequestedThemeVariant = ThemeFor(config.Theme);

        var window = new ShellWindow(_shell, _options, _diagnostics);
        desktop.MainWindow = window;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        _shell.StructureChanged += () => RequestedThemeVariant = ThemeFor(_shell.Config.Theme);

        _diagnostics.Line($"start view={start.ToKey()} lang={config.Lang} theme={config.Theme}");
        base.OnFrameworkInitializationCompleted();
    }

    public static ThemeVariant ThemeFor(string? theme) =>
        string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
}
