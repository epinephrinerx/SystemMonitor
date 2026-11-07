using Avalonia.VisualTree;
using Avalonia;
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Visuals;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App
{
    public partial class MeterCard : UserControl
    {
        private MainViewModel? _vm;

        public MeterCard()
        {
            InitializeComponent();
            BarHost.SizeChanged += (_, _) => Refresh();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Refresh();
        }

        private MainViewModel? FindVm()
        {
            for (Avalonia.Visual? v = this.GetVisualParent(); v != null; v = v.GetVisualParent())
            {
                if (v is Avalonia.StyledElement se && se.DataContext is MainViewModel vm)
                    return vm;
            }
            return null;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _vm = FindVm();
            if (_vm != null)
                _vm.PropertyChanged += OnVmChanged;
            Refresh();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_vm != null)
                _vm.PropertyChanged -= OnVmChanged;
            _vm = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnVmChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Colors")
                Refresh();
        }

        private void Refresh()
        {
            var item = DataContext as MetricItem;
            if (item == null) return;

            double w = BarHost.Bounds.Width;
            BarFill.Width = Math.Max(0, w * Math.Clamp(item.Usage, 0, 100) / 100.0);
            BarFill.Background = HexBrush.Parse(item.ColorHex);
            Accent.Background = HexBrush.Parse(item.AccentHex);
            BarTrack.Background = HexBrush.Parse(_vm?.Colors.BarEmpty ?? "#dfe4ec");
        }
    }
}
