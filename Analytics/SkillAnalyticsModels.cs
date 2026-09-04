namespace Circle_Tracker.Analytics
{
    public record StarMasteryBracket(
        double MinStars, double MaxStars,
        int TotalAttempts, int Passes, double PassRatePercent,
        decimal MeanAccuracy, decimal MedianAccuracy, decimal P90Accuracy,
        string SkillZone
    )
    {
        public string Label => MaxStars >= 99.0 ? $"{MinStars:F1}+★" : $"{MinStars:F1}–{MinStars + 0.4:F1}★";
        public double MinStar => MinStars;
        public double MaxStar => MaxStars;
        public int PassCount => Passes;
        public int AttemptCount => TotalAttempts;
        public double ProgressPercent => Passes > 0 ? (double)MeanAccuracy : 0.0;
        public string ZoneColor => SkillZone switch
        {
            "Comfort" => "#4ade80",
            "Push" => "#fb923c",
            "Pass-Only" => "#f87171",
            _ => "#94a3b8"
        };
        public string ZoneLabel => SkillZone;
        public string Subtext => TotalAttempts > 0 ? $"{Passes}/{TotalAttempts} passed ({PassRatePercent:F0}%)" : "No plays";
    }

    public record OdAccuracyTier(
        string TierName, double MinOd, double MaxOd, double HitWindow300Ms,
        int TotalPlays, decimal MeanAccuracy, double Ratio100sTo300s
    )
    {
        public string Label => string.IsNullOrEmpty(TierName) ? $"OD {MinOd:F1}-{MaxOd:F1}" : TierName;
        public double AvgAccuracy => (double)MeanAccuracy;
    }

    public record BpmBracketStats(
        string Label, int MinBpm, int MaxBpm,
        int PlayCount, decimal MeanAccuracy, double MissesPerHundredHits
    )
    {
        public double AvgAccuracy => (double)MeanAccuracy;
        public double MissDensityPer100 => MissesPerHundredHits;
    }
}
