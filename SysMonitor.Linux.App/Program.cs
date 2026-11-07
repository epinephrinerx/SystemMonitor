using Avalonia;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux;
using SysMonitor.Model;
using SysMonitor.Linux.Sampling;
using System;

namespace SysMonitor.Linux.App
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var path = ConfigPaths.ConfigFile(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), Environment.GetEnvironmentVariable("HOME"));
            var settings = SettingsStore.Load(path);
            var vm = new MainViewModel(settings);
            ViewMode? mode = ParseMode(args);
            if (mode.HasValue)
            {
                vm.SetMode(mode.Value);
            }
            var sampler = new LinuxSampler("/proc", "/sys");
            App.WindowFactory = () => new MainWindow(vm, path, now => sampler.Sample(now));
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex) when (ex.Message.Contains("XOpenDisplay", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("sysmonitor: no graphical display found. Start it from an X11 or Wayland desktop session (DISPLAY or WAYLAND_DISPLAY must be set).");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                Environment.Exit(2);
            }
        }

        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

        internal static ViewMode? ParseMode(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    switch (args[i + 1].Trim().ToLowerInvariant())
                    {
                        case "mini":
                            return ViewMode.Mini;
                        case "expanded":
                            return ViewMode.Expanded;
                        case "full":
                            return ViewMode.Full;
                    }
                }
            }
            return null;
        }
    }
}
