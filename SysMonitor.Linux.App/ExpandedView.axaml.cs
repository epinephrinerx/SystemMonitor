using Avalonia;
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Visuals;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App
{
    public partial class ExpandedView : UserControl
    {
        private MainViewModel? _vm;

        public ExpandedView()
        {
            InitializeComponent();
            FullDataButton.Click += (_, _) => _vm?.SetMode(ViewMode.Full);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _vm = DataContext as MainViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += OnViewModelPropertyChanged;
                ApplyColors(_vm.Colors.Panel, _vm.Colors.Sidebar);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_vm != null && e.PropertyName == nameof(_vm.Colors))
            {
                ApplyColors(_vm.Colors.Panel, _vm.Colors.Sidebar);
            }
        }

        private void ApplyColors(string panelHex, string sidebarHex)
        {
            Root.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(panelHex));
            Sidebar.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(sidebarHex));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
                _vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        }
    }
}
