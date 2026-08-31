using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public static class SoundHelper
    {
        private static readonly ILogger _log = AppLogger.Factory.CreateLogger(nameof(SoundHelper));

        public static void PlaySound(string path)
        {
            if (!File.Exists(path))
            {
                _log.LogWarning("Sound file not found: {Path}", path);
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
                            _log.LogWarning("Audio player '{Player}' timed out; killing", player);
                            proc.Kill(entireProcessTree: true);
                        }
                        return;
                    }
                    catch { }
                }
                _log.LogWarning("No supported audio player found (pw-play, paplay, aplay)");
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
                catch (Exception ex) { _log.LogError(ex, "macOS audio error"); }
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
                catch (Exception ex) { _log.LogError(ex, "Windows audio error"); }
            }
        }
    }
}
