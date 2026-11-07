using Avalonia.Media;
using System;

namespace SysMonitor.Linux.App
{
    public static class HexBrush
    {
        private static readonly SolidColorBrush Gray = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

        public static IBrush Parse(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || !Color.TryParse(hex.Trim(), out var c))
            {
                return Gray;
            }
            return new SolidColorBrush(c);
        }
    }
}
