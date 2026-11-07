using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.App
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly string? _configPath;
        private readonly Func<long, Snapshot?>? _sampler;
        private CancellationTokenSource? _cts;
        private DispatcherTimer? _timer;
        private bool _opened;

        public MainWindow() : this(new MainViewModel(new AppSettings()), null, null) { } // designer only

        public MainWindow(MainViewModel vm, string? configPath, Func<long, Snapshot?>? sampler)
        {
            InitializeComponent();
            _vm = vm;
            _configPath = configPath;
            _sampler = sampler;
            DataContext = _vm;
            ApplyTheme();
            Topmost = _vm.AlwaysOnTop;
            Opacity = _vm.Opacity;
            ApplySizeForRequested();
            ApplyVisibility();
            ApplyPosition();
            _vm.PropertyChanged += OnVmChanged;
            SizeChanged += OnSizeChanged;
            DoubleTapped += OnDoubleTapped;
            KeyDown += OnKeyDown;
            Opened += (_, _) => 
            {
                StartSampling();
                _opened = true;
            };
            Closing += OnClosing;
            PositionChanged += OnPositionChanged;
        }

        private void OnVmChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "RequestedMode":
                    ApplySizeForRequested();
                    ApplyVisibility();
                    ApplyPosition();
                    break;
                case "EffectiveMode":
                    ApplyVisibility();
                    break;
                case "IsDark":
                case "Colors":
                    ApplyTheme();
                    break;
                case "AlwaysOnTop":
                    Topmost = _vm.AlwaysOnTop;
                    break;
                case "Opacity":
                    Opacity = _vm.Opacity;
                    break;
            }
        }

        private void ApplyTheme()
        {
            Background = HexBrush.Parse(_vm.Colors.Panel);
            if (Application.Current != null)
                Application.Current.RequestedThemeVariant = _vm.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        private void ApplyVisibility()
        {
            var m = _vm.EffectiveMode;
            MiniHost.IsVisible = m == ViewMode.Mini;
            ExpandedHost.IsVisible = m == ViewMode.Expanded;
            FullHost.IsVisible = m == ViewMode.Full;
        }

        private void ApplySizeForRequested()
        {
            switch (_vm.RequestedMode)
            {
                case ViewMode.Mini:
                    MinWidth = 200;
                    MinHeight = 64;
                    Width = _vm.WindowWidth;
                    Height = _vm.WindowHeight;
                    break;
                case ViewMode.Expanded:
                    MinWidth = 470;
                    MinHeight = 300;
                    Width = _vm.WindowWidth;
                    Height = _vm.WindowHeight;
                    break;
                case ViewMode.Full:
                    MinWidth = 0;
                    MinHeight = 0;
                    if (_vm.Settings.FullW > 0 && _vm.Settings.FullH > 0)
                    {
                        Width = _vm.WindowWidth;
                        Height = _vm.WindowHeight;
                    }
                    else
                    {
                        double w = 1024, h = 768;
                        var wa = Screens?.Primary?.WorkingArea;
                        var sc = Screens?.Primary?.Scaling ?? 1.0;
                        if (wa.HasValue && sc > 0 && wa.Value.Width > 0 && wa.Value.Height > 0)
                        {
                            w = wa.Value.Width / sc;
                            h = wa.Value.Height / sc;
                        }
                        Width = w;
                        Height = h;
                    }
                    break;
            }
            WindowState = WindowState.Normal;
            _vm.SetWindowSize(Width, Height);
        }

        private void OnSizeChanged(object? s, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
            _vm.SetWindowSize(e.NewSize.Width, e.NewSize.Height);
            switch (_vm.RequestedMode)
            {
                case ViewMode.Mini:
                    _vm.Settings.MiniW = e.NewSize.Width;
                    _vm.Settings.MiniH = e.NewSize.Height;
                    break;
                case ViewMode.Expanded:
                    _vm.Settings.ExpW = e.NewSize.Width;
                    _vm.Settings.ExpH = e.NewSize.Height;
                    break;
                case ViewMode.Full:
                    _vm.Settings.FullW = e.NewSize.Width;
                    _vm.Settings.FullH = e.NewSize.Height;
                    break;
            }
        }

        private void OnDoubleTapped(object? s, TappedEventArgs e)
        {
            if (_vm.EffectiveMode == ViewMode.Full) return;
            for (var o = e.Source as Visual; o != null; o = o.GetVisualParent())
            {
                if (o is Button || o is Avalonia.Controls.Primitives.ToggleButton || o is Slider || o is ComboBox || o is Avalonia.Controls.Primitives.ScrollBar)
                    return;
            }
            _vm.CycleMode();
        }

        private void OnKeyDown(object? s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _vm.EffectiveMode == ViewMode.Full)
            {
                _vm.SetMode(ViewMode.Expanded);
                e.Handled = true;
            }
        }

        private void OnPositionChanged(object? s, PixelPointEventArgs e)
        {
            if (!_opened || _vm.RequestedMode == ViewMode.Full) return;
            _vm.Settings.PosX = e.Point.X;
            _vm.Settings.PosY = e.Point.Y;
        }

        private void StartSampling()
        {
            if (_sampler == null || _cts != null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var sampler = _sampler;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => _vm.Tick(Environment.TickCount64);
            _timer.Start();
            _ = Task.Run(async () =>
            {
                long delayMs = 2500;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        long now = Environment.TickCount64;
                        delayMs = _vm.Settings.IntervalsMillis.Metrics;
                        var snap = sampler(now);
                        if (snap != null)
                        {
                            Dispatcher.UIThread.Post(() => _vm.Apply(snap, now));
                        }
                    }
                    catch (Exception) { }
                    try
                    {
                        await Task.Delay((int)Math.Clamp(delayMs, 250L, 60000L), ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception) { break; }
                }
            });
        }

        private void OnClosing(object? s, WindowClosingEventArgs e)
        {
            _cts?.Cancel();
            _timer?.Stop();
            _vm.PropertyChanged -= OnVmChanged;
            if (_configPath != null)
                SettingsStore.Save(_configPath, _vm.Settings);
        }

        private void ApplyPosition()
        {
            if (_vm.RequestedMode != ViewMode.Full && _vm.Settings.PosX.HasValue && _vm.Settings.PosY.HasValue)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint((int)_vm.Settings.PosX.Value, (int)_vm.Settings.PosY.Value);
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                if (_opened)
                {
                    var screen = Screens?.Primary;
                    if (screen != null && screen.WorkingArea.Width > 0 && screen.WorkingArea.Height > 0)
                    {
                        var wa = screen.WorkingArea;
                        double sc = screen.Scaling > 0 ? screen.Scaling : 1.0;
                        int x = wa.X + (int)Math.Max(0, (wa.Width - Width * sc) / 2);
                        int y = wa.Y + (int)Math.Max(0, (wa.Height - Height * sc) / 2);
                        Position = new PixelPoint(x, y);
                    }
                }
            }
        }
    }
}
