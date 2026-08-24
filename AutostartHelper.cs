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

        public static void CreateAutostart()
        {
            if (!OperatingSystem.IsLinux()) return;

            string path = GetAutostartFilePath();
            string? dir = Path.GetDirectoryName(path);
            if (dir != null)
                Directory.CreateDirectory(dir);

            string exe = Process.GetCurrentProcess().MainModule?.FileName ?? AppName;

            File.WriteAllText(path,
                $"""
                [Desktop Entry]
                Type=Application
                Name=Circle Tracker
                Comment=osu! training session tracker
                Exec={exe}
                Icon={AppName}
                X-GNOME-Autostart-enabled=true
                X-KDE-autostart-after=panel
                """);
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
