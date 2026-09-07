using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Circle_Tracker.Sync;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using static Circle_Tracker.Tracker;

namespace Circle_Tracker.ViewModels;

public class MainWindowViewModel : ViewModelBase, IMainWindow, IDialogService
{
    private static readonly ILogger<MainWindowViewModel> _log = AppLogger.For<MainWindowViewModel>();

    private readonly ITrackerService? _tracker;
    private readonly ILiveSessionTracker _liveSessionTracker;
    private readonly IDatabaseManager _dbManager;
    private readonly ISessionAnalyticsService _sessionService;
    private readonly IPlayQueryEngine _queryEngine;
    private readonly ITosuClient? _tosuClient;

    private readonly CancellationTokenSource _shutdownCts = new();
    private DateTime _sessionStartTime = DateTime.UtcNow;

    private DispatcherTimer? _gameTickTimer;
    private DispatcherTimer? _uiUpdateTimer;
    private DispatcherTimer? _secondsTimer;

    private string _statusText = "Ready";
    private int _sessionPlaysCount;
    private string _sessionAccuracyText = "0.00%";
    private string _sessionPassRateText = "0%";
    private string _currentPpText = "0 PP";
    private bool _profileWarningVisible;
    private bool _isSettingsPanelVisible;

    private Func<Task>? _openAnalyticsAction;
    private Func<Task>? _showSessionSummaryAction;

    public event Action? OpenAnalyticsRequested;
    public event Func<Task<bool>>? SessionSummaryRequested;
    public event EventHandler<Bitmap?>? CoverImageChanged;

    public GameplayHudViewModel Hud { get; }
    public BeatmapBannerViewModel Banner { get; }
    public SettingsViewModel Settings { get; }
    public SessionLiveCardViewModel SessionLive { get; }

    public MainWindowViewModel(
        ITrackerService? tracker,
        ILiveSessionTracker liveSessionTracker,
        IDatabaseManager dbManager,
        ISessionAnalyticsService sessionService,
        IPlayQueryEngine queryEngine,
        ITosuClient? tosuClient = null,
        GameplayHudViewModel? hud = null,
        BeatmapBannerViewModel? banner = null,
        SettingsViewModel? settings = null,
        SessionLiveCardViewModel? sessionLive = null)
    {
        _tracker = tracker;
        _liveSessionTracker = liveSessionTracker;
        _dbManager = dbManager;
        _sessionService = sessionService;
        _queryEngine = queryEngine;
        _tosuClient = tosuClient;

        Hud = hud ?? new GameplayHudViewModel();
        Banner = banner ?? new BeatmapBannerViewModel();
        Settings = settings ?? new SettingsViewModel(_tracker, _tosuClient, _dbManager, msg => StatusText = msg);
        SessionLive = sessionLive ?? new SessionLiveCardViewModel();

        Banner.CoverImageChanged += (s, e) => CoverImageChanged?.Invoke(this, e);

        OpenAnalyticsCommand = new RelayCommand(async () => await OpenAnalyticsAsync());
        ResetSessionCommand = new RelayCommand(async () => await ResetSessionAsync());
        RefreshCommand = new RelayCommand(async () => await RefreshDataAsync());

        _liveSessionTracker.MetricsUpdated += OnLiveSessionMetricsUpdated;
        _liveSessionTracker.PlayProcessed += OnLiveSessionMetricsUpdated;
        _liveSessionTracker.AchievementUnlocked += OnAchievementUnlocked;

        if (_tracker != null)
        {
            _tracker.PlayLogged += OnPlayLogged;
            SetupTimers();

            _ = Task.Run(async () =>
            {
                try
                {
                    await _tracker.InitializeStorageAsync(silent: true, _shutdownCts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to initialize storage");
                }
            }, _shutdownCts.Token);
        }

        if (_tosuClient != null)
        {
            _tosuClient.Host = !string.IsNullOrWhiteSpace(Settings.TosuHost) ? Settings.TosuHost : "127.0.0.1";
            _tosuClient.Port = int.TryParse(Settings.TosuPortText, out int port) ? port : 24050;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _tosuClient.ConnectAsync();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to connect to tosu");
                }
            }, _shutdownCts.Token);
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public int SessionPlaysCount
    {
        get => _sessionPlaysCount;
        set => SetProperty(ref _sessionPlaysCount, value);
    }

    public string SessionAccuracyText
    {
        get => _sessionAccuracyText;
        set => SetProperty(ref _sessionAccuracyText, value);
    }

    public string SessionPassRateText
    {
        get => _sessionPassRateText;
        set => SetProperty(ref _sessionPassRateText, value);
    }

    public string CurrentPpText
    {
        get => _currentPpText;
        set => SetProperty(ref _currentPpText, value);
    }

    public bool ProfileWarningVisible
    {
        get => _profileWarningVisible;
        set => SetProperty(ref _profileWarningVisible, value);
    }

    public bool IsSettingsPanelVisible
    {
        get => _isSettingsPanelVisible;
        set => SetProperty(ref _isSettingsPanelVisible, value);
    }

    public ICommand OpenAnalyticsCommand { get; }
    public ICommand ResetSessionCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ConnectSheetsCommand => Settings.ConnectSheetsCommand;
    public ICommand ImportSheetsCommand => Settings.ImportSheetsCommand;
    public ICommand SyncToSheetsCommand => Settings.SyncToSheetsCommand;

    public void SetOpenAnalyticsAction(Func<Task> action) => _openAnalyticsAction = action;
    public void SetShowSessionSummaryAction(Func<Task> action) => _showSessionSummaryAction = action;

    public async Task<bool> RequestSessionSummaryAsync()
    {
        if (_showSessionSummaryAction != null)
        {
            await _showSessionSummaryAction();
            return true;
        }

        if (SessionSummaryRequested != null)
        {
            return await SessionSummaryRequested.Invoke();
        }

        return false;
    }

    private void SetupTimers()
    {
        if (_tracker == null) return;
        try
        {
            _gameTickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _gameTickTimer.Tick += async (s, e) =>
            {
                try
                {
                    await Task.Run(() => _tracker.TickWrapper());
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Tick error");
                }
            };

            _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _uiUpdateTimer.Tick += (s, e) =>
            {
                try
                {
                    UpdateFromSnapshot(_tracker.GetSnapshot());
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "UI update error");
                }
            };

            _secondsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _secondsTimer.Tick += (s, e) =>
            {
                try
                {
                    _tracker.TickEverySecond();
                    _ = Settings.RefreshPendingSyncCountAsync();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Seconds tick error");
                }
            };

            _gameTickTimer.Start();
            _uiUpdateTimer.Start();
            _secondsTimer.Start();
        }
        catch (Exception ex)
        {
            _log.LogDebug("Dispatcher timers not started: {Error}", ex.Message);
        }
    }

    public void UpdateFromSnapshot(TrackerSnapshot snapshot)
    {
        try
        {
            void Apply()
            {
                Hud.UpdateFromSnapshot(snapshot);
                Banner.UpdateFromSnapshot(snapshot);
                Settings.UpdateFromSnapshot(snapshot);
                SessionLive.UpdateFromSnapshot(snapshot);
                bool isConnected = _tosuClient?.IsConnected ?? (!snapshot.MemoryReadError && snapshot.DetectedClient != "Disconnected" && snapshot.DetectedClient != "Connecting...");
                ProfileWarningVisible = !snapshot.ProfileIdentityConfirmed && isConnected;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Apply();
            }
            else
            {
                Dispatcher.UIThread.Post(Apply);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to update from snapshot");
            StatusText = "Failed to update data";
        }
    }

    public void UpdateTime()
    {
        if (_tracker == null) return;
        void Apply()
        {
            try
            {
                var s = _tracker.GetSnapshot();
                Hud.UpdateSessionTime(s.PlayingSeconds, s.IdleSeconds);
            }
            catch (Exception ex)
            {
                _log.LogDebug("UpdateTime error: {Error}", ex.Message);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void OnLiveSessionMetricsUpdated(object? sender, LiveSessionMetrics metrics)
    {
        void Update()
        {
            try
            {
                SessionLive.UpdateFromMetrics(metrics, _sessionStartTime);
                if (metrics.SessionPlayCount == 0)
                {
                    SessionPlaysCount = 0;
                    SessionAccuracyText = "0.00%";
                    SessionPassRateText = "0%";
                    return;
                }

                SessionPlaysCount = metrics.SessionPlayCount;
                SessionAccuracyText = $"{metrics.SessionAccuracy:F2}%";

                var passRate = metrics.SessionPlayCount > 0
                    ? (double)metrics.SessionPassCount / metrics.SessionPlayCount * 100.0
                    : 0.0;
                SessionPassRateText = $"{passRate:F0}%";
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to update live session card");
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
        }
        else
        {
            Dispatcher.UIThread.Post(Update);
        }
    }

    private void OnAchievementUnlocked(object? sender, PostPlayAchievement achievement)
    {
        void Update()
        {
            try
            {
                SessionLive.ShowAchievement(achievement);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to show achievement banner");
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
        }
        else
        {
            Dispatcher.UIThread.Post(Update);
        }
    }

    private void OnPlayLogged(object? sender, (PlayEntryData Data, PlayContext Context) args)
    {
        _ = _liveSessionTracker.OnPlayLoggedAsync(args.Data, args.Context);
    }

    public async Task RefreshDataAsync()
    {
        try
        {
            var metrics = _liveSessionTracker.GetCurrentMetrics();
            SessionPlaysCount = metrics.SessionPlayCount;
            SessionAccuracyText = $"{metrics.SessionAccuracy:F2}%";
            if (metrics.SessionPlayCount > 0)
            {
                var passRate = (double)metrics.SessionPassCount / metrics.SessionPlayCount * 100.0;
                SessionPassRateText = $"{passRate:F0}%";
            }
            StatusText = "Ready";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to refresh data");
            StatusText = "Failed to refresh data";
        }
    }

    public async Task OpenAnalyticsAsync()
    {
        try
        {
            if (_openAnalyticsAction != null)
            {
                await _openAnalyticsAction();
            }
            else
            {
                OpenAnalyticsRequested?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to open analytics");
            StatusText = "Failed to open analytics";
        }
    }

    public async Task ResetSessionAsync()
    {
        try
        {
            _liveSessionTracker.ResetSession();
            SessionLive.ResetSession();
            _sessionStartTime = DateTime.UtcNow;
            SessionPlaysCount = 0;
            SessionAccuracyText = "0.00%";
            SessionPassRateText = "0%";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to reset session");
            StatusText = "Failed to reset session";
        }
    }

    public async Task ConnectSheetsAsync()
    {
        await Settings.ConnectSheetsAsync();
    }

    public async Task ImportSheetsAsync()
    {
        await Settings.ImportSheetsAsync();
    }

    public async Task SyncToSheetsAsync()
    {
        await Settings.SyncToSheetsAsync();
    }

    public async Task<SessionSummaryReport> GenerateSessionSummaryAsync(CancellationToken ct = default)
    {
        return await _liveSessionTracker.GenerateSessionSummaryAsync(ct);
    }

    public AnalyticsViewModel CreateAnalyticsViewModel()
    {
        var skillService = new SkillAnalyticsService(_dbManager);
        var sessionService = new SessionAnalyticsService(_dbManager);
        var queryEngine = new SqlitePlayQueryEngine(_dbManager);
        var exportService = new DataExportService(_dbManager, queryEngine);
        return new AnalyticsViewModel(skillService, sessionService, queryEngine, exportService);
    }

    public async Task ShutdownAsync()
    {
        _shutdownCts.Cancel();

        _gameTickTimer?.Stop();
        _uiUpdateTimer?.Stop();
        _secondsTimer?.Stop();

        Settings.Dispose();
        SessionLive.Dispose();

        if (_tosuClient != null)
        {
            try
            {
                await _tosuClient.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to disconnect tosu cleanly during shutdown");
            }
        }

        if (_tracker != null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _tracker.FlushPendingSubmissionsAsync(cts.Token);
                await _tracker.SessionManager.EndSessionAsync(cts.Token);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to flush submissions and end session cleanly during shutdown");
            }

            try
            {
                using var syncCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _tracker.StopOfflineSyncAsync(syncCts.Token);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to stop offline sync cleanly during shutdown");
            }

            if (_tracker.PlaySink is CompositePlaySink composite)
            {
                foreach (var reg in composite.Registrations)
                {
                    if (reg.Sink is IAsyncDisposable asyncRegSink)
                    {
                        try { await asyncRegSink.DisposeAsync(); } catch { }
                    }
                    else if (reg.Sink is IDisposable dispRegSink)
                    {
                        try { dispRegSink.Dispose(); } catch { }
                    }
                }
            }
            else if (_tracker.PlaySink is IAsyncDisposable asyncSink)
            {
                try { await asyncSink.DisposeAsync(); } catch { }
            }
            else if (_tracker.PlaySink is IDisposable dispSink)
            {
                try { dispSink.Dispose(); } catch { }
            }

            try
            {
                _tracker.SaveSettings();
            }
            catch { }
        }

        SoundHelper.Shutdown();
        _shutdownCts.Dispose();
    }

    void IMainWindow.SetCredentialsFound(bool found)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Settings.CredentialsFound = found;
            Settings.CredentialsStatusText = found ? "Found" : "Missing";
            Settings.CredentialsStatusBrush = found ? AppBrushes.GreenBrush : AppBrushes.RedBrush;
        });
    }

    void IMainWindow.SetSheetsApiReady(bool val)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Settings.SheetsConnected = val;
            Settings.SheetsStatusText = val ? "Sheets: Connected" : "Sheets: Not connected";
            Settings.SheetsStatusBrush = val ? AppBrushes.GreenBrush : AppBrushes.RedBrush;
        });
    }

    void IMainWindow.StopUpdateTimer()
    {
        _uiUpdateTimer?.Stop();
    }

    void IMainWindow.ShowMessage(string message, string title)
    {
        _ = ShowMessageAsync(message, title);
    }

    async Task<bool> IMainWindow.ShowYesNoDialog(string message, string title)
    {
        return await ShowYesNoDialogAsync(message, title);
    }

    public Func<string, string, Task>? ShowMessageDelegate { get; set; }

    public Func<string, string, Task<bool>>? ShowYesNoDialogDelegate { get; set; }

    public async Task ShowMessageAsync(string message, string title = "Info")
    {
        if (ShowMessageDelegate != null)
        {
            await ShowMessageDelegate(message, title);
            return;
        }

        StatusText = $"{title}: {message}";
        await Task.CompletedTask;
    }

    public async Task<bool> ShowYesNoDialogAsync(string message, string title = "Confirm")
    {
        if (ShowYesNoDialogDelegate != null)
            return await ShowYesNoDialogDelegate(message, title);

        _log.LogWarning("ShowYesNoDialogAsync called but no dialog delegate is set. Returning false.");
        return false;
    }

    public async Task<string?> RequestSaveFilePathAsync(string defaultFileName = "export.csv")
    {
        await Task.CompletedTask;
        return null;
    }
}
