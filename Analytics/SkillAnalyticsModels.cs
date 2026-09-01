namespace Circle_Tracker.Analytics
{
    public record StarMasteryBracket(
        double MinStars, double MaxStars,
        int TotalAttempts, int Passes, double PassRatePercent,
        decimal MeanAccuracy, decimal MedianAccuracy, decimal P90Accuracy,
        string SkillZone
    );

    public record AimSpeedProfile(
        int AimDominantPlays, decimal AimAvgAcc, double AimPassRate,
        int SpeedDominantPlays, decimal SpeedAvgAcc, double SpeedPassRate,
        int BalancedPlays, decimal BalancedAvgAcc, double BalancedPassRate,
        double AimBiasPercent, double SpeedBiasPercent
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
