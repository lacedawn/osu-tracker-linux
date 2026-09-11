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

    public class Tracker : IDisposable, IAsyncDisposable
    {
        private static readonly ILogger<Tracker> _log = AppLogger.For<Tracker>();


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

        private int _idleSeconds = 0;
        private int _playingSeconds = 0;

        public int IdleSeconds => Volatile.Read(ref _idleSeconds);
        public int PlayingSeconds => Volatile.Read(ref _playingSeconds);

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
        private TrackerSnapshot? _lastSnapshot;
        private string? _tosuProfileName;
        private bool _isTosuConnected = true;
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

            if (!File.Exists(_settings.SettingsFilePath) && !File.Exists(AppPaths.LegacyTxtPath) && !File.Exists(AppPaths.OldLegacyTxtPath))
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

        public Tracker(IMainWindow form, TrackerOptions options)
        {
            _form = form;
            _tosuClient = options.TosuClient;
            _settings = options.Settings ?? new SettingsService();
            _gameStateManager = options.GameStateManager ?? new GameStateManager();
            _gameStateManager.Username = _settings.Username;
            _beatmapState = options.BeatmapStateTracker ?? new BeatmapStateTracker(options.TosuClient);
            _playSink = ResolvePlaySink(options);
            _sheetsManager = options.SheetsSink ?? (_playSink as ISheetsSink) ?? new GoogleSheetsManager(form, _settings.GetFunctionSeparator);
            _sheetsManager.OnSettingsChanged = _settings.SaveSettings;
            SyncSheetsSettings();
            _settings.SettingsChanged += SyncSheetsSettings;

            _dbManager = new SqliteDatabaseManager(":memory:");
            _sessionManager = options.SessionManager ?? new SessionManager(_dbManager);
            _submissionService = options.PlaySubmissionService ?? new PlaySubmissionService(_playSink, _sessionManager, _beatmapState, _gameStateManager);

            _gameStateManager.GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;
        }

        private static IPlaySink ResolvePlaySink(TrackerOptions options)
        {
            if (options.PlaySink is not null)
                return options.PlaySink;
            if (options.SheetsSink is IPlaySink sheetsPlaySink)
                return sheetsPlaySink;
            if (options.SheetsSink is not null)
                return new SheetsSinkAdapter(options.SheetsSink);
            return new CompositePlaySink();
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

        private TrackerSnapshot BuildSnapshotLocked()
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
                LocalPlayCount: LocalPlayCount,
                ProfileIdentityConfirmed: !string.IsNullOrWhiteSpace(_tosuProfileName ?? _tosuClient.LatestState?.Profile?.Name),
                IsTosuConnected: _isTosuConnected
            );
        }

        private PlayHeaderSnapshot CaptureHeaderLocked()
        {
            return new PlayHeaderSnapshot(
                _beatmapState.CurrentBeatmapChecksum,
                _beatmapState.BeatmapID,
                _beatmapState.BeatmapSetID,
                _beatmapState.BeatmapString ?? "",
                _beatmapState.BeatmapTitle,
                _beatmapState.BeatmapArtist,
                _beatmapState.BeatmapVersion,
                _beatmapState.BeatmapHp,
                _beatmapState.BeatmapBpm,
                _beatmapState.BeatmapStars,
                _beatmapState.BeatmapAim,
                _beatmapState.BeatmapSpeed,
                _beatmapState.BeatmapCs,
                _beatmapState.BeatmapAr,
                _beatmapState.BeatmapOd,
                _beatmapState.RawMods,
                _beatmapState.Hidden,
                _beatmapState.Hardrock,
                _beatmapState.Doubletime,
                _beatmapState.EZ,
                _beatmapState.Halftime,
                _beatmapState.Flashlight,
                _beatmapState.GetModsString(),
                _beatmapState.FirstHitObjectTime,
                _beatmapState.LastClockRate,
                _gameStateManager.IsReplay,
                _gameStateManager.DetectedClient);
        }

        private void ResetPerPlayCountersLocked()
        {
            Play300c = 0;
            Play100c = 0;
            Play50c = 0;
            PlayMissc = 0;
            Accuracy = 0;
            TotalBeatmapHits = 0;
            Time = 0;
        }

        public TrackerSnapshot GetSnapshot()
        {
            TrackerSnapshot? cached = Volatile.Read(ref _lastSnapshot);

            if (cached is not null)
            {
                return cached;
            }

            lock (_snapshotLock)
            {
                cached = _lastSnapshot;

                if (cached is not null)
                {
                    return cached;
                }

                return BuildSnapshotLocked();
            }
        }


        public void Tick()
        {
            if (!_tosuClient.IsConnected)
            {
                lock (_snapshotLock)
                {
                    _gameStateManager.DetectedClient = "Disconnected";
                    _gameStateManager.GameState = GameStatus.Menu;
                    ResetPerPlayCountersLocked();
                    _isTosuConnected = false;
                    _lastSnapshot = BuildSnapshotLocked();
                }
                return;
            }

            var state = _tosuClient.LatestState;
            if (state == null)
            {
                lock (_snapshotLock)
                {
                    _gameStateManager.DetectedClient = "Connecting...";
                    _gameStateManager.GameState = GameStatus.Menu;
                    ResetPerPlayCountersLocked();
                    _isTosuConnected = false;
                    _lastSnapshot = BuildSnapshotLocked();
                }
                return;
            }

            List<(bool Complete, PlayHeaderSnapshot Header, int TotalHits, decimal Accuracy, int C300, int C100, int C50, int Miss, int Time, int GameMode, string? SoundPath, bool SoundEnabled)> deferredSubmissions = new();
            List<int> deferredPpMods = new();
            GameStatus loggedCurrent = GameStatus.Menu;
            bool loggedTransition = false;
            bool loggedCompleted = false;
            int loggedHits = 0;
            bool loggedRetry = false;
            int loggedRetryTime = 0;
            int loggedRetryPrevTime = 0;
            int loggedRetryHits = 0;

            lock (_snapshotLock)
            {
                _isTosuConnected = true;
                _tosuProfileName = state.Profile?.Name;
                GameStatus previousGameState = _gameStateManager.GameState;
                _gameStateManager.UpdateFromState(state);
                GameStatus currentGameState = _gameStateManager.GameState;
                bool songSelectGameState = _gameStateManager.IsSongSelectState(currentGameState);

                string newChecksum = state.Beatmap?.Checksum ?? "";
                if (newChecksum != _beatmapState.CurrentBeatmapChecksum && newChecksum != "")
                {
                    _beatmapState.CurrentBeatmapChecksum = newChecksum;
                    _beatmapState.UpdateBeatmapFromState(state);
                    deferredPpMods.Add(state.Play?.Mods?.Number ?? 0);
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

                bool justEnteredPlaying = false;
                if (currentGameState != previousGameState)
                {
                    if (previousGameState == GameStatus.Playing && currentGameState != GameStatus.Playing)
                    {
                        bool beatmapCompleted = currentGameState == GameStatus.ResultsScreen;
                        loggedCurrent = currentGameState;
                        loggedTransition = true;
                        loggedCompleted = beatmapCompleted;
                        loggedHits = TotalBeatmapHits;
                        deferredSubmissions.Add((beatmapCompleted, CaptureHeaderLocked(), TotalBeatmapHits, Accuracy, Play300c, Play100c, Play50c, PlayMissc, Time, _currentGameMode, _settings.SoundFilePath, _settings.SubmitSoundEnabled));

                        ResetPerPlayCountersLocked();
                    }
                    else if (previousGameState != GameStatus.Playing && currentGameState == GameStatus.Playing)
                    {
                        justEnteredPlaying = true;
                        ResetPerPlayCountersLocked();
                    }
                }

                if (songSelectGameState && state.Play?.Mods != null)
                {
                    int newMods = state.Play.Mods.Number;
                    if (newMods != _beatmapState.RawMods)
                    {
                        _beatmapState.UpdateModsFromBitfield(newMods);
                        _beatmapState.UpdateBeatmapFromState(state);
                        deferredPpMods.Add(newMods);
                    }
                }

                if (currentGameState == GameStatus.Playing && state.Play != null && !justEnteredPlaying)
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

                        if (newSongTime < Time && Time > 0 && previousGameState == GameStatus.Playing)
                        {
                            if (TotalBeatmapHits >= PlaySubmissionService.MinHitsToSubmit)
                            {
                                loggedRetry = true;
                                loggedRetryTime = newSongTime;
                                loggedRetryPrevTime = Time;
                                loggedRetryHits = TotalBeatmapHits;
                                deferredSubmissions.Add((false, CaptureHeaderLocked(), TotalBeatmapHits, Accuracy, Play300c, Play100c, Play50c, PlayMissc, Time, _currentGameMode, _settings.SoundFilePath, _settings.SubmitSoundEnabled));
                            }
                            ResetPerPlayCountersLocked();
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

                var snapshot = BuildSnapshotLocked();
                Volatile.Write(ref _lastSnapshot, snapshot);
            }

            foreach (int mods in deferredPpMods)
            {
                _beatmapState.FireUpdateDifficultyFromPpApi(mods);
            }

            foreach (var submission in deferredSubmissions)
            {
                _submissionService.TryPostBeatmapEntry(submission.Complete, submission.Header, submission.TotalHits, submission.Accuracy, submission.C300, submission.C100, submission.C50, submission.Miss, submission.Time, submission.GameMode, submission.SoundPath, submission.SoundEnabled);
            }

            if (loggedTransition)
            {
                _log.LogInformation("Transitioned from Playing to {NewGameState}. Completed={Completed}. Hits={Hits}", loggedCurrent, loggedCompleted, loggedHits);
            }

            if (loggedRetry)
            {
                _log.LogInformation("Retry detected (Time rewound: {NewSongTime} < {Time}). Hits={Hits}", loggedRetryTime, loggedRetryPrevTime, loggedRetryHits);
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
            TrackerSnapshot? snap = Volatile.Read(ref _lastSnapshot);
            if (snap == null) return;

            if (!snap.IsTosuConnected)
            {
                UiDispatcher.Post(() => _form.UpdateTime());
                return;
            }

            if (snap.IsPlaying)
                Interlocked.Increment(ref _playingSeconds);
            else
                Interlocked.Increment(ref _idleSeconds);

            _ = _sessionManager.UpdateStatsAsync(PlayingSeconds, IdleSeconds, snap.DetectedClient);
            UiDispatcher.Post(() => _form.UpdateTime());
        }

        public Task<bool> FlushPendingSubmissionsAsync(CancellationToken ct = default) => _submissionService.FlushPendingSubmissionsAsync(ct);

        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                _settings.SettingsChanged -= SyncSheetsSettings;
            }
            catch
            {
            }

            try
            {
                if (_submissionService is IDisposable disposableSubmission)
                {
                    disposableSubmission.Dispose();
                }
            }
            catch
            {
            }

            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            DisposePlaySink(_playSink, visited);

            if (_localSqliteSink != null && !ReferenceEquals(_localSqliteSink, _playSink))
            {
                DisposePlaySink(_localSqliteSink, visited);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                _settings.SettingsChanged -= SyncSheetsSettings;
            }
            catch
            {
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _submissionService.FlushPendingSubmissionsAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                if (_submissionService is IDisposable disposableSubmission)
                {
                    disposableSubmission.Dispose();
                }
            }
            catch
            {
            }

            var asyncVisited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await DisposePlaySinkAsync(_playSink, asyncVisited).ConfigureAwait(false);

            if (_localSqliteSink != null && !ReferenceEquals(_localSqliteSink, _playSink))
            {
                await DisposePlaySinkAsync(_localSqliteSink, asyncVisited).ConfigureAwait(false);
            }
        }

        private static void DisposePlaySink(object? sink, HashSet<object>? visited)
        {
            try
            {
                visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

                if (sink == null || !visited.Add(sink))
                {
                    return;
                }

                if (sink is CompositePlaySink composite)
                {
                    foreach (SinkRegistration registration in composite.Registrations)
                    {
                        DisposePlaySink(registration.Sink, visited);
                    }

                    return;
                }

                if (sink is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch
            {
            }
        }

        private static async Task DisposePlaySinkAsync(object? sink, HashSet<object>? visited)
        {
            try
            {
                visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

                if (sink == null || !visited.Add(sink))
                {
                    return;
                }

                if (sink is CompositePlaySink composite)
                {
                    foreach (SinkRegistration registration in composite.Registrations)
                    {
                        await DisposePlaySinkAsync(registration.Sink, visited).ConfigureAwait(false);
                    }

                    return;
                }

                if (sink is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (sink is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch
            {
            }
        }
    }
}
