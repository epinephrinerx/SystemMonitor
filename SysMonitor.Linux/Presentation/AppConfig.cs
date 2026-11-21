using System.Text.Json;

namespace SysMonitor.Linux.Presentation;

/// <summary>One extension's saved state. Only the enable flag exists in this phase.</summary>
public sealed class ExtensionOption
{
    public bool Enabled { get; set; }
}

/// <summary>
/// The settings file, with the Windows 4.3.0 keys and defaults (spec section 7)
/// minus the Windows-only ones.
///
/// Linux path: <c>$XDG_CONFIG_HOME/SystemMonitor/config.json</c>, defaulting to
/// <c>~/.config/SystemMonitor/config.json</c>. A missing or corrupt file never
/// stops the app: the defaults are used, and a corrupt file is kept next to the
/// original as <c>config.json.bad</c>.
/// </summary>
public sealed class AppConfig
{
    public const string FileName = "config.json";

    // General
    public string Lang { get; set; } = "en";
    public string Theme { get; set; } = "light";
    public bool AlwaysOnTop { get; set; }
    public bool Snap { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public double FontScale { get; set; } = 1.0;

    // Display
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowDisk { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool IncludeWireless { get; set; } = true;
    public string NetworkMode { get; set; } = "separated";
    public string CpuMode { get; set; } = "separated";
    public string DiskMode { get; set; } = "separated";
    public string Speed { get; set; } = "balanced";
    public bool TempEstimate { get; set; } = true;
    public bool CpuTemperature { get; set; } = true;
    public bool IncludeRemovable { get; set; } = true;
    public bool IncludeNetwork { get; set; } = true;
    public bool Diagnostics { get; set; } = true;
    public string CloseAction { get; set; } = "tray";

    // Apps / Processes grids. The column names are ProcessGrid.Key values;
    // Normalise turns an unknown value back into the default.
    public string AppsSort { get; set; } = "memory";
    public bool AppsSortDesc { get; set; } = true;
    public string ProcsSort { get; set; } = "memory";
    public bool ProcsSortDesc { get; set; } = true;

    // Main window (R-030). `main_page` is the page key from PageKeys; the
    // sidebar width is clamped 200-320 DIPs.
    public string MainPage { get; set; } = "overall";
    public bool SettingsOpen { get; set; }
    public int SidebarWidth { get; set; } = 230;

    /// <summary>Legacy key only: read for migration, never written again.</summary>
    public bool? ShowSmart { get; set; }

    // Placement. One position is shared by every view (R-024); the per-view
    // keys below still exist in files written by older builds but are neither
    // read nor written any more.
    public double? PosX { get; set; }
    public double? PosY { get; set; }
    public double WidgetW { get; set; } = 270;
    public double WidgetH { get; set; } = 104;
    public double OverallW { get; set; } = 630;
    public double OverallH { get; set; } = 480;
    public bool OverallSized { get; set; }
    public double FullW { get; set; }
    public double FullH { get; set; }

    /// <summary>The Main window's panel size; seeded from Full's on migration.</summary>
    public double MainW { get; set; }
    public double MainH { get; set; }

    public string LastView { get; set; } = "widget";

    /// <summary>Legacy key only: read for migration, never written again.</summary>
    public string FullTab { get; set; } = string.Empty;

    /// <summary>Case-insensitive id -> { enabled }.</summary>
    public Dictionary<string, ExtensionOption> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The metrics interval for the chosen speed (spec section 7).</summary>
    public int MetricsIntervalMs => Speed switch
    {
        "eco" => 5000,
        "fast" => 1000,
        _ => 2500,
    };

    public static string ConfigDirectory
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
            {
                return Path.Combine(xdg, "SystemMonitor");
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            }
            return Path.Combine(home, ".config", "SystemMonitor");
        }
    }

    public static string DefaultPath => Path.Combine(ConfigDirectory, FileName);

    /// <summary>
    /// Load, falling back to defaults. A file that cannot be parsed is renamed
    /// to <c>.bad</c> so the next save does not quietly destroy the evidence.
    /// </summary>
    public static AppConfig Load(string? path = null)
    {
        string file = path ?? DefaultPath;
        string? text = null;
        try
        {
            if (File.Exists(file))
            {
                text = File.ReadAllText(file);
            }
        }
        catch (Exception)
        {
            text = null;
        }

        if (text is null)
        {
            return new AppConfig();
        }

        AppConfig config = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Root is not an object.");
            }
            config.Read(document.RootElement);
        }
        catch (Exception)
        {
            try
            {
                File.Move(file, file + ".bad", overwrite: true);
            }
            catch (Exception)
            {
                // Keeping the bad file is a courtesy, not a requirement.
            }
            return new AppConfig();
        }

        config.Normalise();
        return config;
    }

    /// <summary>Write atomically: a temp file, then a rename over the real one.</summary>
    public bool Save(string? path = null)
    {
        string file = path ?? DefaultPath;
        string temp = file + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                Write(writer);
            }
            File.Move(temp, file, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception)
            {
                // Nothing useful left to do.
            }
            return false;
        }
    }

    /// <summary>Pull the values that came from user input back into their legal ranges.</summary>
    public void Normalise()
    {
        Lang = Text(Lang, "th", "en", "en");
        Theme = Text(Theme, "light", "dark", "light");
        CloseAction = Text(CloseAction, "tray", "exit", "tray");
        Speed = Text(Speed, "eco", "balanced", "fast", "balanced");
        NetworkMode = Text(NetworkMode, "separated", "total", "separated");
        CpuMode = Text(CpuMode, "separated", "total", "separated");
        DiskMode = Text(DiskMode, "separated", "total", "separated");

        // R-030: Overall and Full are one window now, so both legacy values
        // read as Main. An unknown value still means the Widget.
        LastView = string.Equals(LastView, "main", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(LastView, "overall", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(LastView, "full", StringComparison.OrdinalIgnoreCase)
            ? "main"
            : "widget";

        MainPage = LegacyTab(MainPage);
        if (!PageKeys.IsPageKey(MainPage))
        {
            MainPage = PageKeys.Overall;
        }
        SidebarWidth = MainState.ClampWidth(SidebarWidth);

        AppsSort = ProcessGrid.Key(ProcessGrid.ParseColumn(AppsSort, GridColumn.Memory));
        ProcsSort = ProcessGrid.Key(ProcessGrid.ParseColumn(ProcsSort, GridColumn.Memory));

        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.35, 1.0) : 1.0;
        FontScale = TypeRamp.ClampScale(FontScale);

        WidgetW = Limit(WidgetW, 270, 200, 900);
        WidgetH = Limit(WidgetH, 104, 64, 400);
        OverallW = Limit(OverallW, 630, 470, 4000);
        OverallH = Limit(OverallH, 480, 300, 3000);

        // Zero means "unset" for Full; anything else must respect its limits.
        if (FullW > 0)
        {
            FullW = Math.Clamp(FullW, 640, 4000);
        }
        if (FullH > 0)
        {
            FullH = Math.Clamp(FullH, 480, 3000);
        }
        if (FullW < 0)
        {
            FullW = 0;
        }
        if (FullH < 0)
        {
            FullH = 0;
        }

        // Main's geometry works like Full's: zero means unset.
        if (MainW > 0)
        {
            MainW = Math.Clamp(MainW, 640, 4000);
        }
        else
        {
            MainW = 0;
        }
        if (MainH > 0)
        {
            MainH = Math.Clamp(MainH, 480, 3000);
        }
        else
        {
            MainH = 0;
        }

        if (Extensions is null)
        {
            Extensions = new Dictionary<string, ExtensionOption>(StringComparer.OrdinalIgnoreCase);
        }
        else if (!ReferenceEquals(Extensions.Comparer, StringComparer.OrdinalIgnoreCase))
        {
            Extensions = new Dictionary<string, ExtensionOption>(
                Extensions, StringComparer.OrdinalIgnoreCase);
        }

        // show_smart was migrated to extensions.smart.enabled and is not written.
        if (ShowSmart is { } legacy)
        {
            if (!Extensions.ContainsKey("smart"))
            {
                Extensions["smart"] = new ExtensionOption { Enabled = legacy };
            }
            ShowSmart = null;
        }

        // Old Full tab keys from before extensions.
        FullTab = LegacyTab(FullTab);
        if (string.IsNullOrWhiteSpace(FullTab))
        {
            FullTab = string.Empty;
        }
    }

    /// <summary>
    /// The pre-extension Full tab names: "smart" was the status page and
    /// "smart&lt;n&gt;" one disk's SMART. Kept because both the old `full_tab`
    /// and a migrated `main_page` can still hold them.
    /// </summary>
    private static string LegacyTab(string? key)
    {
        string value = key ?? string.Empty;
        if (value.Equals("smart", StringComparison.OrdinalIgnoreCase))
        {
            return "ext:smart:status";
        }
        if (value.StartsWith("smart", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value[5..], out int number))
        {
            return "ext:smart:disk" + number;
        }
        return value.Trim();
    }

    private static double Limit(double value, double fallback, double min, double max)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return fallback;
        }
        return Math.Clamp(value, min, max);
    }

    private static string Text(string? value, string a, string b, string fallback) =>
        string.Equals(value, a, StringComparison.OrdinalIgnoreCase) ? a
        : string.Equals(value, b, StringComparison.OrdinalIgnoreCase) ? b
        : fallback;

    private static string Text(string? value, string a, string b, string c, string fallback) =>
        string.Equals(value, a, StringComparison.OrdinalIgnoreCase) ? a
        : string.Equals(value, b, StringComparison.OrdinalIgnoreCase) ? b
        : string.Equals(value, c, StringComparison.OrdinalIgnoreCase) ? c
        : fallback;

    private void Read(JsonElement root)
    {
        // Only values present in the file override the defaults, so a partial
        // or hand-written file is a patch rather than a wipe.
        Lang = String(root, "lang") ?? Lang;
        Theme = String(root, "theme") ?? Theme;
        AlwaysOnTop = Bool(root, "always_on_top") ?? AlwaysOnTop;
        Snap = Bool(root, "snap") ?? Snap;
        AutoCheckUpdates = Bool(root, "auto_check_updates") ?? AutoCheckUpdates;
        Opacity = Number(root, "opacity") ?? Opacity;
        FontScale = Number(root, "font_scale") ?? FontScale;

        ShowCpu = Bool(root, "show_cpu") ?? ShowCpu;
        ShowRam = Bool(root, "show_ram") ?? ShowRam;
        ShowDisk = Bool(root, "show_disk") ?? ShowDisk;
        ShowNetwork = Bool(root, "show_network") ?? ShowNetwork;
        ShowGpu = Bool(root, "show_gpu") ?? ShowGpu;
        ShowBattery = Bool(root, "show_battery") ?? ShowBattery;
        IncludeWireless = Bool(root, "include_wireless") ?? IncludeWireless;
        NetworkMode = String(root, "network_mode") ?? NetworkMode;
        CpuMode = String(root, "cpu_mode") ?? CpuMode;
        DiskMode = String(root, "disk_mode") ?? DiskMode;
        Speed = String(root, "speed") ?? Speed;
        TempEstimate = Bool(root, "temp_estimate") ?? TempEstimate;
        CpuTemperature = Bool(root, "cpu_temperature") ?? CpuTemperature;
        IncludeRemovable = Bool(root, "include_removable") ?? IncludeRemovable;
        IncludeNetwork = Bool(root, "include_network") ?? IncludeNetwork;
        Diagnostics = Bool(root, "diagnostics") ?? Diagnostics;
        CloseAction = String(root, "close_action") ?? CloseAction;
        AppsSort = String(root, "apps_sort") ?? AppsSort;
        AppsSortDesc = Bool(root, "apps_sort_desc") ?? AppsSortDesc;
        ProcsSort = String(root, "procs_sort") ?? ProcsSort;
        ProcsSortDesc = Bool(root, "procs_sort_desc") ?? ProcsSortDesc;
        ShowSmart = Bool(root, "show_smart") ?? ShowSmart;

        PosX = Number(root, "pos_x") ?? PosX;
        PosY = Number(root, "pos_y") ?? PosY;

        // Legacy names are read only when the new key is absent.
        WidgetW = Number(root, "widget_w") ?? Number(root, "mini_w") ?? WidgetW;
        WidgetH = Number(root, "widget_h") ?? Number(root, "mini_h") ?? WidgetH;
        OverallW = Number(root, "overall_w") ?? Number(root, "exp_w") ?? OverallW;
        OverallH = Number(root, "overall_h") ?? Number(root, "exp_h") ?? OverallH;
        OverallSized = Bool(root, "overall_sized") ?? Bool(root, "exp_sized") ?? OverallSized;

        FullW = Number(root, "full_w") ?? FullW;
        FullH = Number(root, "full_h") ?? FullH;
        LastView = String(root, "last_view") ?? LastView;
        FullTab = String(root, "full_tab") ?? FullTab;

        MainW = Number(root, "main_w") ?? MainW;
        MainH = Number(root, "main_h") ?? MainH;
        MainPage = String(root, "main_page") ?? MainPage;
        SettingsOpen = Bool(root, "settings_open") ?? SettingsOpen;
        SidebarWidth = Integer(root, "sidebar_width") ?? SidebarWidth;

        // R-030 migration: a file without `main_page` was written by the
        // three-view build. Its last view, remembered tab and Full geometry
        // seed the one main window, once; from then on main_* is the source.
        if (!root.TryGetProperty("main_page", out _))
        {
            if (MainW <= 0 && FullW > 0)
            {
                MainW = FullW;
            }
            if (MainH <= 0 && FullH > 0)
            {
                MainH = FullH;
            }
            if (!string.IsNullOrWhiteSpace(FullTab))
            {
                MainPage = FullTab;
            }
            if (string.Equals(LastView, "overall", StringComparison.OrdinalIgnoreCase)
                || string.Equals(LastView, "full", StringComparison.OrdinalIgnoreCase))
            {
                LastView = "main";
            }
        }

        if (root.TryGetProperty("extensions", out JsonElement extensions)
            && extensions.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in extensions.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object
                    && property.Value.TryGetProperty("enabled", out JsonElement enabled)
                    && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    Extensions[property.Name] = new ExtensionOption { Enabled = enabled.GetBoolean() };
                }
            }
        }
    }

    private void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("lang", Lang);
        writer.WriteString("theme", Theme);
        writer.WriteBoolean("always_on_top", AlwaysOnTop);
        writer.WriteBoolean("snap", Snap);
        writer.WriteBoolean("auto_check_updates", AutoCheckUpdates);
        writer.WriteNumber("opacity", Math.Round(Opacity, 3));
        writer.WriteNumber("font_scale", Math.Round(FontScale, 2));

        writer.WriteBoolean("show_cpu", ShowCpu);
        writer.WriteBoolean("show_ram", ShowRam);
        writer.WriteBoolean("show_disk", ShowDisk);
        writer.WriteBoolean("show_network", ShowNetwork);
        writer.WriteBoolean("show_gpu", ShowGpu);
        writer.WriteBoolean("show_battery", ShowBattery);
        writer.WriteBoolean("include_wireless", IncludeWireless);
        writer.WriteString("network_mode", NetworkMode);
        writer.WriteString("cpu_mode", CpuMode);
        writer.WriteString("disk_mode", DiskMode);
        writer.WriteString("speed", Speed);
        writer.WriteBoolean("temp_estimate", TempEstimate);
        writer.WriteBoolean("cpu_temperature", CpuTemperature);
        writer.WriteBoolean("include_removable", IncludeRemovable);
        writer.WriteBoolean("include_network", IncludeNetwork);
        writer.WriteBoolean("diagnostics", Diagnostics);
        writer.WriteString("close_action", CloseAction);
        writer.WriteString("apps_sort", AppsSort);
        writer.WriteBoolean("apps_sort_desc", AppsSortDesc);
        writer.WriteString("procs_sort", ProcsSort);
        writer.WriteBoolean("procs_sort_desc", ProcsSortDesc);

        WriteNumber(writer, "pos_x", PosX);
        WriteNumber(writer, "pos_y", PosY);

        writer.WriteNumber("widget_w", WidgetW);
        writer.WriteNumber("widget_h", WidgetH);
        writer.WriteNumber("overall_w", OverallW);
        writer.WriteNumber("overall_h", OverallH);
        writer.WriteBoolean("overall_sized", OverallSized);
        writer.WriteNumber("full_w", FullW);
        writer.WriteNumber("full_h", FullH);
        writer.WriteNumber("main_w", MainW);
        writer.WriteNumber("main_h", MainH);
        writer.WriteBoolean("settings_open", SettingsOpen);
        writer.WriteNumber("sidebar_width", SidebarWidth);
        writer.WriteString("main_page", MainPage);
        writer.WriteString("last_view", LastView);

        writer.WriteStartObject("extensions");
        foreach (KeyValuePair<string, ExtensionOption> entry in Extensions)
        {
            writer.WriteStartObject(entry.Key);
            writer.WriteBoolean("enabled", entry.Value.Enabled);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static int? Integer(JsonElement root, string name)
    {
        double? number = Number(root, name);
        if (number is not double value || !double.IsFinite(value))
        {
            return null;
        }
        return (int)Math.Round(Math.Clamp(value, int.MinValue, int.MaxValue));
    }

    private static double? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return double.IsFinite(number) ? number : null;
        }
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            && double.IsFinite(parsed))
        {
            return parsed;
        }
        return null;
    }
}
