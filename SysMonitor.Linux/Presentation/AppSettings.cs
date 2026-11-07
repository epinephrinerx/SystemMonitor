using System;
using System.Text;

namespace SysMonitor.Linux.Presentation
{
    public sealed partial class AppSettings
    {
        public string Theme { get; set; } = "light";
        public string Speed { get; set; } = "balanced";
        public string Mode { get; set; } = "mini";
        public bool AlwaysOnTop { get; set; } = true;
        public bool ShowCpu { get; set; } = true;
        public bool ShowRam { get; set; } = true;
        public bool ShowDisk { get; set; } = true;
        public bool ShowNetwork { get; set; } = true;
        public double Opacity { get; set; } = 0.92;
        public double FontScale { get; set; } = 1.0;
        public double MiniW { get; set; } = 270;
        public double MiniH { get; set; } = 104;
        public double ExpW { get; set; } = 630;
        public double ExpH { get; set; } = 480;
        public double FullW { get; set; } = 0;
        public double FullH { get; set; } = 0;
        public double? PosX { get; set; } = null;
        public double? PosY { get; set; } = null;
        public string FullTab { get; set; } = "";

        public void Sanitize()
        {
            Theme = Pick(Theme, "light", "light", "dark");
            Speed = Pick(Speed, "balanced", "eco", "balanced", "fast");
            Mode = Pick(Mode, "mini", "mini", "expanded", "full");
            Opacity = Range(Opacity, 0.92, 0.3, 1.0);
            FontScale = Range(FontScale, 1.0, 0.8, 1.6);
            MiniW = Size(MiniW, 270);
            MiniH = Size(MiniH, 104);
            ExpW = Size(ExpW, 630);
            ExpH = Size(ExpH, 480);
            FullW = FullSize(FullW);
            FullH = FullSize(FullH);
            PosX = Pos(PosX);
            PosY = Pos(PosY);
            FullTab = CleanTab(FullTab);
        }

        private static string Pick(string? v, string def, params string[] allowed)
        {
            if (v == null) return def;
            string n = v.Trim().ToLowerInvariant();
            foreach (var a in allowed)
            {
                if (string.Equals(n, a, StringComparison.Ordinal)) return n;
            }
            return def;
        }

        private static double Range(double v, double def, double lo, double hi)
        {
            if (double.IsNaN(v)) return def;
            return Math.Clamp(v, lo, hi);
        }

        private static double Size(double v, double def)
        {
            if (double.IsNaN(v) || v <= 0) return def;
            return Math.Clamp(v, 100.0, 10000.0);
        }

        private static double FullSize(double v)
        {
            if (double.IsNaN(v) || v <= 0) return 0;
            return Math.Clamp(v, 100.0, 20000.0);
        }

        private static double? Pos(double? v)
        {
            if (!v.HasValue) return null;
            double d = v.Value;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < -100000 || d > 100000) return null;
            return d;
        }

        private static string CleanTab(string? v)
        {
            if (v == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if (char.IsControl(c))
                {
                    sb.Append(' ');
                }
                else if (char.IsHighSurrogate(c) && i + 1 < v.Length && char.IsLowSurrogate(v[i + 1]))
                {
                    sb.Append(c);
                    sb.Append(v[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }
            string s = sb.ToString();
            if (s.Length > 200)
            {
                int cut = 200;
                if (char.IsHighSurrogate(s[cut - 1]) && char.IsLowSurrogate(s[cut]))
                {
                    cut = 199;
                }
                s = s.Substring(0, cut);
            }
            return s.Trim();
        }

        public bool ValueEquals(AppSettings? other)
        {
            if (other == null) return false;
            return
                string.Equals(Theme, other.Theme, StringComparison.Ordinal) &&
                string.Equals(Speed, other.Speed, StringComparison.Ordinal) &&
                string.Equals(Mode, other.Mode, StringComparison.Ordinal) &&
                AlwaysOnTop == other.AlwaysOnTop &&
                ShowCpu == other.ShowCpu &&
                ShowRam == other.ShowRam &&
                ShowDisk == other.ShowDisk &&
                ShowNetwork == other.ShowNetwork &&
                Opacity == other.Opacity &&
                FontScale == other.FontScale &&
                MiniW == other.MiniW &&
                MiniH == other.MiniH &&
                ExpW == other.ExpW &&
                ExpH == other.ExpH &&
                FullW == other.FullW &&
                FullH == other.FullH &&
                PosX == other.PosX &&
                PosY == other.PosY &&
                string.Equals(FullTab, other.FullTab, StringComparison.Ordinal);
        }

        public (long Metrics, long Space, long Temps) IntervalsMillis => (Speed ?? "") switch
        {
            "eco" => (5000L, 30000L, 60000L),
            "fast" => (1000L, 10000L, 30000L),
            _ => (2500L, 15000L, 30000L)
        };

        public ViewMode ModeValue => (Mode ?? "") switch
        {
            "expanded" => ViewMode.Expanded,
            "full" => ViewMode.Full,
            _ => ViewMode.Mini
        };
    }
}
