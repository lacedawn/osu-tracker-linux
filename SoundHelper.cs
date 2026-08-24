using System.Diagnostics;
using System.IO;

namespace Circle_Tracker
{
    public static class SoundHelper
    {
        public static void PlaySound(string path)
        {
            if (!File.Exists(path))
                return;

            if (System.OperatingSystem.IsLinux())
            {
                try
                {
                    var psi = new ProcessStartInfo("paplay", $"\"{path}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                    return;
                }
                catch { }

                try
                {
                    var psi = new ProcessStartInfo("aplay", $"\"{path}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                }
                catch { }
            }
            else if (System.OperatingSystem.IsMacOS())
            {
                try
                {
                    var psi = new ProcessStartInfo("afplay", $"\"{path}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                }
                catch { }
            }
            else if (System.OperatingSystem.IsWindows())
            {
                try
                {
                    var psi = new ProcessStartInfo(
                        "powershell",
                        $"-c \"(New-Object Media.SoundPlayer '{path}').PlaySync()\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                }
                catch { }
            }
        }
    }
}
