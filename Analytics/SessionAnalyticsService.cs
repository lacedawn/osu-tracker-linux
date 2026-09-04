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

        public async Task<Dictionary<string, RollingPeriodStats>> GetRollingAveragesAsync(CancellationToken ct = default)
        {
            var periods = new (string Key, int Days)[]
            {
                ("7D", 7),
                ("30D", 30),
                ("90D", 90)
            };

            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            DateTime now = DateTime.UtcNow;
            string d7 = now.AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string d30 = now.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string d90 = now.AddDays(-90).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            const string sql = @"
                SELECT
                    COUNT(CASE WHEN timestamp >= @d7 THEN 1 END) AS plays_7d,
                    COUNT(DISTINCT CASE WHEN timestamp >= @d7 THEN DATE(timestamp) END) AS active_days_7d,
                    SUM(CASE WHEN timestamp >= @d7 THEN play_time_seconds ELSE 0 END) AS time_7d,
                    SUM(CASE WHEN timestamp >= @d7 THEN total_hits ELSE 0 END) AS hits_7d,
                    SUM(CASE WHEN timestamp >= @d7 THEN accuracy * total_hits ELSE 0.0 END) AS w_acc_7d,
                    AVG(CASE WHEN timestamp >= @d7 THEN accuracy END) AS avg_acc_7d,
                    AVG(CASE WHEN timestamp >= @d7 THEN stars END) AS avg_stars_7d,
                    SUM(CASE WHEN timestamp >= @d7 AND is_complete = 1 THEN 1 ELSE 0 END) AS passes_7d,
                    AVG(CASE WHEN timestamp >= @d7 THEN bpm END) AS avg_bpm_7d,

                    COUNT(CASE WHEN timestamp >= @d30 THEN 1 END) AS plays_30d,
                    COUNT(DISTINCT CASE WHEN timestamp >= @d30 THEN DATE(timestamp) END) AS active_days_30d,
                    SUM(CASE WHEN timestamp >= @d30 THEN play_time_seconds ELSE 0 END) AS time_30d,
                    SUM(CASE WHEN timestamp >= @d30 THEN total_hits ELSE 0 END) AS hits_30d,
                    SUM(CASE WHEN timestamp >= @d30 THEN accuracy * total_hits ELSE 0.0 END) AS w_acc_30d,
                    AVG(CASE WHEN timestamp >= @d30 THEN accuracy END) AS avg_acc_30d,
                    AVG(CASE WHEN timestamp >= @d30 THEN stars END) AS avg_stars_30d,
                    SUM(CASE WHEN timestamp >= @d30 AND is_complete = 1 THEN 1 ELSE 0 END) AS passes_30d,
                    AVG(CASE WHEN timestamp >= @d30 THEN bpm END) AS avg_bpm_30d,

                    COUNT(CASE WHEN timestamp >= @d90 THEN 1 END) AS plays_90d,
                    COUNT(DISTINCT CASE WHEN timestamp >= @d90 THEN DATE(timestamp) END) AS active_days_90d,
                    SUM(CASE WHEN timestamp >= @d90 THEN play_time_seconds ELSE 0 END) AS time_90d,
                    SUM(CASE WHEN timestamp >= @d90 THEN total_hits ELSE 0 END) AS hits_90d,
                    SUM(CASE WHEN timestamp >= @d90 THEN accuracy * total_hits ELSE 0.0 END) AS w_acc_90d,
                    AVG(CASE WHEN timestamp >= @d90 THEN accuracy END) AS avg_acc_90d,
                    AVG(CASE WHEN timestamp >= @d90 THEN stars END) AS avg_stars_90d,
                    SUM(CASE WHEN timestamp >= @d90 AND is_complete = 1 THEN 1 ELSE 0 END) AS passes_90d,
                    AVG(CASE WHEN timestamp >= @d90 THEN bpm END) AS avg_bpm_90d
                FROM plays
                WHERE timestamp >= @d90;";

            var row = await conn.QueryFirstOrDefaultAsync<(
                int Plays7, int ActiveDays7, int Time7, long Hits7, double WAcc7, double? AvgAcc7, double? AvgStars7, int Passes7, double? AvgBpm7,
                int Plays30, int ActiveDays30, int Time30, long Hits30, double WAcc30, double? AvgAcc30, double? AvgStars30, int Passes30, double? AvgBpm30,
                int Plays90, int ActiveDays90, int Time90, long Hits90, double WAcc90, double? AvgAcc90, double? AvgStars90, int Passes90, double? AvgBpm90
            )>(sql, new { d7, d30, d90 });

            var result = new Dictionary<string, RollingPeriodStats>();

            RollingPeriodStats ComputeStats(int days, int totalPlays, int activeDays, int playTimeSec, long totalHits, double weightedAccSum, double? avgAcc, double? avgStars, int passes, double? avgBpm)
            {
                double totalActiveHours = playTimeSec / 3600.0;
                decimal meanAcc = 0.0m;
                if (totalHits > 0)
                {
                    meanAcc = (decimal)(weightedAccSum / totalHits);
                }
                else if (totalPlays > 0 && avgAcc.HasValue)
                {
                    meanAcc = (decimal)avgAcc.Value;
                }

                decimal meanStars = (totalPlays > 0 && avgStars.HasValue) ? (decimal)avgStars.Value : 0.0m;
                double passRate = totalPlays > 0 ? (100.0 * passes / totalPlays) : 0.0;
                double meanBpm = (totalPlays > 0 && avgBpm.HasValue) ? avgBpm.Value : 0.0;
                
                // Calculate per-day averages based on actual active days, not window size
                double playsPerDay = activeDays > 0 ? (double)totalPlays / activeDays : 0.0;
                double hoursPerDay = activeDays > 0 ? totalActiveHours / activeDays : 0.0;

                return new RollingPeriodStats(
                    PeriodDays: days,
                    TotalPlays: totalPlays,
                    TotalActiveHours: Math.Round(totalActiveHours, 2),
                    MeanAccuracy: Math.Round(meanAcc, 2),
                    MeanStars: Math.Round(meanStars, 2),
                    PassRatePercent: Math.Round(passRate, 2),
                    MeanBpm: Math.Round(meanBpm, 1),
                    PlaysPerActiveDay: Math.Round(playsPerDay, 1),
                    HoursPerActiveDay: Math.Round(hoursPerDay, 2)
                );
            }

            result["7D"] = ComputeStats(7, row.Plays7, row.ActiveDays7, row.Time7, row.Hits7, row.WAcc7, row.AvgAcc7, row.AvgStars7, row.Passes7, row.AvgBpm7);
            result["30D"] = ComputeStats(30, row.Plays30, row.ActiveDays30, row.Time30, row.Hits30, row.WAcc30, row.AvgAcc30, row.AvgStars30, row.Passes30, row.AvgBpm30);
            result["90D"] = ComputeStats(90, row.Plays90, row.ActiveDays90, row.Time90, row.Hits90, row.WAcc90, row.AvgAcc90, row.AvgStars90, row.Passes90, row.AvgBpm90);

            return result;
        }

        public async Task<IReadOnlyList<ChokeMapRecord>> GetTopChokeMapsAsync(int limit = 10, CancellationToken ct = default)
        {
            await using var conn = await _dbManager.CreateConnectionAsync(ct);
            // Push grouping, filtering, and aggregation entirely into SQLite
            const string sql = @"
                SELECT
                    beatmap_id,
                    beatmap_set_id,
                    beatmap_string,
                    COUNT(*) AS total_attempts,
                    MAX(consecutive_play_count) AS max_consecutive,
                    SUM(CASE WHEN hit_miss >= 1 AND hit_miss <= 2 AND accuracy >= 95.0 THEN 1 ELSE 0 END) AS choke_count,
                    MAX(CASE WHEN hit_miss >= 1 AND hit_miss <= 2 AND accuracy >= 95.0 THEN accuracy ELSE 0 END) AS best_choke_acc,
                    MIN(CASE WHEN hit_miss >= 1 AND hit_miss <= 2 AND accuracy >= 95.0 THEN hit_miss ELSE 999 END) AS min_misses
                FROM plays
                WHERE beatmap_id > 0
                GROUP BY beatmap_id
                HAVING choke_count > 0 AND (max_consecutive >= 3 OR total_attempts >= 5)
                ORDER BY choke_count DESC, best_choke_acc DESC
                LIMIT @limit;";
            var rows = (await conn.QueryAsync<(
                int BeatmapId, int BeatmapSetId, string BeatmapString,
                int TotalAttempts, int MaxConsecutive,
                int ChokeCount, double BestChokeAcc, int MinMisses
            )>(sql, new { limit })).ToList();
            return rows.Select(r => new ChokeMapRecord(
                BeatmapId: r.BeatmapId,
                BeatmapSetId: r.BeatmapSetId,
                BeatmapString: r.BeatmapString ?? "",
                CoverUrl: r.BeatmapSetId > 0 ? $"https://assets.ppy.sh/beatmaps/{r.BeatmapSetId}/covers/cover.jpg" : "",
                ChokeCount: r.ChokeCount,
                BestChokeAcc: Math.Round((decimal)r.BestChokeAcc, 2),
                MinMisses: r.MinMisses == 999 ? 0 : r.MinMisses,
                TotalMapAttempts: r.TotalAttempts
            )).ToList().AsReadOnly();
        }

        public async Task<HeadToHeadComparison> CompareSessionToBaselineAsync(string sessionId, CancellationToken ct = default)
        {
            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            string cutoff = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            const string sql = @"
                SELECT
                    COUNT(CASE WHEN session_id = @sessionId THEN 1 END) AS SessionPlays,
                    AVG(CASE WHEN session_id = @sessionId THEN accuracy END) AS SessionAcc,
                    AVG(CASE WHEN session_id = @sessionId THEN stars END) AS SessionStars,
                    SUM(CASE WHEN session_id = @sessionId AND is_complete = 1 THEN 1 ELSE 0 END) AS SessionPasses,
                    SUM(CASE WHEN session_id = @sessionId THEN play_time_seconds ELSE 0 END) AS SessionSeconds,

                    COUNT(CASE WHEN timestamp >= @cutoff THEN 1 END) AS BaselinePlays,
                    AVG(CASE WHEN timestamp >= @cutoff THEN accuracy END) AS BaselineAcc,
                    AVG(CASE WHEN timestamp >= @cutoff THEN stars END) AS BaselineStars,
                    SUM(CASE WHEN timestamp >= @cutoff AND is_complete = 1 THEN 1 ELSE 0 END) AS BaselinePasses
                FROM plays
                WHERE session_id = @sessionId OR timestamp >= @cutoff;";

            var row = await conn.QueryFirstOrDefaultAsync<(
                int SessionPlays, double? SessionAcc, double? SessionStars, int SessionPasses, int SessionSeconds,
                int BaselinePlays, double? BaselineAcc, double? BaselineStars, int BaselinePasses
            )>(sql, new { sessionId, cutoff });

            int sCount = row.SessionPlays;
            decimal sAcc = sCount > 0 ? (decimal)(row.SessionAcc ?? 0.0) : 0.0m;
            decimal sStars = sCount > 0 ? (decimal)(row.SessionStars ?? 0.0) : 0.0m;
            double sPassRate = sCount > 0 ? (100.0 * row.SessionPasses / sCount) : 0.0;
            double sMinutes = row.SessionSeconds / 60.0;

            int bCount = row.BaselinePlays;
            decimal bAcc = bCount > 0 ? (decimal)(row.BaselineAcc ?? 0.0) : 0.0m;
            decimal bStars = bCount > 0 ? (decimal)(row.BaselineStars ?? 0.0) : 0.0m;
            double bPassRate = bCount > 0 ? (100.0 * row.BaselinePasses / bCount) : 0.0;

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
