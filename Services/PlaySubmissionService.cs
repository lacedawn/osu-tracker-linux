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

    private string _lastLoggedBeatmapChecksum = "";
    private int _lastLoggedBeatmapId = 0;
    private string _lastLoggedBeatmapString = "";
    private int _lastLoggedMods = -1;
    private int _consecutivePlayCount = 0;
    private bool _lastLoggedComplete = false;

    public int ConsecutivePlayCount => _consecutivePlayCount;

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

    public async Task FlushPendingSubmissionsAsync(CancellationToken ct = default)
    {
        var pending = _activeSubmissionTasks.Keys.ToArray();

        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
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

        bool isSameMap = (!string.IsNullOrEmpty(beatmapState.CurrentBeatmapChecksum) && beatmapState.CurrentBeatmapChecksum == _lastLoggedBeatmapChecksum)
            || (beatmapState.BeatmapID > 0 && beatmapState.BeatmapID == _lastLoggedBeatmapId)
            || (!string.IsNullOrEmpty(beatmapState.BeatmapString) && beatmapState.BeatmapString == _lastLoggedBeatmapString);

        if (isSameMap && beatmapState.RawMods == _lastLoggedMods && !_lastLoggedComplete)
        {
            _consecutivePlayCount++;
        }
        else
        {
            _consecutivePlayCount = 1;
            _lastLoggedBeatmapChecksum = beatmapState.CurrentBeatmapChecksum;
            _lastLoggedBeatmapId = beatmapState.BeatmapID;
            _lastLoggedBeatmapString = beatmapState.BeatmapString;
            _lastLoggedMods = beatmapState.RawMods;
        }

        _lastLoggedComplete = complete;

        float clockRate = beatmapState.LastClockRate > 0 ? beatmapState.LastClockRate : (beatmapState.Doubletime ? 1.5f : beatmapState.Halftime ? 0.75f : 1f);
        int playTime = (int)(Math.Max(0, time - beatmapState.FirstHitObjectTime) / clockRate / 1000f);
        bool accuracyReliable = accuracy > 0 && totalBeatmapHits > 0;

        var data = new PlayEntryData(
            BeatmapString: beatmapState.BeatmapString,
            BeatmapSetID: beatmapState.BeatmapSetID,
            BeatmapID: beatmapState.BeatmapID,
            Hidden: beatmapState.Hidden,
            Hardrock: beatmapState.Hardrock,
            Doubletime: beatmapState.Doubletime,
            EZ: beatmapState.EZ,
            Halftime: beatmapState.Halftime,
            Flashlight: beatmapState.Flashlight,
            BeatmapBpm: beatmapState.BeatmapBpm,
            BeatmapAim: beatmapState.BeatmapAim,
            BeatmapSpeed: beatmapState.BeatmapSpeed,
            BeatmapStars: beatmapState.BeatmapStars,
            BeatmapCs: beatmapState.BeatmapCs,
            BeatmapAr: beatmapState.BeatmapAr,
            BeatmapOd: beatmapState.BeatmapOd,
            TotalBeatmapHits: totalBeatmapHits,
            Accuracy: accuracy,
            Play300c: play300c,
            Play100c: play100c,
            Play50c: play50c,
            PlayMissc: playMissc,
            Complete: complete,
            PlayTimeSeconds: playTime,
            ModsString: beatmapState.GetModsString(),
            PlayCount: _consecutivePlayCount,
            AccuracyReliable: accuracyReliable,
            BeatmapTitle: beatmapState.BeatmapTitle,
            BeatmapArtist: beatmapState.BeatmapArtist,
            BeatmapVersion: beatmapState.BeatmapVersion,
            BeatmapHp: beatmapState.BeatmapHp,
            BeatmapChecksum: beatmapState.CurrentBeatmapChecksum
        );

        var context = new PlayContext(
            SessionId: _sessionManager.SessionId,
            IsReplay: gameStateManager.IsReplay,
            RawMods: beatmapState.RawMods,
            CurrentGameMode: currentGameMode,
            DetectedClient: gameStateManager.DetectedClient,
            SoundFilePath: soundFilePath,
            SubmitSoundEnabled: submitSoundEnabled
        );

        Task submissionTask = Task.Run(async () =>
        {
            await _sheetsLock.WaitAsync(CancellationToken.None);
            try
            {
                await _playSink.TryLogPlayAsync(data, context);
                await _sessionManager.IncrementPlaysAsync();
                PlayLogged?.Invoke(this, (data, context));
            }
            finally
            {
                _sheetsLock.Release();
            }
        }, CancellationToken.None);

        _activeSubmissionTasks.TryAdd(submissionTask, 0);

        _ = submissionTask.ContinueWith(t =>
        {
            _activeSubmissionTasks.TryRemove(t, out _);
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _sheetsLock.Dispose();
    }
}
