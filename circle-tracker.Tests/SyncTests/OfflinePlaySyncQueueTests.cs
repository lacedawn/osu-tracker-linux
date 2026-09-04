using Circle_Tracker.Storage;
using Circle_Tracker.Sync;
using Dapper;
using FluentAssertions;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Moq;
using Moq.Protected;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.SyncTests;

public class OfflinePlaySyncQueueTests
{
    private class MockHttpClientFactory : Google.Apis.Http.HttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public MockHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args)
        {
            return _handler;
        }
    }

    private static async Task<SqliteDatabaseManager> CreateInitializedDbManagerAsync()
    {
        string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var dbManager = new SqliteDatabaseManager(connStr);
        await dbManager.InitializeAsync();
        return dbManager;
    }

    private static SheetsService CreateMockSheetsService()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"spreadsheetId\":\"test-id\",\"tableRange\":\"Sheet1!A1:X1\",\"updates\":{}}", Encoding.UTF8, "application/json")
            });

        return new SheetsService(new BaseClientService.Initializer
        {
            HttpClientFactory = new MockHttpClientFactory(mockHandler.Object),
            ApplicationName = "CircleTrackerTests"
        });
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

    [Fact]
    public async Task FlushPendingQueueAsync_WithLargeBatch_UpdatesAllRowsInSingleBatch()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 750);

        var sheetsService = CreateMockSheetsService();
        using var queue = new OfflinePlaySyncQueue(
            dbManager,
            sheetsService,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true);

        var result = await queue.FlushPendingQueueAsync();

        result.IsSuccess.Should().BeTrue();
        result.SyncedCount.Should().Be(750);

        await using var verifyConn = await dbManager.CreateConnectionAsync();
        var syncedCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Synced';");
        var pendingCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Pending';");

        syncedCount.Should().Be(750);
        pendingCount.Should().Be(0);
    }

    [Fact]
    public async Task FlushPendingQueueAsync_WhenDatabaseFailsMidBatch_RollsBackEntireTransaction()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 750);

        await using (var triggerConn = await dbManager.CreateConnectionAsync())
        {
            await triggerConn.ExecuteAsync(@"
                CREATE TRIGGER fail_mid_batch
                BEFORE UPDATE ON plays
                WHEN NEW.id > 500
                BEGIN
                    SELECT RAISE(ABORT, 'Simulated database disruption mid batch');
                END;");
        }

        var sheetsService = CreateMockSheetsService();
        using var queue = new OfflinePlaySyncQueue(
            dbManager,
            sheetsService,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true);

        var result = await queue.FlushPendingQueueAsync();

        result.IsSuccess.Should().BeFalse();

        await using var verifyConn = await dbManager.CreateConnectionAsync();
        var syncedCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Synced';");
        var pendingCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Pending';");

        syncedCount.Should().Be(0);
        pendingCount.Should().Be(750);
    }

    [Fact]
    public async Task FlushQueueAsync_DelegatesToFlushPendingQueueAsync()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 5);

        var sheetsService = CreateMockSheetsService();
        using var queue = new OfflinePlaySyncQueue(
            dbManager,
            sheetsService,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true);

        var result = await queue.FlushQueueAsync();

        result.IsSuccess.Should().BeTrue();
        result.SyncedCount.Should().Be(5);

        await using var verifyConn = await dbManager.CreateConnectionAsync();
        var syncedCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Synced';");

        syncedCount.Should().Be(5);
    }

    [Fact]
    public async Task FlushPendingQueueAsync_WhenApiNotReady_ReturnsFailureWithoutModifyingDatabase()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 10);

        var sheetsService = CreateMockSheetsService();
        using var queue = new OfflinePlaySyncQueue(
            dbManager,
            sheetsService,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => false);

        var result = await queue.FlushPendingQueueAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not ready");

        await using var verifyConn = await dbManager.CreateConnectionAsync();
        var pendingCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Pending';");
        pendingCount.Should().Be(10);
    }

    [Fact]
    public async Task FlushPendingQueueAsync_WhenNoPendingPlays_ReturnsSuccessWithZeroCount()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();

        var sheetsService = CreateMockSheetsService();
        using var queue = new OfflinePlaySyncQueue(
            dbManager,
            sheetsService,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true);

        var result = await queue.FlushPendingQueueAsync();

        result.IsSuccess.Should().BeTrue();
        result.SyncedCount.Should().Be(0);
    }
}
