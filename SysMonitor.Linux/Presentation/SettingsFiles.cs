using System;
using System.IO;

namespace SysMonitor.Linux.Presentation
{
    public static class ConfigPaths
    {
        public static string? ConfigFile(string? xdgConfigHome, string? home)
        {
            string? x = xdgConfigHome?.Trim();
            if (!string.IsNullOrEmpty(x) && Path.IsPathRooted(x))
            {
                return Path.Combine(x, "sysmonitor", "config.linux.json");
            }

            string? h = home?.Trim();
            if (!string.IsNullOrEmpty(h) && Path.IsPathRooted(h))
            {
                return Path.Combine(h, ".config", "sysmonitor", "config.linux.json");
            }

            return null;
        }
    }

    public static class SettingsStore
    {
        public static AppSettings Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new AppSettings();
            }

            try
            {
                if (Directory.Exists(path))
                {
                    return new AppSettings();
                }

                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > 1048576)
                {
                    return new AppSettings();
                }

                return AppSettings.Parse(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return new AppSettings();
            }
        }

        public static bool Save(string? path, AppSettings? s)
        {
            if (string.IsNullOrWhiteSpace(path) || s == null)
            {
                return false;
            }

            s.Sanitize();
            string tmp = path + "." + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".tmp";
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(tmp, s.ToJson());
                File.Move(tmp, path, true);
                return true;
            }
            catch (Exception)
            {
                try
                {
                    if (File.Exists(tmp))
                    {
                        File.Delete(tmp);
                    }
                }
                catch (Exception)
                {
                }

                return false;
            }
        }
    }
}
