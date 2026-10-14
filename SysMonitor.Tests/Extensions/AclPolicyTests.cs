using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using SysMonitor.Extensions;

namespace SysMonitor.Tests.Extensions;

[TestClass]
public class AclPolicyTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SysMon_AclTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            TestJunctionHelper.DeleteTree(_tempDir);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    #region In-Memory ACL Evaluation Tests (No Disk / Admin Required)

    [TestMethod]
    public void InMemory_AdminOwned_AdminsSystemFull_UsersRead_ReturnsTrue_Bug1Fix()
    {
        var fileSec = new FileSecurity();
        fileSec.SetOwner(AclPolicy.AdminSid);

        fileSec.AddAccessRule(new FileSystemAccessRule(
            AclPolicy.AdminSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        fileSec.AddAccessRule(new FileSystemAccessRule(
            AclPolicy.SystemSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        // Users: Read (In bug #1, this failed because WriteOrModifyRights included Synchronize).
        // With the fix, Users: Read MUST be accepted!
        fileSec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Read,
            AccessControlType.Allow));

        bool result = AclPolicy.IsAdminOnlySecurity(fileSec, isDirectory: false);
        Assert.IsTrue(result, "Admin-owned file with Admins/SYSTEM FullControl and Users Read must pass");
    }

    [TestMethod]
    [DataRow(FileSystemRights.WriteData)]
    [DataRow(FileSystemRights.CreateFiles)]
    [DataRow(FileSystemRights.AppendData)]
    [DataRow(FileSystemRights.Delete)]
    [DataRow(FileSystemRights.DeleteSubdirectoriesAndFiles)]
    [DataRow(FileSystemRights.ChangePermissions)]
    [DataRow(FileSystemRights.TakeOwnership)]
    public void InMemory_UsersHaveWriteOrModifyRights_ReturnsFalse(FileSystemRights right)
    {
        var fileSec = new FileSecurity();
        fileSec.SetOwner(AclPolicy.AdminSid);

        fileSec.AddAccessRule(new FileSystemAccessRule(
            AclPolicy.AdminSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        fileSec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            right,
            AccessControlType.Allow));

        bool result = AclPolicy.CheckSecurity(fileSec, isDirectory: false);
        Assert.IsFalse(result, $"Rule granting {right} to Users must be rejected");
    }

    [TestMethod]
    public void InMemory_OwnerIsStandardUser_ReturnsFalse()
    {
        var fileSec = new FileSecurity();
        var userSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        fileSec.SetOwner(userSid);

        fileSec.AddAccessRule(new FileSystemAccessRule(
            AclPolicy.AdminSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        bool ownerCheck = AclPolicy.IsAdminOrSystemOwner(fileSec);
        Assert.IsFalse(ownerCheck, "File owned by standard user must fail owner check");

        bool fullCheck = AclPolicy.IsAdminOnlySecurity(fileSec, isDirectory: false);
        Assert.IsFalse(fullCheck, "File owned by standard user must fail IsAdminOnlySecurity");
    }

    [TestMethod]
    public void InMemory_Directory_InheritOnlyUserAce_DoesNotApplyToDirectory_ReturnsTrue()
    {
        var dirSec = new DirectorySecurity();
        dirSec.SetOwner(AclPolicy.AdminSid);

        dirSec.AddAccessRule(new FileSystemAccessRule(
            AclPolicy.AdminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        // Add user write rule with InheritOnly flag: does NOT apply to this directory
        dirSec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.WriteData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.InheritOnly,
            AccessControlType.Allow));

        bool result = AclPolicy.CheckSecurity(dirSec, isDirectory: true);
        Assert.IsTrue(result, "InheritOnly ACE must not affect the directory itself");
    }

    [TestMethod]
    [DataRow(FileSystemRights.DeleteSubdirectoriesAndFiles)]
    [DataRow(FileSystemRights.Delete)]
    [DataRow(FileSystemRights.ChangePermissions)]
    [DataRow(FileSystemRights.TakeOwnership)]
    public void InMemory_AncestorDirectory_UserCanDeleteOrModify_ReturnsFalse(FileSystemRights forbiddenRight)
    {
        var ancestorSec = new DirectorySecurity();
        ancestorSec.SetOwner(AclPolicy.AdminSid);

        ancestorSec.AddAccessRule(new FileSystemAccessRule(
            AclPolicy.AdminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        ancestorSec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            forbiddenRight,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        bool result = AclPolicy.CheckAncestorSecurity(ancestorSec);
        Assert.IsFalse(result, $"Ancestor granting {forbiddenRight} to Users must be rejected");
    }

    [TestMethod]
    public void InMemory_AncestorDirectory_UserCanCreateSubdirectories_ReturnsTrue()
    {
        // Users can create subdirectories in C:\ProgramData; this must be permitted on ancestors!
        var ancestorSec = new DirectorySecurity();
        ancestorSec.SetOwner(AclPolicy.AdminSid);

        ancestorSec.AddAccessRule(new FileSystemAccessRule(
            AclPolicy.AdminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        ancestorSec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.CreateDirectories | FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        bool result = AclPolicy.CheckAncestorSecurity(ancestorSec);
        Assert.IsTrue(result, "Ancestor allowing Users to create directories (like C:\\ProgramData) must be accepted");
    }

    [TestMethod]
    public void InMemory_AncestorDirectory_OwnedByStandardUser_ReturnsFalse()
    {
        // The owner can always rewrite the DACL, so no ACE needs to grant anything.
        var ancestorSec = new DirectorySecurity();
        ancestorSec.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
        ancestorSec.AddAccessRule(new FileSystemAccessRule(
            AclPolicy.AdminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        Assert.IsFalse(AclPolicy.CheckAncestorSecurity(ancestorSec));
    }

    [TestMethod]
    public void InMemory_AncestorDirectory_OwnedByTrustedInstaller_ReturnsTrue()
    {
        // The drive root and Windows folders are owned by TrustedInstaller.
        var ancestorSec = new DirectorySecurity();
        ancestorSec.SetOwner(new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"));
        ancestorSec.AddAccessRule(new FileSystemAccessRule(
            AclPolicy.AdminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        Assert.IsTrue(AclPolicy.CheckAncestorSecurity(ancestorSec));
    }

    #endregion

    #region Real Disk Verification Tests

    [TestMethod]
    public void IsAdminOnlyWritable_TempFileCreatedByUser_ReturnsFalse()
    {
        // Files created in user's temp directory inherit user write permissions,
        // so they must NOT be considered admin-only writable.
        string tempFile = Path.Combine(_tempDir, "test_file.txt");
        File.WriteAllText(tempFile, "hello");

        bool result = AclPolicy.IsAdminOnlyWritable(tempFile);

        Assert.IsFalse(result, "File in user's temp directory should fail admin-only check");
    }

    [TestMethod]
    public void IsAdminOnlyWritable_TempDirectory_ReturnsFalse()
    {
        bool result = AclPolicy.IsAdminOnlyWritable(_tempDir);

        Assert.IsFalse(result, "User's temp directory should fail admin-only check");
    }

    [TestMethod]
    public void IsAdminOnlyWritable_EmptyOrNullPath_ReturnsFalse()
    {
        Assert.IsFalse(AclPolicy.IsAdminOnlyWritable(string.Empty));
        Assert.IsFalse(AclPolicy.IsAdminOnlyWritable("   "));
    }

    [TestMethod]
    public void IsAdminOnlyWritable_NonExistentPathInUserTemp_ReturnsFalse()
    {
        string nonExistent = Path.Combine(_tempDir, "does_not_exist.json");
        bool result = AclPolicy.IsAdminOnlyWritable(nonExistent);

        Assert.IsFalse(result, "Non-existent path in user temp should inherit parent check and return false");
    }

    #endregion
}
