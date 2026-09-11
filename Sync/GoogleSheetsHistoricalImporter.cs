using Dapper;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Data.Sqlite;
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

    internal sealed record ParsedImportRow(
        DateTime Timestamp,
        int BeatmapId,
        int BeatmapSetId,
        string BeatmapString,
        int Bpm,
        decimal Stars,
        decimal Aim,
        decimal Speed,
        decimal Cs,
        decimal Ar,
        decimal Od,
        int TotalHits,
        decimal Accuracy,
        int Hit300,
        int Hit100,
        int Hit50,
        int HitMiss,
        int ModsBitfield,
        string ModsString,
        bool IsComplete,
        int PlayTimeSeconds,
        int PlayCount);

    internal static string BuildDedupKey(
        DateTime timestamp,
        int beatmapId,
        int totalHits,
        decimal accuracy,
        int hit300,
        int hit100,
        int hit50,
        int hitMiss,
        int modsBitfield,
        bool isComplete)
    {
        DateTime normalized = timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
        string timePart = normalized.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{timePart}|{beatmapId}|{totalHits}|{accuracy}|{hit300}|{hit100}|{hit50}|{hitMiss}|{modsBitfield}|{(isComplete ? 1 : 0)}");
    }

    internal async Task<SyncResult> ImportParsedRowsAsync(
        IReadOnlyList<ParsedImportRow> parsed,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (parsed.Count == 0)
        {
            return new SyncResult(true, 0, 0, "No data found in spreadsheet");
        }

        return await _dbManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            const string existingSql = @"
                SELECT timestamp AS Timestamp, beatmap_id AS BeatmapId, total_hits AS TotalHits,
                       accuracy AS Accuracy, hit_300 AS Hit300, hit_100 AS Hit100, hit_50 AS Hit50,
                       hit_miss AS HitMiss, mods_bitfield AS ModsBitfield, is_complete AS IsComplete
                FROM plays;";

            var existingRows = await conn.QueryAsync<ExistingPlayKey>(existingSql, transaction: tx);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var existing in existingRows)
            {
                if (DateTime.TryParse(existing.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime existingTimestamp))
                {
                    seen.Add(BuildDedupKey(existingTimestamp, existing.BeatmapId, existing.TotalHits, (decimal)existing.Accuracy, existing.Hit300, existing.Hit100, existing.Hit50, existing.HitMiss, existing.ModsBitfield, existing.IsComplete != 0));
                }
            }

            int importedCount = 0;
            int skippedDuplicates = 0;
            int failedCount = 0;
            int totalRows = parsed.Count;
            DateTime? lastPlayTimestamp = null;
            string? currentSessionId = null;

            for (int i = 0; i < parsed.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var row = parsed[i];

                try
                {
                    string key = BuildDedupKey(row.Timestamp, row.BeatmapId, row.TotalHits, row.Accuracy, row.Hit300, row.Hit100, row.Hit50, row.HitMiss, row.ModsBitfield, row.IsComplete);

                    if (seen.Contains(key))
                    {
                        skippedDuplicates++;
                    }
                    else
                    {
                        if (lastPlayTimestamp.HasValue && (row.Timestamp - lastPlayTimestamp.Value).TotalMinutes > 45)
                        {
                            currentSessionId = Guid.NewGuid().ToString();
                            await InsertSessionAsync(conn, tx, currentSessionId, row.Timestamp, ct);
                        }
                        else if (currentSessionId == null)
                        {
                            currentSessionId = Guid.NewGuid().ToString();
                            await InsertSessionAsync(conn, tx, currentSessionId, row.Timestamp, ct);
                        }

                        await InsertPlayAsync(conn, tx, currentSessionId, row, ct);
                        seen.Add(key);
                        importedCount++;
                    }

                    lastPlayTimestamp = row.Timestamp;

                    if (progress != null && (i % 50 == 0 || i == parsed.Count - 1))
                    {
                        progress.Report(new MigrationProgress(
                            ProcessedRows: i + 1,
                            TotalRows: totalRows,
                            ImportedCount: importedCount,
                            SkippedDuplicates: skippedDuplicates,
                            CurrentBeatmapString: row.BeatmapString,
                            ProgressPercent: (double)(i + 1) / totalRows * 100.0
                        ));
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to import row {RowNumber} ({Beatmap})", i + 2, row.BeatmapString);
                    failedCount++;
                }
            }

            _log.LogInformation("Historical import completed: {Imported} imported, {Skipped} duplicates skipped, {Failed} failed", importedCount, skippedDuplicates, failedCount);

            return new SyncResult(
                Success: true,
                SyncedCount: importedCount,
                FailedCount: failedCount + skippedDuplicates,
                ErrorMessage: failedCount > 0 ? $"{failedCount} rows failed to import" : null
            );
        }, ct).ConfigureAwait(false);
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
            request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMULA;
            var response = await request.ExecuteAsync(ct);

            if (response?.Values == null || response.Values.Count == 0)
            {
                return new SyncResult(true, 0, 0, "No data found in spreadsheet");
            }

            var rows = response.Values;
            var parsed = new List<ParsedImportRow>(rows.Count);
            int failedCount = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                try
                {
                    if (row.Count < 18)
                    {
                        _log.LogWarning("Row {RowNumber} has insufficient columns ({ColumnCount}), skipping", i + 2, row.Count);
                        failedCount++;
                        continue;
                    }

                    var timestamp = ParseTimestamp(GetCellValue(row, 0));
                    if (!timestamp.HasValue)
                    {
                        _log.LogWarning("Row {RowNumber} has invalid timestamp: '{TimestampValue}', skipping", i + 2, GetCellValue(row, 0));
                        failedCount++;
                        continue;
                    }

                    var (beatmapSetId, beatmapId, beatmapString) = ParseHyperlink(GetCellValue(row, 1));

                    if (beatmapSetId == 0 || beatmapId == 0)
                    {
                        _log.LogWarning("Row {RowNumber} has invalid beatmap hyperlink, skipping", i + 2);
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

                    parsed.Add(new ParsedImportRow(
                        Timestamp: timestamp.Value,
                        BeatmapId: beatmapId,
                        BeatmapSetId: beatmapSetId,
                        BeatmapString: beatmapString ?? "",
                        Bpm: bpm,
                        Stars: stars,
                        Aim: aim,
                        Speed: speed,
                        Cs: cs,
                        Ar: ar,
                        Od: od,
                        TotalHits: totalHits,
                        Accuracy: accuracy,
                        Hit300: hit300,
                        Hit100: hit100,
                        Hit50: hit50,
                        HitMiss: hitMiss,
                        ModsBitfield: modsBitfield,
                        ModsString: modsString,
                        IsComplete: isComplete,
                        PlayTimeSeconds: playTimeSeconds,
                        PlayCount: playCount));
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to parse row {RowNumber}", i + 2);
                    failedCount++;
                }
            }

            SyncResult imported = await ImportParsedRowsAsync(parsed, progress, ct).ConfigureAwait(false);

            return new SyncResult(
                Success: imported.Success,
                SyncedCount: imported.SyncedCount,
                FailedCount: failedCount + imported.FailedCount,
                ErrorMessage: failedCount > 0 ? $"{failedCount} rows failed to import" : imported.ErrorMessage
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

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double serialNumber))
        {
            if (serialNumber > 1 && serialNumber < 100000)
            {
                try
                {
                    var excelEpoch = new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc);
                    var timestamp = excelEpoch.AddDays(serialNumber);
                    return timestamp;
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
        }

        string[] formats = {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd h:mm:ss tt",
            "yyyy-MM-dd h:mm tt",
            "M/d/yyyy h:mm:ss tt",
            "M/d/yyyy h:mm tt",
            "M/d/yyyy HH:mm:ss",
            "M/d/yyyy HH:mm",
            "d/M/yyyy HH:mm:ss",
            "d/M/yyyy HH:mm",
            "d/M/yyyy h:mm:ss tt",
            "d/M/yyyy h:mm tt",
            "yyyy.MM.dd HH:mm:ss",
            "yyyy.MM.dd HH:mm",
            "dd.MM.yyyy HH:mm:ss",
            "dd.MM.yyyy HH:mm"
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

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var lastResort))
        {
            return lastResort;
        }

        _log.LogWarning("Failed to parse timestamp: '{Value}'", value);
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

    private sealed class ExistingPlayKey
    {
        public string Timestamp { get; set; } = "";
        public int BeatmapId { get; set; }
        public int TotalHits { get; set; }
        public double Accuracy { get; set; }
        public int Hit300 { get; set; }
        public int Hit100 { get; set; }
        public int Hit50 { get; set; }
        public int HitMiss { get; set; }
        public int ModsBitfield { get; set; }
        public int IsComplete { get; set; }
    }

    private static async Task InsertSessionAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
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
        }, transaction: tx);
    }

    private static Task InsertPlayAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string sessionId,
        ParsedImportRow row,
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

        return conn.ExecuteAsync(sql, new
        {
            SessionId = sessionId,
            Timestamp = row.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            BeatmapId = row.BeatmapId,
            BeatmapSetId = row.BeatmapSetId,
            BeatmapString = row.BeatmapString,
            ModsBitfield = row.ModsBitfield,
            ModsString = row.ModsString,
            Bpm = row.Bpm,
            Stars = (double)row.Stars,
            Aim = (double)row.Aim,
            Speed = (double)row.Speed,
            Cs = (double)row.Cs,
            Ar = (double)row.Ar,
            Od = (double)row.Od,
            TotalHits = row.TotalHits,
            Hit300 = row.Hit300,
            Hit100 = row.Hit100,
            Hit50 = row.Hit50,
            HitMiss = row.HitMiss,
            Accuracy = (double)row.Accuracy,
            IsComplete = row.IsComplete ? 1 : 0,
            PlayTimeSeconds = row.PlayTimeSeconds,
            PlayCount = row.PlayCount,
            SyncedAt = row.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        }, transaction: tx);
    }
}
