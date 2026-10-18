namespace SysMonitor.Ext.Smart;

/// <summary>
/// English and Thai translations for all strings used by the SMART extension.
/// </summary>
internal static class SmartStrings
{
    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["ext_name"] = "SMART",
        ["smart"] = "SMART",
        ["smart_health"] = "Health",
        ["smart_life"] = "Life left",
        ["smart_hours"] = "Power-on time",
        ["smart_hours_fmt"] = "{0:N0} h ({1:N0} days)",
        ["smart_serial"] = "Serial number",
        ["smart_firmware"] = "Firmware",
        ["smart_good"] = "Good",
        ["smart_caution"] = "Caution",
        ["smart_bad"] = "Bad",
        ["smart_unknown"] = "Unknown",
        ["smart_partial"] = "SMART data is incomplete or not trusted",
        ["smart_waiting"] = "Reading SMART for the first time…",
        ["smart_need_admin"] = "SMART needs the app to run as administrator (the button is in Settings)",
        ["smart_missing_core"] = "DiskInfoCore.dll was not found in the SMART extension folder, so SMART cannot be read",
        ["smart_failed"] = "SMART could not be read; it will try again",
        ["smart_none"] = "No disk reported SMART data",
        ["temp_label"] = "Temp",
        ["type"] = "Type",
    };

    private static readonly Dictionary<string, string> Th = new(StringComparer.Ordinal)
    {
        ["ext_name"] = "SMART",
        ["smart"] = "SMART",
        ["smart_health"] = "สุขภาพ",
        ["smart_life"] = "อายุที่เหลือ",
        ["smart_hours"] = "เวลาที่เปิดใช้งาน",
        ["smart_hours_fmt"] = "{0:N0} ชม. ({1:N0} วัน)",
        ["smart_serial"] = "หมายเลขซีเรียล",
        ["smart_firmware"] = "เฟิร์มแวร์",
        ["smart_good"] = "ดี",
        ["smart_caution"] = "ควรระวัง",
        ["smart_bad"] = "เสีย",
        ["smart_unknown"] = "ไม่ทราบ",
        ["smart_partial"] = "ได้ข้อมูล SMART ไม่ครบหรือไม่น่าเชื่อถือ",
        ["smart_waiting"] = "กำลังอ่าน SMART ครั้งแรก…",
        ["smart_need_admin"] = "SMART ต้องเปิดโปรแกรมแบบผู้ดูแลระบบ (ปุ่มอยู่ในหน้าตั้งค่า)",
        ["smart_missing_core"] = "ไม่พบ DiskInfoCore.dll ในโฟลเดอร์ส่วนเสริม SMART จึงอ่าน SMART ไม่ได้",
        ["smart_failed"] = "อ่าน SMART ไม่สำเร็จ จะลองใหม่เอง",
        ["smart_none"] = "ไม่พบดิสก์ที่รายงาน SMART",
        ["temp_label"] = "อุณหภูมิ",
        ["type"] = "ชนิด",
    };

    public static string Get(string key, string? lang)
    {
        var dict = string.Equals(lang, "th", StringComparison.OrdinalIgnoreCase) ? Th : En;
        if (dict.TryGetValue(key, out string? value))
        {
            return value;
        }
        if (En.TryGetValue(key, out string? fallback))
        {
            return fallback;
        }
        return key;
    }

    /// <summary>
    /// Helper for parity tests to assert that both language dictionaries contain identical keys.
    /// </summary>
    internal static IReadOnlyList<string> MissingKeys()
    {
        var missing = new List<string>();
        foreach (string k in En.Keys)
        {
            if (!Th.ContainsKey(k)) missing.Add($"Th missing key: {k}");
        }
        foreach (string k in Th.Keys)
        {
            if (!En.ContainsKey(k)) missing.Add($"En missing key: {k}");
        }
        return missing;
    }
}
