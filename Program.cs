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
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    class Program
    {
        private static readonly ILogger<Program> _log = AppLogger.For<Program>();
        private static FileStream? _lockFile;
        private static IServiceProvider? _serviceProvider;

        [STAThread]
        public static int Main(string[] args)
        {
            if (TryHandleHeadlessArgs(args, out int headlessExit))
            {
                return headlessExit;
            }

            string lockPath = SingleInstanceLock.GetLockFilePath();
            if (!SingleInstanceLock.PrefersXdgRuntimeDir(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")))
            {
                _log.LogWarning("XDG_RUNTIME_DIR is not set; lock file {LockPath} lives under /tmp where symlinks can hijack it. Prefer XDG_RUNTIME_DIR.", lockPath);
            }
            try
            {
                _lockFile = SingleInstanceLock.TryAcquire(lockPath);
            }
            catch (IOException)
            {
                _log.LogError("Another instance is already running");
                return 1;
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogError(ex, "Another instance is already running (lock file inaccessible)");
                return 1;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not acquire lock file, continuing startup");
            }

            try
            {
                _log.LogInformation("Circle Tracker started");
                _serviceProvider = ConfigureServices();
                ConfigureTosuEndpoint(_serviceProvider);
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                try
                {
                    ShutdownAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Error during shutdown drain");
                }

                SingleInstanceLock.Release(_lockFile, lockPath);
            }

            return 0;
        }

        public static bool TryHandleHeadlessArgs(string[] args, out int exitCode)
        {
            exitCode = 0;

            foreach (string arg in args)
            {
                if (arg == "--help" || arg == "-h")
                {
                    Console.WriteLine("circle-tracker [options]");
                    Console.WriteLine("  --help        Show this help");
                    Console.WriteLine("  --version     Show version");
                    Console.WriteLine("  --smoke-test  Run headless self-check and exit");
                    exitCode = 0;
                    return true;
                }

                if (arg == "--version" || arg == "-v")
                {
                    Console.WriteLine(GetAppVersion());
                    exitCode = 0;
                    return true;
                }

                if (arg == "--smoke-test")
                {
                    exitCode = RunSmokeTest();
                    return true;
                }
            }

            return false;
        }

        public static string GetAppVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "0.0.0";
        }

        public static int RunSmokeTest()
        {
            try
            {
                string soundPath = SettingsService.FindFile(Path.Combine("assets", "sectionpass.wav"));

                if (!File.Exists(soundPath))
                {
                    Console.Error.WriteLine($"smoke-test failed: missing {soundPath}");
                    return 1;
                }

                AppPaths.EnsureDirectories();

                string smokeJson = Path.Combine(Path.GetTempPath(), $"ct_smoke_{Guid.NewGuid():N}.json");
                string smokeTxt = Path.Combine(Path.GetTempPath(), $"ct_smoke_{Guid.NewGuid():N}.txt");
                var settings = new SettingsService(smokeJson, smokeTxt);
                string host = SettingsService.SanitizeTosuHost(settings.TosuHost);
                int port = SettingsService.SanitizeTosuPort(settings.TosuPort);

                if (host.Length == 0 || port < 1)
                {
                    Console.Error.WriteLine("smoke-test failed: invalid tosu endpoint");
                    return 1;
                }

                bool credentialsPresent = AppPaths.CredentialsExist();
                Console.WriteLine($"smoke-test ok: sound={soundPath} tosu={host}:{port} credentials={(credentialsPresent ? "present" : "missing-local-ok")}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"smoke-test failed: {ex.Message}");
                return 1;
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

            services.AddSingleton<ISettingsService, SettingsService>();
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

            services.AddSingleton<GameplayHudViewModel>();
            services.AddSingleton<BeatmapBannerViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<SessionLiveCardViewModel>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddTransient<AnalyticsViewModel>();

            return services.BuildServiceProvider();
        }

        public static IServiceProvider? GetServiceProvider() => _serviceProvider;

        private static void ConfigureTosuEndpoint(IServiceProvider provider)
        {
            try
            {
                ISettingsService settings = provider.GetRequiredService<ISettingsService>();
                ITosuClient tosuClient = provider.GetRequiredService<ITosuClient>();
                tosuClient.Host = settings.TosuHost;
                tosuClient.Port = settings.TosuPort;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to configure tosu endpoint before start");
            }
        }

        private static async Task ShutdownAsync()
        {
            IServiceProvider? provider = _serviceProvider;

            if (provider == null)
            {
                return;
            }

            try
            {
                ITrackerService? trackerService = provider.GetService<ITrackerService>();

                if (trackerService != null)
                {
                    using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                    try
                    {
                        await trackerService.FlushPendingSubmissionsAsync(drainCts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    try
                    {
                        await trackerService.FlushOfflineSyncAsync(drainCts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    try
                    {
                        await trackerService.StopOfflineSyncAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (trackerService is IAsyncDisposable asyncTracker)
                        {
                            await asyncTracker.DisposeAsync().ConfigureAwait(false);
                        }
                        else if (trackerService is IDisposable disposableTracker)
                        {
                            disposableTracker.Dispose();
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            try
            {
                ITosuClient? tosuClient = provider.GetService<ITosuClient>();

                if (tosuClient is IAsyncDisposable asyncTosu)
                {
                    await asyncTosu.DisposeAsync().ConfigureAwait(false);
                }
                else if (tosuClient is IDisposable disposableTosu)
                {
                    disposableTosu.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                if (provider is IDisposable disposableProvider)
                {
                    disposableProvider.Dispose();
                }
            }
            catch
            {
            }
        }
    }
}
