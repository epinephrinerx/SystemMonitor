using System;
using System.Collections.Generic;

namespace SysMonitor.Linux.Presentation
{
    public static class CardLayout
    {
        public static IReadOnlyList<(int Row, int Column)> Place(int itemCount, int columns = 2)
        {
            if (itemCount <= 0)
                return Array.Empty<(int Row, int Column)>();

            itemCount = Math.Min(itemCount, 100000);
            columns = Math.Max(columns, 1);
            var result = new List<(int Row, int Column)>(itemCount);

            for (int i = 0; i < itemCount; i++)
            {
                result.Add((i / columns, i % columns));
            }

            return result;
        }

        public static int RowCount(int itemCount, int columns = 2)
        {
            if (itemCount <= 0)
                return 0;

            itemCount = Math.Min(itemCount, 100000);
            columns = Math.Max(columns, 1);

            return (int)(((long)itemCount + columns - 1) / columns);
        }
    }
}
