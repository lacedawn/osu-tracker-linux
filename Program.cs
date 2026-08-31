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

        private static Mutex? _singleInstanceMutex;
        private static FileStream? _lockFile;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                _singleInstanceMutex = new Mutex(
                    initiallyOwned: true,
                    name: "Global\\circle-tracker-singleton",
                    out bool createdNew);
                if (!createdNew)
                {
                    _log.LogError("Another instance is already running");
                    _singleInstanceMutex.Dispose();
                    return;
                }
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is PlatformNotSupportedException)
            {
                string lockPath = Path.Combine(
                    Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/tmp",
                    "circle-tracker.lock");
                try
                {
                    _lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    _log.LogError("Another instance is already running (lock file)");
                    return;
                }
            }

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                if (_singleInstanceMutex != null)
                {
                    try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                    _singleInstanceMutex.Dispose();
                }
                if (_lockFile != null)
                {
                    _lockFile.Dispose();
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
