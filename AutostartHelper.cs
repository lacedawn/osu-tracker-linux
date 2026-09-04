using System;
using System.Diagnostics;
using System.IO;

namespace Circle_Tracker
{
    public static class AutostartHelper
    {
        private const string AppName = "circle-tracker";

        private static string GetAutostartFilePath()
        {
            if (OperatingSystem.IsLinux())
            {
                string autostartDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "autostart");
                return Path.Combine(autostartDir, $"{AppName}.desktop");
            }
            return string.Empty;
        }

        public static bool AutostartExists()
        {
            string path = GetAutostartFilePath();
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public static string FormatExec(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                return AppName;
            }

            if (exePath.StartsWith("flatpak run ", StringComparison.OrdinalIgnoreCase))
            {
                return exePath;
            }

            if (exePath.Contains(' ') && !exePath.StartsWith('"'))
            {
                return $"\"{exePath}\"";
            }

            return exePath;
        }

        public static string GetExecutablePath()
        {
            string? flatpakId = Environment.GetEnvironmentVariable("FLATPAK_ID");
            if (!string.IsNullOrEmpty(flatpakId))
            {
                return $"flatpak run {flatpakId}";
            }

            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? AppName;
            return FormatExec(exePath);
        }

        public static string GenerateDesktopEntry(string? exec = null)
        {
            string exe = exec != null ? FormatExec(exec) : GetExecutablePath();
            return $"""
[Desktop Entry]
Type=Application
Name=Circle Tracker
Comment=osu! training session tracker
Exec={exe}
Icon={AppName}
X-GNOME-Autostart-enabled=true
X-KDE-autostart-after=panel
""";
        }

        public static void CreateAutostart()
        {
            if (!OperatingSystem.IsLinux()) return;

            string path = GetAutostartFilePath();
            string? dir = Path.GetDirectoryName(path);
            if (dir != null)
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, GenerateDesktopEntry());
        }

        public static void DeleteAutostart()
        {
            string path = GetAutostartFilePath();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { File.Delete(path); }
                catch { }
            }
        }
    }
}
