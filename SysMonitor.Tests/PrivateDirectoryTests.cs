using System.Security.AccessControl;
using System.Security.Principal;
using SysMonitor.Setup;

namespace SysMonitor.Tests;

/// <summary>
/// R-022: the directory the elevated uninstaller runs from. Only the access
/// rules are checked; nothing is created on disk (the directory would be
/// closed to the test process itself, which is the point).
/// </summary>
[TestClass]
public class PrivateDirectoryTests
{
    [TestMethod]
    public void Only_administrators_and_system_get_access_and_nothing_is_inherited()
    {
        DirectorySecurity security = PrivateDirectory.AdminOnlySecurity();

        Assert.IsTrue(security.AreAccessRulesProtected, "no rule comes down from the parent folder");
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToList();

        CollectionAssert.AreEquivalent(
            new[]
            {
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            },
            rules.Select(r => (SecurityIdentifier)r.IdentityReference).ToArray());
        Assert.IsTrue(rules.All(r => r.AccessControlType == AccessControlType.Allow));
        Assert.IsTrue(rules.All(r => r.FileSystemRights.HasFlag(FileSystemRights.FullControl)));
    }

    [TestMethod]
    public void No_rule_names_users_everyone_or_authenticated_users()
    {
        var forbidden = new[]
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
        };

        IEnumerable<SecurityIdentifier> named = PrivateDirectory.AdminOnlySecurity()
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Select(r => (SecurityIdentifier)r.IdentityReference);

        Assert.IsFalse(named.Any(sid => forbidden.Contains(sid)));
    }
}
