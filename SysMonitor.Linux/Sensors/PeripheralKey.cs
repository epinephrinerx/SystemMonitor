using System.Security.Cryptography;
using System.Text;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// The opaque identity of a peripheral battery.
///
/// UPower identifies a device by an object path that contains the Bluetooth
/// address, and by a native path or serial that identifies the hardware itself.
/// None of those may reach the model, the log or the probe, because they
/// identify the person's device and travel into text a person pastes into a
/// report. A short hash keeps a stable key -- the same device keeps the same id
/// across samples, so the views can remember it -- without carrying anything
/// that could be traced back.
/// </summary>
internal static class PeripheralKey
{
    /// <summary>Twelve hex characters is 48 bits: no collisions in a list of peripherals.</summary>
    private const int Length = 12;

    public static string ForUpower(string objectPath) => Hash("upower:" + objectPath);

    public static string ForSysfs(string name) => Hash("sysfs:" + name);

    private static string Hash(string source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..Length];
}
