using Circle_Tracker.Storage;
using Circle_Tracker.Sync;
using Dapper;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace circle_tracker.Tests.IntegrationTests;

public class OfflinePlaySyncQueueIntegrationTests
{
    private static async Task<SqliteDatabaseManager> CreateInitializedDbManagerAsync()
    {
        string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var dbManager = new SqliteDatabaseManager(connStr);
        await dbManager.InitializeAsync();
        return dbManager;
    }

    private static async Task SeedPendingPlaysAsync(SqliteDatabaseManager dbManager, int count)
    {
        await using var conn = await dbManager.CreateConnectionAsync();
        var sessionId = Guid.NewGuid().ToString();
        var timestampStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        await conn.ExecuteAsync(
            "INSERT INTO sessions (id, start_time, total_plays, playing_seconds, idle_seconds, efficiency_percent) VALUES (@Id, @Start, 0, 0, 0, 0.0);",
            new { Id = sessionId, Start = timestampStr });

        const string insertSql = @"
            INSERT INTO plays (
                session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_checksum,
                beatmap_string, beatmap_title, beatmap_artist, beatmap_version,
                mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp,
                total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable,
                is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client,
                sync_status
            ) VALUES (
                @SessionId, @Timestamp, @BeatmapId, @BeatmapSetId, '',
                'Test Map', '', '', '',
                0, 'NM', 180, 5.5, 2.5, 2.8, 4.0, 9.0, 8.0, 6.0,
                500, 450, 40, 10, 0, 98.5, 1,
                1, 120, 1, 0, 0, 'Test',
                'Pending'
            );";

        var plays = Enumerable.Range(1, count).Select(i => new
        {
            SessionId = sessionId,
            Timestamp = DateTime.UtcNow.AddSeconds(-i).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            BeatmapId = 10000 + i,
            BeatmapSetId = 1000 + i
        });

        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(insertSql, plays, tx);
        tx.Commit();
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
                return true;

            await Task.Delay(10).ConfigureAwait(false);
        }

        return await condition().ConfigureAwait(false);
    }

    [Fact]
    public async Task WireUp_AfterSheetsConnect_BackgroundSyncStarts()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 3);

        int appendedRows = 0;
        var sheetsAppender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) =>
        {
            Interlocked.Add(ref appendedRows, rows.Count);
            return Task.CompletedTask;
        });

        await using var queue = new OfflinePlaySyncQueue(
            dbManager,
            null,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true,
            sheetsAppender);

        queue.StartBackgroundSync(TimeSpan.FromMilliseconds(50));

        await WaitUntilAsync(
            () => Task.FromResult(Volatile.Read(ref appendedRows) >= 3),
            TimeSpan.FromSeconds(5));

        await queue.StopBackgroundSyncAsync();

        Volatile.Read(ref appendedRows).Should().Be(3);
    }

    [Fact]
    public async Task WireUp_SheetsApiNotReady_BackgroundSyncSkipsFlush()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 3);

        int appendCallCount = 0;
        var sheetsAppender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) =>
        {
            Interlocked.Increment(ref appendCallCount);
            return Task.CompletedTask;
        });

        await using var queue = new OfflinePlaySyncQueue(
            dbManager,
            null,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => false,
            sheetsAppender);

        queue.StartBackgroundSync(TimeSpan.FromMilliseconds(50));

        await Task.Delay(300);

        await queue.StopBackgroundSyncAsync();

        appendCallCount.Should().Be(0);
    }

    [Fact]
    public async Task StopBackgroundSync_PendingFlushCompletes()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 5);

        int appenderInvocations = 0;
        var slowAppender = new Func<IList<IList<object>>, CancellationToken, Task>(async (rows, ct) =>
        {
            Interlocked.Increment(ref appenderInvocations);
            await Task.Delay(100).ConfigureAwait(false);
        });

        await using var queue = new OfflinePlaySyncQueue(
            dbManager,
            null,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true,
            slowAppender);

        queue.StartBackgroundSync(TimeSpan.FromMilliseconds(10));

        await WaitUntilAsync(
            () => Task.FromResult(Volatile.Read(ref appenderInvocations) > 0),
            TimeSpan.FromSeconds(5));

        await queue.StopBackgroundSyncAsync();

        Volatile.Read(ref appenderInvocations).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FlushPendingQueueAsync_MarksPlaysSynced()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 3);

        var sheetsAppender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) => Task.CompletedTask);

        await using var queue = new OfflinePlaySyncQueue(
            dbManager,
            null,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true,
            sheetsAppender);

        await queue.FlushPendingQueueAsync();

        await using var verifyConn = await dbManager.CreateConnectionAsync();
        int syncedCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Synced';");

        syncedCount.Should().Be(3);
    }
}
