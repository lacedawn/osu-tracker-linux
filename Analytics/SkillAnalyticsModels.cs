namespace Circle_Tracker.Analytics
{
    public record StarMasteryBracket(
        double MinStars, double MaxStars,
        int TotalAttempts, int Passes, double PassRatePercent,
        decimal MeanAccuracy, decimal MedianAccuracy, decimal P90Accuracy,
        string SkillZone
    );


    public record OdAccuracyTier(
        string TierName, double MinOd, double MaxOd, double HitWindow300Ms,
        int TotalPlays, decimal MeanAccuracy, double Ratio100sTo300s
    );

    public record BpmBracketStats(
        string Label, int MinBpm, int MaxBpm,
        int PlayCount, decimal MeanAccuracy, double MissesPerHundredHits
    );
}
