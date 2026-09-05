using Avalonia;
using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Circle_Tracker.Sync;
using Circle_Tracker.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace Circle_Tracker
{
    class Program
    {
        private static readonly ILogger<Program> _log = AppLogger.For<Program>();
        private static FileStream? _lockFile;
        private static IServiceProvider? _serviceProvider;

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
                _serviceProvider = ConfigureServices();
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

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<ITosuClient, TosuClient>();
            services.AddSingleton<ITrackerService, TrackerService>();
            services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<ITrackerService>().SessionManager);
            services.AddSingleton<IDatabaseManager>(sp => sp.GetRequiredService<ISessionManager>().GetDatabaseManager());

            services.AddSingleton<ISessionAnalyticsService>(sp =>
                new SessionAnalyticsService(sp.GetRequiredService<IDatabaseManager>()));
            services.AddSingleton<ISkillAnalyticsService>(sp =>
                new SkillAnalyticsService(sp.GetRequiredService<IDatabaseManager>()));

            services.AddSingleton<IPlayQueryEngine>(sp =>
                new SqlitePlayQueryEngine(sp.GetRequiredService<IDatabaseManager>()));

            services.AddSingleton<IDataExportService>(sp =>
                new DataExportService(sp.GetRequiredService<IDatabaseManager>(), sp.GetRequiredService<IPlayQueryEngine>()));

            services.AddSingleton<ILiveSessionTracker, LiveSessionTracker>();

            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<AnalyticsViewModel>();

            return services.BuildServiceProvider();
        }

        public static IServiceProvider? GetServiceProvider() => _serviceProvider;
    }
}
