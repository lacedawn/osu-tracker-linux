namespace Circle_Tracker.Analytics
{
    public record FatigueBucket(
        string TimeRangeLabel, int StartMinute, int EndMinute,
        int SampleSize, decimal MeanAccuracy, decimal AccDeltaFromSessionAvg, double MissRatePerMinute
    );

    public record RollingPeriodStats(
        int PeriodDays,
        int TotalPlays,
        double TotalActiveHours,
        decimal MeanAccuracy,
        decimal MeanStars,
        double PassRatePercent,
        double MeanBpm
    );

    public record ChokeMapRecord(
        int BeatmapId, int BeatmapSetId, string BeatmapString,
        string CoverUrl, int ChokeCount, decimal BestChokeAcc, int MinMisses,
        int TotalMapAttempts
    );

    public record HeadToHeadComparison(
        decimal SessionAcc, decimal Baseline30DAcc, decimal DeltaAcc,
        decimal SessionStars, decimal Baseline30DStars, decimal DeltaStars,
        double SessionPassRate, double Baseline30DPassRate, double DeltaPassRate,
        int SessionPlays, double SessionActiveMinutes
    );
}
