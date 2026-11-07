using System;
using System.Globalization;

namespace SysMonitor.Linux.Presentation
{
    public static class Fmt
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string One(double x) => Math.Round(x, 1, MidpointRounding.AwayFromZero).ToString("0.0", Inv);

        public static string Percent(int v) => Math.Clamp(v, 0, 100).ToString(Inv) + "%";

        public static string Size(double gb)
        {
            if (double.IsNaN(gb) || double.IsInfinity(gb) || gb < 0)
                return "N/A";

            if (gb < 1)
            {
                double mb = Math.Round(gb * 1024.0, MidpointRounding.AwayFromZero) + 0.0;
                if (mb >= 1024)
                    return "1.0 GB";
                return mb.ToString("0", Inv) + " MB";
            }

            if (gb < 1024)
            {
                double g = Math.Round(gb, 1, MidpointRounding.AwayFromZero);
                if (g >= 1024.0)
                    return "1.0 TB";
                return One(gb) + " GB";
            }

            return One(gb / 1024.0) + " TB";
        }

        public static string Rate(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0)
                return "0 KB/s";

            if (v < 1)
            {
                double kb = Math.Round(v * 1024.0, MidpointRounding.AwayFromZero) + 0.0;
                if (kb >= 1024)
                    return "1.0 MB/s";
                return kb.ToString("0", Inv) + " KB/s";
            }

            return One(v) + " MB/s";
        }

        public static string Temp(int? c) => c.HasValue ? c.Value.ToString(Inv) + " C" : "N/A";

        public static string Speed(double mbps)
        {
            if (double.IsNaN(mbps) || double.IsInfinity(mbps) || mbps <= 0)
                return "N/A";

            if (mbps >= 1000)
                return One(mbps / 1000.0) + " Gbps";

            double m = Math.Round(mbps, MidpointRounding.AwayFromZero);
            if (m >= 1000)
                return "1.0 Gbps";

            return m.ToString("0", Inv) + " Mbps";
        }
    }
}
