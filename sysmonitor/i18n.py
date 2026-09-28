"""Thai / English strings, carried over from the 1.0.5 i18n work."""

STRINGS = {
    "th": {
        "title": "SysMonitor",
        "connecting": "กำลังอ่านข้อมูลฮาร์ดแวร์...",
        "no_data": "กรุณาเลือกข้อมูลที่จะแสดงผล",
        "window_settings": "การตั้งค่าหน้าต่าง",
        "always_on_top": "หน้าสุดเสมอ",
        "snap": "ดูดขอบจอ",
        "light_mode": "โหมดสว่าง",
        "display_settings": "ข้อมูลที่แสดงผล",
        "show_cpu": "แสดง CPU",
        "show_ram": "แสดง Memory",
        "show_disk": "แสดง Disk",
        "per_core": "แยกราย Core",
        "total": "รวมทั้งหมด",
        "per_drive": "แยกราย Drive",
        "speed": "ความถี่ในการอ่าน",
        "eco": "ประหยัด",
        "balanced": "สมดุล",
        "fast": "เร็ว",
        "language": "ภาษา",
        "opacity": "ความโปร่งใส",
        "cpu": "CPU",
        "cores": "คอร์",
        "memory": "Memory",
        "disk": "Disk Storage",
        "in_use": "ใช้งานอยู่",
        "read": "อ่าน",
        "write": "เขียน",
        "all_drives": "ทุกไดรฟ์",
        "temp_legend": "= อุณหภูมิ  •  n/a = ไม่มีเซ็นเซอร์วัดอุณหภูมิ  •  ~ = ค่าประมาณจากโหลด ไม่ใช่ค่าที่วัดจริง",
        "temp_label": "อุณหภูมิ",
        "no_sensor": "n/a = ไม่มีเซ็นเซอร์วัดอุณหภูมิ",
        "reset_size": "รีเซ็ตขนาดหน้าต่าง",
        "close": "ปิดโปรแกรม",
        "collapse": "ย่อหน้าต่าง",
        "expand_hint": "ดับเบิลคลิกเพื่อขยาย",
        "drive": "ไดรฟ์",
    },
    "en": {
        "title": "SysMonitor",
        "connecting": "Reading hardware...",
        "no_data": "Select something to display",
        "window_settings": "Window",
        "always_on_top": "Always on top",
        "snap": "Snap to edge",
        "light_mode": "Light mode",
        "display_settings": "Display",
        "show_cpu": "Show CPU",
        "show_ram": "Show Memory",
        "show_disk": "Show Disk",
        "per_core": "Per core",
        "total": "Combined",
        "per_drive": "Per drive",
        "speed": "Refresh rate",
        "eco": "Eco",
        "balanced": "Balanced",
        "fast": "Fast",
        "language": "Language",
        "opacity": "Opacity",
        "cpu": "CPU",
        "cores": "cores",
        "memory": "Memory",
        "disk": "Disk Storage",
        "in_use": "in use",
        "read": "R",
        "write": "W",
        "all_drives": "All drives",
        "temp_legend": "= temperature  •  n/a = no temperature sensor  •  ~ = modelled from load, not measured",
        "temp_label": "Temp",
        "no_sensor": "n/a = no temperature sensor",
        "reset_size": "Reset window size",
        "close": "Close",
        "collapse": "Collapse",
        "expand_hint": "Double-click to expand",
        "drive": "Drive",
    },
}


class Lang:
    def __init__(self, code="th"):
        self.code = code if code in STRINGS else "th"

    def set(self, code):
        if code in STRINGS:
            self.code = code

    def __call__(self, key):
        return STRINGS[self.code].get(key, STRINGS["en"].get(key, key))
