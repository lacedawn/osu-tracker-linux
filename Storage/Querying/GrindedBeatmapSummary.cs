namespace Circle_Tracker.Storage.Querying
{
    public record GrindedBeatmapSummary(
        int BeatmapId,
        int BeatmapSetId,
        string BeatmapString,
        int TotalAttempts,
        double CumulativeHours)
    {
        public GrindedBeatmapSummary() : this(0, 0, "", 0, 0.0)
        {
        }
    }
}
