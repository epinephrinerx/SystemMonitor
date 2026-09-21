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
    // The defaults a fresh install starts from. A machine that already has a
    // config file keeps whatever is in it; changing these moves nobody who is
    // already running the app.
    public string Lang { get; set; } = "en";
    public string Theme { get; set; } = "light";         // light | dark

    /// <summary>
    /// Off by default. A monitor that sits above everything is in the way more
    /// often than it is wanted; the setting is one click away for people who
    /// do want it.
    /// </summary>
    public bool AlwaysOnTop { get; set; }

    public bool Snap { get; set; } = true;
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Multiplies the whole type ramp. Bars keep their height; rows grow
    /// taller as the text in them does.
    /// </summary>
    public double FontScale { get; set; } = 1.0;
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowDisk { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool IncludeWireless { get; set; } = true;
    public string NetworkMode { get; set; } = "separated";  // separated | total
    public string CpuMode { get; set; } = "separated";   // separated | total
    public string DiskMode { get; set; } = "separated";  // separated | total
    public string Speed { get; set; } = "balanced";      // eco | balanced | fast

    /// <summary>Show '~' modelled temperatures where no sensor exists.</summary>
    public bool TempEstimate { get; set; } = true;

    /// <summary>
    /// Read the ACPI thermal zone for a real CPU temperature. On by default:
    /// unlike the Python build's admin-only WMI class, the source used here
    /// needs no elevation and costs one query every half minute.
    /// </summary>
    public bool CpuTemperature { get; set; } = true;
    public bool IncludeRemovable { get; set; } = true;

    /// <summary>
    /// Include mapped network drives. On by default, but they are probed on a
    /// longer leash than local volumes: a share whose host is asleep or behind
    /// a dropped VPN blocks until SMB gives up, which is seconds.
    /// </summary>
    public bool IncludeNetwork { get; set; } = true;
    public bool Diagnostics { get; set; } = true;

    /// <summary>
    /// What the close button does: "tray" hides the widget behind a
    /// notification-area icon, "exit" quits.
    ///
    /// Tray by default: this is a monitor, and a monitor that is gone the
    /// moment you dismiss its window is a monitor you have to keep restarting.
    /// Only a fresh install sees this -- anyone with a config file already has
    /// their own answer saved in it.
    /// </summary>
    public string CloseAction { get; set; } = "tray";

    /// <summary>
    /// Whether the tray icon has been lifted out of Windows 11's overflow
    /// flyout already. Asked once: if the user later drags it back in, that is
    /// their answer and we do not overrule it.
    /// </summary>
    public bool TrayPromoted { get; set; }

    public double? PosX { get; set; }
    public double? PosY { get; set; }

    // Panel sizes in device-independent px; WPF applies DPI scaling on top.
    public double WidgetW { get; set; } = 270;
    public double WidgetH { get; set; } = 104;   // four core bars plus the heading
    public double OverallW { get; set; } = 630;
    public double OverallH { get; set; } = 480;

    /// <summary>
    /// Has the overall view been sized by hand? Until it has, it measures its
    /// own contents and takes exactly the room they need -- the right size
    /// depends on how many cores, drives and adapters the machine has and on
    /// the font scale, so any fixed number is wrong for somebody.
    /// </summary>
    public bool OverallSized { get; set; }

    /// <summary>
    /// The full view's size. It is a window like the others now, not an
    /// OS full-screen mode, so it has a size worth remembering. Zero means
    /// "not set yet" and the view opens at its smallest, for the user to
    /// enlarge if they want to.
    /// </summary>
    public double FullW { get; set; }
    public double FullH { get; set; }

    /// <summary>Which tab the full view was last on.</summary>
    public string FullTab { get; set; } = string.Empty;

    // Resize limits, matching the Python build.
    [JsonIgnore] public static (double W, double H) MinWidget => (200, 64);
    [JsonIgnore] public static (double W, double H) MaxWidget => (900, 400);
    [JsonIgnore] public static (double W, double H) MinOverall => (470, 300);

    /// <summary>
    /// Below this the full view has no room for tabs and their contents, and
    /// drops back to the overall view rather than showing something cramped.
    /// </summary>
    [JsonIgnore] public static (double W, double H) MinFull => (640, 480);

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

    public static AppConfig Load() => LoadFrom(Path);

    /// <summary>
    /// Load from a named file. Separate from <see cref="Load"/> so the
    /// migration can be tested without writing to the real profile.
    /// </summary>
    public static AppConfig LoadFrom(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            AppConfig config = JsonSerializer.Deserialize<AppConfig>(text, Options)
                               ?? new AppConfig();
            Migrate(config, text);
            return config;
        }
        catch (Exception)
        {
            // A missing or corrupt file just means defaults; never a startup failure.
            return new AppConfig();
        }
    }

    /// <summary>
    /// Carry settings over from the key names used before the three views were
    /// named widget / overall / full.
    ///
    /// The names in this file are generated from the property names, so
    /// renaming `ExpW` to `OverallW` silently renamed `exp_w` to `overall_w`
    /// as well -- and every existing install would have opened one morning
    /// with its window back at the default size and no idea why. The old keys
    /// are read once and then written out under the new names on the next
    /// save; nothing has to be kept forever.
    /// </summary>
    private static void Migrate(AppConfig config, string text)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(text);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // Only fall back when the new key is absent: a file that has both --
        // one written by this version, one left over -- must follow the new one.
        if (!root.TryGetProperty("widget_w", out _))
        {
            config.WidgetW = Old(root, "mini_w", config.WidgetW);
            config.WidgetH = Old(root, "mini_h", config.WidgetH);
        }
        if (!root.TryGetProperty("overall_w", out _))
        {
            config.OverallW = Old(root, "exp_w", config.OverallW);
            config.OverallH = Old(root, "exp_h", config.OverallH);
            if (root.TryGetProperty("exp_sized", out JsonElement sized)
                && sized.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                config.OverallSized = sized.GetBoolean();
            }
        }
    }

    private static double Old(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double parsed)
            ? parsed
            : fallback;

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
