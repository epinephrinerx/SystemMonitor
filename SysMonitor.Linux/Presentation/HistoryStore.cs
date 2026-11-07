using System;
using System.Collections.Generic;
using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation
{
    /// <summary>
    /// UI-thread only: not thread-safe.
    /// </summary>
    public sealed class HistoryStore
    {
        private readonly int _capacity;
        private readonly List<string> _order = new();
        private readonly Dictionary<string, Series> _series = new(StringComparer.Ordinal);

        private sealed class Series
        {
            public History History;
            public int Missed;

            public Series(History history)
            {
                History = history;
                Missed = 0;
            }
        }

        public HistoryStore(int capacity = 120)
        {
            _capacity = Math.Min(Math.Max(capacity, 2), 10000);
        }

        public void Record(Snapshot? s)
        {
            if (s == null)
                return;

            var points = new Dictionary<string, double>(StringComparer.Ordinal);
            var pointOrder = new List<string>();

            void Add(string key, double v)
            {
                if (points.TryAdd(key, v))
                {
                    pointOrder.Add(key);
                }
            }

            Add("cpu", s.CpuTotal);
            Add("ram", s.Ram?.Usage ?? 0);

            if (s.Cores != null)
            {
                for (int i = 0; i < s.Cores.Count; i++)
                {
                    var c = s.Cores[i];
                    if (c != null)
                    {
                        Add("core:" + i.ToString(CultureInfo.InvariantCulture), c.Usage);
                    }
                }
            }

            foreach (var (letter, d) in MetricBuilder.OrderedDisks(s))
            {
                string key = "disk:" + letter;
                Add(key, Math.Clamp(d.Usage, 0, 100));
                Add(key + ":read", d.ReadMb);
                Add(key + ":write", d.WriteMb);
            }

            foreach (var (key, a) in MetricBuilder.IncludedAdapters(s))
            {
                Add(key + ":down", a.DownMb);
                Add(key + ":up", a.UpMb);
            }

            foreach (var key in pointOrder)
            {
                if (!_series.TryGetValue(key, out var series))
                {
                    series = new Series(new History(_capacity));
                    _series[key] = series;
                    _order.Add(key);
                }
                series.History.Add(points[key]);
                series.Missed = 0;
            }

            var kept = new List<string>(_order.Count);
            foreach (var key in _order)
            {
                if (points.ContainsKey(key))
                {
                    kept.Add(key);
                    continue;
                }
                var ser = _series[key];
                ser.Missed++;
                if (ser.Missed >= _capacity)
                {
                    _series.Remove(key);
                }
                else
                {
                    kept.Add(key);
                }
            }
            _order.Clear();
            _order.AddRange(kept);
        }

        public History? Get(string? key)
        {
            if (key == null)
                return null;

            _series.TryGetValue(key, out var series);
            return series?.History;
        }

        public IReadOnlyCollection<string> Keys => _order.ToArray();

        public void Clear()
        {
            _series.Clear();
            _order.Clear();
        }
    }
}
