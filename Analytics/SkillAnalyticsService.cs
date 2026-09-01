using Circle_Tracker.Storage;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Analytics
{
    public class SkillAnalyticsService : ISkillAnalyticsService
    {
        private readonly IDatabaseManager _dbManager;

        public SkillAnalyticsService(IDatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        public async Task<IReadOnlyList<StarMasteryBracket>> GetStarMasteryCurveAsync(CancellationToken ct = default)
        {
            var bracketsDef = new (double Min, double Max)[]
            {
                (4.0, 4.5),
                (4.5, 5.0),
                (5.0, 5.5),
                (5.5, 6.0),
                (6.0, 6.5),
                (6.5, 7.0),
                (7.0, 7.5),
                (7.5, 8.0),
                (8.0, 99.0)
            };

            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            const string sql = @"
                WITH BracketPlays AS (
                    SELECT
                        CASE 
                            WHEN stars >= 8.0 THEN 8.0 
                            ELSE CAST(stars * 2 AS INTEGER) / 2.0 
                        END AS bracket,
                        accuracy,
                        is_complete,
                        ROW_NUMBER() OVER (
                            PARTITION BY CASE WHEN stars >= 8.0 THEN 8.0 ELSE CAST(stars * 2 AS INTEGER) / 2.0 END 
                            ORDER BY accuracy ASC
                        ) - 1 AS row_idx,
                        COUNT(*) OVER (
                            PARTITION BY CASE WHEN stars >= 8.0 THEN 8.0 ELSE CAST(stars * 2 AS INTEGER) / 2.0 END
                        ) AS total_count
                    FROM plays
                    WHERE stars >= 4.0
                )
                SELECT 
                    bracket AS Bracket,
                    total_count AS TotalCount,
                    SUM(CASE WHEN is_complete = 1 THEN 1 ELSE 0 END) AS Passes,
                    AVG(accuracy) AS MeanAcc,
                    AVG(CASE 
                        WHEN total_count % 2 = 1 AND row_idx = total_count / 2 THEN accuracy
                        WHEN total_count % 2 = 0 AND (row_idx = (total_count / 2) - 1 OR row_idx = total_count / 2) THEN accuracy
                        ELSE NULL 
                    END) AS MedianAcc,
                    MAX(CASE 
                        WHEN row_idx = CAST((total_count - 1) * 0.90 AS INTEGER) THEN accuracy 
                        ELSE NULL 
                    END) AS P90Acc
                FROM BracketPlays
                GROUP BY bracket, total_count;";

            var rows = (await conn.QueryAsync<(
                double Bracket, int TotalCount, int Passes, double MeanAcc, double? MedianAcc, double? P90Acc
            )>(sql)).ToDictionary(r => r.Bracket);

            var result = new List<StarMasteryBracket>();

            foreach (var (min, max) in bracketsDef)
            {
                if (rows.TryGetValue(min, out var row) && row.TotalCount > 0)
                {
                    int total = row.TotalCount;
                    int passes = row.Passes;
                    double passRate = (100.0 * passes / total);
                    decimal meanAcc = (decimal)row.MeanAcc;
                    decimal medianAcc = (decimal)(row.MedianAcc ?? row.MeanAcc);
                    decimal p90Acc = (decimal)(row.P90Acc ?? row.MeanAcc);

                    string skillZone;
                    if (meanAcc >= 95.00m)
                    {
                        skillZone = "Comfort";
                    }
                    else if (meanAcc > 90.00m)
                    {
                        skillZone = "Push";
                    }
                    else
                    {
                        skillZone = "Pass-Only";
                    }

                    result.Add(new StarMasteryBracket(
                        MinStars: min,
                        MaxStars: max,
                        TotalAttempts: total,
                        Passes: passes,
                        PassRatePercent: Math.Round(passRate, 2),
                        MeanAccuracy: Math.Round(meanAcc, 2),
                        MedianAccuracy: Math.Round(medianAcc, 2),
                        P90Accuracy: Math.Round(p90Acc, 2),
                        SkillZone: skillZone
                    ));
                }
                else
                {
                    result.Add(new StarMasteryBracket(
                        MinStars: min,
                        MaxStars: max,
                        TotalAttempts: 0,
                        Passes: 0,
                        PassRatePercent: 0.0,
                        MeanAccuracy: 0.0m,
                        MedianAccuracy: 0.0m,
                        P90Accuracy: 0.0m,
                        SkillZone: "Comfort"
                    ));
                }
            }

            return result.AsReadOnly();
        }

        public async Task<AimSpeedProfile> GetAimSpeedProfileAsync(CancellationToken ct = default)
        {
            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            const string sql = @"
                SELECT
                    COUNT(CASE WHEN aim >= 1.20 * speed THEN 1 END) AS AimCount,
                    AVG(CASE WHEN aim >= 1.20 * speed THEN accuracy END) AS AimAvgAcc,
                    SUM(CASE WHEN aim >= 1.20 * speed AND is_complete = 1 THEN 1 ELSE 0 END) AS AimPasses,

                    COUNT(CASE WHEN speed >= 1.20 * aim AND NOT (aim >= 1.20 * speed) THEN 1 END) AS SpeedCount,
                    AVG(CASE WHEN speed >= 1.20 * aim AND NOT (aim >= 1.20 * speed) THEN accuracy END) AS SpeedAvgAcc,
                    SUM(CASE WHEN speed >= 1.20 * aim AND NOT (aim >= 1.20 * speed) AND is_complete = 1 THEN 1 ELSE 0 END) AS SpeedPasses,

                    COUNT(CASE WHEN NOT (aim >= 1.20 * speed) AND NOT (speed >= 1.20 * aim) THEN 1 END) AS BalancedCount,
                    AVG(CASE WHEN NOT (aim >= 1.20 * speed) AND NOT (speed >= 1.20 * aim) THEN accuracy END) AS BalancedAvgAcc,
                    SUM(CASE WHEN NOT (aim >= 1.20 * speed) AND NOT (speed >= 1.20 * aim) AND is_complete = 1 THEN 1 ELSE 0 END) AS BalancedPasses
                FROM plays;";

            var row = await conn.QueryFirstOrDefaultAsync<(
                int AimCount, double? AimAvgAcc, int AimPasses,
                int SpeedCount, double? SpeedAvgAcc, int SpeedPasses,
                int BalancedCount, double? BalancedAvgAcc, int BalancedPasses
            )>(sql);

            int aimCount = row.AimCount;
            int speedCount = row.SpeedCount;
            int balancedCount = row.BalancedCount;

            decimal aimAvgAcc = (aimCount > 0 && row.AimAvgAcc.HasValue) ? (decimal)row.AimAvgAcc.Value : 0.0m;
            double aimPassRate = aimCount > 0 ? (100.0 * row.AimPasses / aimCount) : 0.0;

            decimal speedAvgAcc = (speedCount > 0 && row.SpeedAvgAcc.HasValue) ? (decimal)row.SpeedAvgAcc.Value : 0.0m;
            double speedPassRate = speedCount > 0 ? (100.0 * row.SpeedPasses / speedCount) : 0.0;

            decimal balancedAvgAcc = (balancedCount > 0 && row.BalancedAvgAcc.HasValue) ? (decimal)row.BalancedAvgAcc.Value : 0.0m;
            double balancedPassRate = balancedCount > 0 ? (100.0 * row.BalancedPasses / balancedCount) : 0.0;

            int totalBiased = aimCount + speedCount;
            double aimBiasPercent = totalBiased > 0 ? (100.0 * aimCount / totalBiased) : 50.0;
            double speedBiasPercent = totalBiased > 0 ? (100.0 * speedCount / totalBiased) : 50.0;

            return new AimSpeedProfile(
                AimDominantPlays: aimCount,
                AimAvgAcc: Math.Round(aimAvgAcc, 2),
                AimPassRate: Math.Round(aimPassRate, 2),
                SpeedDominantPlays: speedCount,
                SpeedAvgAcc: Math.Round(speedAvgAcc, 2),
                SpeedPassRate: Math.Round(speedPassRate, 2),
                BalancedPlays: balancedCount,
                BalancedAvgAcc: Math.Round(balancedAvgAcc, 2),
                BalancedPassRate: Math.Round(balancedPassRate, 2),
                AimBiasPercent: Math.Round(aimBiasPercent, 2),
                SpeedBiasPercent: Math.Round(speedBiasPercent, 2)
            );
        }

        public async Task<IReadOnlyList<OdAccuracyTier>> GetOdAccuracyCurveAsync(CancellationToken ct = default)
        {
            var tiersDef = new (string Name, double Min, double Max, double Window)[]
            {
                ("OD <= 8.0", 0.0, 8.0, 32.0),
                ("OD 8.1 - 9.0", 8.1, 9.0, 26.0),
                ("OD 9.1 - 9.7", 9.1, 9.7, 21.8),
                ("OD 9.8 - 10.0", 9.8, 10.0, 20.0),
                ("OD > 10.0", 10.1, 12.0, 14.0)
            };

            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            const string sql = @"
                SELECT
                    CASE
                        WHEN od <= 8.05 THEN 0
                        WHEN od > 8.05 AND od <= 9.05 THEN 1
                        WHEN od > 9.05 AND od <= 9.75 THEN 2
                        WHEN od > 9.75 AND od <= 10.05 THEN 3
                        ELSE 4
                    END AS tier_idx,
                    COUNT(*) AS total_plays,
                    AVG(accuracy) AS mean_acc,
                    SUM(hit_300) AS total_300,
                    SUM(hit_100) AS total_100
                FROM plays
                GROUP BY tier_idx;";

            var rows = (await conn.QueryAsync<(
                int TierIdx, int TotalPlays, double? MeanAcc, long? Total300, long? Total100
            )>(sql)).ToDictionary(r => r.TierIdx);

            var result = new List<OdAccuracyTier>();

            for (int i = 0; i < tiersDef.Length; i++)
            {
                var (name, min, max, window) = tiersDef[i];
                if (rows.TryGetValue(i, out var row) && row.TotalPlays > 0)
                {
                    int total = row.TotalPlays;
                    decimal meanAcc = (decimal)(row.MeanAcc ?? 0.0);
                    long sum300 = row.Total300 ?? 0;
                    long sum100 = row.Total100 ?? 0;
                    double ratio = sum300 > 0 ? ((double)sum100 / sum300) : 0.0;

                    result.Add(new OdAccuracyTier(
                        TierName: name,
                        MinOd: min,
                        MaxOd: max,
                        HitWindow300Ms: window,
                        TotalPlays: total,
                        MeanAccuracy: Math.Round(meanAcc, 2),
                        Ratio100sTo300s: Math.Round(ratio, 4)
                    ));
                }
                else
                {
                    result.Add(new OdAccuracyTier(
                        TierName: name,
                        MinOd: min,
                        MaxOd: max,
                        HitWindow300Ms: window,
                        TotalPlays: 0,
                        MeanAccuracy: 0.0m,
                        Ratio100sTo300s: 0.0
                    ));
                }
            }

            return result.AsReadOnly();
        }

        public async Task<IReadOnlyList<BpmBracketStats>> GetBpmSpeedCeilingsAsync(CancellationToken ct = default)
        {
            var bpmBrackets = new (string Label, int Min, int Max)[]
            {
                ("< 170 BPM", 0, 169),
                ("170 - 189 BPM", 170, 189),
                ("190 - 209 BPM", 190, 209),
                ("210 - 229 BPM", 210, 229),
                ("230 - 249 BPM", 230, 249),
                ("250+ BPM", 250, int.MaxValue)
            };

            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            const string sql = @"
                SELECT
                    CASE 
                        WHEN bpm < 170 THEN 0
                        WHEN bpm BETWEEN 170 AND 189 THEN 1
                        WHEN bpm BETWEEN 190 AND 209 THEN 2
                        WHEN bpm BETWEEN 210 AND 229 THEN 3
                        WHEN bpm BETWEEN 230 AND 249 THEN 4
                        ELSE 5
                    END AS bracket_idx,
                    COUNT(*) AS play_count,
                    AVG(accuracy) AS mean_acc,
                    SUM(hit_miss) AS total_misses,
                    SUM(total_hits) AS total_hits
                FROM plays
                GROUP BY bracket_idx;";

            var rows = (await conn.QueryAsync<(
                int BracketIdx, int PlayCount, double? MeanAcc, long? TotalMisses, long? TotalHits
            )>(sql)).ToDictionary(r => r.BracketIdx);

            var result = new List<BpmBracketStats>();

            for (int i = 0; i < bpmBrackets.Length; i++)
            {
                var (label, min, max) = bpmBrackets[i];
                if (rows.TryGetValue(i, out var row) && row.PlayCount > 0)
                {
                    int count = row.PlayCount;
                    decimal meanAcc = (decimal)(row.MeanAcc ?? 0.0);
                    long sumMiss = row.TotalMisses ?? 0;
                    long sumHits = row.TotalHits ?? 0;
                    double missDensity = sumHits > 0 ? (100.0 * sumMiss / sumHits) : 0.0;

                    result.Add(new BpmBracketStats(
                        Label: label,
                        MinBpm: min,
                        MaxBpm: max,
                        PlayCount: count,
                        MeanAccuracy: Math.Round(meanAcc, 2),
                        MissesPerHundredHits: Math.Round(missDensity, 2)
                    ));
                }
                else
                {
                    result.Add(new BpmBracketStats(
                        Label: label,
                        MinBpm: min,
                        MaxBpm: max,
                        PlayCount: 0,
                        MeanAccuracy: 0.0m,
                        MissesPerHundredHits: 0.0
                    ));
                }
            }

            return result.AsReadOnly();
        }
    }
}
