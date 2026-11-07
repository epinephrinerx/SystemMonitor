using Avalonia;
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.Visuals;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.App
{
    public partial class FullView : UserControl
    {
        private MainViewModel? _vm;

        public FullView()
        {
            InitializeComponent();
        }

        private void BackButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.SetMode(ViewMode.Expanded);
            }
        }

        private System.Collections.Generic.List<ChartSpec> _built = new();
        private string _builtColors = "";

        private void RebuildCharts()
        {
            if (_vm == null) return;
            var specs = _vm.SelectedCharts;
            string colors = _vm.Colors.BarEmpty + "|" + _vm.Colors.Border;
            bool same = colors == _builtColors && specs.Count == _built.Count;
            if (same)
            {
                for (int i = 0; i < specs.Count; i++)
                {
                    if (!specs[i].Equals(_built[i])) { same = false; break; }
                }
            }
            if (same)
            {
                // same chart set: keep the controls, just refresh the series and the revision
                int k = 0;
                foreach (var child in ChartsHost.Children)
                {
                    if (child is HistoryChart hc)
                    {
                        hc.Series = _vm.History.Get(specs[k].Key);
                        hc.Revision = _vm.HistoryVersion;
                        k++;
                    }
                }
                return;
            }
            ChartsHost.Children.Clear();
            foreach (var spec in specs)
            {
                ChartsHost.Children.Add(new TextBlock { Text = spec.Title, FontSize = 12 });
                ChartsHost.Children.Add(new HistoryChart
                {
                    Height = 120,
                    Series = _vm.History.Get(spec.Key),
                    LineColor = spec.ColorHex,
                    FixedMax = spec.FixedMax,
                    BackgroundColor = _vm.Colors.BarEmpty,
                    GridColor = _vm.Colors.Border,
                    Revision = _vm.HistoryVersion
                });
            }
            _built = new System.Collections.Generic.List<ChartSpec>(specs);
            _builtColors = colors;
        }

        private void ApplyColors()
        {
            if (_vm != null)
            {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(_vm.Colors.Panel));
                Resources["GroupBorder"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(_vm.Colors.Border));
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_vm != null)
            {
                _vm.PropertyChanged -= Vm_PropertyChanged;
            }
            _vm = DataContext as MainViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += Vm_PropertyChanged;
                RebuildCharts();
                ApplyColors();
            }
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                switch (e.PropertyName)
                {
                    case nameof(_vm.SelectedCharts):
                        RebuildCharts();
                        break;
                    case nameof(_vm.HistoryVersion):
                        foreach (var child in ChartsHost.Children)
                        {
                            if (child is HistoryChart hc)
                            {
                                hc.Revision = _vm.HistoryVersion;
                            }
                        }
                        break;
                    case nameof(_vm.Colors):
                        ApplyColors();
                        RebuildCharts();
                        break;
                }
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= Vm_PropertyChanged;
            }
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_vm != null)
            {
                _vm.PropertyChanged -= Vm_PropertyChanged;
                _vm.PropertyChanged += Vm_PropertyChanged;
            }
        }
    }
}
