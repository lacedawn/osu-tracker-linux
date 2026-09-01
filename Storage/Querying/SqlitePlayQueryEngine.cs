using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage.Querying
{
    public class SqlitePlayQueryEngine : IPlayQueryEngine
    {
        private readonly IDatabaseManager _dbManager;

        private static readonly Dictionary<PlaySortField, string> SortColumnMap = new()
        {
            { PlaySortField.Timestamp, "timestamp" },
            { PlaySortField.Stars, "stars" },
            { PlaySortField.Accuracy, "accuracy" },
            { PlaySortField.Bpm, "bpm" },
            { PlaySortField.TotalHits, "total_hits" },
            { PlaySortField.PlayTimeSeconds, "play_time_seconds" },
            { PlaySortField.ConsecutivePlayCount, "consecutive_play_count" }
        };

        public SqlitePlayQueryEngine(IDatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        public async Task<PagedResult<PlayRecord>> QueryPlaysAsync(PlayQueryFilter filter, CancellationToken ct = default)
        {
            var parameters = new DynamicParameters();
            string whereClause = BuildWhereClause(filter, parameters);

            string sortColumn = SortColumnMap.GetValueOrDefault(filter.SortBy, "timestamp");
            string sortDir = filter.Order == SortOrder.Ascending ? "ASC" : "DESC";

            int page = Math.Max(1, filter.Page);
            int pageSize = Math.Clamp(filter.PageSize, 1, 500);
            int offset = (page - 1) * pageSize;
            parameters.Add("@limit", pageSize);
            parameters.Add("@offset", offset);

            string summarySql = $@"
                SELECT
                    COUNT(*) AS TotalMatches,
                    COALESCE(AVG(accuracy), 0.0) AS AvgAcc,
                    COALESCE(AVG(stars), 0.0) AS AvgStars,
                    COALESCE(SUM(play_time_seconds), 0) AS TotalPlayTime,
                    COALESCE(SUM(total_hits), 0) AS TotalHits,
                    COALESCE(SUM(CASE WHEN is_complete = 1 THEN 1 ELSE 0 END), 0) AS TotalPasses
                FROM plays
                {whereClause};";

            string rowsSql = $@"
                SELECT * FROM plays
                {whereClause}
                ORDER BY {sortColumn} {sortDir}
                LIMIT @limit OFFSET @offset;";

            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            var summaryRow = await conn.QuerySingleAsync<(int TotalMatches, double AvgAcc, double AvgStars, long TotalPlayTime, long TotalHits, int TotalPasses)>(
                summarySql, parameters);

            double passRate = summaryRow.TotalMatches > 0
                ? (100.0 * summaryRow.TotalPasses / summaryRow.TotalMatches)
                : 0.0;

            var summary = new PlayFilterSummary(
                TotalMatches: summaryRow.TotalMatches,
                AverageAccuracy: Math.Round((decimal)summaryRow.AvgAcc, 2),
                AverageStars: Math.Round((decimal)summaryRow.AvgStars, 2),
                TotalPlayTimeSeconds: (int)summaryRow.TotalPlayTime,
                TotalHitsLogged: (int)summaryRow.TotalHits,
                TotalPasses: summaryRow.TotalPasses,
                PassRatePercent: Math.Round(passRate, 2)
            );

            var rawRows = await conn.QueryAsync(rowsSql, parameters);
            var items = new List<PlayRecord>();

            foreach (var row in rawRows)
            {
                items.Add(MapToPlayRecord(row));
            }

            return new PagedResult<PlayRecord>(
                Items: items.AsReadOnly(),
                TotalCount: summaryRow.TotalMatches,
                PageNumber: page,
                PageSize: pageSize,
                Summary: summary
            );
        }

        public async Task<PlayFilterSummary> GetSummaryOnlyAsync(PlayQueryFilter filter, CancellationToken ct = default)
        {
            var parameters = new DynamicParameters();
            string whereClause = BuildWhereClause(filter, parameters);

            string sql = $@"
                SELECT
                    COUNT(*) AS TotalMatches,
                    COALESCE(AVG(accuracy), 0.0) AS AvgAcc,
                    COALESCE(AVG(stars), 0.0) AS AvgStars,
                    COALESCE(SUM(play_time_seconds), 0) AS TotalPlayTime,
                    COALESCE(SUM(total_hits), 0) AS TotalHits,
                    COALESCE(SUM(CASE WHEN is_complete = 1 THEN 1 ELSE 0 END), 0) AS TotalPasses
                FROM plays
                {whereClause};";

            await using var conn = await _dbManager.CreateConnectionAsync(ct);
            var row = await conn.QuerySingleAsync<(int TotalMatches, double AvgAcc, double AvgStars, long TotalPlayTime, long TotalHits, int TotalPasses)>(
                sql, parameters);

            double passRate = row.TotalMatches > 0 ? (100.0 * row.TotalPasses / row.TotalMatches) : 0.0;

            return new PlayFilterSummary(
                TotalMatches: row.TotalMatches,
                AverageAccuracy: Math.Round((decimal)row.AvgAcc, 2),
                AverageStars: Math.Round((decimal)row.AvgStars, 2),
                TotalPlayTimeSeconds: (int)row.TotalPlayTime,
                TotalHitsLogged: (int)row.TotalHits,
                TotalPasses: row.TotalPasses,
                PassRatePercent: Math.Round(passRate, 2)
            );
        }

        public async Task<IReadOnlyList<string>> AutocompleteSearchAsync(string term, int limit = 10, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Array.Empty<string>();

            string escaped = EscapeLikePattern(term);
            string pattern = $"%{escaped}%";

            string sql = @"
                SELECT DISTINCT beatmap_string FROM plays
                WHERE beatmap_string LIKE @pattern ESCAPE '\'
                ORDER BY beatmap_string
                LIMIT @limit;";

            await using var conn = await _dbManager.CreateConnectionAsync(ct);
            var results = (await conn.QueryAsync<string>(sql, new { pattern, limit = Math.Clamp(limit, 1, 50) })).ToList();
            return results.AsReadOnly();
        }

        private string BuildWhereClause(PlayQueryFilter filter, DynamicParameters parameters)
        {
            var clauses = new List<string>();

            BuildDateClauses(filter, clauses, parameters);
            BuildModClauses(filter, clauses, parameters);
            BuildNumericRangeClauses(filter, clauses, parameters);
            BuildStateClauses(filter, clauses, parameters);
            BuildSearchClause(filter, clauses, parameters);

            if (clauses.Count == 0)
                return "";

            return "WHERE " + string.Join(" AND ", clauses);
        }

        private void BuildDateClauses(PlayQueryFilter filter, List<string> clauses, DynamicParameters parameters)
        {
            DateTime now = DateTime.UtcNow;

            switch (filter.DatePreset)
            {
                case DateRangePreset.Today:
                    parameters.Add("@dateStart", now.Date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                    clauses.Add("timestamp >= @dateStart");
                    break;

                case DateRangePreset.Yesterday:
                    parameters.Add("@dateStart", now.Date.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                    parameters.Add("@dateEnd", now.Date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                    clauses.Add("timestamp >= @dateStart AND timestamp < @dateEnd");
                    break;

                case DateRangePreset.Last7Days:
                    parameters.Add("@dateStart", now.AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                    clauses.Add("timestamp >= @dateStart");
                    break;

                case DateRangePreset.Last30Days:
                    parameters.Add("@dateStart", now.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                    clauses.Add("timestamp >= @dateStart");
                    break;

                case DateRangePreset.ThisMonth:
                    parameters.Add("@dateStart", new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                    clauses.Add("timestamp >= @dateStart");
                    break;

                case DateRangePreset.Custom:
                    if (filter.StartTimeUtc.HasValue)
                    {
                        parameters.Add("@customStart", filter.StartTimeUtc.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                        clauses.Add("timestamp >= @customStart");
                    }
                    if (filter.EndTimeUtc.HasValue)
                    {
                        parameters.Add("@customEnd", filter.EndTimeUtc.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                        clauses.Add("timestamp < @customEnd");
                    }
                    break;

                case DateRangePreset.AllTime:
                default:
                    break;
            }
        }

        private void BuildModClauses(PlayQueryFilter filter, List<string> clauses, DynamicParameters parameters)
        {
            switch (filter.ModMode)
            {
                case ModFilterMode.NoModOnly:
                    clauses.Add("mods_bitfield = 0");
                    break;

                case ModFilterMode.ExactMatch:
                    if (filter.RequiredModsBitfield.HasValue)
                    {
                        parameters.Add("@requiredMods", filter.RequiredModsBitfield.Value);
                        clauses.Add("mods_bitfield = @requiredMods");
                    }
                    break;

                case ModFilterMode.ContainsAll:
                    if (filter.RequiredModsBitfield.HasValue)
                    {
                        parameters.Add("@requiredMods", filter.RequiredModsBitfield.Value);
                        clauses.Add("(mods_bitfield & @requiredMods) = @requiredMods");
                    }
                    break;

                case ModFilterMode.ContainsAny:
                    if (filter.RequiredModsBitfield.HasValue)
                    {
                        parameters.Add("@requiredMods", filter.RequiredModsBitfield.Value);
                        clauses.Add("(mods_bitfield & @requiredMods) > 0");
                    }
                    break;

                case ModFilterMode.Any:
                default:
                    break;
            }

            if (filter.ExcludedModsBitfield.HasValue && filter.ExcludedModsBitfield.Value > 0)
            {
                parameters.Add("@excludedMods", filter.ExcludedModsBitfield.Value);
                clauses.Add("(mods_bitfield & @excludedMods) = 0");
            }
        }

        private void BuildNumericRangeClauses(PlayQueryFilter filter, List<string> clauses, DynamicParameters parameters)
        {
            if (filter.MinStars.HasValue)
            {
                parameters.Add("@minStars", filter.MinStars.Value);
                clauses.Add("stars >= @minStars");
            }
            if (filter.MaxStars.HasValue)
            {
                parameters.Add("@maxStars", filter.MaxStars.Value);
                clauses.Add("stars <= @maxStars");
            }

            if (filter.MinBpm.HasValue)
            {
                parameters.Add("@minBpm", filter.MinBpm.Value);
                clauses.Add("bpm >= @minBpm");
            }
            if (filter.MaxBpm.HasValue)
            {
                parameters.Add("@maxBpm", filter.MaxBpm.Value);
                clauses.Add("bpm <= @maxBpm");
            }

            if (filter.MinOd.HasValue)
            {
                parameters.Add("@minOd", filter.MinOd.Value);
                clauses.Add("od >= @minOd");
            }
            if (filter.MaxOd.HasValue)
            {
                parameters.Add("@maxOd", filter.MaxOd.Value);
                clauses.Add("od <= @maxOd");
            }

            if (filter.MinCs.HasValue)
            {
                parameters.Add("@minCs", filter.MinCs.Value);
                clauses.Add("cs >= @minCs");
            }
            if (filter.MaxCs.HasValue)
            {
                parameters.Add("@maxCs", filter.MaxCs.Value);
                clauses.Add("cs <= @maxCs");
            }

            if (filter.MinAr.HasValue)
            {
                parameters.Add("@minAr", filter.MinAr.Value);
                clauses.Add("ar >= @minAr");
            }
            if (filter.MaxAr.HasValue)
            {
                parameters.Add("@maxAr", filter.MaxAr.Value);
                clauses.Add("ar <= @maxAr");
            }

            if (filter.MinAccuracy.HasValue)
            {
                parameters.Add("@minAcc", (double)filter.MinAccuracy.Value);
                clauses.Add("accuracy >= @minAcc");
            }
            if (filter.MaxAccuracy.HasValue)
            {
                parameters.Add("@maxAcc", (double)filter.MaxAccuracy.Value);
                clauses.Add("accuracy <= @maxAcc");
            }

            if (filter.MinHits.HasValue)
            {
                parameters.Add("@minHits", filter.MinHits.Value);
                clauses.Add("total_hits >= @minHits");
            }
            if (filter.MaxHits.HasValue)
            {
                parameters.Add("@maxHits", filter.MaxHits.Value);
                clauses.Add("total_hits <= @maxHits");
            }
        }

        private void BuildStateClauses(PlayQueryFilter filter, List<string> clauses, DynamicParameters parameters)
        {
            switch (filter.PassStatus)
            {
                case PassStatusFilter.PassesOnly:
                    clauses.Add("is_complete = 1");
                    break;
                case PassStatusFilter.FailsAndRetriesOnly:
                    clauses.Add("is_complete = 0");
                    break;
            }

            switch (filter.ReplayFilter)
            {
                case ReplayFilterOption.ExcludeReplays:
                    clauses.Add("is_replay = 0");
                    break;
                case ReplayFilterOption.ReplaysOnly:
                    clauses.Add("is_replay = 1");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(filter.SessionId))
            {
                parameters.Add("@sessionId", filter.SessionId);
                clauses.Add("session_id = @sessionId");
            }

            if (!string.IsNullOrWhiteSpace(filter.DetectedClient))
            {
                parameters.Add("@detectedClient", filter.DetectedClient);
                clauses.Add("detected_client = @detectedClient");
            }
        }

        private void BuildSearchClause(PlayQueryFilter filter, List<string> clauses, DynamicParameters parameters)
        {
            if (string.IsNullOrWhiteSpace(filter.SearchQuery))
                return;

            string escaped = EscapeLikePattern(filter.SearchQuery);
            string pattern = $"%{escaped}%";
            parameters.Add("@search", pattern);
            clauses.Add(@"(beatmap_title LIKE @search ESCAPE '\' OR beatmap_artist LIKE @search ESCAPE '\' OR beatmap_version LIKE @search ESCAPE '\' OR beatmap_string LIKE @search ESCAPE '\')");
        }

        private static string EscapeLikePattern(string input)
        {
            return input
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");
        }

        private static PlayRecord MapToPlayRecord(dynamic row)
        {
            DateTime timestamp = DateTime.MinValue;
            string? tsStr = row.timestamp as string;
            if (tsStr != null)
            {
                DateTime.TryParse(tsStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out timestamp);
            }

            return new PlayRecord(
                Id: (long)row.id,
                SessionId: row.session_id as string,
                TimestampUtc: timestamp,
                BeatmapId: (int)(long)row.beatmap_id,
                BeatmapSetId: (int)(long)row.beatmap_set_id,
                BeatmapChecksum: row.beatmap_checksum as string,
                BeatmapString: (string)(row.beatmap_string ?? ""),
                BeatmapTitle: (string)(row.beatmap_title ?? ""),
                BeatmapArtist: (string)(row.beatmap_artist ?? ""),
                BeatmapVersion: (string)(row.beatmap_version ?? ""),
                ModsBitfield: (int)(long)row.mods_bitfield,
                ModsString: (string)(row.mods_string ?? ""),
                Bpm: (int)(long)row.bpm,
                Stars: (decimal)(double)row.stars,
                Aim: (decimal)(double)row.aim,
                Speed: (decimal)(double)row.speed,
                Cs: (decimal)(double)row.cs,
                Ar: (decimal)(double)row.ar,
                Od: (decimal)(double)row.od,
                Hp: (decimal)(double)row.hp,
                TotalHits: (int)(long)row.total_hits,
                Hit300: (int)(long)row.hit_300,
                Hit100: (int)(long)row.hit_100,
                Hit50: (int)(long)row.hit_50,
                HitMiss: (int)(long)row.hit_miss,
                Accuracy: (decimal)(double)row.accuracy,
                AccuracyReliable: ((long)row.accuracy_reliable) == 1,
                IsComplete: ((long)row.is_complete) == 1,
                PlayTimeSeconds: (int)(long)row.play_time_seconds,
                ConsecutivePlayCount: (int)(long)row.consecutive_play_count,
                GameMode: (int)(long)row.game_mode,
                IsReplay: ((long)row.is_replay) == 1,
                DetectedClient: (string)(row.detected_client ?? "")
            );
        }
    }
}
