namespace SysMonitor;

/// <summary>Thai / English strings, carried over from the 1.0.5 i18n work.</summary>
public sealed class Lang
{
    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["th"] = new()
        {
            ["title"] = "SysMonitor",
            ["connecting"] = "กำลังอ่านข้อมูลฮาร์ดแวร์...",
            ["no_data"] = "กรุณาเลือกข้อมูลที่จะแสดงผล",
            ["window_settings"] = "การตั้งค่าหน้าต่าง",
            ["always_on_top"] = "หน้าสุดเสมอ",
            ["snap"] = "ดูดขอบจอ",
            ["autostart"] = "เปิดพร้อม Windows",
            ["light_mode"] = "โหมดสว่าง",
            ["display_settings"] = "ข้อมูลที่แสดงผล",
            ["show_cpu"] = "แสดง CPU",
            ["show_ram"] = "แสดง Memory",
            ["show_disk"] = "แสดง Disk",
            ["per_core"] = "แยกราย Core",
            ["total"] = "รวมทั้งหมด",
            ["per_drive"] = "แยกราย Drive",
            ["speed"] = "ความถี่ในการอ่าน",
            ["eco"] = "ประหยัด",
            ["balanced"] = "สมดุล",
            ["fast"] = "เร็ว",
            ["language"] = "ภาษา",
            ["opacity"] = "ความโปร่งใส",
            ["font_size"] = "ขนาดตัวอักษร",
            ["cpu"] = "CPU",
            ["cores"] = "คอร์",
            ["memory"] = "Memory",
            ["disk"] = "Disk Storage",
            ["in_use"] = "ใช้งานอยู่",
            ["read"] = "อ่าน",
            ["write"] = "เขียน",
            ["all_drives"] = "ทุกไดรฟ์",
            ["temp_legend"] = "= อุณหภูมิ  •  n/a = ไม่มีเซ็นเซอร์วัดอุณหภูมิ  •  ~ = ค่าประมาณจากโหลด ไม่ใช่ค่าที่วัดจริง",
            ["temp_label"] = "อุณหภูมิ",
            ["cpu_temp"] = "อ่านอุณหภูมิ CPU จริง",
            ["no_sensor"] = "n/a = ไม่มีเซ็นเซอร์วัดอุณหภูมิ",
            ["reset_size"] = "รีเซ็ตขนาดหน้าต่าง",
            ["close"] = "ปิดโปรแกรม",
            ["close_action"] = "เมื่อกดปิด",
            ["close_to_exit"] = "ออกจากโปรแกรม",
            ["close_to_tray"] = "ย่อลง Tray",
            ["tray_show"] = "แสดงหน้าต่าง",
            ["tray_exit"] = "ออกจากโปรแกรม",
            ["collapse"] = "ย่อหน้าต่าง",
            ["expand_hint"] = "ดับเบิลคลิกเพื่อขยาย",
            ["drive"] = "ไดรฟ์",
            ["show_network"] = "แสดง Network",
            ["per_adapter"] = "แยกราย Adapter",
            ["network"] = "Network",
            ["download"] = "ดาวน์โหลด",
            ["upload"] = "อัปโหลด",
            ["wireless"] = "ไร้สาย",
            ["wired"] = "มีสาย",
            ["all_adapters"] = "ทุก Adapter",
            ["fullscreen"] = "เต็มจอ",
            ["exit_fullscreen"] = "ออกจากเต็มจอ",
            ["history"] = "ย้อนหลัง",
            ["utilisation"] = "% การใช้งาน",
            ["over_time"] = "ย้อนหลัง 3 นาที",
            ["no_link"] = "ไม่ทราบความเร็วลิงก์",
        },
        ["en"] = new()
        {
            ["title"] = "SysMonitor",
            ["connecting"] = "Reading hardware...",
            ["no_data"] = "Select something to display",
            ["window_settings"] = "Window",
            ["always_on_top"] = "Always on top",
            ["snap"] = "Snap to edge",
            ["autostart"] = "Start with Windows",
            ["light_mode"] = "Light mode",
            ["display_settings"] = "Display",
            ["show_cpu"] = "Show CPU",
            ["show_ram"] = "Show Memory",
            ["show_disk"] = "Show Disk",
            ["per_core"] = "Per core",
            ["total"] = "Combined",
            ["per_drive"] = "Per drive",
            ["speed"] = "Refresh rate",
            ["eco"] = "Eco",
            ["balanced"] = "Balanced",
            ["fast"] = "Fast",
            ["language"] = "Language",
            ["opacity"] = "Opacity",
            ["font_size"] = "Font size",
            ["cpu"] = "CPU",
            ["cores"] = "cores",
            ["memory"] = "Memory",
            ["disk"] = "Disk Storage",
            ["in_use"] = "in use",
            ["read"] = "R",
            ["write"] = "W",
            ["all_drives"] = "All drives",
            ["temp_legend"] = "= temperature  •  n/a = no temperature sensor  •  ~ = modelled from load, not measured",
            ["temp_label"] = "Temp",
            ["cpu_temp"] = "Read real CPU temperature",
            ["no_sensor"] = "n/a = no temperature sensor",
            ["reset_size"] = "Reset window size",
            ["close"] = "Close",
            ["close_action"] = "Close button",
            ["close_to_exit"] = "Exits",
            ["close_to_tray"] = "Hides to tray",
            ["tray_show"] = "Show window",
            ["tray_exit"] = "Exit",
            ["collapse"] = "Collapse",
            ["expand_hint"] = "Double-click to expand",
            ["drive"] = "Drive",
            ["show_network"] = "Show Network",
            ["per_adapter"] = "Per adapter",
            ["network"] = "Network",
            ["download"] = "Down",
            ["upload"] = "Up",
            ["wireless"] = "Wi-Fi",
            ["wired"] = "Ethernet",
            ["all_adapters"] = "All adapters",
            ["fullscreen"] = "Full screen",
            ["exit_fullscreen"] = "Leave full screen",
            ["history"] = "History",
            ["utilisation"] = "% utilisation",
            ["over_time"] = "over 3 minutes",
            ["no_link"] = "link speed unknown",
        },
    };

    public string Code { get; private set; } = "th";

    public Lang(string code = "th") => Set(code);

    public void Set(string code)
    {
        if (Strings.ContainsKey(code))
        {
            Code = code;
        }
    }

    public string this[string key] =>
        Strings[Code].TryGetValue(key, out string? value) ? value
        : Strings["en"].TryGetValue(key, out string? fallback) ? fallback
        : key;
}
