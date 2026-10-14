using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysMonitor.Extensions;

/// <summary>
/// Result of an Approve operation on the approval store.
/// </summary>
public sealed record ApprovalResult(bool Success, string? Error = null)
{
    public static ApprovalResult Ok() => new(true);
    public static ApprovalResult Failed(string error) => new(false, error);
}

/// <summary>
/// Stored approval record for an extension package.
/// </summary>
public sealed class ApprovedExtensionEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("manifest_sha256")]
    public string ManifestSha256 { get; set; } = string.Empty;

    [JsonPropertyName("approved_at")]
    public DateTimeOffset ApprovedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Persistent store for user-approved extensions (%ProgramData%\SystemMonitor\approved-extensions.json).
/// Requires admin-only write ACL and ownership; rejects unverified, user-writable, or reparse point stores.
/// </summary>
public sealed class ApprovalStore
{
    private readonly string _path;
    private readonly Func<string, bool> _aclChecker;
    private readonly Action<string>? _log;
    private readonly Action<string> _secureDirectory;
    private readonly Action<string> _secureFile;

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SystemMonitor",
            "approved-extensions.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ApprovalStore(
        string path,
        Func<string, bool>? aclChecker = null,
        Action<string>? log = null,
        Action<string>? secureDirectory = null,
        Action<string>? secureFile = null)
    {
        // Taking ownership for Administrators needs an elevated caller, which the tests
        // are not, so they pass no-op securers. Production always uses the real ones.
        _secureDirectory = secureDirectory ?? AclPolicy.SetAdminOnlyWritableDirectory;
        _secureFile = secureFile ?? AclPolicy.SetAdminOnlyWritableFile;
        // One absolute path for every check and every read: a relative path could point
        // somewhere else by the time it is used if the current directory changes.
        string resolved = path;
        try
        {
            resolved = Path.GetFullPath(path);
        }
        catch
        {
            // Keep the raw path; every operation reports it as invalid.
        }
        _path = resolved;
        _aclChecker = aclChecker ?? AclPolicy.IsAdminOnlyWritable;
        _log = log;
    }

    /// <summary>
    /// Checks whether an extension with the specified id and manifest SHA-256 is approved.
    /// Returns false if the file does not exist, is a reparse point, has an invalid ACL/owner,
    /// or is malformed.
    /// </summary>
    public bool IsApproved(string id, string manifestSha256)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(manifestSha256))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(_path);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        if (AclPolicy.HasReparsePointInPath(fullPath))
        {
            _log.SafeLog($"Approval store path '{fullPath}' or one of its ancestors is a reparse point. Treating store as empty.");
            return false;
        }

        if (!_aclChecker(fullPath))
        {
            _log.SafeLog($"Approval store at '{fullPath}' failed ACL check (not admin-only writable). Treating store as empty.");
            return false;
        }

        List<ApprovedExtensionEntry>? entries = LoadEntries();
        if (entries == null)
        {
            return false;
        }

        return entries.Any(e =>
            e != null &&
            string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ManifestSha256, manifestSha256, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Approves an extension by writing its manifest hash to the store and securing the file ACL.
    /// Fails with a descriptive error if the caller lacks permission, if paths are reparse points,
    /// or if the existing directory fails ACL/owner checks; never fails silently.
    /// </summary>
    public ApprovalResult Approve(string id, string name, string manifestSha256)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return ApprovalResult.Failed("Extension id cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(manifestSha256))
        {
            return ApprovalResult.Failed("Manifest sha256 cannot be empty");
        }

        try
        {
            string fullPath = Path.GetFullPath(_path);
            string? dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(dir))
            {
                return ApprovalResult.Failed($"Invalid approval store path '{_path}'");
            }

            // (ก) ถ้าโฟลเดอร์ store หรือไฟล์ store หรือ ancestor ใด ๆ เป็น reparse point -> ล้มพร้อมเหตุผล ไม่เขียน
            if (AclPolicy.HasReparsePointInPath(fullPath) || AclPolicy.HasReparsePointInPath(dir))
            {
                return ApprovalResult.Failed($"Approval store path or ancestor is a reparse point (junction/symlink)");
            }

            // (ข) ถ้าโฟลเดอร์ store มีอยู่แล้วแต่ตัวตรวจ ACL/เจ้าของไม่ผ่าน -> ล้มพร้อมเหตุผล (fail closed; ไม่พยายามแก้เอง)
            if (Directory.Exists(dir))
            {
                if (!_aclChecker(dir))
                {
                    return ApprovalResult.Failed($"Approval store directory '{dir}' exists but is not admin-only writable or admin-owned");
                }
            }
            else
            {
                Directory.CreateDirectory(dir);
                try
                {
                    _secureDirectory(dir);
                }
                catch (Exception ex)
                {
                    // A folder left behind owned by the caller would block every later approval,
                    // including an elevated one, so take back what this call created.
                    TryDeleteEmptyDirectory(dir);
                    return ApprovalResult.Failed($"Failed to set admin-only ACL on directory '{dir}': {ex.Describe()}");
                }
            }

            // The folder may have been swapped for a junction between the first check and now,
            // and CreateDirectory accepts an existing one: look again at what we are about to use.
            if (AclPolicy.HasReparsePointInPath(dir) || !_aclChecker(dir))
            {
                return ApprovalResult.Failed($"Approval store directory '{dir}' changed or is not admin-only writable after it was prepared");
            }

            // (ค) อ่านรายการเดิมมารวมเฉพาะเมื่อไฟล์เดิมผ่านตัวตรวจ ACL/เจ้าของ; ไม่ผ่าน/เสียรูปแบบ -> เริ่มจากรายการว่างและ log (ไม่กลืนเงียบ)
            var list = new List<ApprovedExtensionEntry>();
            if (File.Exists(fullPath))
            {
                if (AclPolicy.IsReparsePoint(fullPath))
                {
                    return ApprovalResult.Failed($"Approval store file '{fullPath}' is a reparse point");
                }

                if (!_aclChecker(fullPath))
                {
                    _log.SafeLog($"Existing approval store at '{fullPath}' failed ACL or owner check. Starting with an empty entry list.");
                }
                else
                {
                    try
                    {
                        string existingJson = File.ReadAllText(fullPath);
                        var existing = JsonSerializer.Deserialize<List<ApprovedExtensionEntry>>(existingJson, JsonOptions);
                        if (existing == null || existing.Any(e => e == null))
                        {
                            _log.SafeLog($"Existing approval store at '{fullPath}' is malformed or contains null entries. Starting with an empty entry list.");
                        }
                        else
                        {
                            list.AddRange(existing);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.SafeLog($"Existing approval store at '{fullPath}' could not be parsed: {ex.Describe()}. Starting with an empty entry list.");
                    }
                }
            }

            list.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            list.Add(new ApprovedExtensionEntry
            {
                Id = id,
                Name = name,
                ManifestSha256 = manifestSha256,
                ApprovedAt = DateTimeOffset.UtcNow
            });

            // (ง) เขียนไฟล์ชั่วคราวในโฟลเดอร์ store แล้วตั้ง ACL แล้ว File.Move(..., overwrite: true)
            string tempFile = Path.Combine(dir, $"appr_{Guid.NewGuid():N}.tmp");
            try
            {
                string json = JsonSerializer.Serialize(list, JsonOptions);
                File.WriteAllText(tempFile, json);

                try
                {
                    _secureFile(tempFile);
                }
                catch (Exception ex)
                {
                    TryDelete(tempFile);
                    return ApprovalResult.Failed($"Failed to set admin-only ACL on temporary file '{tempFile}': {ex.Describe()}");
                }

                File.Move(tempFile, fullPath, overwrite: true);
            }
            catch (Exception ex)
            {
                TryDelete(tempFile);
                return ApprovalResult.Failed($"Failed to write approval store file: {ex.Describe()}");
            }

            return ApprovalResult.Ok();
        }
        catch (Exception ex)
        {
            return ApprovalResult.Failed($"Failed to approve extension: {ex.Describe()}");
        }
    }

    private List<ApprovedExtensionEntry>? LoadEntries()
    {
        try
        {
            string text = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<ApprovedExtensionEntry>>(text, JsonOptions);
            if (list == null || list.Any(e => e == null))
            {
                _log.SafeLog($"Approval store at '{_path}' contains null entries or parsed to null. Treating store as empty.");
                return null;
            }
            return list;
        }
        catch (Exception ex)
        {
            _log.SafeLog($"Failed to parse approval store at '{_path}': {ex.Describe()}. Treating store as empty.");
            return null;
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch
        {
            // Best effort: a non-empty or protected folder is left as it is.
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
