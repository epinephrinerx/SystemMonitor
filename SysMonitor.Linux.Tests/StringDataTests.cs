using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class StringDataTests
{
    [TestMethod]
    public void StringData_Rows_HaveNoDuplicates_AndEveryKeyHasThaiAndEnglish()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string key, string thai, string english) in StringData.Rows)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(key), "Key must not be empty");
            Assert.IsTrue(keys.Add(key), $"Duplicate key in StringData: {key}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(thai), $"Key '{key}' is missing Thai text");
            Assert.IsFalse(string.IsNullOrWhiteSpace(english), $"Key '{key}' is missing English text");
        }

        // 199 original spec keys minus manual_10_h and manual_10_t
        Assert.AreEqual(197, StringData.Rows.Length);
    }

    [TestMethod]
    public void StringData_Amendment_LinuxWordingApplied()
    {
        var langTh = new Lang("th");
        var langEn = new Lang("en");

        // autostart
        Assert.AreEqual("เปิดเมื่อเข้าสู่ระบบ", langTh["autostart"]);
        Assert.AreEqual("Start at login", langEn["autostart"]);

        // about_desc
        Assert.AreEqual("มอนิเตอร์สถานะเครื่อง (CPU, RAM, Disk, GPU, Network) แบบ Widget บน Linux", langTh["about_desc"]);
        Assert.AreEqual("A Linux desktop widget for CPU, RAM, disk, GPU and network status.", langEn["about_desc"]);

        // disk_open_explorer
        Assert.AreEqual("เปิด {0} ในตัวจัดการไฟล์", langTh["disk_open_explorer"]);
        Assert.AreEqual("Open {0} in the file manager", langEn["disk_open_explorer"]);

        // disk_cleanup
        Assert.AreEqual("ดูการใช้พื้นที่ ({0})", langTh["disk_cleanup"]);
        Assert.AreEqual("Disk usage ({0})", langEn["disk_cleanup"]);

        // disk_manager
        Assert.AreEqual("จัดการดิสก์", langTh["disk_manager"]);
        Assert.AreEqual("Disks", langEn["disk_manager"]);

        // manual_4_t
        string m4Th = langTh["manual_4_t"];
        string m4En = langEn["manual_4_t"];
        StringAssert.Contains(m4Th, "ตัวจัดการไฟล์");
        StringAssert.Contains(m4Th, "ดูการใช้พื้นที่");
        StringAssert.Contains(m4Th, "จัดการดิสก์");
        Assert.IsFalse(m4Th.Contains("File Explorer"));
        Assert.IsFalse(m4Th.Contains("Disk Cleanup"));
        Assert.IsFalse(m4Th.Contains("Disk Management"));

        StringAssert.Contains(m4En, "file manager");
        StringAssert.Contains(m4En, "Disk usage");
        StringAssert.Contains(m4En, "Disks");
        Assert.IsFalse(m4En.Contains("File Explorer"));
        Assert.IsFalse(m4En.Contains("Disk Cleanup"));
        Assert.IsFalse(m4En.Contains("Disk Management"));

        // manual_6_t
        string m6Th = langTh["manual_6_t"];
        string m6En = langEn["manual_6_t"];
        StringAssert.Contains(m6Th, "เปิดเมื่อเข้าสู่ระบบ");
        Assert.IsFalse(m6Th.Contains("เปิดพร้อม Windows"));
        StringAssert.Contains(m6En, "start at login");
        Assert.IsFalse(m6En.Contains("Start with Windows"));
        Assert.IsFalse(m6En.Contains("start with Windows"));
    }

    [TestMethod]
    public void StringData_Manual_EndsAtSection9()
    {
        var lang = new Lang("en");

        for (int i = 1; i <= 9; i++)
        {
            Assert.IsTrue(lang.Has("manual_" + i + "_h"), $"manual_{i}_h should exist");
            Assert.IsTrue(lang.Has("manual_" + i + "_t"), $"manual_{i}_t should exist");
        }

        Assert.IsFalse(lang.Has("manual_10_h"), "manual_10_h should be removed");
        Assert.IsFalse(lang.Has("manual_10_t"), "manual_10_t should be removed");
    }

    [TestMethod]
    public void StringData_NoWindowsOrFileExplorer_InVisibleLinuxKeys()
    {
        var langTh = new Lang("th");
        var langEn = new Lang("en");

        string[] visibleKeys =
        {
            "title",
            "autostart",
            "about_desc",
            "disk_open_explorer",
            "disk_cleanup",
            "disk_manager",
            "manual_1_h", "manual_1_t",
            "manual_2_h", "manual_2_t",
            "manual_3_h", "manual_3_t",
            "manual_4_h", "manual_4_t",
            "manual_5_h", "manual_5_t",
            "manual_6_h", "manual_6_t",
            "manual_7_h", "manual_7_t",
            "manual_8_h", "manual_8_t",
            "manual_9_h", "manual_9_t",
        };

        foreach (string key in visibleKeys)
        {
            string th = langTh[key];
            string en = langEn[key];

            Assert.IsFalse(th.Contains("Windows", StringComparison.OrdinalIgnoreCase), $"Key '{key}' Thai has 'Windows'");
            Assert.IsFalse(en.Contains("Windows", StringComparison.OrdinalIgnoreCase), $"Key '{key}' English has 'Windows'");
            Assert.IsFalse(th.Contains("File Explorer", StringComparison.OrdinalIgnoreCase), $"Key '{key}' Thai has 'File Explorer'");
            Assert.IsFalse(en.Contains("File Explorer", StringComparison.OrdinalIgnoreCase), $"Key '{key}' English has 'File Explorer'");
        }
    }

    [TestMethod]
    public void StringData_TempLegend_StartsWithEqualsSign()
    {
        var langTh = new Lang("th");
        var langEn = new Lang("en");

        StringAssert.StartsWith(langTh["temp_legend"], "= ");
        StringAssert.StartsWith(langEn["temp_legend"], "= ");
    }

    [TestMethod]
    public void Lang_SetLanguage_AndFallbackBehaviour()
    {
        var lang = new Lang("en");
        Assert.AreEqual("en", lang.Code);

        Assert.IsTrue(lang.Set("th"));
        Assert.AreEqual("th", lang.Code);

        Assert.IsTrue(lang.Set("EN"));
        Assert.AreEqual("en", lang.Code);

        // Unknown language returns false and does not change code
        Assert.IsFalse(lang.Set("fr"));
        Assert.AreEqual("en", lang.Code);

        // Unknown key returns the key itself
        Assert.AreEqual("non_existent_key", lang["non_existent_key"]);

        // Formatted lookup
        string formatted = lang.Get("disk_open_explorer", "/mnt/data");
        Assert.AreEqual("Open /mnt/data in the file manager", formatted);
    }

    [TestMethod]
    public void ThaiWrap_Soften_InsertsZeroWidthSpaceBeforeLeadingVowels()
    {
        // Test: consonant 'ก' followed by leading vowel 'เ' and consonant 'ก'
        string testInput = "\u0E01\u0E40\u0E01"; // ก + เ + ก
        string softened = ThaiWrap.Soften(testInput);

        Assert.AreEqual(4, softened.Length);
        Assert.AreEqual('\u0E01', softened[0]);
        Assert.AreEqual('\u200B', softened[1]); // zero-width space
        Assert.AreEqual('\u0E40', softened[2]);
        Assert.AreEqual('\u0E01', softened[3]);

        // Null and empty return empty string
        Assert.AreEqual(string.Empty, ThaiWrap.Soften(null));
        Assert.AreEqual(string.Empty, ThaiWrap.Soften(string.Empty));
    }
}
