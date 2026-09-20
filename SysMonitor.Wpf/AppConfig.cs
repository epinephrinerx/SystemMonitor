using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysMonitor;

/// <summary>
/// Settings persisted to %APPDATA%\SysMonitor\config.wpf.json.
///
/// Deliberately a separate file from the Python build's config.json: both can
/// be installed at once during the port, and neither should clobber the
/// other's window position or display choices.
/// </summary>
public sealed class AppConfig
{
    public string Lang { get; set; } = "th";
    public string Theme { get; set; } = "dark";          // dark | light
    public bool AlwaysOnTop { get; set; } = true;
    public bool Snap { get; set; } = true;
    public double Opacity { get; set; } = 0.92;
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowDisk { get; set; } = true;
    public string CpuMode { get; set; } = "separated";   // separated | total
    public string DiskMode { get; set; } = "separated";  // separated | total
    public string Speed { get; set; } = "balanced";      // eco | balanced | fast

    /// <summary>Show '~' modelled temperatures where no sensor exists.</summary>
    public bool TempEstimate { get; set; } = true;

    /// <summary>ACPI thermal zone: needs admin and spawns a process, so opt-in.</summary>
    public bool WmiCpuTemp { get; set; }
    public bool IncludeRemovable { get; set; } = true;
    public bool Diagnostics { get; set; } = true;

    public double? PosX { get; set; }
    public double? PosY { get; set; }

    // Panel sizes in device-independent px; WPF applies DPI scaling on top.
    public double MiniW { get; set; } = 270;
    public double MiniH { get; set; } = 104;   // four core bars plus the heading
    public double ExpW { get; set; } = 630;
    public double ExpH { get; set; } = 480;

    // Resize limits, matching the Python build.
    [JsonIgnore] public static (double W, double H) MinMini => (200, 64);
    [JsonIgnore] public static (double W, double H) MaxMini => (900, 400);
    [JsonIgnore] public static (double W, double H) MinExp => (470, 300);

    /// <summary>Sampling cadence per speed mode: metrics, disk usage, temperatures.</summary>
    [JsonIgnore]
    public (TimeSpan Metrics, TimeSpan Space, TimeSpan Temps) Intervals => Speed switch
    {
        "eco" => (TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)),
        "fast" => (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)),
        _ => (TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)),
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SysMonitor", "config.wpf.json");

    public static AppConfig Load()
    {
        try
        {
            string text = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<AppConfig>(text, Options) ?? new AppConfig();
        }
        catch (Exception)
        {
            // A missing or corrupt file just means defaults; never a startup failure.
            return new AppConfig();
        }
    }

    public void Save()
    {
        try
        {
            string path = Path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception)
        {
            // Settings are a convenience; a read-only profile must not crash us.
        }
    }
}
