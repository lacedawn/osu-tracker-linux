using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Circle_Tracker.Sync;
using Microsoft.Extensions.Logging;

namespace Circle_Tracker.ViewModels;

public class MainWindowViewModel : ViewModelBase, IMainWindow, IDialogService
{
    private static readonly ILogger<MainWindowViewModel> _log = AppLogger.For<MainWindowViewModel>();

    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.FromRgb(0x4a, 0xde, 0x80));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71));
    private static readonly IBrush CyanBrush = new SolidColorBrush(Color.FromRgb(0x7d, 0xd3, 0xfc));
    private static readonly IBrush OrangeBrush = new SolidColorBrush(Color.FromRgb(0xfb, 0x92, 0x3c));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x8f, 0x87, 0xa3));
    private static readonly IBrush WhiteBrush = new SolidColorBrush(Color.FromRgb(0xf5, 0xf4, 0xfa));
    private static readonly IBrush GoldBrush = new SolidColorBrush(Color.FromRgb(0xfa, 0xcc, 0x15));
    private static readonly IBrush PinkBrush = new SolidColorBrush(Color.FromRgb(0xf4, 0x72, 0xb6));

    private readonly ITrackerService? _tracker;
    private readonly ILiveSessionTracker _liveSessionTracker;
    private readonly IDatabaseManager _dbManager;
    private readonly ISessionAnalyticsService _sessionService;
    private readonly IPlayQueryEngine _queryEngine;
    private readonly ITosuClient? _tosuClient;
    private CancellationTokenSource? _reconnectDebounce;

    private DateTime _sessionStartTime = DateTime.UtcNow;
    private CancellationTokenSource? _achievementBannerCts;
    private static readonly HttpClient _imageHttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ConcurrentDictionary<string, Bitmap> _coverCache = new();
    private string _currentCoverUrl = "";
    private CancellationTokenSource? _coverLoadCts;

    private DispatcherTimer? _gameTickTimer;
    private DispatcherTimer? _uiUpdateTimer;
    private DispatcherTimer? _secondsTimer;

    private string _statusText = "Ready";
    private int _sessionPlaysCount;
    private string _sessionAccuracyText = "0.00%";
    private string _sessionPassRateText = "0%";
    private string _currentPpText = "0 PP";

    private string _beatmapTitle = "No beatmap detected";
    private string _beatmapArtist = "-";
    private string _beatmapVersion = "-";
    private string _beatmapStars = "★ 0.00";
    private bool _bannerTrianglesVisible = true;
    private Bitmap? _coverImage;

    private string _gameState = "IDLE";
    private IBrush _gameStateBrush = MutedBrush;

    private string _statCs = "0.0";
    private string _statAr = "0.0";
    private string _statOd = "0.0";
    private string _statHp = "0.0";
    private string _statBpm = "0";
    private string _statMods = "None";
    private IBrush _statArBrush = WhiteBrush;
    private IBrush _statOdBrush = WhiteBrush;
    private IBrush _statBpmBrush = WhiteBrush;
    private IBrush _statModsBrush = MutedBrush;

    private string _hits300 = "0";
    private string _hits100 = "0";
    private string _hits50 = "0";
    private string _hitsMiss = "0";
    private string _totalObjectsText = "Total: 0";

    private string _accuracyText = "100.00%";
    private IBrush _accuracyBrush = GoldBrush;
    private string _playCountBadge = "Play #0";
    private string _sessionTimeText = "Play: 0m  •  Idle: 0m  •  Efficiency: 0%";

    private bool _liveSessionCardVisible;
    private string _deltaAccuracyText = "+0.00%";
    private string _deltaStarsText = "+0.00★";
    private string _deltaBpmText = "+0 BPM";
    private string _sessionStatsText = "0 plays • 0 passes • 0.0 min active";
    private string _sessionElapsedText = "0m session";

    private bool _achievementBannerVisible;
    private string _achievementTitle = "";
    private string _achievementDescription = "";
    private string _achievementAccentColor = "#facc15";

    private bool _tosuConnected;
    private string _tosuStatusText = "tosu: Connecting...";
    private IBrush _tosuStatusBrush = RedBrush;

    private bool _databaseReady;
    private string _dbStatusText = "DB: Ready";
    private IBrush _dbStatusBrush = GreenBrush;
    private int _localPlayCount;

    private bool _sheetsConnected;
    private string _sheetsStatusText = "Sheets: Not connected";
    private IBrush _sheetsStatusBrush = RedBrush;

    private bool _credentialsFound;
    private string _credentialsStatusText = "Missing";
    private IBrush _credentialsStatusBrush = RedBrush;

    private bool _enableLocalLogging = true;
    private string _localDatabasePath = "";
    private bool _submitSoundEnabled = true;
    private string _tosuHost = "127.0.0.1";
    private string _tosuPortText = "24050";
    private bool _startupLaunch;
    private bool _enableSheetsLogging;
    private string _spreadsheetId = "";
    private string _sheetName = "Raw Data";
    private bool _useAltFuncSeparator;

    private Func<Task>? _openAnalyticsAction;
    private Func<Task>? _showSessionSummaryAction;

    public event Action? OpenAnalyticsRequested;
    public event Func<Task<bool>>? SessionSummaryRequested;
    public event EventHandler<Bitmap?>? CoverImageChanged;

    public MainWindowViewModel(
        ITrackerService? tracker,
        ILiveSessionTracker liveSessionTracker,
        IDatabaseManager dbManager,
        ISessionAnalyticsService sessionService,
        IPlayQueryEngine queryEngine,
        ITosuClient? tosuClient = null)
    {
        _tracker = tracker;
        _liveSessionTracker = liveSessionTracker;
        _dbManager = dbManager;
        _sessionService = sessionService;
        _queryEngine = queryEngine;
        _tosuClient = tosuClient;

        OpenAnalyticsCommand = new RelayCommand(async () => await OpenAnalyticsAsync());
        ResetSessionCommand = new RelayCommand(async () => await ResetSessionAsync());
        ExportSessionCommand = new RelayCommand(async () => await ExportSessionAsync());
        AuthenticateOsuCommand = new RelayCommand(async () => await AuthenticateOsuAsync());
        RefreshCommand = new RelayCommand(async () => await RefreshDataAsync());
        ConnectSheetsCommand = new RelayCommand(async () => await ConnectSheetsAsync());
        ImportSheetsCommand = new RelayCommand(async () => await ImportSheetsAsync());

        _liveSessionTracker.MetricsUpdated += OnLiveSessionMetricsUpdated;
        _liveSessionTracker.PlayProcessed += OnLiveSessionMetricsUpdated;
        _liveSessionTracker.AchievementUnlocked += OnAchievementUnlocked;

        if (_tracker != null)
        {
            _tracker.PlayLogged += OnPlayLogged;
            LoadSettingsFromTracker();
            SetupTimers();

            _ = Task.Run(async () =>
            {
                try
                {
                    await _tracker.InitializeStorageAsync(silent: true);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to initialize storage");
                }
            });
        }

        if (_tosuClient != null)
        {
            _tosuClient.Host = !string.IsNullOrWhiteSpace(TosuHost) ? TosuHost : "127.0.0.1";
            _tosuClient.Port = int.TryParse(TosuPortText, out int port) ? port : 24050;
            _tosuClient.ConnectionStateChanged += OnTosuConnectionStateChanged;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _tosuClient.ConnectAsync();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to connect to tosu");
                }
            });
        }

        CheckCredentials();
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

    public string BeatmapTitle
    {
        get => _beatmapTitle;
        set => SetProperty(ref _beatmapTitle, value);
    }

    public string BeatmapArtist
    {
        get => _beatmapArtist;
        set => SetProperty(ref _beatmapArtist, value);
    }

    public string BeatmapVersion
    {
        get => _beatmapVersion;
        set => SetProperty(ref _beatmapVersion, value);
    }

    public string BeatmapStars
    {
        get => _beatmapStars;
        set => SetProperty(ref _beatmapStars, value);
    }

    public bool BannerTrianglesVisible
    {
        get => _bannerTrianglesVisible;
        set => SetProperty(ref _bannerTrianglesVisible, value);
    }

    public Bitmap? CoverImage
    {
        get => _coverImage;
        set
        {
            if (SetProperty(ref _coverImage, value))
            {
                CoverImageChanged?.Invoke(this, value);
            }
        }
    }

    public string GameState
    {
        get => _gameState;
        set => SetProperty(ref _gameState, value);
    }

    public IBrush GameStateBrush
    {
        get => _gameStateBrush;
        set => SetProperty(ref _gameStateBrush, value);
    }

    public string StatCs
    {
        get => _statCs;
        set => SetProperty(ref _statCs, value);
    }

    public string StatAr
    {
        get => _statAr;
        set => SetProperty(ref _statAr, value);
    }

    public string StatOd
    {
        get => _statOd;
        set => SetProperty(ref _statOd, value);
    }

    public string StatHp
    {
        get => _statHp;
        set => SetProperty(ref _statHp, value);
    }

    public string StatBpm
    {
        get => _statBpm;
        set => SetProperty(ref _statBpm, value);
    }

    public string StatMods
    {
        get => _statMods;
        set => SetProperty(ref _statMods, value);
    }

    public IBrush StatArBrush
    {
        get => _statArBrush;
        set => SetProperty(ref _statArBrush, value);
    }

    public IBrush StatOdBrush
    {
        get => _statOdBrush;
        set => SetProperty(ref _statOdBrush, value);
    }

    public IBrush StatBpmBrush
    {
        get => _statBpmBrush;
        set => SetProperty(ref _statBpmBrush, value);
    }

    public IBrush StatModsBrush
    {
        get => _statModsBrush;
        set => SetProperty(ref _statModsBrush, value);
    }

    public string Hits300
    {
        get => _hits300;
        set => SetProperty(ref _hits300, value);
    }

    public string Hits100
    {
        get => _hits100;
        set => SetProperty(ref _hits100, value);
    }

    public string Hits50
    {
        get => _hits50;
        set => SetProperty(ref _hits50, value);
    }

    public string HitsMiss
    {
        get => _hitsMiss;
        set => SetProperty(ref _hitsMiss, value);
    }

    public string TotalObjectsText
    {
        get => _totalObjectsText;
        set => SetProperty(ref _totalObjectsText, value);
    }

    public string AccuracyText
    {
        get => _accuracyText;
        set => SetProperty(ref _accuracyText, value);
    }

    public IBrush AccuracyBrush
    {
        get => _accuracyBrush;
        set => SetProperty(ref _accuracyBrush, value);
    }

    public string PlayCountBadge
    {
        get => _playCountBadge;
        set => SetProperty(ref _playCountBadge, value);
    }

    public string SessionTimeText
    {
        get => _sessionTimeText;
        set => SetProperty(ref _sessionTimeText, value);
    }

    public bool LiveSessionCardVisible
    {
        get => _liveSessionCardVisible;
        set => SetProperty(ref _liveSessionCardVisible, value);
    }

    public string DeltaAccuracyText
    {
        get => _deltaAccuracyText;
        set => SetProperty(ref _deltaAccuracyText, value);
    }

    public string DeltaStarsText
    {
        get => _deltaStarsText;
        set => SetProperty(ref _deltaStarsText, value);
    }

    public string DeltaBpmText
    {
        get => _deltaBpmText;
        set => SetProperty(ref _deltaBpmText, value);
    }

    public string SessionStatsText
    {
        get => _sessionStatsText;
        set => SetProperty(ref _sessionStatsText, value);
    }

    public string SessionElapsedText
    {
        get => _sessionElapsedText;
        set => SetProperty(ref _sessionElapsedText, value);
    }

    public bool AchievementBannerVisible
    {
        get => _achievementBannerVisible;
        set => SetProperty(ref _achievementBannerVisible, value);
    }

    public string AchievementTitle
    {
        get => _achievementTitle;
        set => SetProperty(ref _achievementTitle, value);
    }

    public string AchievementDescription
    {
        get => _achievementDescription;
        set => SetProperty(ref _achievementDescription, value);
    }

    public string AchievementAccentColor
    {
        get => _achievementAccentColor;
        set => SetProperty(ref _achievementAccentColor, value);
    }

    public bool TosuConnected
    {
        get => _tosuConnected;
        set => SetProperty(ref _tosuConnected, value);
    }

    public string TosuStatusText
    {
        get => _tosuStatusText;
        set => SetProperty(ref _tosuStatusText, value);
    }

    public IBrush TosuStatusBrush
    {
        get => _tosuStatusBrush;
        set => SetProperty(ref _tosuStatusBrush, value);
    }

    public bool DatabaseReady
    {
        get => _databaseReady;
        set => SetProperty(ref _databaseReady, value);
    }

    public string DbStatusText
    {
        get => _dbStatusText;
        set => SetProperty(ref _dbStatusText, value);
    }

    public IBrush DbStatusBrush
    {
        get => _dbStatusBrush;
        set => SetProperty(ref _dbStatusBrush, value);
    }

    public int LocalPlayCount
    {
        get => _localPlayCount;
        set => SetProperty(ref _localPlayCount, value);
    }

    public bool SheetsConnected
    {
        get => _sheetsConnected;
        set => SetProperty(ref _sheetsConnected, value);
    }

    public string SheetsStatusText
    {
        get => _sheetsStatusText;
        set => SetProperty(ref _sheetsStatusText, value);
    }

    public IBrush SheetsStatusBrush
    {
        get => _sheetsStatusBrush;
        set => SetProperty(ref _sheetsStatusBrush, value);
    }

    public bool CredentialsFound
    {
        get => _credentialsFound;
        set => SetProperty(ref _credentialsFound, value);
    }

    public string CredentialsStatusText
    {
        get => _credentialsStatusText;
        set => SetProperty(ref _credentialsStatusText, value);
    }

    public IBrush CredentialsStatusBrush
    {
        get => _credentialsStatusBrush;
        set => SetProperty(ref _credentialsStatusBrush, value);
    }

    public bool EnableLocalLogging
    {
        get => _enableLocalLogging;
        set
        {
            if (SetProperty(ref _enableLocalLogging, value) && _tracker != null)
            {
                _tracker.EnableLocalLogging = value;
                _tracker.SaveSettings();
            }
        }
    }

    public string LocalDatabasePath
    {
        get => _localDatabasePath;
        set
        {
            if (SetProperty(ref _localDatabasePath, value) && _tracker != null)
            {
                _tracker.LocalDatabasePath = value;
                _tracker.SaveSettings();
            }
        }
    }

    public bool SubmitSoundEnabled
    {
        get => _submitSoundEnabled;
        set
        {
            if (SetProperty(ref _submitSoundEnabled, value) && _tracker != null)
            {
                _tracker.SubmitSoundEnabled = value;
                _tracker.SaveSettings();
            }
        }
    }

    public string TosuHost
    {
        get => _tosuHost;
        set
        {
            if (SetProperty(ref _tosuHost, value) && _tracker != null)
            {
                _tracker.TosuHost = value;
                _tracker.SaveSettings();
                if (_tosuClient != null && !string.IsNullOrWhiteSpace(value) && !value.Contains(' '))
                {
                    _tosuClient.Host = value.Trim();
                    DebounceReconnect();
                }
            }
        }
    }

    public string TosuPortText
    {
        get => _tosuPortText;
        set
        {
            if (SetProperty(ref _tosuPortText, value) && int.TryParse(value, out int port) && _tracker != null)
            {
                _tracker.TosuPort = port;
                _tracker.SaveSettings();
                if (_tosuClient != null && port >= 1 && port <= 65535)
                {
                    _tosuClient.Port = port;
                    DebounceReconnect();
                }
            }
        }
    }

    public bool StartupLaunch
    {
        get => _startupLaunch;
        set
        {
            if (SetProperty(ref _startupLaunch, value))
            {
                if (value)
                {
                    AutostartHelper.CreateAutostart();
                }
                else
                {
                    AutostartHelper.DeleteAutostart();
                }
            }
        }
    }

    public bool EnableSheetsLogging
    {
        get => _enableSheetsLogging;
        set
        {
            if (SetProperty(ref _enableSheetsLogging, value) && _tracker != null)
            {
                _tracker.EnableGoogleSheetsLogging = value;
                _tracker.SaveSettings();
            }
        }
    }

    public string SpreadsheetId
    {
        get => _spreadsheetId;
        set
        {
            if (SetProperty(ref _spreadsheetId, value) && _tracker != null)
            {
                _tracker.SpreadsheetId = value;
                _tracker.SaveSettings();
            }
        }
    }

    public string SheetName
    {
        get => _sheetName;
        set
        {
            if (SetProperty(ref _sheetName, value) && _tracker != null)
            {
                _tracker.SheetName = value;
                _tracker.SaveSettings();
            }
        }
    }

    public bool UseAltFuncSeparator
    {
        get => _useAltFuncSeparator;
        set
        {
            if (SetProperty(ref _useAltFuncSeparator, value) && _tracker != null)
            {
                _tracker.UseAltFuncSeparator = value;
                _tracker.SaveSettings();
            }
        }
    }

    public ICommand OpenAnalyticsCommand { get; }
    public ICommand ResetSessionCommand { get; }
    public ICommand ExportSessionCommand { get; }
    public ICommand AuthenticateOsuCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ConnectSheetsCommand { get; }
    public ICommand ImportSheetsCommand { get; }

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

    private void LoadSettingsFromTracker()
    {
        if (_tracker == null) return;
        _enableLocalLogging = _tracker.EnableLocalLogging;
        _enableSheetsLogging = _tracker.EnableGoogleSheetsLogging;
        _localDatabasePath = _tracker.LocalDatabasePath;
        _sheetName = _tracker.SheetName;
        _spreadsheetId = _tracker.SpreadsheetId;
        _submitSoundEnabled = _tracker.SubmitSoundEnabled;
        _useAltFuncSeparator = _tracker.UseAltFuncSeparator;
        _tosuHost = _tracker.TosuHost;
        _tosuPortText = _tracker.TosuPort.ToString();
        _startupLaunch = AutostartHelper.AutostartExists();
    }

    private void CheckCredentials()
    {
        CredentialsFound = File.Exists(Path.Combine(AppContext.BaseDirectory, "credentials.json"));
        CredentialsStatusText = CredentialsFound ? "Found" : "Missing";
        CredentialsStatusBrush = CredentialsFound ? GreenBrush : RedBrush;
    }

    private void OnTosuConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            TosuConnected = connected;
            TosuStatusBrush = connected ? GreenBrush : RedBrush;
            TosuStatusText = connected ? "tosu: Connected" : "tosu: Connecting...";
        });
    }

    private void DebounceReconnect()
    {
        _reconnectDebounce?.Cancel();
        _reconnectDebounce = new CancellationTokenSource();
        var token = _reconnectDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, token);
                if (!token.IsCancellationRequested && _tosuClient != null)
                {
                    await _tosuClient.ReconnectAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to reconnect to tosu");
            }
        });
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
                BeatmapTitle = !string.IsNullOrEmpty(snapshot.BeatmapTitle)
                    ? snapshot.BeatmapTitle
                    : (!string.IsNullOrEmpty(snapshot.BeatmapString) ? snapshot.BeatmapString : "No beatmap detected");
                BeatmapArtist = !string.IsNullOrEmpty(snapshot.BeatmapArtist) ? snapshot.BeatmapArtist : "-";
                BeatmapVersion = !string.IsNullOrEmpty(snapshot.BeatmapVersion) ? snapshot.BeatmapVersion : "-";
                BeatmapStars = $"★ {snapshot.BeatmapStars:0.00}";
                BannerTrianglesVisible = string.IsNullOrEmpty(snapshot.BeatmapTitle) && string.IsNullOrEmpty(snapshot.BeatmapString);

                GameState = snapshot.GameStateLabel;
                GameStateBrush = snapshot.GameStateLabel switch
                {
                    "PLAYING" => GreenBrush,
                    "RESULTS" => CyanBrush,
                    "REPLAY" => OrangeBrush,
                    _ => MutedBrush
                };

                StatCs = snapshot.BeatmapCs.ToString("0.0");
                StatAr = snapshot.BeatmapAr.ToString("0.0");
                StatArBrush = snapshot.BeatmapAr >= 10.0m ? GreenBrush : WhiteBrush;

                StatOd = snapshot.BeatmapOd.ToString("0.0");
                StatOdBrush = snapshot.BeatmapOd >= 10.0m ? GreenBrush : WhiteBrush;

                StatHp = snapshot.BeatmapHp.ToString("0.0");
                StatBpm = snapshot.BeatmapBpm.ToString();
                StatBpmBrush = snapshot.BeatmapBpm >= 200 ? OrangeBrush : WhiteBrush;

                StatMods = !string.IsNullOrEmpty(snapshot.ModsString) ? $"+{snapshot.ModsString}" : "None";
                StatModsBrush = !string.IsNullOrEmpty(snapshot.ModsString) ? PinkBrush : MutedBrush;

                Hits300 = snapshot.Play300c.ToString();
                Hits100 = snapshot.Play100c.ToString();
                Hits50 = snapshot.Play50c.ToString();
                HitsMiss = snapshot.PlayMissc.ToString();
                TotalObjectsText = $"Total: {snapshot.TotalBeatmapHits}";

                AccuracyText = $"{snapshot.Accuracy:0.00}%";
                if (snapshot.Accuracy >= 100.0m)
                    AccuracyBrush = GoldBrush;
                else if (snapshot.Accuracy > 95.0m)
                    AccuracyBrush = GreenBrush;
                else
                    AccuracyBrush = WhiteBrush;

                PlayCountBadge = $"Play #{snapshot.PlayCount}";

                bool isTosuConnected = _tosuClient != null ? _tosuClient.IsConnected : !snapshot.MemoryReadError;
                TosuConnected = isTosuConnected;
                TosuStatusText = isTosuConnected ? $"tosu: {snapshot.DetectedClient}" : "tosu: Connecting...";
                TosuStatusBrush = isTosuConnected ? GreenBrush : RedBrush;

                DatabaseReady = snapshot.DatabaseReady;
                DbStatusText = snapshot.DatabaseReady ? $"DB: {snapshot.LocalPlayCount} plays" : "DB: Error";
                DbStatusBrush = snapshot.DatabaseReady ? GreenBrush : RedBrush;
                LocalPlayCount = snapshot.LocalPlayCount;

                SheetsConnected = snapshot.SheetsApiReady;
                SheetsStatusText = snapshot.SheetsApiReady ? "Sheets: Connected" : "Sheets: Not connected";
                SheetsStatusBrush = snapshot.SheetsApiReady ? GreenBrush : RedBrush;

                int playing = snapshot.PlayingSeconds;
                int idle = snapshot.IdleSeconds;
                float total = playing + idle;
                float eff = total > 0 ? 100f * playing / total : 0f;
                int playingMin = playing / 60;
                int idleMin = idle / 60;
                SessionTimeText = $"Play: {playingMin}m  •  Idle: {idleMin}m  •  Efficiency: {(int)eff}%";

                LoadCoverImage(snapshot.CoverUrl);
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
                int playing = s.PlayingSeconds;
                int idle = s.IdleSeconds;
                float total = playing + idle;
                float eff = total > 0 ? 100f * playing / total : 0f;
                int playingMin = playing / 60;
                int idleMin = idle / 60;
                SessionTimeText = $"Play: {playingMin}m  •  Idle: {idleMin}m  •  Efficiency: {(int)eff}%";
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

    private void LoadCoverImage(string coverUrl)
    {
        if (_currentCoverUrl == coverUrl) return;
        _currentCoverUrl = coverUrl;
        _coverLoadCts?.Cancel();

        if (string.IsNullOrEmpty(coverUrl))
        {
            CoverImage = null;
            return;
        }

        if (_coverCache.TryGetValue(coverUrl, out var cached))
        {
            CoverImage = cached;
            return;
        }

        _coverLoadCts = new CancellationTokenSource();
        var token = _coverLoadCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                byte[] data = await _imageHttpClient.GetByteArrayAsync(coverUrl, token);
                if (token.IsCancellationRequested) return;

                using var ms = new MemoryStream(data);
                var bitmap = new Bitmap(ms);
                _coverCache[coverUrl] = bitmap;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_currentCoverUrl == coverUrl)
                    {
                        CoverImage = bitmap;
                    }
                });
            }
            catch (Exception ex)
            {
                _log.LogDebug("Failed to load cover image: {Error}", ex.Message);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_currentCoverUrl == coverUrl)
                    {
                        CoverImage = null;
                    }
                });
            }
        }, token);
    }

    private void OnLiveSessionMetricsUpdated(object? sender, LiveSessionMetrics metrics)
    {
        void Update()
        {
            try
            {
                if (metrics.SessionPlayCount == 0)
                {
                    LiveSessionCardVisible = false;
                    SessionPlaysCount = 0;
                    SessionAccuracyText = "0.00%";
                    SessionPassRateText = "0%";
                    return;
                }

                LiveSessionCardVisible = true;
                SessionPlaysCount = metrics.SessionPlayCount;
                SessionAccuracyText = $"{metrics.SessionAccuracy:F2}%";

                var passRate = metrics.SessionPlayCount > 0
                    ? (double)metrics.SessionPassCount / metrics.SessionPlayCount * 100.0
                    : 0.0;
                SessionPassRateText = $"{passRate:F0}%";

                DeltaAccuracyText = metrics.BaselineDeltaAccuracy >= 0
                    ? $"+{metrics.BaselineDeltaAccuracy:F2}%"
                    : $"{metrics.BaselineDeltaAccuracy:F2}%";

                DeltaStarsText = metrics.BaselineDeltaStars >= 0
                    ? $"+{metrics.BaselineDeltaStars:F2}★"
                    : $"{metrics.BaselineDeltaStars:F2}★";

                DeltaBpmText = metrics.BaselineDeltaBpm >= 0
                    ? $"+{metrics.BaselineDeltaBpm:F0} BPM"
                    : $"{metrics.BaselineDeltaBpm:F0} BPM";

                SessionStatsText = $"{metrics.SessionPlayCount} plays • {metrics.SessionPassCount} passes • {metrics.ActivePlayMinutes:F1} min active";

                var wallClockMinutes = (DateTime.UtcNow - _sessionStartTime).TotalMinutes;
                SessionElapsedText = $"{wallClockMinutes:F0}m session";
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
                AchievementTitle = achievement.Title;
                AchievementDescription = achievement.Description;
                AchievementAccentColor = achievement.AccentColorHex;
                AchievementBannerVisible = true;

                _achievementBannerCts?.Cancel();
                _achievementBannerCts = new CancellationTokenSource();
                var token = _achievementBannerCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(4000, token);
                        if (!token.IsCancellationRequested)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => AchievementBannerVisible = false);
                        }
                    }
                    catch (OperationCanceledException) { }
                }, token);
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
            _sessionStartTime = DateTime.UtcNow;
            SessionPlaysCount = 0;
            SessionAccuracyText = "0.00%";
            SessionPassRateText = "0%";
            LiveSessionCardVisible = false;
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to reset session");
            StatusText = "Failed to reset session";
        }
    }

    public async Task ExportSessionAsync()
    {
        try
        {
            StatusText = "Exporting session...";
            await Task.CompletedTask;
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to export session");
            StatusText = "Export failed";
        }
    }

    public async Task AuthenticateOsuAsync()
    {
        try
        {
            StatusText = "Authenticating...";
            await Task.CompletedTask;
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to authenticate");
            StatusText = "Authentication failed";
        }
    }

    public async Task ConnectSheetsAsync()
    {
        try
        {
            StatusText = "Connecting to Sheets...";
            if (_tracker != null)
            {
                await _tracker.InitGoogleAPIAsync();
                SheetsConnected = _tracker.SheetsApiReady;
                SheetsStatusText = _tracker.SheetsApiReady ? "Sheets: Connected" : "Sheets: Not connected";
                SheetsStatusBrush = _tracker.SheetsApiReady ? GreenBrush : RedBrush;
                StatusText = _tracker.SheetsApiReady ? "Sheets connected" : "Sheets connection failed";
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to connect to Sheets");
            StatusText = "Sheets connection failed";
        }
    }

    public async Task ImportSheetsAsync()
    {
        try
        {
            if (_tracker == null) return;
            if (!_tracker.SheetsApiReady)
            {
                await _tracker.InitGoogleAPIAsync();
            }

            if (!_tracker.SheetsApiReady)
            {
                StatusText = "Sheets API not connected";
                return;
            }

            string spreadsheetId = _tracker.SpreadsheetId;
            string sheetName = _tracker.SheetName;
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                StatusText = "Missing Spreadsheet ID";
                return;
            }

            var sheetsService = GoogleSheetsManager.CreateSheetsService();
            if (sheetsService == null)
            {
                StatusText = "Failed to create Google Sheets service";
                return;
            }

            StatusText = "Importing sheets data...";
            var importer = new GoogleSheetsHistoricalImporter(sheetsService, _dbManager);
            var result = await importer.ImportFromSpreadsheetAsync(spreadsheetId, sheetName, null, CancellationToken.None);

            if (result.Success)
            {
                StatusText = $"Imported {result.SyncedCount} plays";
            }
            else
            {
                StatusText = "Import failed";
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to import from Sheets");
            StatusText = "Import error";
        }
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
        _reconnectDebounce?.Cancel();
        _gameTickTimer?.Stop();
        _uiUpdateTimer?.Stop();
        _secondsTimer?.Stop();

        if (_tosuClient != null)
        {
            _tosuClient.ConnectionStateChanged -= OnTosuConnectionStateChanged;
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
                await _tracker.SessionManager.EndSessionAsync(cts.Token);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to end session cleanly during shutdown");
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
    }

    void IMainWindow.SetCredentialsFound(bool found)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CredentialsFound = found;
            CredentialsStatusText = found ? "Found" : "Missing";
            CredentialsStatusBrush = found ? GreenBrush : RedBrush;
        });
    }

    void IMainWindow.SetSheetsApiReady(bool val)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SheetsConnected = val;
            SheetsStatusText = val ? "Sheets: Connected" : "Sheets: Not connected";
            SheetsStatusBrush = val ? GreenBrush : RedBrush;
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

    public async Task ShowMessageAsync(string message, string title = "Info")
    {
        StatusText = $"{title}: {message}";
        await Task.CompletedTask;
    }

    public async Task<bool> ShowYesNoDialogAsync(string message, string title = "Confirm")
    {
        await Task.CompletedTask;
        return false;
    }

    public async Task<string?> RequestSaveFilePathAsync(string defaultFileName = "export.csv")
    {
        await Task.CompletedTask;
        return null;
    }
}
