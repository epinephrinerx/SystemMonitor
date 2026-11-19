using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class AutostartTests
{
    [TestMethod]
    public void DesktopEntry_Build_CreatesExpectedDesktopEntryContent()
    {
        string exe = "/usr/bin/sysmonitor";
        string content = DesktopEntry.Build(exe);

        string expected =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=System Monitor\n" +
            "Exec=\"/usr/bin/sysmonitor\"\n" +
            "Icon=systemmonitor\n" +
            "X-GNOME-Autostart-enabled=true\n" +
            "Terminal=false\n";

        Assert.AreEqual(expected, content);
    }

    [TestMethod]
    public void DesktopEntry_Quote_EscapesSpecialCharacters()
    {
        string raw = "app \"quoted\" `tick` $var \\slash";
        string quoted = DesktopEntry.Quote(raw);

        // Stage 1 (Exec quoting) escapes ", `, $ and \ with a backslash.
        // Stage 2 (String value escaping) doubles every backslash.
        Assert.AreEqual("\"app \\\\\"quoted\\\\\" \\\\`tick\\\\` \\\\$var \\\\\\\\slash\"", quoted);
    }

    [TestMethod]
    public void DesktopEntry_Quote_EscapesPercentFieldCodes()
    {
        string raw = "/usr/bin/my%app%dir";
        string quoted = DesktopEntry.Quote(raw);

        // Desktop Entry spec reserves % for field codes, escaped strictly as %%
        Assert.AreEqual("\"/usr/bin/my%%app%%dir\"", quoted);
    }

    [TestMethod]
    public void DesktopEntry_Quote_And_Build_ExactFileTextForDollarAndBackslash()
    {
        // Assert the exact file text for $ (\\$) and \ (\\\\) (Amendment 2, item 3)
        string exeWithDollar = "/usr/bin/my$app";
        string quotedDollar = DesktopEntry.Quote(exeWithDollar);
        Assert.AreEqual("\"/usr/bin/my\\\\$app\"", quotedDollar);
        string fileWithDollar = DesktopEntry.Build(exeWithDollar);
        Assert.IsTrue(fileWithDollar.Contains("Exec=\"/usr/bin/my\\\\$app\"\n"));

        string exeWithBackslash = @"/usr/bin/my\app";
        string quotedBackslash = DesktopEntry.Quote(exeWithBackslash);
        Assert.AreEqual("\"/usr/bin/my\\\\\\\\app\"", quotedBackslash);
        string fileWithBackslash = DesktopEntry.Build(exeWithBackslash);
        Assert.IsTrue(fileWithBackslash.Contains("Exec=\"/usr/bin/my\\\\\\\\app\"\n"));
    }

    [TestMethod]
    public void DesktopEntry_RoundTrip_SpecialCharactersPath()
    {
        // Round-trip paths containing $, `, ", \, % and a space
        string path = "/usr/local/bin/my app $var `tick` \"quote\" \\path %pct";
        string quoted = DesktopEntry.Quote(path);
        string? unquoted = DesktopEntry.Unquote(quoted);

        Assert.AreEqual(path, unquoted);
        Assert.AreEqual(path, DesktopEntry.SingleArgument(quoted));
    }

    [TestMethod]
    public void DesktopEntry_Unquote_RemovesQuotesAndUnescapesSpecialCharacters()
    {
        string quoted = "\"app \\\\\"quoted\\\\\" \\\\`tick\\\\` \\\\$var \\\\\\\\slash\"";
        string? unquoted = DesktopEntry.Unquote(quoted);

        Assert.AreEqual("app \"quoted\" `tick` $var \\slash", unquoted);
    }

    [TestMethod]
    public void DesktopEntry_Unquote_HandlesEscapedPercent()
    {
        string exec = "\"/usr/bin/my%%app\"";
        string? unquoted = DesktopEntry.Unquote(exec);

        Assert.AreEqual("/usr/bin/my%app", unquoted);
    }

    [TestMethod]
    public void DesktopEntry_Unquote_RoundTripsQuotedStrings()
    {
        string original = "/usr/local/bin/my app \"v1\" `test` $PATH \\escaped";
        string quoted = DesktopEntry.Quote(original);
        string? unquoted = DesktopEntry.Unquote(quoted);

        Assert.AreEqual(original, unquoted);
    }

    [TestMethod]
    public void DesktopEntry_CanRepresent_RejectsControlCharacters()
    {
        Assert.IsTrue(DesktopEntry.CanRepresent("/usr/bin/sysmonitor"));
        Assert.IsTrue(DesktopEntry.CanRepresent("/opt/my app/bin/sysmonitor"));
        Assert.IsFalse(DesktopEntry.CanRepresent("/usr/bin/sysmonitor\nmalicious"));
        Assert.IsFalse(DesktopEntry.CanRepresent("/usr/bin/sysmonitor\rmalicious"));
        Assert.IsFalse(DesktopEntry.CanRepresent("/usr/bin/sysmonitor\tmalicious"));
        Assert.IsFalse(DesktopEntry.CanRepresent(null));
    }

    [TestMethod]
    public void FileAutostart_SetTrue_PathWithNewline_CannotBeEnabled()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "autostart-newline-" + Guid.NewGuid().ToString("N"));
        var logs = new List<string>();
        try
        {
            var autostart = new FileAutostart("/usr/bin/sysmonitor\ninjection", tempDir, log: logs.Add);

            Assert.IsFalse(autostart.CanEnable);
            autostart.Set(true);

            Assert.IsFalse(File.Exists(autostart.Path));
            Assert.IsFalse(autostart.IsEnabled);
            Assert.IsTrue(logs.Any(l => l.Contains("cannot be enabled")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void DesktopEntry_Unquote_HandlesUnquotedExec()
    {
        Assert.AreEqual("/usr/bin/sysmonitor", DesktopEntry.Unquote("/usr/bin/sysmonitor --expanded"));
        Assert.AreEqual("/usr/bin/sysmonitor", DesktopEntry.Unquote("/usr/bin/sysmonitor"));
        Assert.AreEqual(null, DesktopEntry.Unquote(null));
        Assert.AreEqual(null, DesktopEntry.Unquote("   "));
        Assert.AreEqual(null, DesktopEntry.Unquote("\"unterminated quote"));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsTrue_WhenExecMatchesCurrentExecutable()
    {
        string exe = "/usr/bin/sysmonitor";
        string content = DesktopEntry.Build(exe);

        Assert.IsTrue(DesktopEntry.IsEnabled(content, exe));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsFalse_WhenExecPointsToDifferentExecutable()
    {
        string currentExe = "/usr/bin/sysmonitor";
        string olderExe = "/opt/old/sysmonitor";
        string content = DesktopEntry.Build(olderExe);

        Assert.IsFalse(DesktopEntry.IsEnabled(content, currentExe));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsFalse_WhenContentIsNull()
    {
        Assert.IsFalse(DesktopEntry.IsEnabled(null, "/usr/bin/sysmonitor"));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsFalse_WhenExecMissing()
    {
        string content = "[Desktop Entry]\nType=Application\nName=System Monitor\n";
        Assert.IsFalse(DesktopEntry.IsEnabled(content, "/usr/bin/sysmonitor"));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsFalse_WhenHiddenIsTrue()
    {
        string exe = "/usr/bin/sysmonitor";
        string content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Hidden=true\n" +
            "Exec=\"/usr/bin/sysmonitor\"\n";

        Assert.IsFalse(DesktopEntry.IsEnabled(content, exe));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsFalse_WhenHiddenIsTrueCaseInsensitive()
    {
        string exe = "/usr/bin/sysmonitor";
        string content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Hidden=True\n" +
            "Exec=\"/usr/bin/sysmonitor\"\n";

        Assert.IsFalse(DesktopEntry.IsEnabled(content, exe));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsTrue_WhenHiddenIsFalse()
    {
        string exe = "/usr/bin/sysmonitor";
        string content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Hidden=false\n" +
            "Exec=\"/usr/bin/sysmonitor\"\n";

        Assert.IsTrue(DesktopEntry.IsEnabled(content, exe));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsFalse_WhenExecHasExtraArguments()
    {
        // Exec="<exe>" --probe-update means not enabled (Amendment 2, item 4)
        string exe = "/usr/bin/sysmonitor";
        string content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Exec=\"/usr/bin/sysmonitor\" --probe-update\n";

        Assert.IsFalse(DesktopEntry.IsEnabled(content, exe));
        Assert.IsNull(DesktopEntry.SingleArgument("\"/usr/bin/sysmonitor\" --probe-update"));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsTrue_WhenQuotedExeHasTrailingSpaces()
    {
        // The quoted exe with trailing spaces means enabled (Amendment 2, item 4)
        string exe = "/usr/bin/sysmonitor";
        string content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Exec=\"/usr/bin/sysmonitor\"   \n";

        Assert.IsTrue(DesktopEntry.IsEnabled(content, exe));
        Assert.AreEqual(exe, DesktopEntry.SingleArgument("\"/usr/bin/sysmonitor\"   "));
    }

    [TestMethod]
    public void DesktopEntry_IsEnabled_ReturnsTrue_WhenUnquotedExeHasTrailingSpaces()
    {
        string exe = "/usr/bin/sysmonitor";
        string content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Exec=/usr/bin/sysmonitor   \n";

        Assert.IsTrue(DesktopEntry.IsEnabled(content, exe));
        Assert.AreEqual(exe, DesktopEntry.SingleArgument("/usr/bin/sysmonitor   "));
    }

    [TestMethod]
    public void FileAutostart_Properties_ReflectExecutableAndPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "autostart-props-" + Guid.NewGuid().ToString("N"));
        try
        {
            var autostart = new FileAutostart("/opt/test/sysmonitor", tempDir);

            Assert.AreEqual("/opt/test/sysmonitor", autostart.Executable);
            Assert.AreEqual(Path.Combine(tempDir, "systemmonitor.desktop"), autostart.Path);
            Assert.AreEqual(DesktopEntry.Build("/opt/test/sysmonitor"), autostart.Text);
            Assert.IsFalse(autostart.IsEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void FileAutostart_SetTrue_WritesDesktopFileAtomically()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "autostart-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            var autostart = new FileAutostart("/opt/sysmonitor", tempDir);

            autostart.Set(true);

            Assert.IsTrue(File.Exists(autostart.Path));
            string content = File.ReadAllText(autostart.Path);
            Assert.AreEqual(autostart.Text, content);
            Assert.IsTrue(autostart.IsEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void FileAutostart_SetFalse_DeletesOnlyOurDesktopFile_LeavesOtherDesktopFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "autostart-delete-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            string otherDesktop = Path.Combine(tempDir, "other-app.desktop");
            File.WriteAllText(otherDesktop, "[Desktop Entry]\nName=Other\n");

            var autostart = new FileAutostart("/opt/sysmonitor", tempDir);
            autostart.Set(true);

            Assert.IsTrue(File.Exists(autostart.Path));
            Assert.IsTrue(File.Exists(otherDesktop));

            autostart.Set(false);

            Assert.IsFalse(File.Exists(autostart.Path));
            Assert.IsTrue(File.Exists(otherDesktop), "Disabling autostart must delete only our file and leave other desktop files.");
            Assert.IsFalse(autostart.IsEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void FileAutostart_DefaultDirectory_HonoursXdgConfigHome()
    {
        string? originalXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string fakeXdg = Path.Combine(Path.GetTempPath(), "fake-xdg-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", fakeXdg);
            string defaultDir = FileAutostart.DefaultDirectory;

            Assert.AreEqual(Path.Combine(fakeXdg, "autostart"), defaultDir);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", originalXdg);
        }
    }

    [TestMethod]
    public void FileAutostart_LogsActions()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "autostart-log-" + Guid.NewGuid().ToString("N"));
        var logs = new List<string>();
        try
        {
            var autostart = new FileAutostart("/opt/sysmonitor", tempDir, log: logs.Add);

            autostart.Set(true);
            autostart.Set(false);

            Assert.AreEqual(2, logs.Count);
            Assert.IsTrue(logs[0].StartsWith("autostart enabled: "));
            Assert.IsTrue(logs[1].StartsWith("autostart disabled: "));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
