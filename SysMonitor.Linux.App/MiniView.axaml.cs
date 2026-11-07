using Avalonia;
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Visuals;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App
{
    public partial class MiniView : UserControl
    {
        private MainViewModel? _vm;

        public MiniView()
        {
            InitializeComponent();
            PrevButton.Click += (_, _) => _vm?.Previous(Environment.TickCount64);
            NextButton.Click += (_, _) => _vm?.Next(Environment.TickCount64);
        }

        private void ShowCurrent(MainViewModel vm)
        {
            var cur = vm.Current;
            Card.DataContext = cur;
            Card.IsVisible = cur != null;
            EmptyText.IsVisible = cur == null;
        }

        public void ApplyColors(string panelHex)
        {
            Root.Background = HexBrush.Parse(panelHex);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmChanged;
            }
            _vm = DataContext as MainViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += OnVmChanged;
                ApplyColors(_vm.Colors.Panel);
                ShowCurrent(_vm);
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmChanged;
            }
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmChanged;
                _vm.PropertyChanged += OnVmChanged;
            }
        }

        private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                if (e.PropertyName == "Colors")
                {
                    ApplyColors(_vm.Colors.Panel);
                }
                else if (e.PropertyName == "Current")
                {
                    ShowCurrent(_vm);
                }
            }
        }
    }
}
