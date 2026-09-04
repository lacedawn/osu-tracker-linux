using System;

namespace Circle_Tracker.Analytics
{
    public record RollingPeriodStats(
        int PeriodDays,
        int TotalPlays,
        double TotalActiveHours,
        decimal MeanAccuracy,
        decimal MeanStars,
        double PassRatePercent,
        double MeanBpm,
        double PlaysPerActiveDay,
        double HoursPerActiveDay,
        bool HasSufficientData = true,
        string DateRangeText = "",
        int HistoryDaysAvailable = 0
    )
    {
        public int Days => PeriodDays;
        public double AvgStars => (double)MeanStars;
        public double WeightedAccuracy => (double)MeanAccuracy;
        public double DailyPlayCount => PlaysPerActiveDay;
        public double DailyActiveHours => HoursPerActiveDay;
        public double AvgBpm => MeanBpm;
        public int DaysRemaining => Math.Max(0, PeriodDays - HistoryDaysAvailable);
        public double ProgressPercent => PeriodDays > 0 ? Math.Min(100.0, Math.Round(100.0 * HistoryDaysAvailable / PeriodDays, 1)) : 0.0;
    }

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

    public record DailyTrendItem(
        string DateString,
        int TotalAttempts,
        int Passes,
        double PassRatePercent,
        decimal AvgAccuracy,
        decimal AvgStars,
        double ActiveMinutes
    );
}
