using System;

namespace SysMonitor.Linux.Presentation
{
    public sealed class WidgetNavigator
    {
        public int Count { get; private set; }
        public int Index { get; private set; }
        public int Version { get; private set; }

        public WidgetNavigator(int count = 0)
        {
            Count = Math.Max(count, 0);
            Index = 0;
            Version = 0;
        }

        public void Next()
        {
            if (Count <= 1)
                return;

            Index = Index + 1 >= Count ? 0 : Index + 1;
            Version = Version == int.MaxValue ? int.MaxValue : Version + 1;
        }

        public void Previous()
        {
            if (Count <= 1)
                return;

            Index = Index == 0 ? Count - 1 : Index - 1;
            Version = Version == int.MaxValue ? int.MaxValue : Version + 1;
        }

        public void SetCount(int count)
        {
            count = Math.Max(count, 0);

            if (count == Count)
                return;

            Count = count;
            Index = count == 0 ? 0 : Math.Min(Index, count - 1);
            Version = Version == int.MaxValue ? int.MaxValue : Version + 1;
        }
    }
}
