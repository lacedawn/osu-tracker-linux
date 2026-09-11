using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Circle_Tracker.Storage.Querying;

namespace Circle_Tracker.Sync;

public interface IDataExportService
{
    Task ExportPlaysAsync(
        string destinationFilePath,
        ExportFormat format,
        PlayQueryFilter? filter = null,
        CancellationToken ct = default);

    Task BackupDatabaseAsync(
        string backupFilePath,
        CancellationToken ct = default);
}

public class DataExportService : IDataExportService
{
    private static readonly ILogger<DataExportService> _log = AppLogger.For<DataExportService>();

    private readonly Storage.IDatabaseManager _dbManager;
    private readonly Storage.Querying.IPlayQueryEngine _queryEngine;

    public DataExportService(Storage.IDatabaseManager dbManager, Storage.Querying.IPlayQueryEngine queryEngine)
    {
        _dbManager = dbManager;
        _queryEngine = queryEngine;
    }

    public async Task ExportPlaysAsync(
        string destinationFilePath,
        ExportFormat format,
        PlayQueryFilter? filter = null,
        CancellationToken ct = default)
    {
        _log.LogInformation("Starting export to {Path} in {Format} format", destinationFilePath, format);

        var allPlays = await FetchAllPlaysAsync(filter, ct);

        switch (format)
        {
            case ExportFormat.Csv:
                await ExportToCsvAsync(destinationFilePath, allPlays, ct);
                break;
            case ExportFormat.Json:
                await ExportToJsonAsync(destinationFilePath, allPlays, ct);
                break;
            case ExportFormat.SqliteBackup:
                await BackupDatabaseAsync(destinationFilePath, ct);
                break;
            default:
                throw new ArgumentException($"Unsupported export format: {format}");
        }

        _log.LogInformation("Successfully exported {Count} plays to {Path}", allPlays.Count, destinationFilePath);
    }

    public async Task BackupDatabaseAsync(string backupFilePath, CancellationToken ct = default)
    {
        _log.LogInformation("Creating database backup to {Path}", backupFilePath);

        await using var sourceConn = await _dbManager.CreateConnectionAsync(ct);

        string? backupDirectory = Path.GetDirectoryName(Path.GetFullPath(backupFilePath));
        if (!string.IsNullOrEmpty(backupDirectory) && !Directory.Exists(backupDirectory))
        {
            Directory.CreateDirectory(backupDirectory);
        }

        string tempPath = backupFilePath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var destConn = new SqliteConnection($"Data Source={tempPath}"))
            {
                await destConn.OpenAsync(ct);

                if (sourceConn is SqliteConnection sqliteSource)
                {
                    sqliteSource.BackupDatabase(destConn);
                }
                else
                {
                    throw new InvalidOperationException("Database connection is not a SqliteConnection");
                }
            }

            File.Move(tempPath, backupFilePath, true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
            throw;
        }

        _log.LogInformation("Database backup completed successfully");
    }

    private async Task<List<PlayRecord>> FetchAllPlaysAsync(PlayQueryFilter? filter, CancellationToken ct)
    {
        var allPlays = new List<PlayRecord>();
        int page = 1;
        const int pageSize = 1000;

        var effectiveFilter = filter ?? new PlayQueryFilter();

        while (true)
        {
            var filterWithPaging = effectiveFilter with { Page = page, PageSize = pageSize };
            var result = await _queryEngine.QueryPlaysAsync(filterWithPaging, ct);

            allPlays.AddRange(result.Items);

            if (!result.HasNextPage)
                break;

            page++;
        }

        return allPlays;
    }

    private static async Task ExportToCsvAsync(string filePath, List<PlayRecord> plays, CancellationToken ct)
    {
        await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

        var headers = new[]
        {
            "Id", "SessionId", "Timestamp", "BeatmapId", "BeatmapSetId", "BeatmapChecksum",
            "BeatmapString", "BeatmapTitle", "BeatmapArtist", "BeatmapVersion",
            "ModsBitfield", "ModsString", "Bpm", "Stars", "Aim", "Speed", "Cs", "Ar", "Od", "Hp",
            "TotalHits", "Hit300", "Hit100", "Hit50", "HitMiss", "Accuracy", "AccuracyReliable",
            "IsComplete", "PlayTimeSeconds", "ConsecutivePlayCount", "GameMode", "IsReplay", "DetectedClient"
        };

        await writer.WriteLineAsync(string.Join(",", headers));

        foreach (var play in plays)
        {
            var values = new object?[]
            {
                play.Id,
                play.SessionId,
                play.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                play.BeatmapId,
                play.BeatmapSetId,
                play.BeatmapChecksum,
                play.BeatmapString,
                play.BeatmapTitle,
                play.BeatmapArtist,
                play.BeatmapVersion,
                play.ModsBitfield,
                play.ModsString,
                play.Bpm,
                play.Stars.ToString("F2", CultureInfo.InvariantCulture),
                play.Aim.ToString("F2", CultureInfo.InvariantCulture),
                play.Speed.ToString("F2", CultureInfo.InvariantCulture),
                play.Cs.ToString("F2", CultureInfo.InvariantCulture),
                play.Ar.ToString("F2", CultureInfo.InvariantCulture),
                play.Od.ToString("F2", CultureInfo.InvariantCulture),
                play.Hp.ToString("F2", CultureInfo.InvariantCulture),
                play.TotalHits,
                play.Hit300,
                play.Hit100,
                play.Hit50,
                play.HitMiss,
                play.Accuracy.ToString("F2", CultureInfo.InvariantCulture),
                play.AccuracyReliable,
                play.IsComplete,
                play.PlayTimeSeconds,
                play.ConsecutivePlayCount,
                play.GameMode,
                play.IsReplay,
                play.DetectedClient
            };

            var escapedValues = values.Select(v => EscapeCsvField(v?.ToString() ?? ""));
            await writer.WriteLineAsync(string.Join(",", escapedValues));
        }
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "";

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }

    private static async Task ExportToJsonAsync(string filePath, List<PlayRecord> plays, CancellationToken ct)
    {
        var exportData = new
        {
            SchemaVersion = 2,
            ExportTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            TotalPlays = plays.Count,
            Plays = plays.Select(p => new
            {
                Id = p.Id,
                SessionId = p.SessionId,
                Timestamp = p.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                Beatmap = new
                {
                    Id = p.BeatmapId,
                    SetId = p.BeatmapSetId,
                    Checksum = p.BeatmapChecksum,
                    FullString = p.BeatmapString,
                    Title = p.BeatmapTitle,
                    Artist = p.BeatmapArtist,
                    Version = p.BeatmapVersion,
                    CoverUrl = p.CoverUrl
                },
                Difficulty = new
                {
                    Bpm = p.Bpm,
                    Stars = p.Stars,
                    Aim = p.Aim,
                    Speed = p.Speed,
                    Cs = p.Cs,
                    Ar = p.Ar,
                    Od = p.Od,
                    Hp = p.Hp
                },
                Mods = new
                {
                    Bitfield = p.ModsBitfield,
                    String = p.ModsString
                },
                Performance = new
                {
                    TotalHits = p.TotalHits,
                    Hit300 = p.Hit300,
                    Hit100 = p.Hit100,
                    Hit50 = p.Hit50,
                    HitMiss = p.HitMiss,
                    Accuracy = p.Accuracy,
                    AccuracyReliable = p.AccuracyReliable,
                    IsComplete = p.IsComplete,
                    PlayTimeSeconds = p.PlayTimeSeconds,
                    ConsecutivePlayCount = p.ConsecutivePlayCount
                },
                Metadata = new
                {
                    GameMode = p.GameMode,
                    IsReplay = p.IsReplay,
                    DetectedClient = p.DetectedClient
                }
            })
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, exportData, options, ct);
    }
}
