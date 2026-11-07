using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysMonitor.Linux.Presentation
{
    public sealed partial class MainViewModel : INotifyPropertyChanged
    {
        public bool IsDark { get => string.Equals(Settings.Theme, "dark", StringComparison.Ordinal); set => SetThemeName(value ? "dark" : "light"); }
        public string ThemeName => Settings.Theme;
        public ThemeColors Colors => Theme.Resolve(Settings.Theme);
        public void ToggleTheme() { IsDark = !IsDark; }
        private void SetThemeName(string name) { if (string.Equals(Settings.Theme, name, StringComparison.Ordinal)) return; Settings.Theme = name; Raise("IsDark"); Raise("ThemeName"); Raise("Colors"); }
        public bool AlwaysOnTop { get => Settings.AlwaysOnTop; set { if (Settings.AlwaysOnTop == value) return; Settings.AlwaysOnTop = value; Raise("AlwaysOnTop"); } }
        public double Opacity { get => Settings.Opacity; set { var tmp = new AppSettings { Opacity = value }; tmp.Sanitize(); if (tmp.Opacity == Settings.Opacity) return; Settings.Opacity = tmp.Opacity; Raise("Opacity"); } }
        public string Speed { get => Settings.Speed; set { var tmp = new AppSettings { Speed = value }; tmp.Sanitize(); if (string.Equals(tmp.Speed, Settings.Speed, StringComparison.Ordinal)) return; Settings.Speed = tmp.Speed; Raise("Speed"); Raise("SpeedIndex"); } }
        public int SpeedIndex { get => Settings.Speed == "eco" ? 0 : Settings.Speed == "fast" ? 2 : 1; set { if (value < 0 || value > 2) return; Speed = value == 0 ? "eco" : value == 2 ? "fast" : "balanced"; } }
    }
}
