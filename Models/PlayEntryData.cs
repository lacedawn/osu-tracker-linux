namespace Circle_Tracker;

public record PlayEntryData(
    string BeatmapString, int BeatmapSetID, int BeatmapID,
    bool Hidden, bool Hardrock, bool Doubletime, bool EZ, bool Halftime, bool Flashlight,
    int BeatmapBpm, decimal BeatmapAim, decimal BeatmapSpeed, decimal BeatmapStars,
    decimal BeatmapCs, decimal BeatmapAr, decimal BeatmapOd,
    int TotalBeatmapHits, decimal Accuracy,
    int Play300c, int Play100c, int Play50c, int PlayMissc,
    bool Complete, int PlayTimeSeconds, string ModsString,
    int PlayCount,
    bool AccuracyReliable,
    string BeatmapTitle = "",
    string BeatmapArtist = "",
    string BeatmapVersion = "",
    decimal BeatmapHp = 0m,
    string BeatmapChecksum = "",
    string ClientId = ""
)
{
    private readonly int? _totalHits;

    public int TotalHits
    {
        get => _totalHits ?? ((Play300c + Play100c + Play50c + PlayMissc) > 0 ? (Play300c + Play100c + Play50c + PlayMissc) : TotalBeatmapHits);
        init => _totalHits = value;
    }
}
