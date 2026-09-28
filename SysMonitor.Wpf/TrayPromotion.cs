using Microsoft.Win32;

namespace SysMonitor;

/// <summary>
/// Ask Windows 11 to show our notification icon on the taskbar instead of
/// filing it behind the overflow arrow.
///
/// Every icon Windows 11 has not seen before goes into the hidden flyout, and
/// stays there until the user drags it out. For an icon whose entire job is
/// being the way back to a window that has just disappeared, that is the one
/// place it must not be: the widget vanishes and nothing visible replaced it.
///
/// The shell records the choice per icon under
/// `HKCU\Control Panel\NotifyIconSettings`, keyed by a hash it computes
/// itself, so the entry has to be found by the executable path it carries
/// rather than by a name we can predict. It appears only after the icon has
/// been registered at least once, which is why this runs after the icon is
/// shown and quietly does nothing when the entry is not there yet.
///
/// Done once and remembered. Forcing the flag on every run would overrule a
/// user who had deliberately dragged the icon back into the flyout, and a
/// program that keeps undoing what you just did is worse than one that never
/// helped.
/// </summary>
internal static class TrayPromotion
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    /// <summary>
    /// Promote our icon if the shell knows about it. Returns true once it has
    /// been done, so the caller can stop asking.
    /// </summary>
    public static bool Promote(string exePath)
    {
        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
            if (root is null)
            {
                return false;       // older Windows: the flyout works differently
            }

            bool promoted = false;
            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? entry = root.OpenSubKey(name, writable: true);
                if (entry?.GetValue("ExecutablePath") is not string path
                    || !path.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                promoted = true;
            }
            return promoted;
        }
        catch (Exception error)
        {
            // A locked-down profile may refuse the write. The icon still works;
            // it just sits in the flyout.
            Diag.ReportException("Tray promotion", error);
            return false;
        }
    }
}
