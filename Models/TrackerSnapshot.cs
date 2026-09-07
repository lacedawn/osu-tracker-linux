namespace Circle_Tracker;

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
    int LocalPlayCount = 0,
    bool ProfileIdentityConfirmed = false
)
{
    public string CoverUrl => BeatmapSetId > 0 ? $"https://assets.ppy.sh/beatmaps/{BeatmapSetId}/covers/cover.jpg" : "";
}
