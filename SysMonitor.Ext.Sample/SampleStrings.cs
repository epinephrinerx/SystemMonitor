namespace SysMonitor.Ext.Sample;

/// <summary>
/// English and Thai translations for all strings used by the Sample extension.
/// </summary>
internal static class SampleStrings
{
    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["name"] = "Sample",
        ["widget_title"] = "Sample",
        ["widget_verdict"] = "Running",
        ["widget_line"] = "Third-party extension loaded",
        ["widget_message"] = "Sample extension is active (lang: {0})",
        ["overview_note"] = "Sample extension running in language: {0}",
        ["overview_row_title"] = "Sample Status",
        ["overview_row_detail"] = "Language: {0}",
        ["overview_row_val"] = "Active",
        ["tab_title"] = "Sample",
        ["tab_subtitle"] = "Third-party extension",
        ["tab_detail"] = "Exercising the approval and isolation flow",
        ["tab_hw"] = "Sample Extension 1.0",
        ["fact_status"] = "Status",
        ["fact_status_val"] = "Active",
        ["fact_lang"] = "Language",
        ["fact_type"] = "Type",
        ["fact_type_val"] = "Third-party (untrusted / user approved)",
    };

    private static readonly Dictionary<string, string> Th = new(StringComparer.Ordinal)
    {
        ["name"] = "ตัวอย่าง",
        ["widget_title"] = "ตัวอย่าง",
        ["widget_verdict"] = "กำลังทำงาน",
        ["widget_line"] = "โหลดส่วนเสริมภายนอกเรียบร้อย",
        ["widget_message"] = "ส่วนเสริมตัวอย่างกำลังทำงาน (ภาษา: {0})",
        ["overview_note"] = "ส่วนเสริมตัวอย่างทำงานในภาษา: {0}",
        ["overview_row_title"] = "สถานะตัวอย่าง",
        ["overview_row_detail"] = "ภาษา: {0}",
        ["overview_row_val"] = "ทำงานอยู่",
        ["tab_title"] = "ตัวอย่าง",
        ["tab_subtitle"] = "ส่วนเสริมภายนอก",
        ["tab_detail"] = "ทดสอบขั้นตอนการอนุมัติและการแยกกระบวนการ",
        ["tab_hw"] = "ส่วนเสริมตัวอย่าง 1.0",
        ["fact_status"] = "สถานะ",
        ["fact_status_val"] = "ทำงานอยู่",
        ["fact_lang"] = "ภาษา",
        ["fact_type"] = "ประเภท",
        ["fact_type_val"] = "ส่วนเสริมภายนอก (ได้รับการอนุมัติจากผู้ใช้)",
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
