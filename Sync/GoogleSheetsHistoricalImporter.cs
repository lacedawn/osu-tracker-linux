using Dapper;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Sync;

public interface IGoogleSheetsHistoricalImporter
{
    Task<SyncResult> ImportFromSpreadsheetAsync(
        string spreadsheetId,
        string sheetName,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default);
}

public class GoogleSheetsHistoricalImporter : IGoogleSheetsHistoricalImporter
{
    private static readonly ILogger<GoogleSheetsHistoricalImporter> _log = AppLogger.For<GoogleSheetsHistoricalImporter>();
    private static readonly Regex HyperlinkRegex = new(
        @"^=HYPERLINK\(""https:\/\/osu\.ppy\.sh\/beatmapsets\/(\d+)(?:#osu\/(\d+))?""[;,]\s*""(.+?)""\)",
        RegexOptions.Compiled);

    private readonly SheetsService _sheetsService;
    private readonly Storage.IDatabaseManager _dbManager;

    public GoogleSheetsHistoricalImporter(SheetsService sheetsService, Storage.IDatabaseManager dbManager)
    {
        _sheetsService = sheetsService;
        _dbManager = dbManager;
    }

    public async Task<SyncResult> ImportFromSpreadsheetAsync(
        string spreadsheetId,
        string sheetName,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            var range = $"'{sheetName}'!A2:X";
            var request = _sheetsService.Spreadsheets.Values.Get(spreadsheetId, range);
            var response = await request.ExecuteAsync(ct);

            if (response?.Values == null || response.Values.Count == 0)
            {
                return new SyncResult(true, 0, 0, "No data found in spreadsheet");
            }

            var rows = response.Values;
            int totalRows = rows.Count;
            int importedCount = 0;
            int skippedDuplicates = 0;
            int failedCount = 0;

            var sessionMap = new Dictionary<string, string>();
            DateTime? lastPlayTimestamp = null;
            string? currentSessionId = null;

            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string currentBeatmap = "";

                try
                {
                    if (row.Count < 18)
                    {
                        _log.LogWarning($"Row {i + 2} has insufficient columns ({row.Count}), skipping");
                        failedCount++;
                        continue;
                    }

                    var timestamp = ParseTimestamp(GetCellValue(row, 0));
                    if (!timestamp.HasValue)
                    {
                        _log.LogWarning($"Row {i + 2} has invalid timestamp, skipping");
                        failedCount++;
                        continue;
                    }

                    var (beatmapSetId, beatmapId, beatmapString) = ParseHyperlink(GetCellValue(row, 1));
                    currentBeatmap = beatmapString ?? "Unknown";

                    if (beatmapSetId == 0 || beatmapId == 0)
                    {
                        _log.LogWarning($"Row {i + 2} has invalid beatmap hyperlink, skipping");
                        failedCount++;
                        continue;
                    }

                    bool hd = GetCellValue(row, 2) == "1";
                    bool hr = GetCellValue(row, 3) == "1";
                    bool dt = GetCellValue(row, 4) == "1";
                    int bpm = ParseInt(GetCellValue(row, 5));
                    decimal aim = ParseDecimal(GetCellValue(row, 6));
                    decimal speed = ParseDecimal(GetCellValue(row, 7));
                    decimal stars = ParseDecimal(GetCellValue(row, 8));
                    decimal cs = ParseDecimal(GetCellValue(row, 9));
                    decimal ar = ParseDecimal(GetCellValue(row, 10));
                    decimal od = ParseDecimal(GetCellValue(row, 11));
                    int totalHits = ParseInt(GetCellValue(row, 12));
                    decimal accuracy = ParseDecimal(GetCellValue(row, 13));
                    int hit300 = ParseInt(GetCellValue(row, 14));
                    int hit100 = ParseInt(GetCellValue(row, 15));
                    int hit50 = ParseInt(GetCellValue(row, 16));
                    int hitMiss = ParseInt(GetCellValue(row, 17));
                    bool ez = row.Count > 18 && GetCellValue(row, 18) == "1";
                    bool ht = row.Count > 19 && GetCellValue(row, 19) == "1";
                    bool fl = row.Count > 20 && GetCellValue(row, 20) == "1";
                    bool isComplete = row.Count > 21 && GetCellValue(row, 21) == "1";
                    int playCount = row.Count > 22 ? ParseInt(GetCellValue(row, 22)) : 1;
                    int playTimeSeconds = row.Count > 23 ? ParseInt(GetCellValue(row, 23)) : 0;

                    int modsBitfield = BuildModsBitfield(hd, hr, dt, ez, ht, fl);
                    string modsString = BuildModsString(hd, hr, dt, ez, ht, fl);

                    if (lastPlayTimestamp.HasValue &&
                        (timestamp.Value - lastPlayTimestamp.Value).TotalMinutes > 45)
                    {
                        currentSessionId = Guid.NewGuid().ToString();
                        await InsertSessionAsync(conn, currentSessionId, timestamp.Value, ct);
                    }
                    else if (currentSessionId == null)
                    {
                        currentSessionId = Guid.NewGuid().ToString();
                        await InsertSessionAsync(conn, currentSessionId, timestamp.Value, ct);
                    }

                    lastPlayTimestamp = timestamp.Value;

                    var isDuplicate = await CheckDuplicateAsync(conn, timestamp.Value, beatmapId, totalHits, ct);
                    if (isDuplicate)
                    {
                        skippedDuplicates++;
                    }
                    else
                    {
                        await InsertPlayAsync(conn, currentSessionId, timestamp.Value, beatmapId, beatmapSetId,
                            beatmapString ?? "", bpm, stars, aim, speed, cs, ar, od, totalHits, accuracy,
                            hit300, hit100, hit50, hitMiss, modsBitfield, modsString, isComplete,
                            playTimeSeconds, playCount, ct);
                        importedCount++;
                    }

                    if (progress != null && (i % 50 == 0 || i == rows.Count - 1))
                    {
                        progress.Report(new MigrationProgress(
                            ProcessedRows: i + 1,
                            TotalRows: totalRows,
                            ImportedCount: importedCount,
                            SkippedDuplicates: skippedDuplicates,
                            CurrentBeatmapString: currentBeatmap,
                            ProgressPercent: (double)(i + 1) / totalRows * 100.0
                        ));
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"Failed to process row {i + 2} ({currentBeatmap})");
                    failedCount++;
                }
            }

            return new SyncResult(
                Success: true,
                SyncedCount: importedCount,
                FailedCount: failedCount + skippedDuplicates,
                ErrorMessage: failedCount > 0 ? $"{failedCount} rows failed to import" : null
            );
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to import from Google Sheets");
            return new SyncResult(false, 0, 0, ex.Message);
        }
    }

    private static string GetCellValue(IList<object> row, int index)
    {
        return index < row.Count ? row[index]?.ToString() ?? "" : "";
    }

    private static DateTime? ParseTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string[] formats = {
            "yyyy-MM-dd h:mm tt",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd h:mm:ss tt",
            "yyyy-MM-dd HH:mm:ss"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result))
            {
                return result;
            }
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fallback))
        {
            return fallback;
        }

        return null;
    }

    private static (int BeatmapSetId, int BeatmapId, string? BeatmapString) ParseHyperlink(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (0, 0, null);

        var match = HyperlinkRegex.Match(value);
        if (!match.Success)
            return (0, 0, value);

        int.TryParse(match.Groups[1].Value, out int setId);
        int.TryParse(match.Groups[2].Value, out int mapId);
        string beatmapString = match.Groups[3].Value;

        return (setId, mapId, beatmapString);
    }

    private static int ParseInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result);
        return result;
    }

    private static decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal result);
        return result;
    }

    private static int BuildModsBitfield(bool hd, bool hr, bool dt, bool ez, bool ht, bool fl)
    {
        int mods = 0;
        if (ez) mods |= (1 << 1);
        if (hd) mods |= (1 << 3);
        if (hr) mods |= (1 << 4);
        if (dt) mods |= (1 << 6);
        if (ht) mods |= (1 << 8);
        if (fl) mods |= (1 << 10);
        return mods;
    }

    private static string BuildModsString(bool hd, bool hr, bool dt, bool ez, bool ht, bool fl)
    {
        var mods = new List<string>();
        if (ez) mods.Add("EZ");
        if (hd) mods.Add("HD");
        if (hr) mods.Add("HR");
        if (dt) mods.Add("DT");
        if (ht) mods.Add("HT");
        if (fl) mods.Add("FL");
        return mods.Count > 0 ? string.Join("", mods) : "NM";
    }

    private static async Task<bool> CheckDuplicateAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        DateTime timestamp,
        int beatmapId,
        int totalHits,
        CancellationToken ct)
    {
        const string sql = @"
            SELECT COUNT(*) FROM plays
            WHERE timestamp = @Timestamp
              AND beatmap_id = @BeatmapId
              AND total_hits = @TotalHits;";

        var count = await conn.ExecuteScalarAsync<int>(sql, new
        {
            Timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            BeatmapId = beatmapId,
            TotalHits = totalHits
        });

        return count > 0;
    }

    private static async Task InsertSessionAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string sessionId,
        DateTime startTime,
        CancellationToken ct)
    {
        const string sql = @"
            INSERT OR IGNORE INTO sessions (id, start_time, total_plays, playing_seconds, idle_seconds, efficiency_percent)
            VALUES (@Id, @StartTime, 0, 0, 0, 0.0);";

        await conn.ExecuteAsync(sql, new
        {
            Id = sessionId,
            StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        });
    }

    private static async Task InsertPlayAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string sessionId,
        DateTime timestamp,
        int beatmapId,
        int beatmapSetId,
        string beatmapString,
        int bpm,
        decimal stars,
        decimal aim,
        decimal speed,
        decimal cs,
        decimal ar,
        decimal od,
        int totalHits,
        decimal accuracy,
        int hit300,
        int hit100,
        int hit50,
        int hitMiss,
        int modsBitfield,
        string modsString,
        bool isComplete,
        int playTimeSeconds,
        int playCount,
        CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO plays (
                session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_checksum,
                beatmap_string, beatmap_title, beatmap_artist, beatmap_version,
                mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp,
                total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable,
                is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client,
                sync_status, synced_at
            ) VALUES (
                @SessionId, @Timestamp, @BeatmapId, @BeatmapSetId, '',
                @BeatmapString, '', '', '',
                @ModsBitfield, @ModsString, @Bpm, @Stars, @Aim, @Speed, @Cs, @Ar, @Od, 0.0,
                @TotalHits, @Hit300, @Hit100, @Hit50, @HitMiss, @Accuracy, 1,
                @IsComplete, @PlayTimeSeconds, @PlayCount, 0, 0, 'Legacy Import',
                'Synced', @SyncedAt
            );";

        await conn.ExecuteAsync(sql, new
        {
            SessionId = sessionId,
            Timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            BeatmapId = beatmapId,
            BeatmapSetId = beatmapSetId,
            BeatmapString = beatmapString,
            ModsBitfield = modsBitfield,
            ModsString = modsString,
            Bpm = bpm,
            Stars = (double)stars,
            Aim = (double)aim,
            Speed = (double)speed,
            Cs = (double)cs,
            Ar = (double)ar,
            Od = (double)od,
            TotalHits = totalHits,
            Hit300 = hit300,
            Hit100 = hit100,
            Hit50 = hit50,
            HitMiss = hitMiss,
            Accuracy = (double)accuracy,
            IsComplete = isComplete ? 1 : 0,
            PlayTimeSeconds = playTimeSeconds,
            PlayCount = playCount,
            SyncedAt = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        });
    }
}
