using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SysMonitor.Native;
using SysMonitor.ViewModels;

namespace SysMonitor;

/// <summary>
/// A drive's links into Windows: double-click opens File Explorer, a right
/// click offers Explorer, Disk Cleanup and Disk Management, and a warning bar
/// on a drive that is filling up opens Disk Cleanup.
///
/// Only a drive row (overall view) and a disk tab (full view) react. Every
/// other place keeps the window's own double-click and right-click, so the
/// rest of the window behaves exactly as before.
/// </summary>
public partial class MainWindow
{
    private static MeterRow? RowOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as MeterRow;

    /// <summary>Double-click on a drive row: File Explorer, and no resize.</summary>
    private void OnMeterRowDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && RowOf(sender) is { DriveLetter: { } letter })
        {
            DiskTools.OpenExplorer(letter);
            e.Handled = true;       // the window's double-click would resize it
        }
    }

    private void OnMeterRowMenu(object sender, ContextMenuEventArgs e)
    {
        if (RowOf(sender) is { DriveLetter: { } letter } && sender is FrameworkElement target)
        {
            ShowDriveMenu(target, new[] { letter });
            e.Handled = true;
        }
    }

    /// <summary>The warning bar: open Disk Cleanup, and put the warning away.</summary>
    private void OnAlertDown(object sender, MouseButtonEventArgs e)
    {
        if (RowOf(sender) is { DriveLetter: { } letter })
        {
            DiskTools.OpenCleanup(letter);
            _model.DismissAlert(letter);
        }
        e.Handled = true;
    }

    /// <summary>The cross on the warning bar: put it away without opening anything.</summary>
    private void OnAlertDismissDown(object sender, MouseButtonEventArgs e)
    {
        if (RowOf(sender) is { DriveLetter: { } letter })
        {
            _model.DismissAlert(letter);
        }
        e.Handled = true;
    }

    /// <summary>The mini view's detail line, when it is carrying a drive warning.</summary>
    private void OnWidgetDetailDown(object sender, MouseButtonEventArgs e)
    {
        if (_model.WidgetAlertLetter is string letter)
        {
            DiskTools.OpenCleanup(letter);
            _model.DismissAlert(letter);
            e.Handled = true;
        }
    }

    private IReadOnlyList<string> LettersOf(object sender) =>
        (sender as FrameworkElement)?.Tag is string key
            ? _model.Tabs.FirstOrDefault(t => t.Key == key)?.DriveLetters
              ?? Array.Empty<string>()
            : Array.Empty<string>();

    /// <summary>
    /// Double-click on a disk tab. One drive opens straight away; a disk with
    /// several partitions asks which, with the same menu a right-click gives.
    /// </summary>
    private void OnTabDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }
        IReadOnlyList<string> letters = LettersOf(sender);
        if (letters.Count == 0 || sender is not FrameworkElement target)
        {
            return;
        }
        if (letters.Count == 1)
        {
            DiskTools.OpenExplorer(letters[0]);
        }
        else
        {
            ShowDriveMenu(target, letters);
        }
        e.Handled = true;
    }

    private void OnTabMenu(object sender, ContextMenuEventArgs e)
    {
        IReadOnlyList<string> letters = LettersOf(sender);
        if (letters.Count > 0 && sender is FrameworkElement target)
        {
            ShowDriveMenu(target, letters);
            e.Handled = true;
        }
    }

    private void ShowDriveMenu(FrameworkElement target, IReadOnlyList<string> letters)
    {
        ContextMenu menu = BuildDriveMenu(letters);
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Explorer, Disk Cleanup and Disk Management, then the window's own items.
    /// With several drives each gets a sub-menu, because "open the drive" has
    /// no meaning until you say which.
    /// </summary>
    private ContextMenu BuildDriveMenu(IReadOnlyList<string> letters)
    {
        var lang = new Lang(_config.Lang);
        var menu = new ContextMenu();

        if (letters.Count == 1)
        {
            AddDriveItems(menu.Items, letters[0] + ":", letters[0], lang);
        }
        else
        {
            foreach (string letter in letters)
            {
                var drive = new MenuItem { Header = letter + ":" };
                AddDriveItems(drive.Items, letter + ":", letter, lang);
                menu.Items.Add(drive);
            }
        }

        var management = new MenuItem { Header = lang["disk_manager"] };
        management.Click += (_, _) => DiskTools.OpenManagement();
        menu.Items.Add(management);

        menu.Items.Add(new Separator());
        FillWindowMenu(menu);
        return menu;
    }

    private static void AddDriveItems(ItemCollection items, string name, string letter, Lang lang)
    {
        var explorer = new MenuItem { Header = string.Format(lang["disk_open_explorer"], name) };
        explorer.Click += (_, _) => DiskTools.OpenExplorer(letter);
        items.Add(explorer);

        var cleanup = new MenuItem
        {
            Header = string.Format(lang["disk_cleanup"], name),
            // Disk Cleanup has nothing to free on a share or a removable stick.
            IsEnabled = Win32.IsFixedDrive(letter),
        };
        cleanup.Click += (_, _) => DiskTools.OpenCleanup(letter);
        items.Add(cleanup);
    }
}
