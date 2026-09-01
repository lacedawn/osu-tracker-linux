using Circle_Tracker.Storage;
using Dapper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Analytics
{
    public class SessionAnalyticsService : ISessionAnalyticsService
    {
        private readonly IDatabaseManager _dbManager;

        public SessionAnalyticsService(IDatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        public async Task<IReadOnlyList<FatigueBucket>> GetSessionFatigueCurveAsync(CancellationToken ct = default)
        {
            var bucketDefs = new (string Label, int Start, int End)[]
            {
                ("0 - 15m (Warmup)", 0, 15),
                ("15 - 30m (Early Peak)", 15, 30),
                ("30 - 45m (Peak Window)", 30, 45),
                ("45 - 60m", 45, 60),
                ("60 - 75m", 60, 75),
                ("75 - 90m", 75, 90),
                ("90m+ (Extended)", 90, int.MaxValue)
            };

            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            var sessionRows = (await conn.QueryAsync<(string Id, string StartTime, string? EndTime, int TotalPlays, int PlayingSeconds, int IdleSeconds)>(
                "SELECT id, start_time, end_time, total_plays, playing_seconds, idle_seconds FROM sessions;")).ToList();

            var playRows = (await conn.QueryAsync<(string SessionId, string Timestamp, double Accuracy, int HitMiss, int PlayTimeSeconds)>(
                "SELECT session_id, timestamp, accuracy, hit_miss, play_time_seconds FROM plays WHERE session_id IS NOT NULL;")).ToList();

            var playsBySession = playRows
                .Where(p => !string.IsNullOrEmpty(p.SessionId))
                .GroupBy(p => p.SessionId!)
                .ToDictionary(g => g.Key, g => g.ToList());

            var qualifyingPlays = new List<(double ElapsedMinutes, double Accuracy, double SessionAvgAcc, int HitMiss, int PlayTimeSeconds)>();

            foreach (var session in sessionRows)
            {
                if (!playsBySession.TryGetValue(session.Id, out var sPlays) || sPlays.Count == 0)
                    continue;

                DateTime startTime;
                if (!DateTime.TryParse(session.StartTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out startTime))
                    continue;

                DateTime endTime = startTime;
                if (!string.IsNullOrEmpty(session.EndTime) && DateTime.TryParse(session.EndTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedEnd))
                {
                    endTime = parsedEnd;
                }
                else
                {
                    foreach (var p in sPlays)
                    {
                        if (DateTime.TryParse(p.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var pt) && pt > endTime)
                        {
                            endTime = pt;
                        }
                    }
                }

                double durationMinutes = Math.Max((endTime - startTime).TotalMinutes, (session.PlayingSeconds + session.IdleSeconds) / 60.0);
                int playCount = Math.Max(session.TotalPlays, sPlays.Count);

                if (durationMinutes < 20.0 && playCount < 8)
                    continue;

                double sessionAvgAcc = sPlays.Average(p => p.Accuracy);

                foreach (var play in sPlays)
                {
                    if (DateTime.TryParse(play.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var playTime))
                    {
                        double elapsed = Math.Max(0, (playTime - startTime).TotalMinutes);
                        qualifyingPlays.Add((elapsed, play.Accuracy, sessionAvgAcc, play.HitMiss, play.PlayTimeSeconds));
                    }
                }
            }

            var result = new List<FatigueBucket>();

            foreach (var (label, startMin, endMin) in bucketDefs)
            {
                var bucketPlays = endMin == int.MaxValue
                    ? qualifyingPlays.Where(p => p.ElapsedMinutes >= startMin).ToList()
                    : qualifyingPlays.Where(p => p.ElapsedMinutes >= startMin && p.ElapsedMinutes < endMin).ToList();

                int sampleSize = bucketPlays.Count;
                decimal meanAcc = sampleSize > 0 ? (decimal)bucketPlays.Average(p => p.Accuracy) : 0.0m;
                decimal accDelta = sampleSize > 0 ? (decimal)bucketPlays.Average(p => p.Accuracy - p.SessionAvgAcc) : 0.0m;

                long totalMisses = bucketPlays.Sum(p => (long)p.HitMiss);
                double totalPlayMinutes = bucketPlays.Sum(p => (double)p.PlayTimeSeconds) / 60.0;
                double missRate = totalPlayMinutes > 0 ? (totalMisses / totalPlayMinutes) : 0.0;

                result.Add(new FatigueBucket(
                    TimeRangeLabel: label,
                    StartMinute: startMin,
                    EndMinute: endMin,
                    SampleSize: sampleSize,
                    MeanAccuracy: Math.Round(meanAcc, 2),
                    AccDeltaFromSessionAvg: Math.Round(accDelta, 2),
                    MissRatePerMinute: Math.Round(missRate, 2)
                ));
            }

            return result.AsReadOnly();
        }

        public async Task<Dictionary<string, RollingPeriodStats>> GetRollingAveragesAsync(CancellationToken ct = default)
        {
            var periods = new (string Key, int Days)[]
            {
                ("7D", 7),
                ("30D", 30),
                ("90D", 90)
            };

            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            string minDate = DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var plays = (await conn.QueryAsync<(string Timestamp, double Accuracy, int TotalHits, double Stars, int IsComplete, int Bpm, int PlayTimeSeconds)>(
                "SELECT timestamp, accuracy, total_hits, stars, is_complete, bpm, play_time_seconds FROM plays WHERE timestamp >= @minDate;",
                new { minDate })).ToList();

            var parsedPlays = new List<(DateTime Time, double Accuracy, int TotalHits, double Stars, int IsComplete, int Bpm, int PlayTimeSeconds)>();
            foreach (var p in plays)
            {
                if (DateTime.TryParse(p.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    parsedPlays.Add((dt, p.Accuracy, p.TotalHits, p.Stars, p.IsComplete, p.Bpm, p.PlayTimeSeconds));
                }
            }

            var result = new Dictionary<string, RollingPeriodStats>();
            DateTime now = DateTime.UtcNow;

            foreach (var (key, days) in periods)
            {
                DateTime cutoff = now.AddDays(-days);
                var window = parsedPlays.Where(p => p.Time >= cutoff).ToList();

                int totalPlays = window.Count;
                double totalActiveHours = window.Sum(p => (double)p.PlayTimeSeconds) / 3600.0;

                long totalHits = window.Sum(p => (long)p.TotalHits);
                decimal meanAcc = 0.0m;
                if (totalHits > 0)
                {
                    double weightedAccSum = window.Sum(p => p.Accuracy * p.TotalHits);
                    meanAcc = (decimal)(weightedAccSum / totalHits);
                }
                else if (totalPlays > 0)
                {
                    meanAcc = (decimal)window.Average(p => p.Accuracy);
                }

                decimal meanStars = totalPlays > 0 ? (decimal)window.Average(p => p.Stars) : 0.0m;
                double passRate = totalPlays > 0 ? (100.0 * window.Count(p => p.IsComplete == 1) / totalPlays) : 0.0;
                double meanBpm = totalPlays > 0 ? window.Average(p => (double)p.Bpm) : 0.0;

                result[key] = new RollingPeriodStats(
                    PeriodDays: days,
                    TotalPlays: totalPlays,
                    TotalActiveHours: Math.Round(totalActiveHours, 2),
                    MeanAccuracy: Math.Round(meanAcc, 2),
                    MeanStars: Math.Round(meanStars, 2),
                    PassRatePercent: Math.Round(passRate, 2),
                    MeanBpm: Math.Round(meanBpm, 1)
                );
            }

            return result;
        }

        public async Task<IReadOnlyList<ChokeMapRecord>> GetTopChokeMapsAsync(int limit = 10, CancellationToken ct = default)
        {
            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            var plays = (await conn.QueryAsync<(int BeatmapId, int BeatmapSetId, string BeatmapString, double Accuracy, int HitMiss, int ConsecutivePlayCount, int IsComplete)>(
                "SELECT beatmap_id, beatmap_set_id, beatmap_string, accuracy, hit_miss, consecutive_play_count, is_complete FROM plays WHERE beatmap_id > 0;")).ToList();

            var groups = plays.GroupBy(p => p.BeatmapId);
            var records = new List<ChokeMapRecord>();

            foreach (var group in groups)
            {
                var mapPlays = group.ToList();
                int totalAttempts = mapPlays.Count;
                int maxConsecutive = mapPlays.Max(p => p.ConsecutivePlayCount);

                if (maxConsecutive < 3 && totalAttempts < 5)
                    continue;

                var chokes = mapPlays.Where(p => p.HitMiss >= 1 && p.HitMiss <= 2 && p.Accuracy >= 95.0).ToList();
                if (chokes.Count == 0)
                    continue;

                var first = mapPlays[0];
                int chokeCount = chokes.Count;
                decimal bestAcc = (decimal)chokes.Max(p => p.Accuracy);
                int minMisses = chokes.Min(p => p.HitMiss);
                string coverUrl = first.BeatmapSetId > 0 ? $"https://assets.ppy.sh/beatmaps/{first.BeatmapSetId}/covers/cover.jpg" : "";

                records.Add(new ChokeMapRecord(
                    BeatmapId: first.BeatmapId,
                    BeatmapSetId: first.BeatmapSetId,
                    BeatmapString: first.BeatmapString ?? "",
                    CoverUrl: coverUrl,
                    ChokeCount: chokeCount,
                    BestChokeAcc: Math.Round(bestAcc, 2),
                    MinMisses: minMisses,
                    TotalMapAttempts: totalAttempts
                ));
            }

            return records
                .OrderByDescending(r => r.ChokeCount)
                .ThenByDescending(r => r.BestChokeAcc)
                .Take(limit)
                .ToList()
                .AsReadOnly();
        }

        public async Task<HeadToHeadComparison> CompareSessionToBaselineAsync(string sessionId, CancellationToken ct = default)
        {
            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            string minDate = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var plays = (await conn.QueryAsync<(string? SessionId, string Timestamp, double Accuracy, double Stars, int IsComplete, int PlayTimeSeconds)>(
                "SELECT session_id, timestamp, accuracy, stars, is_complete, play_time_seconds FROM plays;")).ToList();

            var sessionPlays = plays.Where(p => string.Equals(p.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)).ToList();

            DateTime cutoff = DateTime.UtcNow.AddDays(-30);
            var baselinePlays = new List<(string? SessionId, string Timestamp, double Accuracy, double Stars, int IsComplete, int PlayTimeSeconds)>();
            foreach (var p in plays)
            {
                if (DateTime.TryParse(p.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt) && dt >= cutoff)
                {
                    baselinePlays.Add(p);
                }
            }

            int sCount = sessionPlays.Count;
            decimal sAcc = sCount > 0 ? (decimal)sessionPlays.Average(p => p.Accuracy) : 0.0m;
            decimal sStars = sCount > 0 ? (decimal)sessionPlays.Average(p => p.Stars) : 0.0m;
            double sPassRate = sCount > 0 ? (100.0 * sessionPlays.Count(p => p.IsComplete == 1) / sCount) : 0.0;
            double sMinutes = sessionPlays.Sum(p => (double)p.PlayTimeSeconds) / 60.0;

            int bCount = baselinePlays.Count;
            decimal bAcc = bCount > 0 ? (decimal)baselinePlays.Average(p => p.Accuracy) : 0.0m;
            decimal bStars = bCount > 0 ? (decimal)baselinePlays.Average(p => p.Stars) : 0.0m;
            double bPassRate = bCount > 0 ? (100.0 * baselinePlays.Count(p => p.IsComplete == 1) / bCount) : 0.0;

            return new HeadToHeadComparison(
                SessionAcc: Math.Round(sAcc, 2),
                Baseline30DAcc: Math.Round(bAcc, 2),
                DeltaAcc: Math.Round(sAcc - bAcc, 2),
                SessionStars: Math.Round(sStars, 2),
                Baseline30DStars: Math.Round(bStars, 2),
                DeltaStars: Math.Round(sStars - bStars, 2),
                SessionPassRate: Math.Round(sPassRate, 2),
                Baseline30DPassRate: Math.Round(bPassRate, 2),
                DeltaPassRate: Math.Round(sPassRate - bPassRate, 2),
                SessionPlays: sCount,
                SessionActiveMinutes: Math.Round(sMinutes, 1)
            );
        }
    }
}
