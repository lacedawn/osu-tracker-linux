using Circle_Tracker.Storage;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Services;

public interface IPlaySubmissionService
{
    event EventHandler<(PlayEntryData Data, PlayContext Context)>? PlayLogged;
    int ConsecutivePlayCount { get; }
    Task<bool> FlushPendingSubmissionsAsync(CancellationToken ct = default);
    void TryPostBeatmapEntry(
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
        bool submitSoundEnabled = false);
    void TryPostBeatmapEntry(
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
        bool submitSoundEnabled = false);
    void TryPostBeatmapEntry(
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
        bool submitSoundEnabled = false);
}
