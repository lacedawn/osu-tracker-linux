namespace Circle_Tracker.Services;

public interface IHitTrackingService
{
    int Play300c { get; }
    int Play100c { get; }
    int Play50c { get; }
    int PlayMissc { get; }
    int TotalBeatmapHits { get; }
    decimal Accuracy { get; }
    int Time { get; }
    
    void UpdateHitStatistics(int new300c, int new100c, int new50c, int newMissc, decimal newAcc, int newSongTime);
    void ResetHitStatistics();
}
