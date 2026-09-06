using Microsoft.Extensions.Logging;

namespace Circle_Tracker.Services;

public class HitTrackingService : IHitTrackingService
{
    private static readonly ILogger<HitTrackingService> _log = AppLogger.For<HitTrackingService>();
    
    private const int MaxHitJumpPerTick = 50;
    private const int MaxTimeBetweenHitsMs = 30000;

    public int Play300c { get; private set; }
    public int Play100c { get; private set; }
    public int Play50c { get; private set; }
    public int PlayMissc { get; private set; }
    public int TotalBeatmapHits { get; private set; }
    public decimal Accuracy { get; private set; }
    public int Time { get; private set; }

    public void UpdateHitStatistics(int new300c, int new100c, int new50c, int newMissc, decimal newAcc, int newSongTime)
    {
        int newHits = new300c + new100c + new50c;

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

        Time = newSongTime;
    }

    public void ResetHitStatistics()
    {
        Play300c = 0;
        Play100c = 0;
        Play50c = 0;
        PlayMissc = 0;
        Accuracy = 0;
        TotalBeatmapHits = 0;
        Time = 0;
    }
}
