using Circle_Tracker.Storage;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Services;

public class PlaySubmissionService : IPlaySubmissionService, IDisposable
{
    public const int MinHitsToSubmit = 40;

    private readonly IPlaySink _playSink;
    private readonly ISessionManager _sessionManager;
    private readonly IBeatmapStateTracker? _beatmapState;
    private readonly IGameStateManager? _gameStateManager;

    private readonly SemaphoreSlim _sheetsLock = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<Task, byte> _activeSubmissionTasks = new();
    private readonly object _dedupLock = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    private string _lastLoggedBeatmapChecksum = "";
    private int _lastLoggedBeatmapId = 0;
    private string _lastLoggedBeatmapString = "";
    private int _lastLoggedMods = -1;
    private int _consecutivePlayCount = 0;
    private bool _lastLoggedComplete = false;

    public int ConsecutivePlayCount
    {
        get
        {
            lock (_dedupLock)
            {
                return _consecutivePlayCount;
            }
        }
    }

    public event EventHandler<(PlayEntryData Data, PlayContext Context)>? PlayLogged;

    public PlaySubmissionService(IPlaySink playSink, SessionManager sessionManager)
        : this(playSink, (ISessionManager)sessionManager, null, null)
    {
    }

    public PlaySubmissionService(
        IPlaySink playSink,
        SessionManager sessionManager,
        IBeatmapStateTracker? beatmapState,
        IGameStateManager? gameStateManager)
        : this(playSink, (ISessionManager)sessionManager, beatmapState, gameStateManager)
    {
    }

    public PlaySubmissionService(
        IPlaySink playSink,
        ISessionManager sessionManager,
        IBeatmapStateTracker? beatmapState = null,
        IGameStateManager? gameStateManager = null)
    {
        _playSink = playSink ?? throw new ArgumentNullException(nameof(playSink));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _beatmapState = beatmapState;
        _gameStateManager = gameStateManager;
    }

    public async Task<bool> FlushPendingSubmissionsAsync(CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken externalToken = ct;

        while (true)
        {
            Task[] pending = _activeSubmissionTasks.Keys.ToArray();

            if (pending.Length == 0)
            {
                return true;
            }

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, timeoutCts.Token);
                await Task.WhenAll(pending).WaitAsync(Timeout.InfiniteTimeSpan, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (externalToken.IsCancellationRequested && !timeoutCts.IsCancellationRequested)
                {
                    externalToken = CancellationToken.None;
                    continue;
                }

                return _activeSubmissionTasks.IsEmpty;
            }
        }
    }

    public void TryPostBeatmapEntry(
        bool complete,
        int totalBeatmapHits,
        decimal accuracy = 0,
        int play300c = 0,
        int play100c = 0,
        int play50c = 0,
        int playMissc = 0,
        int time = 0,
        int currentGameMode = 0,
        string? soundFilePath = null,
        bool submitSoundEnabled = false)
    {
        if (_beatmapState == null || _gameStateManager == null)
        {
            throw new InvalidOperationException("BeatmapState and GameStateManager must be provided via constructor or method parameters.");
        }

        TryPostBeatmapEntry(
            complete,
            _beatmapState,
            _gameStateManager,
            totalBeatmapHits,
            accuracy,
            play300c,
            play100c,
            play50c,
            playMissc,
            time,
            currentGameMode,
            soundFilePath,
            submitSoundEnabled);
    }

    public void TryPostBeatmapEntry(
        bool complete,
        IBeatmapStateTracker beatmapState,
        IGameStateManager gameStateManager,
        int totalBeatmapHits,
        decimal accuracy = 0,
        int play300c = 0,
        int play100c = 0,
        int play50c = 0,
        int playMissc = 0,
        int time = 0,
        int currentGameMode = 0,
        string? soundFilePath = null,
        bool submitSoundEnabled = false)
    {
        if (totalBeatmapHits < MinHitsToSubmit || gameStateManager.IsReplay || currentGameMode != 0)
            return;

        var header = new PlayHeaderSnapshot(
            beatmapState.CurrentBeatmapChecksum,
            beatmapState.BeatmapID,
            beatmapState.BeatmapSetID,
            beatmapState.BeatmapString,
            beatmapState.BeatmapTitle,
            beatmapState.BeatmapArtist,
            beatmapState.BeatmapVersion,
            beatmapState.BeatmapHp,
            beatmapState.BeatmapBpm,
            beatmapState.BeatmapStars,
            beatmapState.BeatmapAim,
            beatmapState.BeatmapSpeed,
            beatmapState.BeatmapCs,
            beatmapState.BeatmapAr,
            beatmapState.BeatmapOd,
            beatmapState.RawMods,
            beatmapState.Hidden,
            beatmapState.Hardrock,
            beatmapState.Doubletime,
            beatmapState.EZ,
            beatmapState.Halftime,
            beatmapState.Flashlight,
            beatmapState.GetModsString(),
            beatmapState.FirstHitObjectTime,
            beatmapState.LastClockRate,
            gameStateManager.IsReplay,
            gameStateManager.DetectedClient);

        TryPostBeatmapEntry(
            complete,
            header,
            totalBeatmapHits,
            accuracy,
            play300c,
            play100c,
            play50c,
            playMissc,
            time,
            currentGameMode,
            soundFilePath,
            submitSoundEnabled);
    }

    public void TryPostBeatmapEntry(
        bool complete,
        PlayHeaderSnapshot header,
        int totalBeatmapHits,
        decimal accuracy = 0,
        int play300c = 0,
        int play100c = 0,
        int play50c = 0,
        int playMissc = 0,
        int time = 0,
        int currentGameMode = 0,
        string? soundFilePath = null,
        bool submitSoundEnabled = false)
    {
        if (totalBeatmapHits < MinHitsToSubmit || header.IsReplay || currentGameMode != 0)
            return;

        string snapshotChecksum = header.Checksum;
        int snapshotBeatmapId = header.BeatmapID;
        string snapshotBeatmapString = header.BeatmapString;
        int snapshotRawMods = header.RawMods;
        int snapshotPlayCount;

        lock (_dedupLock)
        {
            bool isSameMap = (!string.IsNullOrEmpty(snapshotChecksum) && snapshotChecksum == _lastLoggedBeatmapChecksum)
                || (snapshotBeatmapId > 0 && snapshotBeatmapId == _lastLoggedBeatmapId)
                || (!string.IsNullOrEmpty(snapshotBeatmapString) && snapshotBeatmapString == _lastLoggedBeatmapString);

            if (isSameMap && snapshotRawMods == _lastLoggedMods && !_lastLoggedComplete)
            {
                _consecutivePlayCount++;
            }
            else
            {
                _consecutivePlayCount = 1;
                _lastLoggedBeatmapChecksum = snapshotChecksum;
                _lastLoggedBeatmapId = snapshotBeatmapId;
                _lastLoggedBeatmapString = snapshotBeatmapString;
                _lastLoggedMods = snapshotRawMods;
            }

            _lastLoggedComplete = complete;
            snapshotPlayCount = _consecutivePlayCount;
        }

        float clockRate = header.LastClockRate > 0 ? header.LastClockRate : (header.Doubletime ? 1.5f : header.Halftime ? 0.75f : 1f);
        int playTime = (int)(Math.Max(0, time - header.FirstHitObjectTime) / clockRate / 1000f);
        bool accuracyReliable = accuracy > 0 && totalBeatmapHits > 0;

        var data = new PlayEntryData(
            BeatmapString: header.BeatmapString,
            BeatmapSetID: header.BeatmapSetID,
            BeatmapID: header.BeatmapID,
            Hidden: header.Hidden,
            Hardrock: header.Hardrock,
            Doubletime: header.Doubletime,
            EZ: header.EZ,
            Halftime: header.Halftime,
            Flashlight: header.Flashlight,
            BeatmapBpm: header.BeatmapBpm,
            BeatmapAim: header.BeatmapAim,
            BeatmapSpeed: header.BeatmapSpeed,
            BeatmapStars: header.BeatmapStars,
            BeatmapCs: header.BeatmapCs,
            BeatmapAr: header.BeatmapAr,
            BeatmapOd: header.BeatmapOd,
            TotalBeatmapHits: totalBeatmapHits,
            Accuracy: accuracy,
            Play300c: play300c,
            Play100c: play100c,
            Play50c: play50c,
            PlayMissc: playMissc,
            Complete: complete,
            PlayTimeSeconds: playTime,
            ModsString: header.ModsString,
            PlayCount: snapshotPlayCount,
            AccuracyReliable: accuracyReliable,
            BeatmapTitle: header.BeatmapTitle,
            BeatmapArtist: header.BeatmapArtist,
            BeatmapVersion: header.BeatmapVersion,
            BeatmapHp: header.BeatmapHp,
            BeatmapChecksum: snapshotChecksum,
            ClientId: Guid.NewGuid().ToString()
        );

        var context = new PlayContext(
            SessionId: _sessionManager.SessionId,
            IsReplay: header.IsReplay,
            RawMods: snapshotRawMods,
            CurrentGameMode: currentGameMode,
            DetectedClient: header.DetectedClient,
            SoundFilePath: soundFilePath,
            SubmitSoundEnabled: submitSoundEnabled
        );

        CancellationToken disposeToken;

        try
        {
            disposeToken = _disposeCts.Token;
        }
        catch (ObjectDisposedException)
        {
            disposeToken = CancellationToken.None;
        }

        Task submissionTask = Task.Run(async () =>
        {
            await _sheetsLock.WaitAsync(disposeToken).ConfigureAwait(false);
            try
            {
                await _playSink.TryLogPlayAsync(data, context, disposeToken).ConfigureAwait(false);
                await _sessionManager.IncrementPlaysAsync(disposeToken).ConfigureAwait(false);
                PlayLogged?.Invoke(this, (data, context));
            }
            finally
            {
                _sheetsLock.Release();
            }
        });

        _activeSubmissionTasks.TryAdd(submissionTask, 0);

        _ = submissionTask.ContinueWith(t =>
        {
            _activeSubmissionTasks.TryRemove(t, out _);
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _disposeCts.Cancel();
        }
        catch
        {
        }

        _sheetsLock.Dispose();
        _disposeCts.Dispose();
    }
}
