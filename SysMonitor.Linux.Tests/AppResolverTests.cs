using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class AppResolverTests
{
    private const int TestUid = 1000;

    private static string RelativeAppSlice =>
        Path.Combine("sys", "fs", "cgroup", "user.slice", $"user-{TestUid}.slice", $"user@{TestUid}.service", "app.slice");

    private static ProcessRow Row(int pid, string name, double? cpu = null, long memory = 1000) =>
        new()
        {
            Pid = pid,
            Name = name,
            CpuPercent = cpu,
            MemoryBytes = memory,
        };

    [TestMethod]
    public void Read_HandoffSection3_HierarchyAndNamingConventions()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        string appsDir = fake.CreateDirectory("usr/share/applications");

        // Desktop files
        fake.WriteFile("usr/share/applications/org.kde.konsole.desktop",
            "[Desktop Entry]\nName=Konsole\nExec=konsole\nIcon=org.kde.konsole\n");
        fake.WriteFile("usr/share/applications/org.kde.dolphin.desktop",
            "[Desktop Entry]\nName=Dolphin\nExec=dolphin %u\nIcon=system-file-manager\n");
        fake.WriteFile("usr/share/applications/google-chrome.desktop",
            "[Desktop Entry]\nName=Google Chrome\nExec=/usr/bin/google-chrome-stable %U\nIcon=google-chrome\n");
        fake.WriteFile("usr/share/applications/dropbox.desktop",
            "[Desktop Entry]\nName=Dropbox\nExec=dropbox start\nIcon=dropbox\n");

        // 1. app-org.kde.konsole-153839.scope with nested tab(153854).scope
        fake.WriteFile(Path.Combine(appSlice, "app-org.kde.konsole-153839.scope", "cgroup.procs"), "1001\n");
        fake.WriteFile(Path.Combine(appSlice, "app-org.kde.konsole-153839.scope", "tab(153854).scope", "cgroup.procs"), "1002\n");

        // 2. app-dolphin@132bf9f4.service and app-org.kde.dolphin@e9469414.service
        // Both resolve to org.kde.dolphin.desktop (exact match and suffix match)
        fake.WriteFile(Path.Combine(appSlice, "app-dolphin@132bf9f4.service", "cgroup.procs"), "2001\n");
        fake.WriteFile(Path.Combine(appSlice, "app-org.kde.dolphin@e9469414.service", "cgroup.procs"), "2002\n");

        // 3. app-google\x2dchrome@6d1540ed.service (systemd \x2d escaping)
        fake.WriteFile(Path.Combine(appSlice, @"app-google\x2dchrome@6d1540ed.service", "cgroup.procs"), "3001\n");

        // 4. app-dropbox@autostart.service
        fake.WriteFile(Path.Combine(appSlice, "app-dropbox@autostart.service", "cgroup.procs"), "4001\n");

        var processes = new[]
        {
            Row(1001, "konsole", cpu: 1.5, memory: 50_000_000),
            Row(1002, "bash", cpu: 0.5, memory: 10_000_000),
            Row(2001, "dolphin", cpu: 2.0, memory: 80_000_000),
            Row(2002, "dolphin", cpu: 1.0, memory: 70_000_000),
            Row(3001, "chrome", cpu: 5.0, memory: 200_000_000),
            Row(4001, "dropbox", cpu: null, memory: 40_000_000),
        };

        var resolver = new AppResolver(
            fake.Root,
            uid: TestUid,
            searchDirectories: new[] { appsDir },
            language: "en");

        IReadOnlyList<AppEntry> apps = resolver.Read(processes);

        // Expected apps: Dolphin, Dropbox, Google Chrome, Konsole
        Assert.AreEqual(4, apps.Count);

        // Dolphin: resolved to org.kde.dolphin.desktop, grouped across both units -> 2 instances
        AppEntry dolphin = apps.First(a => a.Name == "Dolphin");
        Assert.AreEqual("org.kde.dolphin", dolphin.Id);
        Assert.AreEqual(2, dolphin.Instances.Count);
        Assert.IsFalse(dolphin.Autostart);
        Assert.AreEqual(150_000_000L, dolphin.MemoryBytes);
        Assert.AreEqual(3.0, dolphin.CpuPercent);

        // Konsole: 1 instance with 2 processes (parent + nested tab)
        AppEntry konsole = apps.First(a => a.Name == "Konsole");
        Assert.AreEqual("org.kde.konsole", konsole.Id);
        Assert.AreEqual(1, konsole.Instances.Count);
        Assert.AreEqual(2, konsole.Instances[0].Processes.Count);
        Assert.AreEqual(60_000_000L, konsole.MemoryBytes);
        Assert.AreEqual(2.0, konsole.CpuPercent);

        // Google Chrome: \x2d unescaped to '-', resolved to google-chrome.desktop
        AppEntry chrome = apps.First(a => a.Name == "Google Chrome");
        Assert.AreEqual("google-chrome", chrome.Id);
        Assert.AreEqual(1, chrome.Instances.Count);
        Assert.AreEqual(200_000_000L, chrome.MemoryBytes);

        // Dropbox: @autostart service -> Autostart is true
        AppEntry dropbox = apps.First(a => a.Name == "Dropbox");
        Assert.AreEqual("dropbox", dropbox.Id);
        Assert.IsTrue(dropbox.Autostart, "Instance named @autostart must set Autostart = true");
    }

    [TestMethod]
    public void Read_Localization_PrefersNameThaiWhenConfigured()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        string appsDir = fake.CreateDirectory("usr/share/applications");

        fake.WriteFile("usr/share/applications/calc.desktop",
            "[Desktop Entry]\n" +
            "Name=Calculator\n" +
            "Name[th]=เครื่องคิดเลข\n" +
            "Exec=calc\n");

        fake.WriteFile(Path.Combine(appSlice, "app-calc-100.scope", "cgroup.procs"), "500\n");
        var procs = new[] { Row(500, "calc") };

        // 1. Language = "th" -> prefers Name[th]
        var resolverTh = new AppResolver(fake.Root, uid: TestUid, searchDirectories: new[] { appsDir }, language: "th");
        IReadOnlyList<AppEntry> appsTh = resolverTh.Read(procs);
        Assert.AreEqual(1, appsTh.Count);
        Assert.AreEqual("เครื่องคิดเลข", appsTh[0].Name);

        // 2. Language = "en" -> uses plain Name
        var resolverEn = new AppResolver(fake.Root, uid: TestUid, searchDirectories: new[] { appsDir }, language: "en");
        IReadOnlyList<AppEntry> appsEn = resolverEn.Read(procs);
        Assert.AreEqual(1, appsEn.Count);
        Assert.AreEqual("Calculator", appsEn[0].Name);
    }

    [TestMethod]
    public void Read_UnresolvedId_SetsNameEqualToId_AndExecNull()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        string appsDir = fake.CreateDirectory("usr/share/applications");

        // No desktop file for mystery-daemon
        fake.WriteFile(Path.Combine(appSlice, "app-mystery-daemon-999.scope", "cgroup.procs"), "888\n");
        var procs = new[] { Row(888, "mystery-daemon") };

        var resolver = new AppResolver(fake.Root, uid: TestUid, searchDirectories: new[] { appsDir });
        IReadOnlyList<AppEntry> apps = resolver.Read(procs);

        Assert.AreEqual(1, apps.Count);
        Assert.AreEqual("mystery-daemon", apps[0].Id);
        Assert.AreEqual("mystery-daemon", apps[0].Name);
        Assert.IsNull(apps[0].Exec);
        Assert.IsNull(apps[0].Icon);
        Assert.IsNull(apps[0].DesktopFile);
    }

    [TestMethod]
    public void Read_AmbiguousSuffixMatch_RemainsUnresolved()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        string appsDir = fake.CreateDirectory("usr/share/applications");

        // Two files ending with .editor.desktop -> ambiguous match for id "editor"
        fake.WriteFile("usr/share/applications/org.kde.editor.desktop",
            "[Desktop Entry]\nName=KDE Editor\nExec=keditor\n");
        fake.WriteFile("usr/share/applications/org.gnome.editor.desktop",
            "[Desktop Entry]\nName=GNOME Editor\nExec=geditor\n");

        fake.WriteFile(Path.Combine(appSlice, "app-editor-123.scope", "cgroup.procs"), "777\n");
        var procs = new[] { Row(777, "editor") };

        var resolver = new AppResolver(fake.Root, uid: TestUid, searchDirectories: new[] { appsDir });
        IReadOnlyList<AppEntry> apps = resolver.Read(procs);

        Assert.AreEqual(1, apps.Count);
        // Ambiguous -> stays unresolved as "editor" without picking either desktop file
        Assert.AreEqual("editor", apps[0].Name);
        Assert.IsNull(apps[0].Exec);
    }

    [TestMethod]
    public void Read_InstanceWithEmptyProcs_IsDropped()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        string appsDir = fake.CreateDirectory("usr/share/applications");

        fake.WriteFile("usr/share/applications/ghost.desktop",
            "[Desktop Entry]\nName=Ghost App\nExec=ghost\n");

        // Unit directory exists, but cgroup.procs is empty or only lists dead PIDs
        fake.WriteFile(Path.Combine(appSlice, "app-ghost-111.scope", "cgroup.procs"), "99999\n");
        var procs = new[] { Row(100, "unrelated") };

        var resolver = new AppResolver(fake.Root, uid: TestUid, searchDirectories: new[] { appsDir });
        IReadOnlyList<AppEntry> apps = resolver.Read(procs);

        // Ghost App has no live processes, so it is dropped completely
        Assert.AreEqual(0, apps.Count);
    }

    [TestMethod]
    public void Read_SumsCpuAndMemory_CpuNullOnlyWhenAllMembersNull()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        string appsDir = fake.CreateDirectory("usr/share/applications");

        fake.WriteFile("usr/share/applications/multi.desktop",
            "[Desktop Entry]\nName=Multi Proc\nExec=multi\n");

        fake.WriteFile(Path.Combine(appSlice, "app-multi-1.scope", "cgroup.procs"), "10\n20\n");
        fake.WriteFile(Path.Combine(appSlice, "app-multi-2.scope", "cgroup.procs"), "30\n");

        // Case A: mixed known and null CPU -> sum of known values
        var mixedProcs = new[]
        {
            Row(10, "m1", cpu: 5.5, memory: 100),
            Row(20, "m2", cpu: null, memory: 200),
            Row(30, "m3", cpu: 4.5, memory: 300),
        };

        var resolver = new AppResolver(fake.Root, uid: TestUid, searchDirectories: new[] { appsDir });
        IReadOnlyList<AppEntry> appsA = resolver.Read(mixedProcs);

        Assert.AreEqual(1, appsA.Count);
        Assert.AreEqual(600L, appsA[0].MemoryBytes);
        Assert.IsNotNull(appsA[0].CpuPercent);
        Assert.AreEqual(10.0, appsA[0].CpuPercent ?? double.NaN, 0.001);

        // Case B: all members have null CPU -> app CPU is null
        var allNullProcs = new[]
        {
            Row(10, "m1", cpu: null, memory: 100),
            Row(20, "m2", cpu: null, memory: 200),
            Row(30, "m3", cpu: null, memory: 300),
        };
        IReadOnlyList<AppEntry> appsB = resolver.Read(allNullProcs);
        Assert.AreEqual(1, appsB.Count);
        Assert.IsNull(appsB[0].CpuPercent, "CPU must be null when all members are null");
    }

    [TestMethod]
    public void Read_CodingOwnerNote_AppsWithIdenticalNameAndExecMerged_DifferentExecKeptSeparate()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        string appsDir = fake.CreateDirectory("usr/share/applications");

        // Two .desktop files with identical Name and Exec (like Chrome packages)
        fake.WriteFile("usr/share/applications/google-chrome.desktop",
            "[Desktop Entry]\nName=Google Chrome\nExec=chrome --opt\n");
        fake.WriteFile("usr/share/applications/com.google.Chrome.desktop",
            "[Desktop Entry]\nName=Google Chrome\nExec=chrome --opt\n");

        // A third .desktop file with same Name but DIFFERENT Exec
        fake.WriteFile("usr/share/applications/chrome-canary.desktop",
            "[Desktop Entry]\nName=Google Chrome\nExec=chrome-canary --opt\n");

        fake.WriteFile(Path.Combine(appSlice, "app-google-chrome-10.scope", "cgroup.procs"), "101\n");
        fake.WriteFile(Path.Combine(appSlice, "app-com.google.Chrome-20.scope", "cgroup.procs"), "102\n");
        fake.WriteFile(Path.Combine(appSlice, "app-chrome-canary-30.scope", "cgroup.procs"), "103\n");

        var procs = new[]
        {
            Row(101, "chrome"),
            Row(102, "chrome"),
            Row(103, "chrome-canary"),
        };

        var resolver = new AppResolver(fake.Root, uid: TestUid, searchDirectories: new[] { appsDir });
        IReadOnlyList<AppEntry> apps = resolver.Read(procs);

        // Exactly 2 apps: the first two are merged into 1 app with 2 instances, and canary stays separate!
        Assert.AreEqual(2, apps.Count);

        AppEntry stable = apps.First(a => a.Exec == "chrome --opt");
        Assert.AreEqual(2, stable.Instances.Count, "Two desktop files with identical Name and Exec must merge");

        AppEntry canary = apps.First(a => a.Exec == "chrome-canary --opt");
        Assert.AreEqual(1, canary.Instances.Count, "Desktop file with different Exec must stay separate");
    }

    [TestMethod]
    public void UnescapeUnit_DecodesUtf8HexBytes()
    {
        // \xc3\xa9 is UTF-8 encoding of 'é' (0xC3 0xA9)
        string? decoded = AppResolver.UnescapeUnit(@"caf\xc3\xa9");
        Assert.AreEqual("café", decoded);

        // Invalid UTF-8 byte sequence returns null without throwing
        string? invalid = AppResolver.UnescapeUnit(@"broken\xff\xfe");
        Assert.IsNull(invalid);
    }

    [TestMethod]
    public void ParseUnit_WithUtf8Escapes_DecodesNameOrLeavesRawWhenInvalid()
    {
        // Valid UTF-8 \xc3\xa9 -> café
        AppResolver.UnitName? unit1 = AppResolver.ParseUnit(@"app-caf\xc3\xa9-123.scope");
        Assert.IsNotNull(unit1);
        Assert.AreEqual("café", unit1.Value.Id);
        Assert.AreEqual("123", unit1.Value.Instance);
        Assert.IsFalse(unit1.Value.Service);

        // Invalid UTF-8 bytes in ID -> returns UnitName with raw text and Unresolved = true
        AppResolver.UnitName? unit2 = AppResolver.ParseUnit(@"app-broken\xff\xfe-456.scope");
        Assert.IsNotNull(unit2);
        Assert.AreEqual(@"broken\xff\xfe", unit2.Value.Id);
        Assert.AreEqual("456", unit2.Value.Instance);
        Assert.IsFalse(unit2.Value.Service);
        Assert.IsTrue(unit2.Value.Unresolved);

        // Invalid UTF-8 bytes in instance of a service -> returns UnitName with raw text and Unresolved = true
        AppResolver.UnitName? unit3 = AppResolver.ParseUnit(@"app-cafe@bad\xff.service");
        Assert.IsNotNull(unit3);
        Assert.AreEqual("cafe", unit3.Value.Id);
        Assert.AreEqual(@"bad\xff", unit3.Value.Instance);
        Assert.IsTrue(unit3.Value.Service);
        Assert.IsTrue(unit3.Value.Unresolved);
    }

    [TestMethod]
    public void Read_Utf8EscapedUnit_DecodesToUtf8_AndInvalidUtf8StaysUnresolvedWithoutThrowing()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        string appsDir = fake.CreateDirectory("usr/share/applications");

        fake.WriteFile("usr/share/applications/café.desktop",
            "[Desktop Entry]\nName=Café App\nExec=cafe\n");

        fake.WriteFile(Path.Combine(appSlice, @"app-caf\xc3\xa9-123.scope", "cgroup.procs"), "801\n");
        fake.WriteFile(Path.Combine(appSlice, @"app-broken\xff-456.scope", "cgroup.procs"), "802\n");

        var procs = new[]
        {
            Row(801, "cafe"),
            Row(802, "broken"),
        };

        var resolver = new AppResolver(fake.Root, uid: TestUid, searchDirectories: new[] { appsDir });
        IReadOnlyList<AppEntry> apps = resolver.Read(procs);

        Assert.AreEqual(2, apps.Count);

        AppEntry cafeApp = apps.First(a => a.Name == "Café App");
        Assert.AreEqual("café", cafeApp.Id);
        Assert.AreEqual("cafe", cafeApp.Exec);

        AppEntry brokenApp = apps.First(a => a.Id == @"broken\xff");
        Assert.AreEqual(@"broken\xff", brokenApp.Name);
        Assert.IsNull(brokenApp.Exec, "Invalid UTF-8 unit must stay unresolved");
    }

    [TestMethod]
    public void Read_InvalidUtf8InInstance_WithMatchingDesktopFilePresent_StaysUnresolvedWithoutThrowing()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        string appsDir = fake.CreateDirectory("usr/share/applications");

        // cafe.desktop is present
        fake.WriteFile("usr/share/applications/cafe.desktop",
            "[Desktop Entry]\nName=Cafe App\nExec=/usr/bin/cafe\nIcon=cafe\n");

        // Unit has invalid UTF-8 byte in instance name (@bad\xff)
        fake.WriteFile(Path.Combine(appSlice, @"app-cafe@bad\xff.service", "cgroup.procs"), "901\n");

        var procs = new[] { Row(901, "cafe") };

        var resolver = new AppResolver(fake.Root, uid: TestUid, searchDirectories: new[] { appsDir });
        IReadOnlyList<AppEntry> apps = resolver.Read(procs);

        Assert.AreEqual(1, apps.Count);
        AppEntry app = apps[0];
        Assert.AreEqual("cafe", app.Id);
        Assert.AreEqual("cafe", app.Name);
        Assert.IsNull(app.Exec, "Exec must be null when unit has invalid UTF-8 in instance");
        Assert.IsNull(app.DesktopFile, "DesktopFile must be null when unit is unresolved");
        Assert.IsNull(app.Icon, "Icon must be null when unit is unresolved");
    }
}
