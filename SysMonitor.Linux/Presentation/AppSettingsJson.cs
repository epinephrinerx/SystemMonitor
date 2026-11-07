using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SysMonitor.Linux.Presentation
{
    public sealed partial class AppSettings
    {
        private static bool Str(JsonElement e, out string v)
        {
            v = e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
            return e.ValueKind == JsonValueKind.String;
        }

        private static bool Bool(JsonElement e, out bool v)
        {
            v = e.ValueKind == JsonValueKind.True;
            return e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False;
        }

        private static bool Num(JsonElement e, out double v)
        {
            v = 0;
            return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out v) && !double.IsInfinity(v);
        }

        private static double F(double v) => double.IsFinite(v) ? v : 0;

        public static AppSettings Parse(string? json)
        {
            var s = new AppSettings();
            if (string.IsNullOrWhiteSpace(json))
                return s;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return s;

                foreach (var p in root.EnumerateObject())
                {
                    switch (p.Name)
                    {
                        case "theme":
                            if (Str(p.Value, out var theme))
                                s.Theme = theme;
                            break;
                        case "speed":
                            if (Str(p.Value, out var speed))
                                s.Speed = speed;
                            break;
                        case "mode":
                            if (Str(p.Value, out var mode))
                                s.Mode = mode;
                            break;
                        case "always_on_top":
                            if (Bool(p.Value, out var alwaysOnTop))
                                s.AlwaysOnTop = alwaysOnTop;
                            break;
                        case "show_cpu":
                            if (Bool(p.Value, out var showCpu))
                                s.ShowCpu = showCpu;
                            break;
                        case "show_ram":
                            if (Bool(p.Value, out var showRam))
                                s.ShowRam = showRam;
                            break;
                        case "show_disk":
                            if (Bool(p.Value, out var showDisk))
                                s.ShowDisk = showDisk;
                            break;
                        case "show_network":
                            if (Bool(p.Value, out var showNetwork))
                                s.ShowNetwork = showNetwork;
                            break;
                        case "opacity":
                            if (Num(p.Value, out var opacity))
                                s.Opacity = opacity;
                            break;
                        case "font_scale":
                            if (Num(p.Value, out var fontScale))
                                s.FontScale = fontScale;
                            break;
                        case "mini_w":
                            if (Num(p.Value, out var miniW))
                                s.MiniW = miniW;
                            break;
                        case "mini_h":
                            if (Num(p.Value, out var miniH))
                                s.MiniH = miniH;
                            break;
                        case "exp_w":
                            if (Num(p.Value, out var expW))
                                s.ExpW = expW;
                            break;
                        case "exp_h":
                            if (Num(p.Value, out var expH))
                                s.ExpH = expH;
                            break;
                        case "full_w":
                            if (Num(p.Value, out var fullW))
                                s.FullW = fullW;
                            break;
                        case "full_h":
                            if (Num(p.Value, out var fullH))
                                s.FullH = fullH;
                            break;
                        case "pos_x":
                            if (Num(p.Value, out var posX))
                                s.PosX = posX;
                            else if (p.Value.ValueKind == JsonValueKind.Null)
                                s.PosX = null;
                            break;
                        case "pos_y":
                            if (Num(p.Value, out var posY))
                                s.PosY = posY;
                            else if (p.Value.ValueKind == JsonValueKind.Null)
                                s.PosY = null;
                            break;
                        case "full_tab":
                            if (Str(p.Value, out var fullTab))
                                s.FullTab = fullTab;
                            break;
                    }
                }
            }
            catch (Exception)
            {
                return new AppSettings();
            }

            s.Sanitize();
            return s;
        }

        public string ToJson()
        {
            try
            {
                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("theme", Theme ?? "light");
                    w.WriteString("speed", Speed ?? "balanced");
                    w.WriteString("mode", Mode ?? "mini");
                    w.WriteBoolean("always_on_top", AlwaysOnTop);
                    w.WriteBoolean("show_cpu", ShowCpu);
                    w.WriteBoolean("show_ram", ShowRam);
                    w.WriteBoolean("show_disk", ShowDisk);
                    w.WriteBoolean("show_network", ShowNetwork);
                    w.WriteNumber("opacity", F(Opacity));
                    w.WriteNumber("font_scale", F(FontScale));
                    w.WriteNumber("mini_w", F(MiniW));
                    w.WriteNumber("mini_h", F(MiniH));
                    w.WriteNumber("exp_w", F(ExpW));
                    w.WriteNumber("exp_h", F(ExpH));
                    w.WriteNumber("full_w", F(FullW));
                    w.WriteNumber("full_h", F(FullH));
                    if (PosX.HasValue)
                        w.WriteNumber("pos_x", F(PosX.Value));
                    else
                        w.WriteNull("pos_x");
                    if (PosY.HasValue)
                        w.WriteNumber("pos_y", F(PosY.Value));
                    else
                        w.WriteNull("pos_y");
                    w.WriteString("full_tab", FullTab);
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch (Exception)
            {
                return "{}";
            }
        }
    }
}
