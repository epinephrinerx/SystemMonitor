using System;
using System.Collections.Generic;
using System.Globalization;

namespace SysMonitor.Linux.Presentation
{
    /// <summary>Rotates through metric items. UI-thread only: not thread-safe.</summary>
    public sealed class MiniRotation
    {
        private readonly long _interval;
        private List<MetricItem> _items = new();
        private int _index;
        private long _last;
        private bool _started;
        private int _version;

        public MiniRotation(long intervalMillis = 5000)
        {
            _interval = intervalMillis <= 0 ? 5000 : intervalMillis;
        }

        public int Count => _items.Count;
        public int Index => _index;
        public MetricItem? Current => _items.Count > 0 ? _items[_index] : null;
        public int Version => _version;

        private void Bump()
        {
            if (_version != int.MaxValue) _version++;
        }

        public void Update(IReadOnlyList<MetricItem>? items, long nowMillis)
        {
            string? oldKey = _items.Count > 0 ? _items[_index].Key : null;
            bool wasEmpty = _items.Count == 0;
            int oldIndex = _index;

            var fresh = new List<MetricItem>();
            if (items != null)
            {
                foreach (var it in items)
                {
                    if (it != null)
                    {
                        fresh.Add(it);
                    }
                }
            }
            _items = fresh;

            bool restart = !_started;
            _started = true;

            if (fresh.Count == 0)
            {
                _index = 0;
                if (!wasEmpty)
                {
                    restart = true;
                }
            }
            else if (oldKey == null)
            {
                _index = 0;
                restart = true; // was empty, now has items
            }
            else
            {
                int found = -1;
                for (int i = 0; i < fresh.Count; i++)
                {
                    if (string.Equals(fresh[i].Key, oldKey, StringComparison.Ordinal))
                    {
                        found = i;
                        break;
                    }
                }
                if (found >= 0)
                {
                    _index = found; // selection follows the key; timer is NOT restarted
                }
                else
                {
                    _index = Math.Min(oldIndex, fresh.Count - 1);
                    restart = true;
                }
            }

            if (restart)
            {
                _last = nowMillis;
            }

            string? afterKey = _items.Count > 0 ? _items[_index].Key : null;
            if (_index != oldIndex || !string.Equals(afterKey, oldKey, StringComparison.Ordinal))
            {
                Bump();
            }
        }

        public void Next(long nowMillis)
        {
            int before = _index;
            if (_items.Count > 1)
            {
                _index = (_index + 1) % _items.Count;
                if (_index != before)
                {
                    Bump();
                }
            }
            _last = nowMillis;
        }

        public void Previous(long nowMillis)
        {
            int before = _index;
            if (_items.Count > 1)
            {
                _index = _index == 0 ? _items.Count - 1 : _index - 1;
                if (_index != before)
                {
                    Bump();
                }
            }
            _last = nowMillis;
        }

        public void Tick(long nowMillis)
        {
            System.Int128 elapsed = (System.Int128)nowMillis - (System.Int128)_last;
            if (elapsed >= (System.Int128)_interval)
            {
                if (_items.Count > 1)
                {
                    _index = (_index + 1) % _items.Count;
                    Bump();
                }
                _last = nowMillis;
            }
        }
    }
}
