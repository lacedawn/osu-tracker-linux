using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
            _ = Task.Run(() => PlaySoundInternalAsync(path));
        }

        private static async Task PlaySoundInternalAsync(string path)
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
                        using var proc = Process.Start(psi);
                        if (proc == null) continue;
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        try { await proc.WaitForExitAsync(cts.Token); }
                        catch (OperationCanceledException)
                        {
                            Console.WriteLine($"[SoundHelper] Audio player '{player}' timed out; killing.");
                            proc.Kill(entireProcessTree: true);
                        }
                        return;
                    }
                    catch { }
                }
                Console.WriteLine("[SoundHelper] No supported audio player found (pw-play, paplay, aplay).");
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
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        try { await proc.WaitForExitAsync(cts.Token); }
                        catch (OperationCanceledException) { proc.Kill(entireProcessTree: true); }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[SoundHelper] macOS audio error: {ex.Message}"); }
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
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        try { await proc.WaitForExitAsync(cts.Token); }
                        catch (OperationCanceledException) { proc.Kill(entireProcessTree: true); }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[SoundHelper] Windows audio error: {ex.Message}"); }
            }
        }
    }
}
