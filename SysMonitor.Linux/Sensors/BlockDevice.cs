using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors
{
    public static class BlockDevice
    {
        private static readonly Regex NvmeRegex = new Regex(@"^((?:nvme[0-9]+n[0-9]+)|(?:mmcblk[0-9]+))(?:p([0-9]+))?$", RegexOptions.Compiled);
        private static readonly Regex ScsiRegex = new Regex(@"^((?:sd|vd|hd|xvd)[a-z]+)([0-9]+)?$");

        public static string ParentDisk(string device)
        {
            if (string.IsNullOrEmpty(device))
                return string.Empty;

            // Remove "/dev/" prefix if present
            if (device.StartsWith("/dev/", StringComparison.Ordinal))
            {
                device = device.Substring(5);
            }

            if (device.Contains("mapper/", StringComparison.Ordinal))
                return string.Empty;

            var nvmeMatch = NvmeRegex.Match(device);
            if (nvmeMatch.Success)
            {
                return nvmeMatch.Groups[1].Value;
            }

            var scsiMatch = ScsiRegex.Match(device);
            if (scsiMatch.Success)
            {
                return scsiMatch.Groups[1].Value;
            }

            return string.Empty;
        }

        public static int PartitionNumber(string device)
        {
            if (string.IsNullOrEmpty(device))
                return 0;

            // Remove "/dev/" prefix if present
            if (device.StartsWith("/dev/", StringComparison.Ordinal))
            {
                device = device.Substring(5);
            }

            if (device.Contains("mapper/", StringComparison.Ordinal))
                return 0;

            var nvmeMatch = NvmeRegex.Match(device);
            if (nvmeMatch.Success && nvmeMatch.Groups[2].Success)
            {
                if (int.TryParse(nvmeMatch.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                {
                    return n;
                }
            }

            var scsiMatch = ScsiRegex.Match(device);
            if (scsiMatch.Success && scsiMatch.Groups[2].Success)
            {
                if (int.TryParse(scsiMatch.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                {
                    return n;
                }
            }

            return 0;
        }

        private static string ReadText(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return string.Empty;
                }

                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }
        }

        public static PhysicalDisk Read(string sysRoot, string disk, int number)
        {
            if (string.IsNullOrEmpty(sysRoot) || string.IsNullOrEmpty(disk))
            {
                return new PhysicalDisk
                {
                    Number = number,
                    Bytes = 0,
                    PartitionCount = 0,
                    Rpm = 0
                };
            }

            if (disk == "." || disk == ".." || disk.Contains('/', StringComparison.Ordinal) || disk.Contains('\\', StringComparison.Ordinal) || disk.Contains('\0'))
            {
                return new PhysicalDisk
                {
                    Number = number,
                    Bytes = 0,
                    PartitionCount = 0,
                    Rpm = 0
                };
            }

            var blockPath = Path.Combine(sysRoot, "block", disk);
            if (!Directory.Exists(blockPath))
            {
                return new PhysicalDisk
                {
                    Number = number,
                    Bytes = 0,
                    PartitionCount = 0,
                    Rpm = 0
                };
            }

            var sizePath = Path.Combine(blockPath, "size");
            long bytes = 0;
            if (File.Exists(sizePath))
            {
                if (long.TryParse(ReadText(sizePath), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size > 0 && size <= long.MaxValue / 512)
                {
                    bytes = size * 512;
                }
            }

            var model = string.Empty;
            var serial = string.Empty;
            var firmware = string.Empty;
            var media = string.Empty;
            var bus = string.Empty;

            var devicePath = Path.Combine(blockPath, "device");
            if (Directory.Exists(devicePath))
            {
                var modelPath = Path.Combine(devicePath, "model");
                if (File.Exists(modelPath))
                {
                    model = ReadText(modelPath).Trim();
                }

                var serialPath = Path.Combine(devicePath, "serial");
                if (File.Exists(serialPath))
                {
                    serial = ReadText(serialPath).Trim();
                }

                var firmwarePath = Path.Combine(devicePath, "firmware_rev");
                if (File.Exists(firmwarePath))
                {
                    firmware = ReadText(firmwarePath).Trim();
                }
                else
                {
                    var revPath = Path.Combine(devicePath, "rev");
                    if (File.Exists(revPath))
                    {
                        firmware = ReadText(revPath).Trim();
                    }
                }
            }

            var rotationalPath = Path.Combine(blockPath, "queue", "rotational");
            if (File.Exists(rotationalPath))
            {
                if (int.TryParse(ReadText(rotationalPath), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rotational))
                {
                    media = rotational == 0 ? "SSD" : (rotational == 1 ? "HDD" : "");
                }
            }

            if (disk.StartsWith("nvme", StringComparison.Ordinal))
            {
                bus = "NVMe";
            }
            else if (disk.StartsWith("mmcblk", StringComparison.Ordinal))
            {
                bus = "MMC";
            }

            var partitionCount = 0;
            try
            {
                var dirEntries = Directory.GetDirectories(blockPath);
                foreach (var dir in dirEntries)
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith(disk, StringComparison.Ordinal) && File.Exists(Path.Combine(dir, "partition")))
                    {
                        partitionCount++;
                    }
                }
            }
            catch
            {
                // Ignore any exceptions
            }

            return new PhysicalDisk
            {
                Number = number,
                Model = model,
                Serial = serial,
                Firmware = firmware,
                Bus = bus,
                Interface = string.Empty,
                Media = media,
                PartitionStyle = string.Empty,
                Bytes = bytes,
                PartitionCount = partitionCount,
                Rpm = 0
            };
        }
    }
}
