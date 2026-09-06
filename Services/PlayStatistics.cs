namespace Circle_Tracker.Services;

internal class PlayStatistics
{
    public int Play300c { get; set; }
    public int Play100c { get; set; }
    public int Play50c { get; set; }
    public int PlayMissc { get; set; }
    public int TotalBeatmapHits { get; set; }
    public decimal Accuracy { get; set; }
    public int Time { get; set; }

    public void Reset()
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
