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
                SELECT 
                    CASE 
                        WHEN stars >= 8.0 THEN 8.0 
                        ELSE CAST(stars * 2 AS INTEGER) / 2.0 
                    END AS Bracket,
                    COUNT(*) AS TotalCount,
                    SUM(CASE WHEN is_complete = 1 THEN 1 ELSE 0 END) AS Passes,
                    AVG(CASE WHEN is_complete = 1 THEN accuracy ELSE NULL END) AS MeanPassedAcc,
                    AVG(accuracy) AS AllPlaysAcc
                FROM plays
                WHERE stars >= 4.0
                GROUP BY Bracket;";

            var rows = (await conn.QueryAsync<(
                double Bracket, int TotalCount, int Passes, double? MeanPassedAcc, double? AllPlaysAcc
            )>(sql)).ToDictionary(r => r.Bracket);

            var result = new List<StarMasteryBracket>();

            foreach (var (min, max) in bracketsDef)
            {
                if (rows.TryGetValue(min, out var row) && row.TotalCount > 0)
                {
                    int total = row.TotalCount;
                    int passes = row.Passes;
                    double passRate = total > 0 ? (100.0 * passes / total) : 0.0;
                    decimal meanAcc = row.Passes > 0 ? (decimal)(row.MeanPassedAcc ?? 0.0) : 0m;
                    string skillZone;
                    if (passes == 0)
                    {
                        skillZone = "Unpassed";
                    }
                    else if (meanAcc >= 95.0m && passRate >= 50.0)
                    {
                        skillZone = "Comfort";
                    }
                    else if (meanAcc >= 90.0m || passRate >= 25.0)
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
                        MedianAccuracy: Math.Round(meanAcc, 2),
                        P90Accuracy: Math.Round(meanAcc, 2),
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
                        SkillZone: "Unpassed"
                    ));
                }
            }

            return result.AsReadOnly();
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
                    AVG(CASE WHEN is_complete = 1 THEN accuracy ELSE NULL END) AS mean_acc,
                    SUM(CASE WHEN is_complete = 1 THEN hit_300 ELSE 0 END) AS total_300,
                    SUM(CASE WHEN is_complete = 1 THEN hit_100 ELSE 0 END) AS total_100
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
                    AVG(CASE WHEN is_complete = 1 THEN accuracy ELSE NULL END) AS mean_acc,
                    SUM(CASE WHEN is_complete = 1 THEN hit_miss ELSE 0 END) AS total_misses,
                    SUM(CASE WHEN is_complete = 1 THEN total_hits ELSE 0 END) AS total_hits
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
