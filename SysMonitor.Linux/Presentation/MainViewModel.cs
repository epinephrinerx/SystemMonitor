using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation
{
    /// <summary>
    /// UI-thread only: not thread-safe.
    /// </summary>
    public sealed partial class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly MiniRotation _rotation = new MiniRotation();
        private ViewMode _requested;
        private double _w;
        private double _h;
        private string _selectedKey = "";
        private MetricItem? _lastCurrent;
        private bool _updating;
        private bool _applied;

        public AppSettings Settings { get; }
        public ObservableCollection<MetricItem> Items { get; } = new();
        public ObservableCollection<TabModel> Tabs { get; } = new();
        public HistoryStore History { get; } = new HistoryStore(120);
        public Snapshot? Snapshot { get; private set; }
        public int HistoryVersion { get; private set; }
        public ViewMode RequestedMode => _requested;
        public double WindowWidth => _w;
        public double WindowHeight => _h;
        public ViewMode EffectiveMode => ViewModes.Effective(_requested, _w, _h);
        public MetricItem? Current => _rotation.Current;
        public IReadOnlyList<ChartSpec> SelectedCharts => TabCharts.For(_selectedKey, History.Keys);

        public MainViewModel(AppSettings? settings)
        {
            Settings = settings ?? new AppSettings();
            Settings.Sanitize();
            _requested = Settings.ModeValue;
            var size = SizeFor(_requested);
            _w = size.W;
            _h = size.H;
            _selectedKey = Settings.FullTab ?? "";
            UpdateCollection(Tabs, FullTabs.Build(null), t => t.Key, TabEquals);
            if (!(Settings.FullTab ?? "").StartsWith("disk:", StringComparison.Ordinal))
            {
                FixSelection();
            }
        }

        private (double W, double H) SizeFor(ViewMode m)
        {
            return m switch
            {
                ViewMode.Mini => (Settings.MiniW, Settings.MiniH),
                ViewMode.Expanded => (Settings.ExpW, Settings.ExpH),
                ViewMode.Full => (Settings.FullW > 0 && Settings.FullH > 0) ? (Math.Max(Settings.FullW, 640.0), Math.Max(Settings.FullH, 480.0)) : (1024.0, 768.0),
                _ => (1024.0, 768.0)
            };
        }

        private static string ModeName(ViewMode m)
        {
            return m switch
            {
                ViewMode.Expanded => "expanded",
                ViewMode.Full => "full",
                _ => "mini"
            };
        }

        public void SetWindowSize(double w, double h)
        {
            if (double.IsNaN(w) || double.IsInfinity(w) || w <= 0 || double.IsNaN(h) || double.IsInfinity(h) || h <= 0)
                return;

            var before = EffectiveMode;
            bool wc = w != _w;
            bool hc = h != _h;
            _w = w;
            _h = h;
            if (wc) Raise("WindowWidth");
            if (hc) Raise("WindowHeight");
            if (EffectiveMode != before) Raise("EffectiveMode");
        }

        public void SetMode(ViewMode m)
        {
            bool reenter = (m == _requested) && (EffectiveMode != m);
            if (m == _requested && !reenter) return;
            Settings.Mode = ModeName(m);
            var before = EffectiveMode;
            _requested = m;
            var size = SizeFor(m);
            bool wc = size.W != _w;
            bool hc = size.H != _h;
            _w = size.W;
            _h = size.H;
            Raise("RequestedMode");
            if (EffectiveMode != before) Raise("EffectiveMode");
            if (wc) Raise("WindowWidth");
            if (hc) Raise("WindowHeight");
        }

        public void CycleMode()
        {
            SetMode(ViewModes.Next(EffectiveMode));
        }

        public void Next(long now)
        {
            _rotation.Next(now);
            CheckCurrent();
        }

        public void Previous(long now)
        {
            _rotation.Previous(now);
            CheckCurrent();
        }

        public void Tick(long now)
        {
            _rotation.Tick(now);
            CheckCurrent();
        }

        private void CheckCurrent()
        {
            var cur = _rotation.Current;
            bool changed = !Equals(cur, _lastCurrent);
            _lastCurrent = cur;
            if (changed) Raise("Current");
        }

        public string SelectedTabKey
        {
            get => _selectedKey;
            set
            {
                if (SetKey(value))
                {
                    Raise("SelectedTabKey");
                    Raise("SelectedTab");
                    Raise("SelectedCharts");
                }
            }
        }

        private bool SetKey(string? v)
        {
            v ??= "";
            if (string.Equals(v, _selectedKey, StringComparison.Ordinal)) return false;
            _selectedKey = v;
            Settings.FullTab = v;
            return true;
        }

        public TabModel? SelectedTab
        {
            get
            {
                foreach (var t in Tabs)
                {
                    if (string.Equals(t.Key, _selectedKey, StringComparison.Ordinal)) return t;
                }
                return null;
            }
            set
            {
                if (value == null || _updating || !_applied) return;
                SelectedTabKey = value.Key;
            }
        }

        private bool FixSelection()
        {
            if (Tabs.Count == 0) return false;
            if (SelectedTab != null) return false;
            return SetKey(Tabs[0].Key);
        }

        public void Apply(Snapshot? s, long nowMillis)
        {
            if (s == null) return;

            Snapshot = s;
            History.Record(s);
            HistoryVersion = HistoryVersion == int.MaxValue ? int.MaxValue : HistoryVersion + 1;

            var items = new List<MetricItem>();
            foreach (var it in MetricBuilder.Build(s))
            {
                bool show = it.Kind switch
                {
                    MetricKind.Cpu => Settings.ShowCpu,
                    MetricKind.Ram => Settings.ShowRam,
                    MetricKind.Disk => Settings.ShowDisk,
                    MetricKind.Network => Settings.ShowNetwork,
                    _ => true
                };

                if (show) items.Add(it);
            }

            UpdateCollection(Items, items, i => i.Key, (a, b) => a.Equals(b));
            _rotation.Update(items, nowMillis);
            _updating = true;
            try
            {
                UpdateCollection(Tabs, FullTabs.Build(s), t => t.Key, TabEquals);
            }
            finally
            {
                _updating = false;
            }

            bool keyChanged = FixSelection();
            var cur = _rotation.Current;
            bool curChanged = !Equals(cur, _lastCurrent);
            _lastCurrent = cur;

            Raise("Snapshot");
            Raise("HistoryVersion");
            if (curChanged) Raise("Current");
            if (keyChanged) Raise("SelectedTabKey");
            Raise("SelectedTab");
            Raise("SelectedCharts");
            _applied = true;
        }

        private static bool TabEquals(TabModel a, TabModel b)
        {
            return string.Equals(a.Key, b.Key, StringComparison.Ordinal) &&
                   string.Equals(a.Title, b.Title, StringComparison.Ordinal) &&
                   a.Lines.Count == b.Lines.Count &&
                   a.Lines.SequenceEqual(b.Lines, StringComparer.Ordinal);
        }

        private static void UpdateCollection<T>(ObservableCollection<T> col, IReadOnlyList<T> fresh, Func<T, string> key, Func<T, T, bool> equal)
        {
            bool same = col.Count == fresh.Count;
            if (same)
            {
                for (int i = 0; i < col.Count; i++)
                {
                    if (!string.Equals(key(col[i]), key(fresh[i]), StringComparison.Ordinal))
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (same)
            {
                for (int i = 0; i < fresh.Count; i++)
                {
                    if (!equal(col[i], fresh[i])) col[i] = fresh[i];
                }
            }
            else
            {
                col.Clear();
                foreach (var x in fresh) col.Add(x);
            }
        }
    }
}
