using System.Text;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sampling;
using SysMonitor.Model;

namespace SysMonitor.Linux.App;

/// <summary>
/// <c>--probe-ui</c>: take two real samples, build the same view models the
/// window draws, print them as text and exit. It is the acceptance check that
/// every displayed number comes from the sampler, and it runs without a display.
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
            first = sampler.Sample(includeProcesses: false);
            Thread.Sleep(700);
            snapshot = sampler.Sample(includeProcesses: false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("probe: sampling failed: " + exception.Message);
            return 1;
        }

        // Defaults, not the user's file: the report must be reproducible.
        var shell = new ShellModel(new AppConfig());
        shell.SetSnapshot(snapshot);

        var text = new StringBuilder();
        text.AppendLine("System Monitor 4.3.0 -- --probe-ui");
        text.AppendLine(
            $"first sample ready={first.Ready}  second sample ready={snapshot.Ready}");
        text.AppendLine(
            $"cores={snapshot.Cores.Count}  ram={snapshot.Ram.TotalGb:F1} GB  " +
            $"disks={snapshot.Disks.Count}  adapters={snapshot.Adapters.Count}  " +
            $"gpus={snapshot.Gpus.Count}  cpu={snapshot.CpuTotal}%  " +
            $"cpu_temp={(snapshot.CpuTemp is { } t ? t + "°C" : "n/a")}");
        text.AppendLine();

        text.AppendLine($"Widget pages ({shell.WidgetPages.Count})");
        for (int i = 0; i < shell.WidgetPages.Count; i++)
        {
            WidgetPage page = shell.WidgetPages[i];
            text.AppendLine($"  {i + 1}. [{page.Key}] {page.Kind}  title=\"{page.Title}\"");
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
                text.AppendLine(
                    $"     temp     {badge.Text}" +
                    (badge.Hot ? " (hot, pulses)" : string.Empty) +
                    (badge.Estimated ? " (estimated)" : string.Empty));
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

        text.AppendLine($"Legend: \"{shell.Overall.Legend}\"");
        text.AppendLine(
            $"Histories: cpu={shell.History.Cpu.Count}/{shell.History.Cpu.Capacity} " +
            $"ram={shell.History.Ram.Count}/{shell.History.Ram.Capacity} " +
            $"gpu={shell.History.Gpu.Count}/{shell.History.Gpu.Capacity} " +
            $"disks={shell.History.Disks.Count} adapters={shell.History.Adapters.Count}");
        text.AppendLine($"Theme: {shell.Config.Theme}  lang: {shell.Language.Code}  " +
                        $"opacity: {ShellModel.Readout(shell.Config.Opacity)}  " +
                        $"font: {ShellModel.Readout(shell.Config.FontScale)}");

        Console.Out.Write(text.ToString());
        Console.Out.Flush();
        return snapshot.Ready ? 0 : 2;
    }

    /// <summary>One meter row, with every field the view renders.</summary>
    private static string Describe(MeterRow row)
    {
        var text = new StringBuilder();
        text.Append(row.Title.Length > 0 ? row.Title : "(no label)");
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
            text.Append("  badge=").Append(badge.Text);
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
