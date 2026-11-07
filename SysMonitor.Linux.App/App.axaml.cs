using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SysMonitor.Linux.App
{
    public sealed class App : Application
    {
        public static Func<MainWindow>? WindowFactory { get; set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && WindowFactory != null)
            {
                desktop.MainWindow = WindowFactory();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
