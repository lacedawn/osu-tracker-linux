using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Services;

public interface IBeatmapStateTracker
{
    string CurrentBeatmapChecksum { get; set; }
    int BeatmapID { get; set; }
    int BeatmapSetID { get; set; }
    string BeatmapString { get; set; }
    string BeatmapTitle { get; set; }
    string BeatmapArtist { get; set; }
    string BeatmapVersion { get; set; }
    decimal BeatmapHp { get; set; }
    int BeatmapBpm { get; set; }
    decimal BeatmapStars { get; set; }
    decimal BeatmapAim { get; set; }
    decimal BeatmapSpeed { get; set; }
    decimal BeatmapCs { get; set; }
    decimal BeatmapAr { get; set; }
    decimal BeatmapOd { get; set; }
    int FirstHitObjectTime { get; set; }
    float LastClockRate { get; set; }

    int RawMods { get; set; }
    bool Hidden { get; set; }
    bool Hardrock { get; set; }
    bool Doubletime { get; set; }
    bool EZ { get; set; }
    bool Halftime { get; set; }
    bool Flashlight { get; set; }
    bool Auto { get; set; }

    void UpdateBeatmapFromState(TosuState state);
    void UpdateModsFromBitfield(int rawMods);
    string GetModsString();
    void FireUpdateDifficultyFromPpApi(int modNumber);
    Task UpdateDifficultyFromPpApi(int modNumber, CancellationToken ct = default);
}
