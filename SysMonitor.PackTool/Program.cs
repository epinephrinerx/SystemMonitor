namespace SysMonitor.PackTool;

public static class Program
{
    public static int Main(string[] args)
    {
        string? templatePath = null;
        string? outDir = null;
        string? version = null;
        string? allowlistPath = null;
        var fileSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase))
            {
                PrintUsage();
                return 2;
            }

            if (string.Equals(arg, "--template", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: Missing value for --template");
                    return 2;
                }
                templatePath = args[++i];
            }
            else if (string.Equals(arg, "--out", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: Missing value for --out");
                    return 2;
                }
                outDir = args[++i];
            }
            else if (string.Equals(arg, "--version", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: Missing value for --version");
                    return 2;
                }
                version = args[++i];
            }
            else if (string.Equals(arg, "--allowlist", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: Missing value for --allowlist");
                    return 2;
                }
                allowlistPath = args[++i];
            }
            else if (string.Equals(arg, "--file", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: Missing value for --file");
                    return 2;
                }
                string fileArg = args[++i];
                int eq = fileArg.IndexOf('=');
                if (eq <= 0 || eq == fileArg.Length - 1)
                {
                    Console.Error.WriteLine($"Error: Invalid --file argument '{fileArg}'. Expected <name>=<path>");
                    return 2;
                }
                string name = fileArg.Substring(0, eq).Trim();
                string path = fileArg.Substring(eq + 1).Trim();
                fileSources[name] = path;
            }
            else
            {
                Console.Error.WriteLine($"Error: Unrecognized option '{arg}'");
                PrintUsage();
                return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(templatePath) ||
            string.IsNullOrWhiteSpace(outDir) ||
            string.IsNullOrWhiteSpace(version))
        {
            Console.Error.WriteLine("Error: --template, --out, and --version are required.");
            PrintUsage();
            return 2;
        }

        try
        {
            var options = new PackOptions(
                TemplatePath: templatePath,
                OutDir: outDir,
                Version: version,
                FileSources: fileSources,
                AllowlistPath: allowlistPath
            );

            PackResult result = ExtensionPacker.Pack(options);

            // One line: packed <id> <manifestSha256> <fileCount> files
            Console.WriteLine($"packed {result.Id} {result.ManifestSha256} {result.Manifest.Files.Count} files");
            return 0;
        }
        catch (PackException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: SysMonitor.PackTool.exe --template <extension.template.json> --out <dir> --version <x.y.z> [--file <name>=<path>]... [--allowlist <file>]");
    }
}
