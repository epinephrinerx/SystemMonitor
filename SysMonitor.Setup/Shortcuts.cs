using System.IO;

namespace SysMonitor.Setup;

/// <summary>
/// Desktop and Start menu shortcuts.
///
/// Through WScript.Shell by late binding rather than by declaring IShellLink:
/// the COM object is on every Windows install, and this is a dozen lines
/// instead of an interface definition nobody will ever read again.
/// </summary>
internal static class Shortcuts
{
    /// <summary>Create the shortcut when wanted, remove it when not.</summary>
    public static void Write(bool wanted, string linkPath, string target, string workingDir)
    {
        if (!wanted)
        {
            Remove(linkPath);
            return;
        }
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }
            object? shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return;
            }

            object? link = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell,
                new object[] { linkPath });
            if (link is null)
            {
                return;
            }

            Set(link, "TargetPath", target);
            Set(link, "WorkingDirectory", workingDir);
            Set(link, "IconLocation", target);
            Set(link, "Description", Installer.DisplayName);
            link.GetType().InvokeMember("Save",
                System.Reflection.BindingFlags.InvokeMethod, null, link, null);
        }
        catch (Exception)
        {
            // A missing shortcut is a cosmetic failure, not an install failure.
        }
    }

    private static void Set(object link, string property, string value) =>
        link.GetType().InvokeMember(property,
            System.Reflection.BindingFlags.SetProperty, null, link, new object[] { value });

    private static void Remove(string linkPath)
    {
        try
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }
        }
        catch (Exception)
        {
            // Leave it; it points at an executable that is about to vanish.
        }
    }
}
