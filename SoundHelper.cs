using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public static class SoundHelper
    {
        public static void PlaySound(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[SoundHelper] Sound file not found: {path}");
                return;
            }
            _ = Task.Run(() => PlaySoundInternal(path));
        }

        private static void PlaySoundInternal(string path)
        {
            if (OperatingSystem.IsLinux())
            {
                string[] players = { "pw-play", "paplay", "aplay" };
                foreach (var player in players)
                {
                    try
                    {
                        var psi = new ProcessStartInfo(player, $"\"{path}\"")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        var proc = Process.Start(psi);
                        return;
                    }
                    catch { }
                }
                Console.WriteLine("[SoundHelper] Failed to play sound: no supported audio player found (pw-play, paplay, aplay).");
            }
            else if (OperatingSystem.IsMacOS())
            {
                try
                {
                    var psi = new ProcessStartInfo("afplay", $"\"{path}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    var proc = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SoundHelper] macOS audio error: {ex.Message}");
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                try
                {
                    var psi = new ProcessStartInfo(
                        "powershell",
                        $"-c \"(New-Object Media.SoundPlayer '{path}').Play()\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    var proc = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SoundHelper] Windows audio error: {ex.Message}");
                }
            }
        }
    }
}
