using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
using SysMonitor.Tests.DummyExtension;

namespace SysMonitor.Tests.Extensions;

[TestClass]
public class StringTableAndI18nTests
{
    [TestMethod]
    public void StringTable_Get_ReturnsExpectedLanguageAndFallback()
    {
        var table = new StringTable(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["th"] = new Dictionary<string, string>
            {
                ["hello"] = "สวัสดี",
                ["status"] = "พร้อม",
            },
            ["en"] = new Dictionary<string, string>
            {
                ["hello"] = "Hello",
                ["status"] = "Ready",
                ["only_en"] = "English only",
            }
        });

        Assert.AreEqual("สวัสดี", table.Get("th", "hello"));
        Assert.AreEqual("Hello", table.Get("en", "hello"));
        // Thai fallback to English
        Assert.AreEqual("English only", table.Get("th", "only_en"));
        // Unknown key returns key name
        Assert.AreEqual("unknown_key", table.Get("th", "unknown_key"));
        // Case-insensitive language code
        Assert.AreEqual("สวัสดี", table.Get("TH", "hello"));
    }

    [TestMethod]
    public void StringTable_MissingKeys_ReportsDifference()
    {
        var th = new Dictionary<string, string> { ["k1"] = "v1" };
        var en = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" };
        var table = new StringTable(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["th"] = th,
            ["en"] = en
        });

        var missingInTh = StringTable.MissingKeys(table);
        CollectionAssert.AreEquivalent(new[] { "k2" }, (System.Collections.ICollection)missingInTh);
    }

    [TestMethod]
    public void I18n_ExtensionKeys_HaveFullParityBetweenThAndEn()
    {
        var langTh = new Lang("th");
        var langEn = new Lang("en");

        string[] requiredKeys =
        {
            "extensions",
            "ext_status_not_approved",
            "ext_status_not_trusted_when_elevated",
            "ext_status_contract_mismatch",
            "ext_status_host_too_old",
            "ext_status_hash_mismatch",
            "ext_status_unsafe_path",
            "ext_status_file_missing",
            "ext_status_manifest_invalid",
            "ext_status_entry_not_found",
            "ext_status_load_failed",
            "ext_status_duplicate_id",
            "ext_status_disabled",
        };

        foreach (string key in requiredKeys)
        {
            string thVal = langTh[key];
            string enVal = langEn[key];

            Assert.AreNotEqual(key, thVal, $"Key '{key}' is missing in Thai dictionary");
            Assert.AreNotEqual(key, enVal, $"Key '{key}' is missing in English dictionary");
            Assert.IsFalse(string.IsNullOrWhiteSpace(thVal));
            Assert.IsFalse(string.IsNullOrWhiteSpace(enVal));
        }
    }
}
