using System;
using System.Globalization;

namespace SysMonitor.Linux.Presentation
{
    public enum LoadLevel
    {
        Normal,
        Warm,
        Hot
    }

    public static class LoadColor
    {
        private static bool IsValidHex(string? hex)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#')
            {
                return false;
            }
            for (int i = 1; i < hex.Length; i++)
            {
                if (!Uri.IsHexDigit(hex[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public static string Hex(double value, string? accentHex)
        {
            string accent = IsValidHex(accentHex) ? accentHex! : "#3b82f6";
            if (double.IsInfinity(value) && value > 0)
            {
                return "#ef4444";
            }
            if (value >= 90)
            {
                return "#ef4444";
            }
            if (value >= 70)
            {
                return "#eab308";
            }
            return accent;
        }

        public static LoadLevel Level(double value)
        {
            if (double.IsNaN(value))
            {
                return LoadLevel.Normal;
            }
            if (value >= 90)
            {
                return LoadLevel.Hot;
            }
            if (value >= 70)
            {
                return LoadLevel.Warm;
            }
            return LoadLevel.Normal;
        }
    }
}
