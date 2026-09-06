using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public enum GameStatus
    {
        Menu = 0,
        Edit = 1,
        Playing = 2,
        SongSelect = 5,
        ResultsScreen = 7,
        MultiplayerRoom = 11,
        MultiplayerSongSelect = 12,
        Unknown = -1
    }

    [Flags]
    public enum OsuMods
    {
        None = 0,
        NoFail = 1,
        Easy = 1 << 1,
        TouchDevice = 1 << 2,
        Hidden = 1 << 3,
        HardRock = 1 << 4,
        SuddenDeath = 1 << 5,
        DoubleTime = 1 << 6,
        Relax = 1 << 7,
        HalfTime = 1 << 8,
        Nightcore = 1 << 9,
        Flashlight = 1 << 10,
        Autoplay = 1 << 11,
        SpunOut = 1 << 12,
        Autopilot = 1 << 13,
        Perfect = 1 << 14,
    }

    public record TrackerSnapshot(
        bool IsPlaying,
        bool IsReplay,
        string DetectedClient,
        string BeatmapString,
        string BeatmapTitle,
        string BeatmapArtist,
        string BeatmapVersion,
        int BeatmapId,
        int BeatmapSetId,
        decimal BeatmapHp,
        decimal BeatmapStars,
        decimal BeatmapAim,
        decimal BeatmapSpeed,
        decimal BeatmapCs,
        decimal BeatmapAr,
        decimal BeatmapOd,
        int BeatmapBpm,
        int TotalBeatmapHits,
        int Play300c,
        int Play100c,
        int Play50c,
        int PlayMissc,
        decimal Accuracy,
        int Time,
        string ModsString,
        string GameStateLabel,
        bool SheetsApiReady,
        bool MemoryReadError,
        int PlayingSeconds,
        int IdleSeconds,
        int PlayCount = 0,
        bool DatabaseReady = false,
        int LocalPlayCount = 0
    )
    {
        public string CoverUrl => BeatmapSetId > 0 ? $"https://assets.ppy.sh/beatmaps/{BeatmapSetId}/covers/cover.jpg" : "";
    }

    internal class SheetsSinkAdapter : IPlaySink
    {
        private readonly ISheetsSink _sink;
        public string SinkName => "Google Sheets Adapter";
        public bool IsReady => _sink.SheetsApiReady;
        public Task InitializeAsync(bool silent = false, CancellationToken ct = default)
        {
            return _sink.InitGoogleAPIAsync(silent);
        }
        public Task TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct = default)
        {
            return _sink.TryAppendPlayEntry(data, context.IsReplay, context.RawMods, context.CurrentGameMode,
                DateTime.MinValue, _ => { }, context.SoundFilePath, context.SubmitSoundEnabled, ct);
        }
        public SheetsSinkAdapter(ISheetsSink sink) => _sink = sink;
    }

    public class Tracker
    {
        private static readonly ILogger<Tracker> _log = AppLogger.For<Tracker>();

        private const int MinHitsToSubmit = 40;
        private const int MaxHitJumpPerTick = 50;
        private const int MaxTimeBetweenHitsMs = 30000;

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

        private int Play300c { get; set; } = 0;
        private int Play100c { get; set; } = 0;
        private int Play50c { get; set; } = 0;
        private int PlayMissc { get; set; } = 0;
        private int TotalBeatmapHits { get; set; } = 0;
        private decimal Accuracy { get; set; } = 0;
        private int Time { get; set; } = 0;

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
            _gameStateManager = new GameStateManager();
            _gameStateManager.Username = _settings.Username;
            _beatmapState = new BeatmapStateTracker(tosuClient);

            var sheetsManager = new GoogleSheetsManager(form, _settings.GetFunctionSeparator);
            sheetsManager.OnSettingsChanged = _settings.SaveSettings;
            _sheetsManager = sheetsManager;
            SyncSheetsSettings();
            _settings.SettingsChanged += SyncSheetsSettings;

            _dbManager = new SqliteDatabaseManager(_settings.LocalDatabasePath);
            _localSqliteSink = new LocalSqlitePlaySink(_dbManager);
            _sessionManager = new SessionManager(_dbManager);

            _compositeSink = new CompositePlaySink();
            _compositeSink.AddSink(_localSqliteSink, () => _settings.EnableLocalLogging);
            _compositeSink.AddSink(sheetsManager, () => _settings.EnableGoogleSheetsLogging);
            _playSink = _compositeSink;

            _tosuClient.Host = _settings.TosuHost;
            _tosuClient.Port = _settings.TosuPort;

            _submissionService = new PlaySubmissionService(_playSink, _sessionManager, _beatmapState, _gameStateManager);

            _gameStateManager.GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;

            if (!File.Exists(_settings.SettingsFilePath) && !File.Exists(Path.Combine(AppContext.BaseDirectory, "user_settings.txt")))
            {
                string welcomeMsg = "Welcome to circle tracker!\n\n" +
                    "This app connects to 'tosu' running alongside osu!.\n\n" +
                    "Works with both osu!stable (Wine) and osu!lazer.";
                _form.ShowMessage(welcomeMsg, "Welcome to Circle Tracker!");
            }
        }

        public Tracker(IMainWindow form, ITosuClient tosuClient, ISheetsSink sheetsSink, ISettingsService? settings = null)
        {
            _form = form;
            _tosuClient = tosuClient;
            _settings = settings ?? new SettingsService();
            _gameStateManager = new GameStateManager();
            _gameStateManager.Username = _settings.Username;
            _beatmapState = new BeatmapStateTracker(tosuClient);
            _sheetsManager = sheetsSink;
            _sheetsManager.OnSettingsChanged = _settings.SaveSettings;
            SyncSheetsSettings();
            _settings.SettingsChanged += SyncSheetsSettings;

            if (sheetsSink is IPlaySink playSink)
            {
                _playSink = playSink;
            }
            else
            {
                _playSink = new SheetsSinkAdapter(sheetsSink);
            }

            _dbManager = new SqliteDatabaseManager(":memory:");
            _sessionManager = new SessionManager(_dbManager);
            _submissionService = new PlaySubmissionService(_playSink, _sessionManager, _beatmapState, _gameStateManager);

            _gameStateManager.GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;
        }

        public Tracker(IMainWindow form, ITosuClient tosuClient, IPlaySink playSink, SessionManager? sessionManager = null, ISheetsSink? sheetsSink = null, IGameStateManager? gameStateManager = null, IBeatmapStateTracker? beatmapState = null, IPlaySubmissionService? submissionService = null, ISettingsService? settings = null)
        {
            _form = form;
            _tosuClient = tosuClient;
            _settings = settings ?? new SettingsService();
            _gameStateManager = gameStateManager ?? new GameStateManager();
            _gameStateManager.Username = _settings.Username;
            _beatmapState = beatmapState ?? new BeatmapStateTracker(tosuClient);
            _playSink = playSink;
            _sheetsManager = sheetsSink ?? (playSink as ISheetsSink) ?? new GoogleSheetsManager(form, _settings.GetFunctionSeparator);
            _sheetsManager.OnSettingsChanged = _settings.SaveSettings;
            SyncSheetsSettings();
            _settings.SettingsChanged += SyncSheetsSettings;

            _dbManager = new SqliteDatabaseManager(":memory:");
            _sessionManager = sessionManager ?? new SessionManager(_dbManager);
            _submissionService = submissionService ?? new PlaySubmissionService(_playSink, _sessionManager, _beatmapState, _gameStateManager);

            _gameStateManager.GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;
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
                    TotalBeatmapHits: TotalBeatmapHits,
                    Play300c: Play300c,
                    Play100c: Play100c,
                    Play50c: Play50c,
                    PlayMissc: PlayMissc,
                    Accuracy: Accuracy,
                    Time: Time,
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
                            currentGameState, beatmapCompleted, TotalBeatmapHits);
                        _submissionService.TryPostBeatmapEntry(beatmapCompleted, TotalBeatmapHits, Accuracy, Play300c, Play100c, Play50c, PlayMissc, Time, _currentGameMode, _settings.SoundFilePath, _settings.SubmitSoundEnabled);

                        Play300c = 0;
                        Play100c = 0;
                        Play50c = 0;
                        PlayMissc = 0;
                        Accuracy = 0;
                        TotalBeatmapHits = 0;
                        Time = 0;
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
                        int newHits = new300c + new100c + new50c;
                        int newSongTime = state.Beatmap?.Time?.Live ?? 0;

                        if (newMissc > PlayMissc)
                            PlayMissc = newMissc;

                        if (newHits < TotalBeatmapHits && newSongTime >= Time)
                        {
                            _log.LogWarning("Hit count regression detected: {NewHits} < {TotalHits} without time rewind", newHits, TotalBeatmapHits);
                        }

                        if (newHits > TotalBeatmapHits)
                        {
                            int hitDelta = newHits - TotalBeatmapHits;
                            int timeDelta = newSongTime - Time;

                            if (timeDelta > MaxTimeBetweenHitsMs && hitDelta > 0)
                            {
                                _log.LogInformation("Large time jump detected: {TimeDelta}ms with {HitDelta} hits (potential intro skip)", timeDelta, hitDelta);
                            }

                            if (hitDelta < MaxHitJumpPerTick)
                            {
                                Accuracy = newAcc;
                                Play300c = new300c;
                                Play100c = new100c;
                                Play50c = new50c;
                                TotalBeatmapHits = newHits;
                            }
                            else if (timeDelta > 500)
                            {
                                Accuracy = newAcc;
                                Play300c = new300c;
                                Play100c = new100c;
                                Play50c = new50c;
                                TotalBeatmapHits = newHits;
                            }
                        }

                        if (newSongTime < Time && Time > 0)
                        {
                            if (TotalBeatmapHits >= MinHitsToSubmit)
                            {
                                _log.LogInformation("Retry detected (Time rewound: {NewSongTime} < {Time}). Hits={Hits}",
                                    newSongTime, Time, TotalBeatmapHits);
                                _submissionService.TryPostBeatmapEntry(false, TotalBeatmapHits, Accuracy, Play300c, Play100c, Play50c, PlayMissc, Time, _currentGameMode, _settings.SoundFilePath, _settings.SubmitSoundEnabled);
                            }
                            Play300c = 0;
                            Play100c = 0;
                            Play50c = 0;
                            PlayMissc = 0;
                            Accuracy = 0;
                            TotalBeatmapHits = 0;
                            Time = newSongTime;
                        }
                        else
                        {
                            Time = newSongTime;
                        }
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
}
