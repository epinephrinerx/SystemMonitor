using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SysMonitor.Extensions;

/// <summary>
/// Enforces and verifies that file system access control lists (ACLs) permit write access
/// solely to Administrators and SYSTEM accounts, verifies object ownership, and traverses
/// ancestors to prevent reparse point or deletion attacks.
/// </summary>
public static class AclPolicy
{
    public static readonly SecurityIdentifier AdminSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static readonly SecurityIdentifier SystemSid =
        new(WellKnownSidType.LocalSystemSid, null);

    /// <summary>
    /// Pure write and modification rights. Composite rights like Write, Modify, FullControl are excluded
    /// because their bitmasks contain read and synchronize bits that collide with standard read permissions.
    /// </summary>
    public const FileSystemRights WriteOrModifyRights =
        FileSystemRights.WriteData |
        FileSystemRights.CreateFiles |
        FileSystemRights.AppendData |
        FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.WriteAttributes |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership;

    /// <summary>
    /// Forbidden rights on ancestor directories: non-admin accounts must not be able to delete,
    /// take ownership, or change permissions on any parent folder up to the drive root.
    /// (CreateFiles/AppendData are permitted on ancestors so C:\ProgramData remains standard).
    /// </summary>
    public const FileSystemRights AncestorForbiddenRights =
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.Delete |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership;

    /// <summary>
    /// Checks whether the security descriptor indicates ownership by BUILTIN\Administrators or SYSTEM.
    /// </summary>
    public static bool IsAdminOrSystemOwner(FileSystemSecurity security)
    {
        try
        {
            IdentityReference? ownerRef = security.GetOwner(typeof(SecurityIdentifier));
            if (ownerRef is SecurityIdentifier sid)
            {
                return sid.Equals(AdminSid) || sid.Equals(SystemSid);
            }
            else if (ownerRef != null)
            {
                var translated = (SecurityIdentifier)ownerRef.Translate(typeof(SecurityIdentifier));
                return translated.Equals(AdminSid) || translated.Equals(SystemSid);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies that no non-admin/SYSTEM account has any of the specified forbidden rights.
    /// When evaluating a directory, rules with PropagationFlags.InheritOnly are ignored because
    /// they do not apply to the directory itself.
    /// </summary>
    public static bool CheckSecurityDescriptor(
        CommonObjectSecurity security,
        FileSystemRights forbiddenRights,
        bool isDirectory)
    {
        AuthorizationRuleCollection rules;
        try
        {
            rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier));
        }
        catch
        {
            return false;
        }

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == AccessControlType.Allow)
            {
                // InheritOnly rules apply only to child items, not to the directory itself
                if (isDirectory && rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly))
                {
                    continue;
                }

                if ((rule.FileSystemRights & forbiddenRights) != 0)
                {
                    if (rule.IdentityReference is SecurityIdentifier sid)
                    {
                        if (!sid.Equals(AdminSid) && !sid.Equals(SystemSid))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        try
                        {
                            var translated = (SecurityIdentifier)rule.IdentityReference.Translate(typeof(SecurityIdentifier));
                            if (!translated.Equals(AdminSid) && !translated.Equals(SystemSid))
                            {
                                return false;
                            }
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Checks that the security descriptor has write/modify permissions restricted to Administrators and SYSTEM.
    /// </summary>
    public static bool CheckSecurity(FileSystemSecurity security, bool isDirectory = false)
    {
        return CheckSecurityDescriptor(security, WriteOrModifyRights, isDirectory);
    }

    /// <summary>
    /// Checks that an ancestor directory does not grant delete, take ownership, or change permissions
    /// to standard accounts, respecting InheritOnly.
    /// </summary>
    public static bool CheckAncestorSecurity(DirectorySecurity security)
    {
        // The owner of an object can always rewrite its DACL, so an ancestor owned by a
        // standard user is replaceable whatever its ACEs say.
        return IsTrustedAncestorOwner(security)
               && CheckSecurityDescriptor(security, AncestorForbiddenRights, isDirectory: true);
    }

    private static readonly SecurityIdentifier TrustedInstallerSid =
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    /// <summary>System folders such as the drive root are owned by TrustedInstaller.</summary>
    private static bool IsTrustedAncestorOwner(FileSystemSecurity security)
    {
        try
        {
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            return owner != null
                   && (owner.Equals(AdminSid) || owner.Equals(SystemSid) || owner.Equals(TrustedInstallerSid));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Combined check: owner must be Admin/SYSTEM and ACL must be admin-only writable.
    /// </summary>
    public static bool IsAdminOnlySecurity(FileSystemSecurity security, bool isDirectory = false)
    {
        return IsAdminOrSystemOwner(security) && CheckSecurity(security, isDirectory);
    }

    /// <summary>
    /// Checks whether the given path points to a reparse point (junction, symbolic link, or mount point).
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                FileAttributes attr = File.GetAttributes(path);
                return (attr & FileAttributes.ReparsePoint) != 0;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Checks whether the path or any of its existing ancestor directories is a reparse point.
    /// </summary>
    public static bool HasReparsePointInPath(string path)
    {
        try
        {
            string? current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    if (IsReparsePoint(current))
                    {
                        return true;
                    }
                }

                string? parent = Path.GetDirectoryName(current);
                if (parent == current)
                {
                    break;
                }
                current = parent;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Checks whether the specified file or directory (and its parent and all ancestors) allows
    /// write/modify permissions only to Administrators and SYSTEM, verifies ownership, and ensures
    /// neither the path nor its ancestors are reparse points.
    /// </summary>
    public static bool IsAdminOnlyWritable(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path);

            if (HasReparsePointInPath(fullPath))
            {
                return false;
            }

            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                FileSecurity fileSecurity = fileInfo.GetAccessControl();
                if (!IsAdminOnlySecurity(fileSecurity, isDirectory: false))
                {
                    return false;
                }

                string? parentDir = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
                {
                    return false;
                }

                var dirInfo = new DirectoryInfo(parentDir);
                DirectorySecurity dirSecurity = dirInfo.GetAccessControl();
                if (!IsAdminOnlySecurity(dirSecurity, isDirectory: true))
                {
                    return false;
                }

                DirectoryInfo? ancestor = dirInfo.Parent;
                while (ancestor != null)
                {
                    DirectorySecurity ancestorSec = ancestor.GetAccessControl();
                    if (!CheckAncestorSecurity(ancestorSec))
                    {
                        return false;
                    }
                    ancestor = ancestor.Parent;
                }

                return true;
            }

            if (Directory.Exists(fullPath))
            {
                var dirInfo = new DirectoryInfo(fullPath);
                DirectorySecurity dirSecurity = dirInfo.GetAccessControl();
                if (!IsAdminOnlySecurity(dirSecurity, isDirectory: true))
                {
                    return false;
                }

                DirectoryInfo? ancestor = dirInfo.Parent;
                while (ancestor != null)
                {
                    DirectorySecurity ancestorSec = ancestor.GetAccessControl();
                    if (!CheckAncestorSecurity(ancestorSec))
                    {
                        return false;
                    }
                    ancestor = ancestor.Parent;
                }

                return true;
            }

            // Path does not exist yet: verify nearest existing ancestor directory
            string? existingAncestor = Path.GetDirectoryName(fullPath);
            while (!string.IsNullOrEmpty(existingAncestor) && !Directory.Exists(existingAncestor))
            {
                existingAncestor = Path.GetDirectoryName(existingAncestor);
            }

            if (!string.IsNullOrEmpty(existingAncestor) && Directory.Exists(existingAncestor))
            {
                var dirInfo = new DirectoryInfo(existingAncestor);
                DirectorySecurity dirSecurity = dirInfo.GetAccessControl();
                if (!IsAdminOnlySecurity(dirSecurity, isDirectory: true))
                {
                    return false;
                }

                DirectoryInfo? ancestor = dirInfo.Parent;
                while (ancestor != null)
                {
                    DirectorySecurity ancestorSec = ancestor.GetAccessControl();
                    if (!CheckAncestorSecurity(ancestorSec))
                    {
                        return false;
                    }
                    ancestor = ancestor.Parent;
                }

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Configures directory ACL with inheritance disabled so only Administrators and SYSTEM have FullControl,
    /// while standard users have read/execute access.
    /// </summary>
    public static void SetAdminOnlyWritableDirectory(string directoryPath)
    {
        var dirInfo = new DirectoryInfo(directoryPath);
        DirectorySecurity dirSec = new();
        dirSec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        try
        {
            dirSec.SetOwner(AdminSid);
        }
        catch
        {
            // Best effort if privilege not held
        }

        dirSec.AddAccessRule(new FileSystemAccessRule(
            AdminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        dirSec.AddAccessRule(new FileSystemAccessRule(
            SystemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        dirSec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        dirInfo.SetAccessControl(dirSec);
    }

    /// <summary>
    /// Configures file ACL with inheritance disabled so only Administrators and SYSTEM have FullControl,
    /// while standard users have read access.
    /// </summary>
    public static void SetAdminOnlyWritableFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        FileSecurity fileSec = new();
        fileSec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        try
        {
            fileSec.SetOwner(AdminSid);
        }
        catch
        {
            // Best effort if privilege not held
        }

        fileSec.AddAccessRule(new FileSystemAccessRule(
            AdminSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        fileSec.AddAccessRule(new FileSystemAccessRule(
            SystemSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        fileSec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Read,
            AccessControlType.Allow));

        fileInfo.SetAccessControl(fileSec);
    }
}
