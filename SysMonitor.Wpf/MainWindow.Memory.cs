using System.Windows;
using System.Windows.Controls;
using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor;

/// <summary>
/// The memory tab's buttons: open a group, graph it, end a process, and stop,
/// start or restart a service or change how it starts.
///
/// Nothing here acts without a yes: ending a process loses unsaved work and
/// stopping a service can take other things down with it.
/// </summary>
public partial class MainWindow
{
    private MemoryViewModel Memory => _model.Memory;

    private static string? TagText(object sender) =>
        (sender as FrameworkElement)?.Tag?.ToString();

    private void OnMemExpand(object sender, RoutedEventArgs e)
    {
        string? name = TagText(sender);
        ProcessGroupVm? group = Memory.Groups.FirstOrDefault(g => g.Name == name);
        if (group is not null)
        {
            group.Expanded = !group.Expanded;
        }
    }

    private void OnMemPick(object sender, RoutedEventArgs e)
    {
        if (TagText(sender) is string name)
        {
            Memory.Toggle(name);
            // Show the graph now rather than at the next reading.
            _model.PushHistory(_sampler.Current);
        }
    }

    private async void OnMemKillProcess(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TagText(sender), out int pid))
        {
            return;
        }
        ProcessEntry? process = Memory.All.SelectMany(g => g.Members).FirstOrDefault(p => p.Pid == pid);
        if (process is null)
        {
            return;
        }

        var lang = new Lang(_config.Lang);
        string text = string.Format(lang["mem_confirm_kill"], process.Name, process.Pid);
        int hosted = Memory.ServicesOn(process.Pid);
        if (hosted > 0)
        {
            text += "\n\n" + string.Format(lang["mem_confirm_host"], hosted);
        }
        if (!Confirm(text, lang))
        {
            return;
        }
        await Run(MemoryTools.KillAsync(new[] { process }), lang);
    }

    private async void OnMemKillGroup(object sender, RoutedEventArgs e)
    {
        string? name = TagText(sender);
        ProcessGroup? group = Memory.All.FirstOrDefault(g => g.Name == name);
        if (group is null)
        {
            return;
        }

        var lang = new Lang(_config.Lang);
        List<ProcessEntry> targets = group.Members.Where(MemoryTools.CanKill).ToList();
        string text = string.Format(lang["mem_confirm_kill_group"], group.Name, targets.Count);
        int hosted = targets.Sum(p => Memory.ServicesOn(p.Pid));
        if (hosted > 0)
        {
            text += "\n\n" + string.Format(lang["mem_confirm_host"], hosted);
        }
        if (!Confirm(text, lang))
        {
            return;
        }
        await Run(MemoryTools.KillAsync(targets), lang);
    }

    private async void OnSvcStop(object sender, RoutedEventArgs e) =>
        await ChangeService(sender, ServiceOp.Stop, "mem_confirm_stop");

    private async void OnSvcStart(object sender, RoutedEventArgs e) =>
        await ChangeService(sender, ServiceOp.Start, "mem_confirm_start");

    private async void OnSvcRestart(object sender, RoutedEventArgs e) =>
        await ChangeService(sender, ServiceOp.Restart, "mem_confirm_restart");

    private async Task ChangeService(object sender, ServiceOp op, string confirmKey)
    {
        ServiceVm? service = Memory.Services.FirstOrDefault(s => s.Name == TagText(sender));
        if (service is null)
        {
            return;
        }
        var lang = new Lang(_config.Lang);
        if (!Confirm(string.Format(lang[confirmKey], service.Display), lang))
        {
            return;
        }
        await Run(MemoryTools.ServiceAsync(op, service.Name), lang);
    }

    /// <summary>Open the three start types under the button; picking one asks first.</summary>
    private void OnSvcStartup(object sender, RoutedEventArgs e)
    {
        ServiceVm? service = Memory.Services.FirstOrDefault(s => s.Name == TagText(sender));
        if (service is null || sender is not FrameworkElement target)
        {
            return;
        }

        var lang = new Lang(_config.Lang);
        var menu = new ContextMenu { PlacementTarget = target };
        foreach ((ServiceStart start, string key) in new[]
                 {
                     (ServiceStart.Automatic, "mem_auto"),
                     (ServiceStart.Manual, "mem_manual"),
                     (ServiceStart.Disabled, "mem_disabled"),
                 })
        {
            var item = new MenuItem
            {
                Header = lang[key],
                IsChecked = service.Entry.Start == start,
            };
            item.Click += async (_, _) =>
            {
                if (service.Entry.Start == start
                    || !Confirm(string.Format(lang["mem_confirm_startup"], service.Display, lang[key]), lang))
                {
                    return;
                }
                await Run(MemoryTools.ServiceAsync(ServiceOp.SetStart, service.Name, start), lang);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private bool Confirm(string text, Lang lang) =>
        MessageBox.Show(this, text, lang["title"], MessageBoxButton.YesNo,
                        MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>Wait for the job, say how it went, and read again soon.</summary>
    private async Task Run(Task<OpResult> job, Lang lang)
    {
        Memory.Status = string.Empty;
        OpResult result = await job;
        Memory.Status = result.Ok ? string.Empty
            : result.Cancelled ? lang["mem_cancelled"]
            : string.Format(lang["mem_failed"], result.Message);
        _sampler.RefreshMemory();
    }
}
