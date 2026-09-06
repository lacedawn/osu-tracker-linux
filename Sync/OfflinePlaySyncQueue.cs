using Dapper;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Sync;

public interface IOfflinePlaySyncQueue
{
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
    Task<SyncResult> FlushQueueAsync(CancellationToken ct = default);
    Task<SyncResult> FlushPendingQueueAsync(CancellationToken ct = default);
    void StartBackgroundSync(TimeSpan checkInterval);
    void StopBackgroundSync();
    Task StopBackgroundSyncAsync();
}

public class OfflinePlaySyncQueue : IOfflinePlaySyncQueue, IDisposable, IAsyncDisposable
{
    private static readonly ILogger<OfflinePlaySyncQueue> _log = AppLogger.For<OfflinePlaySyncQueue>();

    private const int BatchSize = 50;

    private readonly Storage.IDatabaseManager _dbManager;
    private readonly SheetsService? _sheetsService;
    private readonly string _spreadsheetId;
    private readonly string _sheetName;
    private readonly Func<string> _getFunctionSeparator;
    private readonly Func<bool> _getSheetsApiReady;
    private readonly Func<IList<IList<object>>, CancellationToken, Task>? _sheetsAppender;

    private CancellationTokenSource? _backgroundCts;
    private Task? _backgroundTask;

    public OfflinePlaySyncQueue(
        Storage.IDatabaseManager dbManager,
        SheetsService? sheetsService,
        string spreadsheetId,
        string sheetName,
        Func<string> getFunctionSeparator,
        Func<bool> getSheetsApiReady,
        Func<IList<IList<object>>, CancellationToken, Task>? sheetsAppender = null)
    {
        _dbManager = dbManager;
        _sheetsService = sheetsService;
        _spreadsheetId = spreadsheetId;
        _sheetName = sheetName;
        _getFunctionSeparator = getFunctionSeparator;
        _getSheetsApiReady = getSheetsApiReady;
        _sheetsAppender = sheetsAppender;
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        await using var conn = await _dbManager.CreateConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Pending';");
    }

    public Task<SyncResult> FlushQueueAsync(CancellationToken ct = default)
    {
        return FlushPendingQueueAsync(ct);
    }

    public async Task<SyncResult> FlushPendingQueueAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_getSheetsApiReady())
            {
                return new SyncResult(false, 0, 0, "Google Sheets API not ready");
            }

            await using var conn = await _dbManager.CreateConnectionAsync(ct);

            const string selectSql = @"
                SELECT id, timestamp, beatmap_id, beatmap_set_id, beatmap_string,
                       mods_bitfield, mods_string, bpm, aim, speed, stars, cs, ar, od,
                       total_hits, accuracy, hit_300, hit_100, hit_50, hit_miss,
                       is_complete, play_time_seconds, consecutive_play_count
                FROM plays
                WHERE sync_status = 'Pending'
                ORDER BY id ASC;";

            var pendingPlays = (await conn.QueryAsync<PendingPlay>(selectSql)).ToList();

            if (pendingPlays.Count == 0)
            {
                return new SyncResult(true, 0, 0, null);
            }

            int totalSynced = 0;
            var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            for (int i = 0; i < pendingPlays.Count; i += BatchSize)
            {
                var batch = pendingPlays.Skip(i).Take(BatchSize).ToList();
                var rows = batch.Select(play => BuildRowData(play, _getFunctionSeparator())).ToList();
                var batchIds = batch.Select(p => p.Id).ToList();

                try
                {
                    if (_sheetsAppender != null)
                    {
                        await _sheetsAppender(rows, ct);
                    }
                    else if (_sheetsService != null)
                    {
                        var valueRange = new ValueRange { Values = rows };
                        var range = $"'{_sheetName}'!A:X";
                        var appendRequest = _sheetsService.Spreadsheets.Values.Append(valueRange, _spreadsheetId, range);
                        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

                        _log.LogInformation("Syncing batch of {Count} plays to Google Sheets", batch.Count);
                        await appendRequest.ExecuteAsync(ct);
                    }
                    else
                    {
                        throw new InvalidOperationException("Google Sheets service is not configured");
                    }

                    const string updateSql = @"
                        UPDATE plays
                        SET sync_status = 'Synced', synced_at = @SyncedAt
                        WHERE id IN @Ids;";

                    using var tx = conn.BeginTransaction();
                    try
                    {
                        await conn.ExecuteAsync(updateSql, new { Ids = batchIds, SyncedAt = nowUtc }, tx);
                        tx.Commit();
                        totalSynced += batch.Count;
                        _log.LogInformation("Successfully marked {Count} plays as synced", batch.Count);
                    }
                    catch (Exception dbEx)
                    {
                        try
                        {
                            tx.Rollback();
                        }
                        catch
                        {
                        }
                        _log.LogError(dbEx, "Failed to mark batch as synced (plays may be duplicated on next sync)");
                    }
                }
                catch (Exception batchEx)
                {
                    _log.LogError(batchEx, "Failed to sync batch starting at index {Index}", i);
                    if (totalSynced == 0)
                    {
                        return new SyncResult(false, 0, 0, batchEx.Message);
                    }
                    break;
                }
            }

            _log.LogInformation("Successfully synced {Count} of {Total} plays to Google Sheets", totalSynced, pendingPlays.Count);
            return new SyncResult(true, totalSynced, 0, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to flush offline sync queue");
            return new SyncResult(false, 0, 0, ex.Message);
        }
    }

    public void StartBackgroundSync(TimeSpan checkInterval)
    {
        if (_backgroundTask != null)
        {
            _log.LogWarning("Background sync already running");
            return;
        }

        _backgroundCts = new CancellationTokenSource();
        _backgroundTask = Task.Run(() => BackgroundSyncLoopAsync(checkInterval, _backgroundCts.Token));
        _log.LogInformation("Started background sync loop with interval {Interval}", checkInterval);
    }

    public async Task StopBackgroundSyncAsync()
    {
        if (_backgroundTask == null)
            return;

        _log.LogInformation("Stopping background sync loop");
        _backgroundCts?.Cancel();
        try
        {
            await _backgroundTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
        }
        catch (AggregateException)
        {
        }
        _backgroundCts?.Dispose();
        _backgroundCts = null;
        _backgroundTask = null;
    }

    public void StopBackgroundSync()
    {
        StopBackgroundSyncAsync().GetAwaiter().GetResult();
    }

    private async Task BackgroundSyncLoopAsync(TimeSpan checkInterval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkInterval, ct);

                if (!_getSheetsApiReady())
                    continue;

                var pendingCount = await GetPendingCountAsync(ct);
                if (pendingCount > 0)
                {
                    _log.LogInformation("Background sync found {Count} pending plays", pendingCount);
                    await FlushQueueAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Background sync iteration failed");
            }
        }
    }

    private static IList<object> BuildRowData(PendingPlay play, string functionSeparator)
    {
        DateTime timestamp = DateTime.Parse(play.Timestamp);
        string dateTimeFormat = "yyyy'-'MM'-'dd h':'mm tt";
        string escapedName = (play.BeatmapString ?? "").Replace("\"", "\"\"");

        bool hd = (play.ModsBitfield & (1 << 3)) != 0;
        bool hr = (play.ModsBitfield & (1 << 4)) != 0;
        bool dt = (play.ModsBitfield & (1 << 6)) != 0;
        bool ez = (play.ModsBitfield & (1 << 1)) != 0;
        bool ht = (play.ModsBitfield & (1 << 8)) != 0;
        bool fl = (play.ModsBitfield & (1 << 10)) != 0;

        string modsLabel = !string.IsNullOrWhiteSpace(play.ModsString) && play.ModsString != "NM"
            ? $" +{play.ModsString}"
            : "";

        return new List<object>
        {
            timestamp.ToString(dateTimeFormat, CultureInfo.InvariantCulture),
            $"=HYPERLINK(\"https://osu.ppy.sh/beatmapsets/{play.BeatmapSetId}#osu/{play.BeatmapId}\"{functionSeparator} \"{escapedName + modsLabel}\")",
            hd ? "1" : "",
            hr ? "1" : "",
            dt ? "1" : "",
            play.Bpm,
            play.Aim,
            play.Speed,
            play.Stars,
            play.Cs,
            play.Ar,
            play.Od,
            play.TotalHits,
            play.Accuracy,
            play.Hit300,
            play.Hit100,
            play.Hit50,
            play.HitMiss,
            ez ? "1" : "",
            ht ? "1" : "",
            fl ? "1" : "",
            play.IsComplete == 1 ? "1" : "0",
            play.ConsecutivePlayCount,
            play.PlayTimeSeconds
        };
    }

    public void Dispose()
    {
        StopBackgroundSync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopBackgroundSyncAsync();
    }

    private class PendingPlay
    {
        public long Id { get; set; }
        public string Timestamp { get; set; } = "";
        public int BeatmapId { get; set; }
        public int BeatmapSetId { get; set; }
        public string BeatmapString { get; set; } = "";
        public int ModsBitfield { get; set; }
        public string ModsString { get; set; } = "";
        public int Bpm { get; set; }
        public double Aim { get; set; }
        public double Speed { get; set; }
        public double Stars { get; set; }
        public double Cs { get; set; }
        public double Ar { get; set; }
        public double Od { get; set; }
        public int TotalHits { get; set; }
        public double Accuracy { get; set; }
        public int Hit300 { get; set; }
        public int Hit100 { get; set; }
        public int Hit50 { get; set; }
        public int HitMiss { get; set; }
        public int IsComplete { get; set; }
        public int PlayTimeSeconds { get; set; }
        public int ConsecutivePlayCount { get; set; }
    }
}
