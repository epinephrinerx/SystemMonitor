using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using SysMonitor.Extensibility;
using SysMonitor.Native;

namespace SysMonitor.Extensions;

/// <summary>
/// Prompts presented to the user during extension approval.
/// </summary>
public sealed record ApprovalPrompt(
    string Id,
    string Name,
    string Version,
    string ManifestShaPrefix,
    IReadOnlyList<string> Files,
    bool HasNativeFiles);

/// <summary>
/// Execution environment seams for ExtensionApproval.Run.
/// </summary>
public sealed class ApprovalEnvironment
{
    public required Func<bool> IsElevated { get; init; }
    public required string ExtensionsRoot { get; init; }
    public required ApprovalStore Store { get; init; }
    public required FirstPartyAllowlist FirstParty { get; init; }
    public required Func<ApprovalPrompt, bool> Confirm { get; init; }
    public required Action<string> Log { get; init; }
    public required Version HostVersion { get; init; }
}

/// <summary>
/// CLI handler and process launcher for the one-time elevated approval helper.
/// </summary>
public static class ExtensionApproval
{
    public const string Switch = "--approve-extension";

    public const int ExitApproved = 0;
    public const int ExitBadArguments = 1;
    public const int ExitDeclined = 2;
    public const int ExitInspectFailed = 3;
    public const int ExitWriteFailed = 4;
    public const int ExitNotElevated = 5;

    private static readonly Regex IdRegex = new(@"^[a-z0-9][a-z0-9._-]{0,63}\z", RegexOptions.Compiled);

    /// <summary>
    /// Parses and validates CLI arguments formatted as [... , "--approve-extension", id, sha].
    /// Returns true only if the switch is followed by exactly two arguments with valid id and hex hash formats.
    /// </summary>
    public static bool TryParse(string[] args, out string id, out string manifestSha256)
    {
        id = string.Empty;
        manifestSha256 = string.Empty;

        if (args == null || args.Length < 3)
        {
            return false;
        }

        int idx = Array.IndexOf(args, Switch);
        if (idx < 0 || idx != args.Length - 3 || Array.LastIndexOf(args, Switch) != idx)
        {
            return false;
        }

        string rawId = args[idx + 1];
        string rawSha = args[idx + 2];

        if (string.IsNullOrEmpty(rawId) || rawId == "." || rawId == ".." || !IdRegex.IsMatch(rawId))
        {
            return false;
        }

        if (rawSha.Length != 64 || !IsAllHex(rawSha))
        {
            return false;
        }

        id = rawId;
        manifestSha256 = rawSha.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// Builds ProcessStartInfo configured to launch the elevated helper via runas.
    /// Throws ArgumentException if any argument fails format validation.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string exePath, string id, string manifestSha256)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new ArgumentException("Executable path cannot be empty", nameof(exePath));
        }

        if (string.IsNullOrEmpty(id) || id == "." || id == ".." || !IdRegex.IsMatch(id))
        {
            throw new ArgumentException($"Invalid extension id '{id}'", nameof(id));
        }

        if (manifestSha256 == null || manifestSha256.Length != 64 || !IsAllHex(manifestSha256))
        {
            throw new ArgumentException("Invalid manifest SHA-256 hash", nameof(manifestSha256));
        }

        string normalizedSha = manifestSha256.ToLowerInvariant();
        return new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
            Arguments = $"{Switch} {id} {normalizedSha}"
        };
    }

    /// <summary>
    /// Executes the elevated approval helper workflow against the provided environment seams.
    /// Never loads extension code or ALCs, and never throws exceptions.
    /// </summary>
    public static int Run(string[] args, ApprovalEnvironment env)
    {
        try
        {
            if (env == null)
            {
                return ExitInspectFailed;
            }

            // 1. Parse arguments
            if (!TryParse(args, out string id, out string sha))
            {
                env.Log.SafeLog("ExtensionApproval: failed to parse arguments");
                return ExitBadArguments;
            }

            // 2. Must be elevated
            if (!env.IsElevated())
            {
                env.Log.SafeLog("ExtensionApproval: process is not elevated");
                return ExitNotElevated;
            }

            // 3. Inspect the extension package structurally
            var options = new ExtensionLoaderOptions
            {
                ExtensionsRoot = env.ExtensionsRoot,
                HostVersion = env.HostVersion,
                FirstParty = env.FirstParty,
                Log = env.Log
            };

            string folder = Path.Combine(env.ExtensionsRoot, id);
            ExtensionInspection inspection = ExtensionLoader.Inspect(folder, options);
            if (inspection.Status != ExtensionStatus.Loaded || inspection.Manifest == null || inspection.ManifestSha256 == null)
            {
                env.Log.SafeLog($"ExtensionApproval: inspect failed for '{id}': {inspection.Status} - {inspection.Detail}");
                return ExitInspectFailed;
            }

            // 4. Recomputed manifest SHA-256 must match the argument
            if (!string.Equals(inspection.ManifestSha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                env.Log.SafeLog($"ExtensionApproval: manifest SHA mismatch for '{id}'. Expected {sha}, got {inspection.ManifestSha256}");
                return ExitInspectFailed;
            }

            // 5. First-party packages do not need store write
            if (inspection.IsFirstParty)
            {
                env.Log.SafeLog($"ExtensionApproval: '{id}' is first-party, approval granted without store write");
                return ExitApproved;
            }

            // 6. Build prompt and confirm with the user (sanitise display text against spoofing)
            string hashPrefix = sha.Length >= 12 ? sha[..12] : sha;
            string sanitizedName = SanitizeText(inspection.Manifest.Name, 60);
            string sanitizedVersion = SanitizeText(inspection.Manifest.Version, 32);

            var fileList = new List<string>();
            int displayCount = Math.Min(inspection.Manifest.Files.Count, 10);
            for (int i = 0; i < displayCount; i++)
            {
                fileList.Add(SanitizeText(inspection.Manifest.Files[i].Path, 80));
            }
            if (inspection.Manifest.Files.Count > 10)
            {
                fileList.Add($"(+{inspection.Manifest.Files.Count - 10} more)");
            }

            bool hasNative = inspection.Manifest.Files.Any(f => f.Native);

            var prompt = new ApprovalPrompt(
                Id: id,
                Name: sanitizedName,
                Version: sanitizedVersion,
                ManifestShaPrefix: hashPrefix,
                Files: fileList,
                HasNativeFiles: hasNative);

            if (!env.Confirm(prompt))
            {
                env.Log.SafeLog($"ExtensionApproval: confirmation declined for '{id}'");
                return ExitDeclined;
            }

            // 7. Write approval to the store (using sanitised name)
            ApprovalResult result = env.Store.Approve(id, sanitizedName, sha);
            if (!result.Success)
            {
                env.Log.SafeLog($"ExtensionApproval: store write failed for '{id}': {result.Error}");
                return ExitWriteFailed;
            }

            return ExitApproved;
        }
        catch (Exception ex)
        {
            env?.Log.SafeLog($"ExtensionApproval.Run encountered unexpected error: {ex.Describe()}");
            return ExitInspectFailed;
        }
    }

    /// <summary>
    /// Builds the real production ApprovalEnvironment and runs the approval flow.
    /// </summary>
    public static int RunProduction(string[] args)
    {
        try
        {
            Assembly appAssembly = typeof(App).Assembly;
            string extensionsRoot = Path.Combine(AppContext.BaseDirectory, "extensions");
            var store = new ApprovalStore(ApprovalStore.DefaultPath, log: Diag.Write);
            var firstParty = FirstPartyAllowlist.FromAssembly(appAssembly, Diag.Write);
            Version hostVersion = appAssembly.GetName().Version ?? new Version(1, 0, 0);

            var env = new ApprovalEnvironment
            {
                IsElevated = Win32.IsElevated,
                ExtensionsRoot = extensionsRoot,
                Store = store,
                FirstParty = firstParty,
                Log = Diag.Write,
                HostVersion = hostVersion,
                Confirm = prompt =>
                {
                    // Helper must not touch configuration (AppConfig.Load migrates and may write)
                    string langCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th" ? "th" : "en";
                    var lang = new Lang(langCode);
                    string files = string.Join("\n", prompt.Files.Select(f => "  • " + f));
                    string body = string.Format(
                        lang["ext_approve_body"],
                        prompt.Name,
                        prompt.Id,
                        prompt.Version,
                        prompt.ManifestShaPrefix,
                        files);

                    if (prompt.HasNativeFiles)
                    {
                        body += "\n\n" + lang["ext_approve_native_warning"];
                    }

                    Window? ownerWindow = null;
                    try
                    {
                        // This hidden window becomes Application.MainWindow, and closing it would
                        // end the app with exit code 0 under OnMainWindowClose, swallowing the
                        // decline/failure code. Shut down only explicitly.
                        if (Application.Current is { } app)
                        {
                            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                        }

                        ownerWindow = new Window
                        {
                            WindowStyle = WindowStyle.None,
                            ResizeMode = ResizeMode.NoResize,
                            ShowInTaskbar = false,
                            Width = 0,
                            Height = 0,
                            WindowStartupLocation = WindowStartupLocation.Manual,
                            Left = -10000,
                            Top = -10000,
                            Topmost = true,
                            Opacity = 0
                        };
                        ownerWindow.Show();

                        MessageBoxResult result = MessageBox.Show(
                            ownerWindow,
                            body,
                            lang["ext_approve_title"],
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning,
                            MessageBoxResult.No);

                        return result == MessageBoxResult.Yes;
                    }
                    finally
                    {
                        ownerWindow?.Close();
                    }
                }
            };

            return Run(args, env);
        }
        catch (Exception ex)
        {
            Diag.ReportException("ExtensionApproval.RunProduction", ex);
            return ExitInspectFailed;
        }
    }

    internal static string SanitizeText(string? input, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(input.Length);
        bool inWhitespace = false;

        // Per rune, so format characters outside the BMP (tag characters) are removed too.
        foreach (System.Text.Rune rune in input.EnumerateRunes())
        {
            UnicodeCategory cat = System.Text.Rune.GetUnicodeCategory(rune);
            if (System.Text.Rune.IsControl(rune) ||
                cat == UnicodeCategory.Format ||
                cat == UnicodeCategory.LineSeparator ||
                cat == UnicodeCategory.ParagraphSeparator)
            {
                continue;
            }

            if (System.Text.Rune.IsWhiteSpace(rune))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
            }
            else
            {
                sb.Append(rune.ToString());
                inWhitespace = false;
            }
        }

        string result = sb.ToString().Trim();
        if (result.Length > maxLength)
        {
            // Never cut between the two halves of a surrogate pair.
            int cut = char.IsHighSurrogate(result[maxLength - 1]) ? maxLength - 1 : maxLength;
            result = result[..cut] + '…';
        }

        return result;
    }

    private static bool IsAllHex(string s)
    {
        foreach (char c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }
        return true;
    }
}
