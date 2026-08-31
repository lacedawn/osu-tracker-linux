using Avalonia;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;

namespace Circle_Tracker
{
    class Program
    {
        private static readonly ILogger<Program> _log = AppLogger.For<Program>();

        private static FileStream? _lockFile;

        [STAThread]
        public static void Main(string[] args)
        {
            string lockDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
            string lockPath = Path.Combine(lockDir, "circle-tracker.lock");
            try
            {
                _lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                _log.LogError("Another instance is already running");
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not acquire lock file, continuing startup");
            }

            try
            {
                _log.LogInformation("Circle Tracker started");
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                if (_lockFile != null)
                {
                    try { _lockFile.Dispose(); } catch { }
                    try { if (File.Exists(lockPath)) File.Delete(lockPath); } catch { }
                }
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
