using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker;

public class Tracker
    {
        private static readonly ILogger<Tracker> _log = AppLogger.For<Tracker>();

        private const int MinHitsToSubmit = 40;

        private readonly IMainWindow _form;
        private readonly ITosuClient _tosuClient;
        private readonly IPlaySink _playSink;
        private readonly ISheetsSink _sheetsManager;
        private readonly IDatabaseManager? _dbManager;
        private readonly LocalSqlitePlaySink? _localSqliteSink;
        private readonly CompositePlaySink? _compositeSink;
        private readonly SessionManager _sessionManager;

        public int IdleSeconds { get; private set; } = 0;
        public int PlayingSeconds { get; private set; } = 0;

        private readonly ISettingsService _settings;
        public ISettingsService Settings => _settings;

        private readonly IGameStateManager _gameStateManager;
        public string DetectedClient => _gameStateManager.DetectedClient;
        private readonly IBeatmapStateTracker _beatmapState;
        public IBeatmapStateTracker BeatmapState => _beatmapState;
        private readonly IHitTrackingService _hitTracking;

        private int _currentGameMode = 0;

        private DateTime LastPostTime { get; set; }
        private int _tickLock = 0;
        private readonly object _snapshotLock = new();
        private readonly IPlaySubmissionService _submissionService;
        public IPlaySubmissionService SubmissionService => _submissionService;

        public bool DatabaseReady => _dbManager?.IsHealthy ?? false;
        public int LocalPlayCount => _localSqliteSink?.TotalPlaysRecorded ?? 0;
        public SessionManager SessionManager => _sessionManager;
        public IPlaySink PlaySink => _playSink;
        
        public event EventHandler<(PlayEntryData Data, PlayContext Context)>? PlayLogged
        {
            add => _submissionService.PlayLogged += value;
            remove => _submissionService.PlayLogged -= value;
        }

        public ISessionAnalyticsService? GetSessionAnalyticsService()
        {
            if (_dbManager == null) return null;
            return new Analytics.SessionAnalyticsService(_dbManager);
        }

        public bool SheetsApiReady => _sheetsManager?.SheetsApiReady ?? false;
        public int SheetRows => _sheetsManager?.SheetRows ?? 0;

        private void SyncSheetsSettings()
        {
            if (_sheetsManager is GoogleSheetsManager gsm)
            {
                gsm.SpreadsheetId = _settings.SpreadsheetId;
                gsm.SheetName = _settings.SheetName;
                gsm.SpreadsheetTimezoneVerified = _settings.SpreadsheetTimezoneVerified;
                gsm.UseAltFuncSeparator = _settings.UseAltFuncSeparator;
            }
        }

        public Tracker(IMainWindow form, ITosuClient tosuClient, ISettingsService? settings = null)
        {
            _form = form;
            _tosuClient = tosuClient;
            _settings = settings ?? new SettingsService();
            _gameStateManager = new GameStateManager { Username = _settings.Username };
            _beatmapState = new BeatmapStateTracker(tosuClient);
            _hitTracking = new HitTrackingService();

            var (playSink, compositeSink, localSink, sessionManager, dbManager, sheetsManager) = TrackerInitializer.InitializeProductionServices(form, _settings);
            _playSink = playSink;
            _compositeSink = compositeSink;
            _localSqliteSink = localSink;
            _sessionManager = sessionManager;
            _dbManager = dbManager;
            _sheetsManager = sheetsManager;

            ConfigureSettings();
            _tosuClient.Host = _settings.TosuHost;
            _tosuClient.Port = _settings.TosuPort;

            _submissionService = new PlaySubmissionService(_playSink, _sessionManager, _beatmapState, _gameStateManager);
            _gameStateManager.GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;

            TrackerInitializer.ShowWelcomeMessageIfFirstRun(_form, _settings.SettingsFilePath);
        }

        public Tracker(IMainWindow form, ITosuClient tosuClient, ISheetsSink sheetsSink, ISettingsService? settings = null)
        {
            _form = form;
            _tosuClient = tosuClient;
            _settings = settings ?? new SettingsService();
            _gameStateManager = new GameStateManager { Username = _settings.Username };
            _beatmapState = new BeatmapStateTracker(tosuClient);
            _hitTracking = new HitTrackingService();
            _sheetsManager = sheetsSink;

            var (playSink, sessionManager, dbManager) = TrackerInitializer.InitializeTestServices(sheetsSink);
            _playSink = playSink;
            _sessionManager = sessionManager;
            _dbManager = dbManager;

            ConfigureSettings();
            _submissionService = new PlaySubmissionService(_playSink, _sessionManager, _beatmapState, _gameStateManager);
            _gameStateManager.GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;
        }

        public Tracker(IMainWindow form, ITosuClient tosuClient, IPlaySink playSink, SessionManager? sessionManager = null, ISheetsSink? sheetsSink = null, IGameStateManager? gameStateManager = null, IBeatmapStateTracker? beatmapState = null, IPlaySubmissionService? submissionService = null, ISettingsService? settings = null)
        {
            _form = form;
            _tosuClient = tosuClient;
            _settings = settings ?? new SettingsService();
            _gameStateManager = gameStateManager ?? new GameStateManager { Username = _settings.Username };
            _beatmapState = beatmapState ?? new BeatmapStateTracker(tosuClient);
            _hitTracking = new HitTrackingService();
            _playSink = playSink;
            _sheetsManager = sheetsSink ?? (playSink as ISheetsSink) ?? new GoogleSheetsManager(form, _settings.GetFunctionSeparator);

            _dbManager = new SqliteDatabaseManager(":memory:");
            _sessionManager = sessionManager ?? new SessionManager(_dbManager);
            _submissionService = submissionService ?? new PlaySubmissionService(_playSink, _sessionManager, _beatmapState, _gameStateManager);

            ConfigureSettings();
            _gameStateManager.GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;
        }

        private void ConfigureSettings()
        {
            _sheetsManager.OnSettingsChanged = _settings.SaveSettings;
            SyncSheetsSettings();
            _settings.SettingsChanged += SyncSheetsSettings;
        }

        public Task InitGoogleAPIAsync(bool silent = false) => _sheetsManager.InitGoogleAPIAsync(silent);

        public async Task InitializeStorageAsync(bool silent = false, CancellationToken ct = default)
        {
            if (_compositeSink != null)
            {
                await _compositeSink.InitializeAsync(silent, ct);
            }
            else
            {
                await _playSink.InitializeAsync(silent, ct);
            }
            await _sessionManager.InitializeAsync(ct);
        }

        public TrackerSnapshot GetSnapshot()
        {
            lock (_snapshotLock)
            {
                return new TrackerSnapshot(
                    IsPlaying: _gameStateManager.IsPlaying,
                    IsReplay: _gameStateManager.IsReplay,
                    DetectedClient: _gameStateManager.DetectedClient,
                    BeatmapString: _beatmapState.BeatmapString ?? "",
                    BeatmapTitle: _beatmapState.BeatmapTitle,
                    BeatmapArtist: _beatmapState.BeatmapArtist,
                    BeatmapVersion: _beatmapState.BeatmapVersion,
                    BeatmapId: _beatmapState.BeatmapID,
                    BeatmapSetId: _beatmapState.BeatmapSetID,
                    BeatmapHp: _beatmapState.BeatmapHp,
                    BeatmapStars: _beatmapState.BeatmapStars,
                    BeatmapAim: _beatmapState.BeatmapAim,
                    BeatmapSpeed: _beatmapState.BeatmapSpeed,
                    BeatmapCs: _beatmapState.BeatmapCs,
                    BeatmapAr: _beatmapState.BeatmapAr,
                    BeatmapOd: _beatmapState.BeatmapOd,
                    BeatmapBpm: _beatmapState.BeatmapBpm,
                    TotalBeatmapHits: _hitTracking.TotalBeatmapHits,
                    Play300c: _hitTracking.Play300c,
                    Play100c: _hitTracking.Play100c,
                    Play50c: _hitTracking.Play50c,
                    PlayMissc: _hitTracking.PlayMissc,
                    Accuracy: _hitTracking.Accuracy,
                    Time: _hitTracking.Time,
                    ModsString: _beatmapState.GetModsString(),
                    GameStateLabel: _gameStateManager.GameStateLabel,
                    SheetsApiReady: SheetsApiReady,
                    MemoryReadError: _gameStateManager.MemoryReadError,
                    PlayingSeconds: PlayingSeconds,
                    IdleSeconds: IdleSeconds,
                    PlayCount: _submissionService.ConsecutivePlayCount,
                    DatabaseReady: DatabaseReady,
                    LocalPlayCount: LocalPlayCount
                );
            }
        }


        public void Tick()
        {
            if (!_tosuClient.IsConnected)
            {
                lock (_snapshotLock) { _gameStateManager.DetectedClient = "Disconnected"; }
                return;
            }

            var state = _tosuClient.LatestState;
            if (state == null)
            {
                lock (_snapshotLock) { _gameStateManager.DetectedClient = "Connecting..."; }
                return;
            }

            lock (_snapshotLock)
            {
                GameStatus previousGameState = _gameStateManager.GameState;
                _gameStateManager.UpdateFromState(state);
                GameStatus currentGameState = _gameStateManager.GameState;
                bool songSelectGameState = _gameStateManager.IsSongSelectState(currentGameState);

                string newChecksum = state.Beatmap?.Checksum ?? "";
                if (newChecksum != _beatmapState.CurrentBeatmapChecksum && newChecksum != "")
                {
                    _beatmapState.CurrentBeatmapChecksum = newChecksum;
                    _beatmapState.UpdateBeatmapFromState(state);
                    _beatmapState.FireUpdateDifficultyFromPpApi(state.Play?.Mods?.Number ?? 0);
                }

                _currentGameMode = state.Play?.Mode?.Number ?? state.Settings?.Mode?.Number ?? 0;

                if (_gameStateManager.MemoryReadError && string.IsNullOrEmpty(_beatmapState.BeatmapString))
                    _beatmapState.BeatmapString = "";

                if (state.Beatmap?.Stats?.Stars != null)
                {
                    var stars = state.Beatmap.Stats.Stars;
                    if (stars.Total > 0) _beatmapState.BeatmapStars = stars.Total;
                    if (stars.Aim > 0) _beatmapState.BeatmapAim = stars.Aim;
                    if (stars.Speed > 0) _beatmapState.BeatmapSpeed = stars.Speed;
                }

                if (currentGameState != previousGameState)
                {
                    if (previousGameState == GameStatus.Playing && currentGameState != GameStatus.Playing)
                    {
                        bool beatmapCompleted = currentGameState == GameStatus.ResultsScreen;
                        _log.LogInformation("Transitioned from Playing to {NewGameState}. Completed={Completed}. Hits={Hits}",
                            currentGameState, beatmapCompleted, _hitTracking.TotalBeatmapHits);
                        _submissionService.TryPostBeatmapEntry(beatmapCompleted, _hitTracking.TotalBeatmapHits, _hitTracking.Accuracy, 
                            _hitTracking.Play300c, _hitTracking.Play100c, _hitTracking.Play50c, _hitTracking.PlayMissc, _hitTracking.Time, 
                            _currentGameMode, _settings.SoundFilePath, _settings.SubmitSoundEnabled);
                        _hitTracking.ResetHitStatistics();
                    }
                }

                if (songSelectGameState && state.Play?.Mods != null)
                {
                    int newMods = state.Play.Mods.Number;
                    if (newMods != _beatmapState.RawMods)
                    {
                        _beatmapState.UpdateModsFromBitfield(newMods);
                        _beatmapState.UpdateBeatmapFromState(state);
                        _beatmapState.FireUpdateDifficultyFromPpApi(newMods);
                    }
                }

                if (currentGameState == GameStatus.Playing && state.Play != null)
                {
                    var hits = state.Play.Hits;
                    if (hits != null)
                    {
                        decimal newAcc = state.Play.Accuracy;
                        int new300c = hits.H300;
                        int new100c = hits.H100;
                        int new50c = hits.H50;
                        int newMissc = hits.Misses;
                        int newSongTime = state.Beatmap?.Time?.Live ?? 0;

                        // Handle time rewind (retry) - submit if enough hits
                        if (newSongTime < _hitTracking.Time && _hitTracking.Time > 0)
                        {
                            if (_hitTracking.TotalBeatmapHits >= MinHitsToSubmit)
                            {
                                _log.LogInformation("Retry detected (Time rewound: {NewSongTime} < {Time}). Hits={Hits}",
                                    newSongTime, _hitTracking.Time, _hitTracking.TotalBeatmapHits);
                                _submissionService.TryPostBeatmapEntry(false, _hitTracking.TotalBeatmapHits, _hitTracking.Accuracy,
                                    _hitTracking.Play300c, _hitTracking.Play100c, _hitTracking.Play50c, _hitTracking.PlayMissc,
                                    _hitTracking.Time, _currentGameMode, _settings.SoundFilePath, _settings.SubmitSoundEnabled);
                            }
                            _hitTracking.ResetHitStatistics();
                        }

                        _hitTracking.UpdateHitStatistics(new300c, new100c, new50c, newMissc, newAcc, newSongTime);
                    }

                    if (state.Play.Mods != null)
                        _beatmapState.UpdateModsFromBitfield(state.Play.Mods.Number);
                }
            }
        }

        public void TickWrapper()
        {
            if (Interlocked.CompareExchange(ref _tickLock, 1, 0) != 0)
                return;
            try
            {
                Tick();
            }
            finally
            {
                Interlocked.Exchange(ref _tickLock, 0);
            }
        }

        public void TickEverySecond()
        {
            if (_gameStateManager.IsPlaying)
                PlayingSeconds++;
            else
                IdleSeconds++;

            _ = _sessionManager.UpdateStatsAsync(PlayingSeconds, IdleSeconds, _gameStateManager.DetectedClient);
            _form.UpdateTime();
        }

        public Task FlushPendingSubmissionsAsync(CancellationToken ct = default) => _submissionService.FlushPendingSubmissionsAsync(ct);
    }
