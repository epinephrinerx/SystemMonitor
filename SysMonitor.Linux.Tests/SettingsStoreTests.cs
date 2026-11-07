#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    private static void AssertDefaults(AppSettings actual)
    {
        // oracle: section 1 enumerates these nineteen literal defaults; section 2 failure loads return them.
        Assert.AreEqual("light", actual.Theme);
        Assert.AreEqual("balanced", actual.Speed);
        Assert.AreEqual("mini", actual.Mode);
        Assert.AreEqual(true, actual.AlwaysOnTop);
        Assert.AreEqual(true, actual.ShowCpu);
        Assert.AreEqual(true, actual.ShowRam);
        Assert.AreEqual(true, actual.ShowDisk);
        Assert.AreEqual(true, actual.ShowNetwork);
        Assert.AreEqual(0.92, actual.Opacity);
        Assert.AreEqual(1.0, actual.FontScale);
        Assert.AreEqual(270.0, actual.MiniW);
        Assert.AreEqual(104.0, actual.MiniH);
        Assert.AreEqual(630.0, actual.ExpW);
        Assert.AreEqual(480.0, actual.ExpH);
        Assert.AreEqual(0.0, actual.FullW);
        Assert.AreEqual(0.0, actual.FullH);
        Assert.IsNull(actual.PosX);
        Assert.IsNull(actual.PosY);
        Assert.AreEqual("", actual.FullTab);
    }

    private static void WithTemp(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SysMonitor-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { test(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void ConfigFile_RootedXdgWins()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(dir, Path.Combine(dir, "home"));
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.AreEqual(Path.Combine(dir, "sysmonitor", "config.linux.json"), actual);
        });
    }

    [TestMethod]
    public void ConfigFile_RelativeXdgFallsBack()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile("relative", dir);
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.AreEqual(Path.Combine(dir, ".config", "sysmonitor", "config.linux.json"), actual);
        });
    }

    [TestMethod]
    public void ConfigFile_BlankXdgFallsBack()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile("", dir);
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.AreEqual(Path.Combine(dir, ".config", "sysmonitor", "config.linux.json"), actual);
        });
    }

    [TestMethod]
    public void ConfigFile_WhitespaceXdgFallsBack()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(" \t ", dir);
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.AreEqual(Path.Combine(dir, ".config", "sysmonitor", "config.linux.json"), actual);
        });
    }

    [TestMethod]
    public void ConfigFile_NullXdgFallsBack()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(null, dir);
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.AreEqual(Path.Combine(dir, ".config", "sysmonitor", "config.linux.json"), actual);
        });
    }

    [TestMethod]
    public void ConfigFile_TrimXdg()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(" \t" + dir + "\n ", null);
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.AreEqual(Path.Combine(dir, "sysmonitor", "config.linux.json"), actual);
        });
    }

    [TestMethod]
    public void ConfigFile_TrimHome()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(null, " \t" + dir + "\n ");
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.AreEqual(Path.Combine(dir, ".config", "sysmonitor", "config.linux.json"), actual);
        });
    }

    [TestMethod]
    public void ConfigFile_BothNull()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(null, null);
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.IsNull(actual);
        });
    }

    [TestMethod]
    public void ConfigFile_BothRelative()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile("xdg", "home");
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.IsNull(actual);
        });
    }

    [TestMethod]
    public void ConfigFile_RelativeHome()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(null, "relative");
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.IsNull(actual);
        });
    }

    [TestMethod]
    public void ConfigFile_BothEmpty()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile("", "");
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.IsNull(actual);
        });
    }

    [TestMethod]
    public void ConfigFile_BothWhitespace()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(" \t", "\n ");
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.IsNull(actual);
        });
    }

    [TestMethod]
    public void ConfigFile_InvalidXdgNullHome()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile("relative", null);
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.IsNull(actual);
        });
    }

    [TestMethod]
    public void ConfigFile_RootedXdgBlankHome()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(dir, " ");
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.AreEqual(Path.Combine(dir, "sysmonitor", "config.linux.json"), actual);
        });
    }

    [TestMethod]
    public void ConfigFile_FilesystemRoot()
    {
        WithTemp(dir =>
        {
            var actual = ConfigPaths.ConfigFile(Path.GetPathRoot(dir), null);
            // oracle: section 2 trims both inputs; rooted XDG wins, else rooted home/.config, else null.
            Assert.AreEqual(Path.Combine(Path.GetPathRoot(dir)!, "sysmonitor", "config.linux.json"), actual);
        });
    }

    [TestMethod]
    public void Load_MissingFile()
    {
        WithTemp(dir =>
        {
            var actual = SettingsStore.Load(Path.Combine(dir, "missing.json"));
            // oracle: section 2 missing file yields defaults.
            AssertDefaults(actual);
        });
    }

    [TestMethod]
    public void Load_DirectoryPath()
    {
        WithTemp(dir =>
        {
            var actual = SettingsStore.Load(dir);
            // oracle: section 2 directory path yields defaults.
            AssertDefaults(actual);
        });
    }

    [TestMethod]
    public void Load_NullPath()
    {
        WithTemp(dir =>
        {
            var actual = SettingsStore.Load(null);
            // oracle: section 2 null path yields defaults.
            AssertDefaults(actual);
        });
    }

    [TestMethod]
    public void Load_InvalidPathNeverThrows()
    {
        WithTemp(dir =>
        {
            var actual = SettingsStore.Load(Path.Combine(dir, "bad") + "\0");
            // oracle: section 2 unreadable/invalid paths yield defaults without throwing.
            AssertDefaults(actual);
        });
    }

    [TestMethod]
    public void Load_OverOneMiB()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "large.json");
            File.WriteAllText(path, "{\"theme\":\"dark\"}" + new string(' ', 1024 * 1024), new UTF8Encoding(false));
            var actual = SettingsStore.Load(path);
            // oracle: section 2 file larger than 1 MB yields defaults even if valid JSON.
            AssertDefaults(actual);
        });
    }

    [TestMethod]
    public void Load_OneMillionBytesWithinLimit()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "limit.json");
            const string json = "{\"theme\":\"dark\"}";
            File.WriteAllText(path, json + new string(' ', 1_000_000 - json.Length), new UTF8Encoding(false));
            var actual = SettingsStore.Load(path);
            // oracle: Inspector-B (1) a 1,000,000-byte file is below the 1,048,576-byte inclusive limit.
            Assert.AreEqual("dark", actual.Theme);
        });
    }

    [TestMethod]
    public void Load_ExactlyOneMiBReadsFile()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "limit.json");
            const string json = "{\"theme\":\"dark\",\"speed\":\"fast\"}";
            File.WriteAllText(path, json + new string(' ', 1_048_576 - json.Length), new UTF8Encoding(false));
            // oracle: ASCII JSON plus spaces without a BOM is exactly 1,048,576 bytes; Inspector-B (1) includes this boundary.
            Assert.AreEqual(1_048_576L, new FileInfo(path).Length);
            var actual = SettingsStore.Load(path);
            Assert.AreEqual("dark", actual.Theme);
            Assert.AreEqual("fast", actual.Speed);
        });
    }

    [TestMethod]
    public void Load_OneByteOverOneMiBReturnsDefaults()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "over-limit.json");
            const string json = "{\"theme\":\"dark\",\"speed\":\"fast\"}";
            File.WriteAllText(path, json + new string(' ', 1_048_577 - json.Length), new UTF8Encoding(false));
            // oracle: Inspector-B (1) rejects exactly 1,048,577 bytes even though the padded JSON is valid.
            Assert.AreEqual(1_048_577L, new FileInfo(path).Length);
            AssertDefaults(SettingsStore.Load(path));
        });
    }

    [TestMethod]
    public void Load_UnreadableFileReturnsDefaultsWithoutThrowing()
    {
        WithTemp(dir =>
        {
            if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            {
                // oracle: a permission-denial test is inconclusive where Unix file permissions cannot be enforced, including root.
                Assert.Inconclusive("Requires a non-root Unix user and enforced Unix file permissions.");
                return;
            }

            var path = Path.Combine(dir, "unreadable.json");
            File.WriteAllText(path, "{\"theme\":\"dark\"}");
            UnixFileMode? originalMode = null;
            var modeChanged = false;
            try
            {
                try
                {
                    originalMode = File.GetUnixFileMode(path);
                    File.SetUnixFileMode(path, UnixFileMode.None);
                    modeChanged = true;
                }
                catch (Exception ex) when (ex is NotSupportedException
                    or UnauthorizedAccessException or IOException)
                {
                    // oracle: skip only if this OS/filesystem cannot establish the unreadable fixture.
                    Assert.Inconclusive("Cannot enforce Unix file permissions: " + ex.Message);
                    return;
                }

                var denied = false;
                try { using var probe = File.OpenRead(path); }
                catch (UnauthorizedAccessException) { denied = true; }
                catch (IOException) { denied = true; }
                if (!denied)
                {
                    // oracle: root/capabilities or a filesystem ignoring mode bits cannot exercise permission-denied Load.
                    Assert.Inconclusive("The current process can still read a file with UnixFileMode.None.");
                    return;
                }

                // oracle: Inspector-B (4) unreadable files return all defaults and never throw; SUT exceptions are not caught here.
                AssertDefaults(SettingsStore.Load(path));
            }
            finally
            {
                if (modeChanged && originalMode.HasValue) File.SetUnixFileMode(path, originalMode.Value);
            }
        });
    }

    [TestMethod]
    public void Load_Corrupt()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "{oops");
            var actual = SettingsStore.Load(path);
            // oracle: section 2 Load delegates malformed/empty/non-object text to Parse defaults.
            AssertDefaults(actual);
        });
    }

    [TestMethod]
    public void Load_Empty()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "");
            var actual = SettingsStore.Load(path);
            // oracle: section 2 Load delegates malformed/empty/non-object text to Parse defaults.
            AssertDefaults(actual);
        });
    }

    [TestMethod]
    public void Load_Whitespace()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, " \t ");
            var actual = SettingsStore.Load(path);
            // oracle: section 2 Load delegates malformed/empty/non-object text to Parse defaults.
            AssertDefaults(actual);
        });
    }

    [TestMethod]
    public void Load_NonObject()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "[]");
            var actual = SettingsStore.Load(path);
            // oracle: section 2 Load delegates malformed/empty/non-object text to Parse defaults.
            AssertDefaults(actual);
        });
    }

    [TestMethod]
    public void Load_ValidSettings()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "{\"theme\":\" DARK \",\"mini_w\":50,\"show_cpu\":false}");
            var actual = SettingsStore.Load(path);
            // oracle: section 2 Load uses Parse, including sanitisation.
            Assert.AreEqual("dark", actual.Theme);
            Assert.AreEqual(100.0, actual.MiniW);
            Assert.IsFalse(actual.ShowCpu);
        });
    }

    [TestMethod]
    public void Save_NullPath()
    {
        WithTemp(dir =>
        {
            var saved = SettingsStore.Save(null, new AppSettings());
            // oracle: section 2 null path returns false and creates no files.
            Assert.IsFalse(saved);
            Assert.AreEqual(0, Directory.GetFileSystemEntries(dir).Length);
        });
    }

    [TestMethod]
    public void Save_NullSettings()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            var saved = SettingsStore.Save(path, null);
            // oracle: section 2 null settings returns false without creating target; R2-01 leaves no *.tmp files.
            Assert.IsFalse(saved);
            Assert.IsFalse(File.Exists(path));
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories).Length);
        });
    }

    [TestMethod]
    public void Save_NewFileLeavesNoTemporaryFile()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            var saved = SettingsStore.Save(path, new AppSettings { Theme = "dark" });
            // oracle: R2-01 successful Save moves its process-unique temporary file to the target, leaving no *.tmp files.
            Assert.IsTrue(saved);
            Assert.AreEqual("dark", SettingsStore.Load(path).Theme);
            CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(dir));
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories).Length);
        });
    }

    [TestMethod]
    public void Save_OverwriteExistingFile()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "old content");
            var saved = SettingsStore.Save(path, new AppSettings { Speed = "fast" });
            // oracle: section 2 File.Move overwrites old content; R2-01 leaves no process-unique *.tmp file.
            Assert.IsTrue(saved);
            Assert.AreEqual("fast", SettingsStore.Load(path).Speed);
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories).Length);
        });
    }

    [TestMethod]
    public void Save_CreatesNestedDirectory()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "a", "b", "config.json");
            var saved = SettingsStore.Save(path, new AppSettings());
            // oracle: section 2 Save creates missing parent directories; R2-01 leaves no *.tmp file in them.
            Assert.IsTrue(saved);
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories).Length);
        });
    }

    [TestMethod]
    public void Save_ParentIsFileReturnsFalse()
    {
        WithTemp(dir =>
        {
            var parent = Path.Combine(dir, "parent");
            File.WriteAllText(parent, "unchanged");
            var path = Path.Combine(parent, "config.json");
            var saved = SettingsStore.Save(path, new AppSettings());
            // oracle: section 2 failure returns false and preserves parent file; R2-01 leaves no *.tmp files.
            Assert.IsFalse(saved);
            Assert.AreEqual("unchanged", File.ReadAllText(parent));
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories).Length);
            CollectionAssert.AreEqual(new[] { parent }, Directory.GetFiles(dir));
        });
    }

    [TestMethod]
    public void Save_TargetIsDirectoryCleansWrittenTmp()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            Directory.CreateDirectory(path);
            var saved = SettingsStore.Save(path, new AppSettings());
            // oracle: R2-01 failed move to a directory cleans the previously written process-unique *.tmp file.
            Assert.IsFalse(saved);
            Assert.IsTrue(Directory.Exists(path));
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories).Length);
        });
    }

    [TestMethod]
    public void Save_InvalidPathReturnsFalse()
    {
        WithTemp(dir =>
        {
            var saved = SettingsStore.Save(Path.Combine(dir, "bad") + "\0", new AppSettings());
            // oracle: section 2 any write failure returns false without throwing.
            Assert.IsFalse(saved);
            Assert.AreEqual(0, Directory.GetFileSystemEntries(dir).Length);
        });
    }

    [TestMethod]
    public void Save_SanitizesSameInstance()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            var settings = new AppSettings { Theme = " DARK ", MiniW = 50, Opacity = double.NaN };
            var saved = SettingsStore.Save(path, settings);
            // oracle: H05 Save calls Sanitize on supplied settings before writing.
            Assert.IsTrue(saved);
            Assert.AreEqual("dark", settings.Theme);
            Assert.AreEqual(100.0, settings.MiniW);
            Assert.AreEqual(0.92, settings.Opacity);
            Assert.IsTrue(settings.ValueEquals(SettingsStore.Load(path)));
        });
    }

    [TestMethod]
    public void Save_LongTabIsTruncated()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "config.json");
            var settings = new AppSettings { FullTab = new string('x', 201) };
            var saved = SettingsStore.Save(path, settings);
            // oracle: H05 persisted FullTab is truncated to 200 by Save sanitisation.
            Assert.IsTrue(saved);
            Assert.AreEqual(new string('x', 200), settings.FullTab);
            Assert.AreEqual(new string('x', 200), SettingsStore.Load(path).FullTab);
        });
    }

    [TestMethod]
    public void Load_MultipleCallsReturnIndependentDefaults()
    {
        WithTemp(dir =>
        {
            var path = Path.Combine(dir, "absent");
            var first = SettingsStore.Load(path);
            first.Theme = "dark";
            var second = SettingsStore.Load(path);
            // oracle: section 2 invalid/missing path returns new AppSettings each time.
            Assert.AreNotSame(first, second);
            Assert.AreEqual("light", second.Theme);
        });
    }

}
