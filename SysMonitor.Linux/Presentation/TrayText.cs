using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The tray tooltip from spec section 9:
/// <c>SysMonitor\nCPU N%  ·  RAM N%</c> plus <c>  ·  [~]N°C</c> when there is a
/// processor temperature; bare <c>SysMonitor</c> before the first sample.
///
/// The 63-character trim is a Windows NotifyIcon limit (spec section 11), so
/// Linux keeps the whole line.
/// </summary>
public static class TrayText
{
    /// <summary>Hard-coded and never translated (spec section 12).</summary>
    public const string AppName = "SysMonitor";

    public static string Build(Snapshot? snapshot, Lang? lang = null)
    {
        if (snapshot is not { Ready: true })
        {
            return AppName;
        }

        Lang table = lang ?? new Lang("en");
        string text =
            $"{AppName}\n{table["cpu"]} {snapshot.CpuTotal.ToString(CultureInfo.InvariantCulture)}%" +
            $"  ·  {table["memory"]} {snapshot.Ram.Usage.ToString(CultureInfo.InvariantCulture)}%";

        if (snapshot.CpuTemp is { } temperature)
        {
            text += "  ·  "
                + (snapshot.CpuTempEstimated ? "~" : string.Empty)
                + temperature.ToString(CultureInfo.InvariantCulture)
                + "°C";
        }
        return text;
    }
}
