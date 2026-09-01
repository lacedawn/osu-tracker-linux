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
            var plays = (await conn.QueryAsync<(double Stars, double Accuracy, int IsComplete)>(
                "SELECT stars, accuracy, is_complete FROM plays WHERE stars >= 4.0 ORDER BY stars ASC;")).ToList();

            var result = new List<StarMasteryBracket>();

            foreach (var (min, max) in bracketsDef)
            {
                var bracketPlays = max >= 99.0
                    ? plays.Where(p => p.Stars >= min).ToList()
                    : plays.Where(p => p.Stars >= min && p.Stars < max).ToList();

                int total = bracketPlays.Count;
                int passes = bracketPlays.Count(p => p.IsComplete == 1);
                double passRate = total > 0 ? (100.0 * passes / total) : 0.0;

                decimal meanAcc = 0.0m;
                decimal medianAcc = 0.0m;
                decimal p90Acc = 0.0m;
                string skillZone = "Comfort";

                if (total > 0)
                {
                    meanAcc = (decimal)bracketPlays.Average(p => p.Accuracy);

                    var sortedAccs = bracketPlays.Select(p => (decimal)p.Accuracy).OrderBy(a => a).ToList();
                    if (total % 2 == 1)
                    {
                        medianAcc = sortedAccs[total / 2];
                    }
                    else
                    {
                        medianAcc = (sortedAccs[(total / 2) - 1] + sortedAccs[total / 2]) / 2.0m;
                    }

                    int p90Index = (int)Math.Floor(0.90 * (total - 1));
                    p90Acc = sortedAccs[Math.Clamp(p90Index, 0, total - 1)];

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

            return result.AsReadOnly();
        }

        public async Task<AimSpeedProfile> GetAimSpeedProfileAsync(CancellationToken ct = default)
        {
            await using var conn = await _dbManager.CreateConnectionAsync(ct);
            var plays = (await conn.QueryAsync<(double Aim, double Speed, double Accuracy, int IsComplete)>(
                "SELECT aim, speed, accuracy, is_complete FROM plays;")).ToList();

            var aimPlays = new List<(double Aim, double Speed, double Accuracy, int IsComplete)>();
            var speedPlays = new List<(double Aim, double Speed, double Accuracy, int IsComplete)>();
            var balancedPlays = new List<(double Aim, double Speed, double Accuracy, int IsComplete)>();

            foreach (var play in plays)
            {
                double ratio = play.Aim / Math.Max(0.1, play.Speed);
                if (play.Aim >= 1.20 * play.Speed)
                {
                    aimPlays.Add(play);
                }
                else if (play.Speed >= 1.20 * play.Aim)
                {
                    speedPlays.Add(play);
                }
                else
                {
                    balancedPlays.Add(play);
                }
            }

            int aimCount = aimPlays.Count;
            int speedCount = speedPlays.Count;
            int balancedCount = balancedPlays.Count;

            decimal aimAvgAcc = aimCount > 0 ? (decimal)aimPlays.Average(p => p.Accuracy) : 0.0m;
            double aimPassRate = aimCount > 0 ? (100.0 * aimPlays.Count(p => p.IsComplete == 1) / aimCount) : 0.0;

            decimal speedAvgAcc = speedCount > 0 ? (decimal)speedPlays.Average(p => p.Accuracy) : 0.0m;
            double speedPassRate = speedCount > 0 ? (100.0 * speedPlays.Count(p => p.IsComplete == 1) / speedCount) : 0.0;

            decimal balancedAvgAcc = balancedCount > 0 ? (decimal)balancedPlays.Average(p => p.Accuracy) : 0.0m;
            double balancedPassRate = balancedCount > 0 ? (100.0 * balancedPlays.Count(p => p.IsComplete == 1) / balancedCount) : 0.0;

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
            var plays = (await conn.QueryAsync<(double Od, double Accuracy, int Hit300, int Hit100)>(
                "SELECT od, accuracy, hit_300, hit_100 FROM plays;")).ToList();

            var result = new List<OdAccuracyTier>();

            foreach (var (name, min, max, window) in tiersDef)
            {
                var tierPlays = min <= 0.0
                    ? plays.Where(p => p.Od <= max).ToList()
                    : max >= 12.0
                        ? plays.Where(p => p.Od > 10.0).ToList()
                        : plays.Where(p => p.Od > (min - 0.05) && p.Od <= (max + 0.05)).ToList();

                int total = tierPlays.Count;
                decimal meanAcc = total > 0 ? (decimal)tierPlays.Average(p => p.Accuracy) : 0.0m;

                long sum300 = tierPlays.Sum(p => (long)p.Hit300);
                long sum100 = tierPlays.Sum(p => (long)p.Hit100);
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
            var plays = (await conn.QueryAsync<(int Bpm, double Accuracy, int HitMiss, int TotalHits)>(
                "SELECT bpm, accuracy, hit_miss, total_hits FROM plays;")).ToList();

            var result = new List<BpmBracketStats>();

            foreach (var (label, min, max) in bpmBrackets)
            {
                var bracketPlays = plays.Where(p => p.Bpm >= min && p.Bpm <= max).ToList();

                int count = bracketPlays.Count;
                decimal meanAcc = count > 0 ? (decimal)bracketPlays.Average(p => p.Accuracy) : 0.0m;

                long sumMiss = bracketPlays.Sum(p => (long)p.HitMiss);
                long sumHits = bracketPlays.Sum(p => (long)p.TotalHits);
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

            return result.AsReadOnly();
        }
    }
}
