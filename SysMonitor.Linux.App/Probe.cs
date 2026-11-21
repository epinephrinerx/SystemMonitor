using System.Text;
using Avalonia;
using Avalonia.Platform;
using SkiaSharp;
using SysMonitor.Linux;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sampling;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.App;

/// <summary>
/// <c>--probe-ui</c>: take two real samples, build the same view models the
/// window draws, print them as text and exit. It is the acceptance check that
/// every displayed number comes from the sampler, and it runs without a display.
/// The second sample reads processes and services so the memory tab can be
/// printed too; both are read-only.
/// </summary>
internal static class Probe
{
    public static int Run()
    {
        Snapshot snapshot;
        Snapshot first;
        try
        {
            var sampler = new LinuxSampler();
            first = sampler.Sample(includeProcesses: false, includeGrid: true);
            Thread.Sleep(700);
            snapshot = sampler.Sample(
                includeProcesses: true, includeServices: true, keepMemory: false, includeGrid: true);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("probe: sampling failed: " + exception.Message);
            return 1;
        }

        // Defaults, not the user's file: the report must be reproducible.
        var shell = new ShellModel(
            new AppConfig(),
            ownUid: Posix.Uid,
            killTargets: new ProcessSignals());
        shell.SetSnapshot(snapshot);

        var text = new StringBuilder();
        text.AppendLine("System Monitor 4.3.0 -- --probe-ui");
        text.AppendLine(
            $"first sample ready={first.Ready}  second sample ready={snapshot.Ready}");
        text.AppendLine(
            $"cores={snapshot.Cores.Count}  ram={snapshot.Ram.TotalGb:F1} GB  " +
            $"disks={snapshot.Disks.Count}  adapters={snapshot.Adapters.Count}  " +
            $"gpus={snapshot.Gpus.Count}  batteries={snapshot.Batteries.Count}  " +
            $"peripherals={snapshot.Peripherals.Count}  cpu={snapshot.CpuTotal}%  " +
            $"cpu_temp={(snapshot.CpuTemp is { } t ? t + "°C" : "n/a")}  " +
            $"processes={snapshot.Processes.Count}  services={snapshot.Services.Count}");
        text.AppendLine();

        DescribeFonts(text);

        text.AppendLine($"Widget pages ({shell.WidgetPages.Count})");
        for (int i = 0; i < shell.WidgetPages.Count; i++)
        {
            WidgetPage page = shell.WidgetPages[i];
            text.AppendLine(
                $"  {i + 1}. [{MaskKey(page.Key)}] {page.Kind}  " +
                $"title=\"{Privacy.MaskInterface(page.Title)}\"");
            if (page.Message.Length > 0)
            {
                text.AppendLine($"     message  \"{page.Message}\"");
            }
            if (page.Value.Length > 0)
            {
                text.AppendLine($"     value    {page.Value}");
            }
            if (page.Detail.Length > 0)
            {
                text.AppendLine($"     detail   \"{page.Detail}\"");
            }
            if (page.Badge is { } badge)
            {
                text.AppendLine($"     badge    {Describe(badge)}");
            }
            if (page.Alert is { } alert)
            {
                text.AppendLine($"     ALERT    {alert.Level} {alert.Usage}% \"{alert.Text(shell.Language)}\"");
            }
            if (page.DriveKey is { } drive)
            {
                text.AppendLine($"     drive    {drive}");
            }
            if (page.Rows.Count > 0)
            {
                text.AppendLine($"     columns  {page.Columns}");
                foreach (MeterRow row in page.Rows)
                {
                    text.AppendLine("     row      " + Describe(row));
                }
            }
            text.AppendLine();
        }

        text.AppendLine($"Overall sections ({shell.Overall.Sections.Count})");
        foreach (SectionModel section in shell.Overall.Sections)
        {
            text.AppendLine(
                $"  [{section.Key}] \"{section.Title}\"  columns={section.Columns}" +
                (section.Note.Length > 0 ? $"  note=\"{section.Note}\"" : string.Empty));
            foreach (MeterRow row in section.Rows)
            {
                text.AppendLine("     - " + Describe(row));
            }
            text.AppendLine();
        }

        if (shell.Overall.Message.Length > 0)
        {
            text.AppendLine($"Overall message: \"{shell.Overall.Message}\"");
        }

        DescribeMain(text, shell);
        DescribeMainView(text, shell);
        DescribeApps(text, shell);
        DescribeMemory(text, shell, snapshot);
        DescribeIntegrations(text, shell, snapshot);

        text.AppendLine($"Legend: \"{shell.Overall.Legend}\"");
        text.AppendLine(
            $"Histories: cpu={shell.History.Cpu.Count}/{shell.History.Cpu.Capacity} " +
            $"ram={shell.History.Ram.Count}/{shell.History.Ram.Capacity} " +
            $"gpu={shell.History.Gpu.Count}/{shell.History.Gpu.Capacity} " +
            $"disks={shell.History.Disks.Count} adapters={shell.History.Adapters.Count} " +
            $"cores={shell.History.Cores.Count} " +
            $"process_groups={shell.History.ProcessGroups.Count}");
        text.AppendLine($"Theme: {shell.Config.Theme}  lang: {shell.Language.Code}  " +
                        $"opacity: {ShellModel.Readout(shell.Config.Opacity)}  " +
                        $"font: {ShellModel.Readout(shell.Config.FontScale)}");

        Console.Out.Write(text.ToString());
        Console.Out.Flush();
        return snapshot.Ready ? 0 : 2;
    }

    /// <summary>
    /// The bundled UI font (spec section 1): the family the windows resolve to,
    /// the system fallbacks behind it, and every embedded face as the same Skia
    /// build the renderer uses reads it. The bytes come from the assembly's
    /// avares resources, so a font dropped from the build, renamed, or shipped
    /// at the wrong weight is visible here without opening a window.
    /// </summary>
    private static void DescribeFonts(StringBuilder text)
    {
        text.AppendLine(
            $"Fonts: family=\"{Ui.Family.Name}\"  " +
            $"fallbacks=\"{string.Join(", ", Ui.Family.FamilyNames.Skip(1))}\"");
        text.AppendLine($"  stack {Ui.FontStack}");

        var loader = new StandardAssetLoader(typeof(Probe).Assembly);
        foreach ((string file, int weight) in Ui.Faces)
        {
            string head = $"  face {file}";
            try
            {
                using Stream stream = loader.Open(new Uri(Ui.FontFolder + "/" + file));
                using SKTypeface? typeface = SKTypeface.FromStream(stream);
                if (typeface is null)
                {
                    text.AppendLine(head + "  unreadable (Skia returned no typeface)");
                    continue;
                }

                string missing = string.Concat(Glyphs.Where(c => typeface.GetGlyph(c) == 0));
                text.AppendLine(
                    head +
                    $"  family=\"{typeface.FamilyName}\"" +
                    $"  weight={typeface.FontWeight}" +
                    (typeface.FontWeight == weight ? " (ok)" : $" (EXPECTED {weight})") +
                    $"  fallback glyphs={(missing.Length > 0 ? "\"" + missing + "\"" : "none")}");
            }
            catch (Exception exception)
            {
                text.AppendLine(head + "  unreadable: " + exception.Message);
            }
        }

        text.AppendLine();
    }

    /// <summary>
    /// The characters the window draws as text rather than as a path icon:
    /// the arrows and separators in the readouts, the tilde of an estimated
    /// temperature, the warning sign on a disk alert, and Thai letters with a
    /// mark. Anything the bundled face lacks must come from the fallback list.
    /// The on-AC marker is not here: it is the power icon, not a text glyph
    /// (R-030 fix 4).
    /// </summary>
    private const string Glyphs = "↓↑°·…≈~⚠▲ก์";

    /// <summary>
    /// The Main window's models: the sidebar's module items and, for every
    /// module page, its label, options, Usage charts with their peaks, Sensors
    /// and Properties. No window is created.
    /// </summary>
    private static void DescribeMain(StringBuilder text, ShellModel shell)
    {
        FullModel full = shell.Full;
        text.AppendLine(
            $"Main page={shell.Main.Page}  settings_open={shell.Main.SettingsOpen}  " +
            $"sidebar_width={shell.Main.SidebarWidth}  full_screen={shell.MainFullScreen}");
        DescribeSidebar(text, shell);

        text.AppendLine($"Module pages ({full.Tabs.Count})  selected={full.SelectedKey ?? "(none)"}");
        foreach (FullTab tab in full.Tabs)
        {
            text.AppendLine(
                $"  [{MaskKey(tab.Key)}] label=\"{tab.Label}\" " +
                $"title=\"{Privacy.MaskInterface(tab.Title)}\"" +
                (tab.IsDisk ? "  (disk)" : string.Empty) +
                (tab.Badge is { } tabBadge ? $"  badge={Describe(tabBadge)}" : string.Empty));
            text.AppendLine($"     rail detail  \"{tab.Detail}\"");
            text.AppendLine($"     rail summary \"{tab.Summary}\"");
            text.AppendLine($"     hardware     \"{tab.Hardware}\"");
            if (tab.Options.Count > 0)
            {
                text.AppendLine(
                    "     options  " + string.Join("  ", tab.Options.Select(option =>
                        $"{option.ConfigKey}={option.LabelKey}:{(option.Value ? "on" : "off")}")));
            }
            foreach (FullCard card in tab.BigCards)
            {
                text.AppendLine("     usage  " + Describe(card));
            }
            foreach (FullCard card in tab.SmallCards)
            {
                text.AppendLine("     small  " + Describe(card));
            }
            foreach (FullCard card in tab.Sensors)
            {
                text.AppendLine("     sensor " + Describe(card));
            }
            foreach (FullFact fact in tab.Properties)
            {
                text.AppendLine($"     property  {fact.Title} = \"{FactValue(fact, shell)}\"");
            }
            text.AppendLine();
        }
    }

    /// <summary>
    /// The Main window's view (R-030 B2): what the sidebar and the selected
    /// page are made of. The module items and section titles are derived from
    /// the same models the view draws, so the report is complete even when a
    /// control tree cannot be built without the Avalonia platform.
    /// </summary>
    private static void DescribeMainView(StringBuilder text, ShellModel shell)
    {
        string page = shell.Main.Page;
        text.AppendLine("Main view (R-030 B2)");
        text.AppendLine(
            $"  sidebar_width={shell.Main.SidebarWidth}  settings_open={shell.Main.SettingsOpen}  " +
            $"page={MaskKey(page)}  full_screen={shell.MainFullScreen}");

        text.AppendLine("  nav:");
        // The real sidebar order: the three pages, then the three actions that
        // sit under Processes and before the Settings expander (R-030 fix 5).
        foreach ((string key, string label, bool action) in new[]
                 {
                     (PageKeys.Overall, shell.Language["overall"], false),
                     (PageKeys.Apps, shell.Language["apps"], false),
                     (PageKeys.Processes, shell.Language["processes"], false),
                     (ActionKeys.Restart, shell.Language["restart"], true),
                     (ActionKeys.OpenManual, shell.Language["manual"], true),
                     (ActionKeys.OpenAbout, shell.Language["about"], true),
                 })
        {
            string state = action ? "  (action)" : key == page ? "  (selected)" : string.Empty;
            text.AppendLine($"    [{key}] \"{label}\"" + state);
        }

        text.AppendLine($"  module items ({shell.MainSidebar.Count}):");
        int number = 1;
        foreach (SidebarItem item in shell.MainSidebar)
        {
            text.AppendLine(
                $"    {number}. [{MaskKey(item.Key)}] title=\"{MaskName(item.Title)}\"  " +
                $"value=\"{item.Value}\"  device=\"{Privacy.MaskInterface(item.Device)}\"  " +
                $"accent={item.Accent.ToHex()}  points={item.Primary.Count}" +
                (item.Secondary is null ? string.Empty : $" +{item.Secondary.Count}"));
            number++;
        }

        text.AppendLine("  right frame sections:");
        foreach (string title in MainSectionTitles(shell))
        {
            text.AppendLine("    " + title);
        }

        // A control tree needs Avalonia's platform (fonts and cursors), and the
        // probe runs before any application exists. It still reports what the
        // view is made of, from the same models the view draws.
        if (Application.Current is null)
        {
            text.AppendLine("  view: model only (the probe runs without an Avalonia application)");
        }
        else
        {
            try
            {
                var view = new MainView(shell, _ => { });
                view.Render();
                text.AppendLine(
                    $"  view built: nav={view.NavCount}  module_items={view.ModuleItemCount}  " +
                    $"settings_open={view.SettingsOpen}");
            }
            catch (Exception exception)
            {
                text.AppendLine(
                    "  view not built: " + exception.GetType().Name + ": " + exception.Message);
            }
        }
        text.AppendLine();
    }

    /// <summary>
    /// The Apps page's own report (R-030 phase C part 1): the header the page
    /// draws, the saved sort, and the top five apps by memory. No command line
    /// and no desktop file path is printed.
    /// </summary>
    private static void DescribeApps(StringBuilder text, ShellModel shell)
    {
        IReadOnlyList<AppEntry> apps = shell.Snapshot?.Apps ?? Array.Empty<AppEntry>();
        GridColumn column = ProcessGrid.ParseColumn(shell.Config.AppsSort, GridColumn.Memory);

        text.AppendLine("Apps page (R-030 phase C part 1)");
        text.AppendLine($"  heading \"{shell.Language["apps"]}\"  {shell.Language.Get("apps_count", apps.Count)}");
        text.AppendLine(
            "  columns " + string.Join(
                " · ",
                new[]
                {
                    shell.Language["col_name"],
                    shell.Language["col_instances"],
                    shell.Language["col_cpu"],
                    shell.Language["col_memory"],
                    shell.Language["col_gpu"],
                    shell.Language["col_gpu_memory"],
                }));
        text.AppendLine(
            $"  search \"{shell.Language["apps_search"]}\"  " +
            $"empty \"{shell.Language["apps_empty"]}\"  " +
            $"sort={ProcessGrid.Key(column)} desc={shell.Config.AppsSortDesc}");

        IReadOnlyList<AppEntry> top = ProcessGrid.Sort(apps, GridColumn.Memory, descending: true);
        int shown = Math.Min(5, top.Count);
        text.AppendLine($"  top {shown} by memory:");
        foreach (AppEntry app in top.Take(5))
        {
            text.AppendLine(
                $"    {app.Name}  instances={app.Instances.Count}  " +
                $"cpu={Percent(app.CpuPercent)}  memory={MemoryBoard.Size(app.MemoryBytes)}  " +
                $"gpu={Percent(app.GpuPercent)}  " +
                $"gpu_memory={BytesOrDash(app.GpuMemoryBytes)}");
        }
        if (top.Count == 0)
        {
            text.AppendLine("    N/A");
        }
        text.AppendLine();
    }

    private static string Percent(double? value) =>
        value is double percent ? $"{percent:F1} %" : "—";

    private static string BytesOrDash(long? bytes) =>
        bytes is long value ? MemoryBoard.Size(value) : "—";

    /// <summary>The section titles the selected page shows, in page order.</summary>
    private static IReadOnlyList<string> MainSectionTitles(ShellModel shell)
    {
        var titles = new List<string>();
        string page = shell.Main.Page;

        if (string.Equals(page, PageKeys.Overall, StringComparison.Ordinal))
        {
            titles.AddRange(shell.Overall.Sections.Select(section => section.Title));
            return titles;
        }
        if (string.Equals(page, PageKeys.Apps, StringComparison.Ordinal))
        {
            titles.Add(shell.Language["apps"]);
            titles.Add(shell.Language.Get("apps_count", shell.Snapshot?.Apps.Count ?? 0));
            return titles;
        }
        if (string.Equals(page, PageKeys.Processes, StringComparison.Ordinal))
        {
            titles.Add(shell.Language["mem_processes"]);
            titles.Add(shell.Language["mem_services"]);
            return titles;
        }

        FullTab? tab = shell.Full.Selected;
        if (tab is null)
        {
            return titles;
        }
        titles.Add(shell.Language["usage"]);
        if (tab.Sensors.Count > 0)
        {
            titles.Add(shell.Language["sensors"]);
        }
        if (tab.Properties.Count > 0)
        {
            titles.Add(shell.Language["properties"]);
        }
        return titles;
    }

    /// <summary>The sidebar's items, with the probe's privacy mask on names.</summary>
    private static void DescribeSidebar(StringBuilder text, ShellModel shell)
    {
        text.AppendLine($"Main sidebar ({shell.MainSidebar.Count})");
        foreach (SidebarItem item in shell.MainSidebar)
        {
            text.AppendLine(
                $"  [{MaskKey(item.Key)}] title=\"{MaskName(item.Title)}\"  value=\"{item.Value}\"  " +
                $"device=\"{Privacy.MaskInterface(item.Device)}\"  accent={item.Accent.ToHex()}  " +
                $"points={item.Primary.Count}" +
                (item.Secondary is null ? string.Empty : $" +{item.Secondary.Count}") +
                (item.Badge is { } badge ? $"  badge={Describe(badge)}" : string.Empty));
        }
        text.AppendLine();
    }

    /// <summary>
    /// A value the probe must not print: the Wi-Fi network name is kept in the
    /// model and shown in the window, but a report must not carry it. An
    /// interface named after its MAC address is masked wherever it appears.
    /// </summary>
    private static string FactValue(FullFact fact, ShellModel shell)
    {
        if (fact.Private
            && fact.Value.Length > 0
            && !string.Equals(fact.Value, shell.Language["na"], StringComparison.Ordinal))
        {
            return "(hidden)";
        }
        return Privacy.MaskInterface(fact.Value);
    }

    /// <summary>Mask the device part of "Wi-Fi · enx&lt;MAC&gt;".</summary>
    private static string MaskName(string text)
    {
        int separator = text.LastIndexOf(" · ", StringComparison.Ordinal);
        return separator < 0
            ? Privacy.MaskInterface(text)
            : text[..(separator + 3)] + Privacy.MaskInterface(text[(separator + 3)..]);
    }

    /// <summary>
    /// A page or card key whose device part is an <c>enx&lt;MAC&gt;</c> name:
    /// "net&lt;name&gt;" and "net&lt;name&gt;:down" keep their shape, the name is
    /// masked.
    /// </summary>
    private static string MaskKey(string key)
    {
        if (key.StartsWith("net:", StringComparison.Ordinal))
        {
            return "net:" + Privacy.MaskInterface(key[4..]);
        }
        if (!key.StartsWith("net", StringComparison.Ordinal))
        {
            return key;
        }
        int colon = key.IndexOf(':', 3);
        string id = colon < 0 ? key[3..] : key[3..colon];
        string suffix = colon < 0 ? string.Empty : key[colon..];
        return "net" + Privacy.MaskInterface(id) + suffix;
    }

    /// <summary>The top process groups and the first services of each scope.</summary>
    private static void DescribeMemory(StringBuilder text, ShellModel shell, Snapshot snapshot)
    {
        MemoryTabModel memory = shell.Memory;
        text.AppendLine($"Memory: process groups shown={memory.Groups.Count}");
        foreach (MemProcessGroup group in memory.Groups.Take(10))
        {
            text.AppendLine(
                $"  {group.Name}  size={group.SizeText}  count={group.Count}  " +
                $"killable={(group.CanKill ? "yes" : "no (protected)")}  " +
                $"graphed={(group.Graphed ? "yes" : "no")}");
            foreach (MemProcess member in group.Members.Take(3))
            {
                text.AppendLine(
                    $"     pid={member.Pid}  size={member.SizeText}  private={member.PrivateText(shell.Language)}  " +
                    $"protected={(member.Protected ? "yes" : "no")}");
            }
        }
        if (memory.GroupNote.Length > 0)
        {
            text.AppendLine("  note: " + memory.GroupNote);
        }

        int userTotal = snapshot.Services.Count(s => s.UserUnit);
        int systemTotal = snapshot.Services.Count - userTotal;
        text.AppendLine(
            $"Services: user={userTotal} (shown {memory.UserServices.Count})  " +
            $"system={systemTotal} (shown {memory.SystemServices.Count})");
        foreach (MemService service in memory.UserServices.Take(5))
        {
            text.AppendLine("  USER   " + Describe(service, shell));
        }
        foreach (MemService service in memory.SystemServices.Take(5))
        {
            text.AppendLine("  SYSTEM " + Describe(service, shell));
        }
        if (memory.ServiceNote.Length > 0)
        {
            text.AppendLine("  note: " + memory.ServiceNote);
        }
        text.AppendLine();
    }

    /// <summary>The running version, as three parts, the way the updater reads tags.</summary>
    private static string Version =>
        typeof(Probe).Assembly.GetName().Version?.ToString(3) ?? "4.3.0";

    /// <summary>
    /// The phase-3 integrations (spec sections 8 and 9): what autostart would
    /// write without writing it, the tray tooltip for this snapshot, and the
    /// Linux update asset names. The autostart directory is only read.
    /// </summary>
    private static void DescribeIntegrations(StringBuilder text, ShellModel shell, Snapshot snapshot)
    {
        var autostart = new FileAutostart();
        text.AppendLine(
            $"Autostart: enabled={autostart.IsEnabled}  can_enable={autostart.CanEnable}  " +
            $"file={autostart.Path}  exe={autostart.Executable}");
        if (autostart.CanEnable)
        {
            text.AppendLine("  would write:");
            foreach (string line in autostart.Text.TrimEnd('\n').Split('\n'))
            {
                text.AppendLine("    " + line);
            }
        }
        else
        {
            text.AppendLine(
                "  would write: nothing (the executable path has a control character)");
        }

        text.AppendLine("Tray tooltip: " + Escape(TrayText.Build(snapshot, shell.Language)));

        string architecture = UpdateAssets.ArchitectureSuffix();
        bool package = DebPackage.IsInstalled(
            Environment.ProcessPath, new ProcessCommandRunner());
        UpdateAssetSet assets = package
            ? UpdateAssetSet.Deb()
            : UpdateAssetSet.TarGz(architecture);
        (string file, string checksum) = assets.Names(Version);
        text.AppendLine($"Update assets ({assets.Describe()}): {file} + {checksum}");
        text.AppendLine();
    }

    /// <summary>One line, with the newline visible: a tooltip is two lines.</summary>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("\n", "\\n");

    /// <summary>
    /// <c>--probe-update</c>: one real GitHub check (GET only, no download) and
    /// the parsed result on stdout. It prints the tag it picked and why, and
    /// the files it would install, because which of them is picked is the whole
    /// point of the Linux release rule. The log lines go to stderr.
    /// </summary>
    public static int RunUpdate()
    {
        string version = Version;
        string architecture = UpdateAssets.ArchitectureSuffix();
        bool package = DebPackage.IsInstalled(
            Environment.ProcessPath, new ProcessCommandRunner());
        UpdateAssetSet assets = package
            ? UpdateAssetSet.Deb()
            : UpdateAssetSet.TarGz(architecture);
        (string file, string checksum) = assets.Names(version);

        var updater = new GitHubUpdater(
            version,
            architecture: architecture,
            log: line => Console.Error.WriteLine("probe-update: " + line));
        UpdateCheck result = updater.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();

        Console.WriteLine($"System Monitor {version} -- --probe-update");
        Console.WriteLine($"endpoint {UpdateReleaseParser.Endpoint}");
        Console.WriteLine(
            $"install {(updater.DebInstall ? "deb package (systemmonitor)" : "tar.gz (per-user)")}  " +
            $"arch {architecture}");
        Console.WriteLine($"assets {file} + {checksum}");
        if (!result.Succeeded)
        {
            Console.WriteLine($"result check_failed  reason={result.Reason}");
        }
        else if (result.Version is null)
        {
            Console.WriteLine($"result up_to_date  {result.Detail}");
        }
        else
        {
            Console.WriteLine(
                $"result ok  tag={result.Tag}  version={result.Version}  {result.Detail}");
        }
        return 0;
    }

    private static string Describe(MemService service, ShellModel shell) =>
        $"{service.Name}  \"{service.Display}\"  " +
        $"state={service.StateText(shell.Language)}  startup={service.StartupText(shell.Language)}  " +
        $"pid={service.Pid}  size={service.SizeText}  shared={service.SharedWith}  " +
        $"scope={(service.UserUnit ? "user" : "system")}";

    private static string Describe(FullCard card) =>
        $"key={MaskKey(card.Key)}  title=\"{card.Title}\"  value=\"{card.Value}\"  " +
        $"peak=\"{card.Peak}\"  detail=\"{card.Detail}\"  unit=\"{card.Unit}\"  " +
        $"ceiling=\"{card.Ceiling}\"  accent={card.Accent.ToHex()}  max={card.Maximum}  " +
        $"points={card.History.Count}";

    /// <summary>
    /// One badge: its kind first, because a power badge deliberately has no text
    /// and must not be mistaken for an empty temperature badge.
    /// </summary>
    private static string Describe(TempBadge badge) => badge.Kind switch
    {
        BadgeKind.Power => "Power (icon only)",
        _ => $"Temperature {badge.Text}" +
             (badge.Hot ? " (hot, pulses)" : string.Empty) +
             (badge.Estimated ? " (estimated)" : string.Empty),
    };

    /// <summary>One meter row, with every field the view renders.</summary>
    private static string Describe(MeterRow row)
    {
        var text = new StringBuilder();
        text.Append(row.Title.Length > 0 ? Privacy.MaskInterface(row.Title) : "(no label)");
        text.Append("  ").Append(row.Percent).Append('%');
        if (!string.IsNullOrEmpty(row.ValueText))
        {
            text.Append("  value=").Append(row.ValueText);
        }
        text.Append("  bar ").Append(row.BarColour.ToHex());
        if (row.Compact)
        {
            text.Append("  compact");
        }
        if (!row.ShowValue)
        {
            text.Append("  no-value");
        }
        if (row.Detail.Length > 0)
        {
            text.Append("  detail=\"").Append(row.Detail).Append('"');
        }
        if (row.Badge is { } badge)
        {
            text.Append("  badge=").Append(Describe(badge));
        }
        if (row.Alert is { } alert)
        {
            text.Append("  ALERT=").Append(alert.Level).Append(' ').Append(alert.Usage).Append('%');
        }
        if (row.DriveKey is { } drive)
        {
            text.Append("  drive=").Append(drive);
        }
        return text.ToString();
    }
}
