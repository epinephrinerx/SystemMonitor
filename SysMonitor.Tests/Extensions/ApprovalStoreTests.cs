using System.IO;
using System.Text.Json;
using SysMonitor.Extensions;

namespace SysMonitor.Tests.Extensions;

[TestClass]
public class ApprovalStoreTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SysMon_ApprovalTests_" + Guid.NewGuid().ToString("N"));
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

    [TestMethod]
    public void IsApproved_RealAclCheckerOnTempFile_ReturnsFalse()
    {
        // Temp files are user-writable, so real AclPolicy check fails and store is treated as empty.
        string storePath = Path.Combine(_tempDir, "approved-extensions.json");
        const string extId = "ext_one";
        const string sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var entries = new[]
        {
            new { id = extId, name = "Extension One", manifest_sha256 = sha256 }
        };
        File.WriteAllText(storePath, JsonSerializer.Serialize(entries));

        bool logged = false;
        var store = new ApprovalStore(storePath, log: _ => logged = true);

        bool approved = store.IsApproved(extId, sha256);

        Assert.IsFalse(approved, "Store on user-writable path must be rejected by default ACL checker");
        Assert.IsTrue(logged, "Reason must be logged when ACL validation fails");
    }

    [TestMethod]
    public void IsApproved_FakeAclCheckerReturningFalse_ReturnsFalseAndLogs()
    {
        string storePath = Path.Combine(_tempDir, "approved.json");
        const string extId = "ext_one";
        const string sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        var entries = new[]
        {
            new { id = extId, name = "Ext", manifest_sha256 = sha256 }
        };
        File.WriteAllText(storePath, JsonSerializer.Serialize(entries));

        string? logMessage = null;
        var store = new ApprovalStore(storePath, aclChecker: _ => false, log: msg => logMessage = msg);

        bool result = store.IsApproved(extId, sha256);

        Assert.IsFalse(result);
        Assert.IsNotNull(logMessage);
        Assert.IsTrue(logMessage.Contains("failed ACL check"));
    }

    [TestMethod]
    public void IsApproved_FakeAclCheckerReturningTrue_MatchesIdAndHash()
    {
        string storePath = Path.Combine(_tempDir, "approved.json");
        const string extId = "ext_valid";
        const string sha256 = "1111111111111111111111111111111111111111111111111111111111111111";

        var entries = new[]
        {
            new { id = extId, name = "Ext", manifest_sha256 = sha256 }
        };
        File.WriteAllText(storePath, JsonSerializer.Serialize(entries));

        var store = new ApprovalStore(storePath, aclChecker: _ => true);

        Assert.IsTrue(store.IsApproved(extId, sha256));
        Assert.IsTrue(store.IsApproved(extId.ToUpperInvariant(), sha256.ToUpperInvariant()), "Case-insensitive check");
        Assert.IsFalse(store.IsApproved(extId, "different_hash"));
        Assert.IsFalse(store.IsApproved("different_id", sha256));
    }

    [TestMethod]
    public void IsApproved_MalformedJson_ReturnsFalseAndLogs()
    {
        string storePath = Path.Combine(_tempDir, "approved.json");
        File.WriteAllText(storePath, "{ this is not valid JSON ");

        string? logMessage = null;
        var store = new ApprovalStore(storePath, aclChecker: _ => true, log: msg => logMessage = msg);

        bool result = store.IsApproved("any_id", "any_hash");

        Assert.IsFalse(result);
        Assert.IsNotNull(logMessage);
        Assert.IsTrue(logMessage.Contains("Failed to parse approval store"));
    }

    [TestMethod]
    public void IsApproved_NullEntryInArray_TreatsStoreAsEmptyAndLogs()
    {
        string storePath = Path.Combine(_tempDir, "approved_with_null.json");
        File.WriteAllText(storePath, "[ null, { \"id\": \"ext1\", \"name\": \"Ext\", \"manifest_sha256\": \"1111111111111111111111111111111111111111111111111111111111111111\" } ]");

        string? logMessage = null;
        var store = new ApprovalStore(storePath, aclChecker: _ => true, log: msg => logMessage = msg);

        bool result = store.IsApproved("ext1", "1111111111111111111111111111111111111111111111111111111111111111");

        Assert.IsFalse(result);
        Assert.IsNotNull(logMessage);
        Assert.IsTrue(logMessage.Contains("null entries") || logMessage.Contains("parsed to null"));
    }

    [TestMethod]
    public void IsApproved_ReparsePointPath_ReturnsFalse()
    {
        string realDir = Path.Combine(_tempDir, "real_store");
        Directory.CreateDirectory(realDir);

        string junctionDir = Path.Combine(_tempDir, "junction_store");
        if (!TestJunctionHelper.TryCreateJunction(junctionDir, realDir))
        {
            Assert.Inconclusive("Could not create directory junction on this system.");
        }

        string storePath = Path.Combine(junctionDir, "approved.json");
        // The target really approves the extension: only the reparse-point rejection can make this false.
        File.WriteAllText(Path.Combine(realDir, "approved.json"),
            "[{\"id\":\"any_id\",\"name\":\"x\",\"manifest_sha256\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"approved_at\":\"2026-01-01T00:00:00+00:00\"}]");

        var store = new ApprovalStore(storePath, aclChecker: _ => true);
        bool result = store.IsApproved("any_id", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        Assert.IsFalse(result, "ApprovalStore must reject reparse point paths");

        // Control: the same file reached by its real path is honoured, so the test above is not vacuous.
        var direct = new ApprovalStore(Path.Combine(realDir, "approved.json"), aclChecker: _ => true);
        Assert.IsTrue(direct.IsApproved("any_id", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
    }

    [TestMethod]
    public void Approve_WhenStoreDirectoryIsReparsePoint_FailsWithReason()
    {
        string realDir = Path.Combine(_tempDir, "real_appr_dir");
        Directory.CreateDirectory(realDir);

        string junctionDir = Path.Combine(_tempDir, "junction_appr_dir");
        if (!TestJunctionHelper.TryCreateJunction(junctionDir, realDir))
        {
            Assert.Inconclusive("Could not create directory junction on this system.");
        }

        string storePath = Path.Combine(junctionDir, "approved-extensions.json");
        var store = new ApprovalStore(storePath, aclChecker: _ => true);

        ApprovalResult result = store.Approve("ext1", "Ext One", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Error!.Contains("reparse point"));
    }

    [TestMethod]
    public void Approve_WhenSecuringNewDirectoryFails_RemovesTheDirectoryItCreated()
    {
        string storeDir = Path.Combine(_tempDir, "new_store_dir");
        string storePath = Path.Combine(storeDir, "approved.json");
        var store = new ApprovalStore(
            storePath,
            aclChecker: _ => true,
            secureDirectory: _ => throw new UnauthorizedAccessException("not allowed to take ownership"),
            secureFile: _ => { });

        ApprovalResult result = store.Approve("ext1", "Ext", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        Assert.IsFalse(result.Success);
        Assert.IsFalse(Directory.Exists(storeDir), "a folder left behind would block every later approval");
    }

    [TestMethod]
    public void Approve_WhenExistingStoreDirFailsAcl_FailsClosedWithoutModifying()
    {
        string storeDir = Path.Combine(_tempDir, "user_writable_store_dir");
        Directory.CreateDirectory(storeDir);

        string storePath = Path.Combine(storeDir, "approved.json");
        var store = new ApprovalStore(storePath, aclChecker: _ => false);

        ApprovalResult result = store.Approve("ext1", "Ext", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Error!.Contains("not admin-only writable"));
    }

    [TestMethod]
    public void Approve_WhenWriteFails_ReturnsFailureWithReasonNotSwallowed()
    {
        // Point store to a path where writing will fail (create directory with the file name)
        string blockedPath = Path.Combine(_tempDir, "directory_blocking_file.json");
        Directory.CreateDirectory(blockedPath);

        var store = new ApprovalStore(blockedPath, aclChecker: _ => true);
        ApprovalResult result = store.Approve("id", "name", "hash");

        Assert.IsFalse(result.Success, "Approve must fail when target file cannot be written");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error), "Failure reason must be returned, not swallowed");
    }

    [TestMethod]
    public void Approve_EmptyIdOrHash_ReturnsFailure()
    {
        var store = new ApprovalStore(Path.Combine(_tempDir, "approved.json"));

        ApprovalResult emptyId = store.Approve("", "name", "hash");
        Assert.IsFalse(emptyId.Success);

        ApprovalResult emptyHash = store.Approve("id", "name", "");
        Assert.IsFalse(emptyHash.Success);
    }
}
