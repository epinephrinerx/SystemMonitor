using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
using SysMonitor.Tests.DummyExtension;

namespace SysMonitor.Tests.Extensions;

/// <summary>
/// Shared helper for constructing valid and invalid extension packages in temporary directories.
/// </summary>
internal static class ExtensionTestPackageBuilder
{
    public static string DummyDllSourcePath
    {
        get
        {
            string path = Path.Combine(AppContext.BaseDirectory, "SysMonitor.Tests.DummyExtension.dll");
            if (!File.Exists(path))
            {
                path = typeof(DummyNormalExtension).Assembly.Location;
            }
            return path;
        }
    }

    public static string ComputeSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string CreateExtensionDirectory(
        string rootDir,
        string id,
        out string manifestSha256,
        string? entryType = null,
        bool includeNativeFile = false,
        bool omitNativeFromManifest = false,
        string nativeFileName = "dummynative.dll",
        bool nativeFirst = false)
    {
        string extDir = Path.Combine(rootDir, id);
        Directory.CreateDirectory(extDir);

        string targetDll = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        File.Copy(DummyDllSourcePath, targetDll, overwrite: true);

        byte[] dllBytes = File.ReadAllBytes(targetDll);
        string dllSha256 = ComputeSha256(dllBytes);

        var files = new List<ExtensionFile>
        {
            new()
            {
                Path = "SysMonitor.Tests.DummyExtension.dll",
                Sha256 = dllSha256,
                Native = false
            }
        };

        if (includeNativeFile)
        {
            string systemNativeSource = Path.Combine(Environment.SystemDirectory, "shlwapi.dll");
            string targetNative = Path.Combine(extDir, nativeFileName);
            File.Copy(systemNativeSource, targetNative, overwrite: true);

            if (!omitNativeFromManifest)
            {
                byte[] nativeBytes = File.ReadAllBytes(targetNative);
                var nativeEntry = new ExtensionFile
                {
                    Path = nativeFileName,
                    Sha256 = ComputeSha256(nativeBytes),
                    Native = true
                };
                if (nativeFirst)
                {
                    files.Insert(0, nativeEntry);
                }
                else
                {
                    files.Add(nativeEntry);
                }
            }
        }

        var manifest = new ExtensionManifest
        {
            Id = id,
            Name = "Extension " + id,
            Version = "1.0.0",
            Contract = "1.0",
            Entry = new ExtensionEntry
            {
                Assembly = "SysMonitor.Tests.DummyExtension.dll",
                Type = entryType ?? typeof(DummyNormalExtension).FullName!
            },
            Files = files
        };

        string manifestJson = JsonSerializer.Serialize(manifest, ExtensionManifest.JsonOptions);
        string manifestPath = Path.Combine(extDir, "extension.json");
        File.WriteAllText(manifestPath, manifestJson);

        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        manifestSha256 = ComputeSha256(manifestBytes);

        return extDir;
    }

    public static string CreateExtensionDirectoryWithManifest(
        string rootDir,
        string id,
        Action<ExtensionManifest> configureManifest)
    {
        string extDir = Path.Combine(rootDir, id);
        Directory.CreateDirectory(extDir);

        string targetDll = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        File.Copy(DummyDllSourcePath, targetDll, overwrite: true);

        byte[] dllBytes = File.ReadAllBytes(targetDll);
        string dllSha256 = ComputeSha256(dllBytes);

        var manifest = new ExtensionManifest
        {
            Id = id,
            Name = "Extension " + id,
            Version = "1.0.0",
            Contract = "1.0",
            Entry = new ExtensionEntry
            {
                Assembly = "SysMonitor.Tests.DummyExtension.dll",
                Type = typeof(DummyNormalExtension).FullName!
            },
            Files = new List<ExtensionFile>
            {
                new()
                {
                    Path = "SysMonitor.Tests.DummyExtension.dll",
                    Sha256 = dllSha256,
                    Native = false
                }
            }
        };

        configureManifest(manifest);

        string manifestJson = JsonSerializer.Serialize(manifest, ExtensionManifest.JsonOptions);
        string manifestPath = Path.Combine(extDir, "extension.json");
        File.WriteAllText(manifestPath, manifestJson);

        return extDir;
    }
}
