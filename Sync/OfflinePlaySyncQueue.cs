using Circle_Tracker;
using Circle_Tracker.Services;
using Dapper;
using Google;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    private const int MaxBatchAttempts = 3;
    private const int BaseBatchRetryDelayMs = 500;
    private const int MaxBatchRetryJitterMs = 200;
    private const int InterBatchDelayMs = 100;

    private readonly Storage.IDatabaseManager _dbManager;
    private readonly SheetsService? _sheetsService;
    private readonly string _spreadsheetId;
    private readonly string _sheetName;
    private readonly Func<string> _getFunctionSeparator;
    private readonly Func<bool> _getSheetsApiReady;
    private readonly Func<IList<IList<object>>, CancellationToken, Task>? _sheetsAppender;
    private readonly Func<CancellationToken, Task<IReadOnlySet<string>>>? _sheetsClientIdReader;
    private readonly CircuitBreaker _circuitBreaker = new(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(60));

    internal Func<int, CancellationToken, Task> RetryDelayProvider { get; set; } = DefaultBatchRetryDelayAsync;

    internal Func<CancellationToken, Task> InterBatchDelayProvider { get; set; } = DefaultInterBatchDelayAsync;

    private static Task DefaultInterBatchDelayAsync(CancellationToken ct)
    {
        return Task.Delay(InterBatchDelayMs, ct);
    }

    private static Task DefaultBatchRetryDelayAsync(int attempt, CancellationToken ct)
    {
        int delayMs = BaseBatchRetryDelayMs * (1 << attempt) + Random.Shared.Next(0, MaxBatchRetryJitterMs + 1);
        return Task.Delay(delayMs, ct);
    }

    private sealed class PermanentBatchFailureException : Exception
    {
        public PermanentBatchFailureException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    internal static bool IsTransientBatchFailure(Exception ex)
    {
        return SheetsFailureClassifier.IsTransient(ex);
    }

    internal static bool IsPermanentBatchFailure(Exception ex)
    {
        return SheetsFailureClassifier.IsPermanent(ex);
    }

    private async Task AppendBatchWithRetryAsync(IList<IList<object>> rows, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxBatchAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (_sheetsAppender != null)
                {
                    await _sheetsAppender(rows, ct);
                }
                else if (_sheetsService != null)
                {
                    var valueRange = new ValueRange { Values = rows };
                    var range = $"'{_sheetName}'!A:Y";
                    var appendRequest = _sheetsService.Spreadsheets.Values.Append(valueRange, _spreadsheetId, range);
                    appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

                    _log.LogInformation("Syncing batch of {Count} plays to Google Sheets", rows.Count);
                    await appendRequest.ExecuteAsync(ct);
                }
                else
                {
                    throw new InvalidOperationException("Google Sheets service is not configured");
                }

                return;
            }
            catch (Exception ex) when (IsPermanentBatchFailure(ex))
            {
                throw new PermanentBatchFailureException(ex.Message, ex);
            }
            catch (Exception ex) when (IsTransientBatchFailure(ex) && attempt < MaxBatchAttempts - 1)
            {
                _log.LogWarning("Transient batch error ({ErrorType}), retrying...", ex.GetType().Name);
                await RetryDelayProvider(attempt, ct);
            }
        }
    }

    private CancellationTokenSource? _backgroundCts;
    private Task? _backgroundTask;

    public OfflinePlaySyncQueue(
        Storage.IDatabaseManager dbManager,
        SheetsService? sheetsService,
        string spreadsheetId,
        string sheetName,
        Func<string> getFunctionSeparator,
        Func<bool> getSheetsApiReady,
        Func<IList<IList<object>>, CancellationToken, Task>? sheetsAppender = null,
        Func<CancellationToken, Task<IReadOnlySet<string>>>? sheetsClientIdReader = null)
    {
        _dbManager = dbManager;
        _sheetsService = sheetsService;
        _spreadsheetId = spreadsheetId;
        _sheetName = sheetName;
        _getFunctionSeparator = getFunctionSeparator;
        _getSheetsApiReady = getSheetsApiReady;
        _sheetsAppender = sheetsAppender;
        _sheetsClientIdReader = sheetsClientIdReader;
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

            await using var selectConn = await _dbManager.CreateConnectionAsync(ct);

            const string selectSql = @"
                SELECT id AS Id, timestamp AS Timestamp,
                        beatmap_id AS BeatmapId, beatmap_set_id AS BeatmapSetId,
                        beatmap_string AS BeatmapString,
                        mods_bitfield AS ModsBitfield, mods_string AS ModsString,
                        bpm AS Bpm, aim AS Aim, speed AS Speed, stars AS Stars,
                        cs AS Cs, ar AS Ar, od AS Od,
                        total_hits AS TotalHits, accuracy AS Accuracy,
                        hit_300 AS Hit300, hit_100 AS Hit100, hit_50 AS Hit50, hit_miss AS HitMiss,
                        is_complete AS IsComplete,
                        play_time_seconds AS PlayTimeSeconds,
                        consecutive_play_count AS ConsecutivePlayCount,
                        COALESCE(client_id, '') AS ClientId,
                        sync_attempts AS SyncAttempts
                FROM plays
                WHERE sync_status = 'Pending'
                ORDER BY id ASC;";

            var pendingPlays = (await selectConn.QueryAsync<PendingPlay>(selectSql)).ToList();

            if (pendingPlays.Count == 0)
            {
                return new SyncResult(true, 0, 0, null);
            }

            int totalSynced = 0;
            int skippedCount = 0;
            var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            for (int i = 0; i < pendingPlays.Count; i += BatchSize)
            {
                var batch = pendingPlays.Skip(i).Take(BatchSize).ToList();
                var batchIds = batch.Select(p => p.Id).ToList();

                if (!_circuitBreaker.AllowRequest())
                {
                    _log.LogWarning("Circuit breaker open, stopping offline sync");
                    break;
                }

                try
                {
                    var stillPending = await ReadStillPendingBatchAsync(batch, batchIds, ct);
                    if (stillPending.Count == 0)
                    {
                        continue;
                    }

                    var alreadyAppended = await ReadAlreadyAppendedAsync(stillPending, ct);
                    var toAppend = stillPending.Where(p => !alreadyAppended.Contains(p.ClientId)).ToList();
                    var markIds = stillPending.Select(p => p.Id).ToList();

                    if (toAppend.Count > 0)
                    {
                        var rows = toAppend.Select(play => BuildRowData(play, _getFunctionSeparator())).ToList();
                        await AppendBatchWithRetryAsync(rows, ct);
                        _circuitBreaker.RecordSuccess();
                    }

                    const string updateSql = @"
                        UPDATE plays
                        SET sync_status = 'Synced', synced_at = @SyncedAt
                        WHERE id IN @Ids AND sync_status = 'Pending';";

                    await using var updateConn = await _dbManager.CreateConnectionAsync(ct);
                    using var tx = updateConn.BeginTransaction();
                    try
                    {
                        int affected = await updateConn.ExecuteAsync(updateSql, new { Ids = markIds, SyncedAt = nowUtc }, tx);
                        tx.Commit();
                        totalSynced += affected;
                        _log.LogInformation("Successfully marked {Count} plays as synced", affected);
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
                catch (PermanentBatchFailureException permanentEx)
                {
                    string skipError = permanentEx.InnerException?.Message ?? permanentEx.Message;
                    int marked = await MarkBatchSkippedAsync(batchIds, skipError, ct);
                    skippedCount += marked;
                    _log.LogError(permanentEx.InnerException ?? permanentEx, "Skipping batch starting at index {Index} due to permanent failure", i);
                }
                catch (Exception batchEx)
                {
                    _circuitBreaker.RecordFailure();
                    _log.LogError(batchEx, "Failed to sync batch starting at index {Index}", i);
                    if (totalSynced == 0 && skippedCount == 0)
                    {
                        return new SyncResult(false, 0, 0, batchEx.Message);
                    }
                    break;
                }

                await DelayBetweenBatchesAsync(i, pendingPlays.Count, ct);
            }

            _log.LogInformation("Successfully synced {Count} of {Total} plays to Google Sheets", totalSynced, pendingPlays.Count);
            return new SyncResult(true, totalSynced, skippedCount, null, skippedCount);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to flush offline sync queue");
            return new SyncResult(false, 0, 0, ex.Message);
        }
    }

    private async Task<List<PendingPlay>> ReadStillPendingBatchAsync(List<PendingPlay> batch, List<long> batchIds, CancellationToken ct)
    {
        await using var conn = await _dbManager.CreateConnectionAsync(ct);
        var liveIds = (await conn.QueryAsync<long>(
            "SELECT id FROM plays WHERE id IN @Ids AND sync_status = 'Pending';",
            new { Ids = batchIds })).ToHashSet();
        return batch.Where(p => liveIds.Contains(p.Id)).ToList();
    }

    private async Task<int> MarkBatchSkippedAsync(IReadOnlyList<long> batchIds, string error, CancellationToken ct)
    {
        const string skipSql = @"
                        UPDATE plays
                        SET sync_status = 'Skipped', sync_error = @Error
                        WHERE id IN @Ids AND sync_status = 'Pending';";

        await using var conn = await _dbManager.CreateConnectionAsync(ct);
        return await conn.ExecuteAsync(skipSql, new { Ids = batchIds, Error = error });
    }

    private async Task DelayBetweenBatchesAsync(int batchStartIndex, int totalCount, CancellationToken ct)
    {
        if (batchStartIndex + BatchSize >= totalCount)
        {
            return;
        }

        await InterBatchDelayProvider(ct);
    }

    private async Task<HashSet<string>> ReadAlreadyAppendedAsync(IReadOnlyList<PendingPlay> batch, CancellationToken ct)
    {
        if (!batch.Any(p => p.SyncAttempts > 0 && !string.IsNullOrEmpty(p.ClientId)))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            return await ReadAppendedClientIdsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not verify already-appended plays, proceeding with append");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private async Task<HashSet<string>> ReadAppendedClientIdsAsync(CancellationToken ct)
    {
        if (_sheetsClientIdReader != null)
        {
            var known = await _sheetsClientIdReader(ct).ConfigureAwait(false);
            return new HashSet<string>(known.Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
        }

        var appended = new HashSet<string>(StringComparer.Ordinal);
        if (_sheetsService == null)
        {
            return appended;
        }

        var range = $"'{_sheetName}'!Y2:Y";
        var request = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, range);
        var response = await request.ExecuteAsync(ct).ConfigureAwait(false);
        if (response?.Values == null)
        {
            return appended;
        }

        foreach (var row in response.Values)
        {
            string? clientId = row.Count > 0 ? row[0]?.ToString() : null;
            if (!string.IsNullOrEmpty(clientId))
            {
                appended.Add(clientId);
            }
        }

        return appended;
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

        var mods = (OsuMods)play.ModsBitfield;
        bool hd = mods.HasFlag(OsuMods.Hidden);
        bool hr = mods.HasFlag(OsuMods.HardRock);
        bool dt = mods.HasFlag(OsuMods.DoubleTime);
        bool ez = mods.HasFlag(OsuMods.Easy);
        bool ht = mods.HasFlag(OsuMods.HalfTime);
        bool fl = mods.HasFlag(OsuMods.Flashlight);

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
            play.PlayTimeSeconds,
            play.ClientId
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
        public string ClientId { get; set; } = "";
        public int SyncAttempts { get; set; }
    }
}
