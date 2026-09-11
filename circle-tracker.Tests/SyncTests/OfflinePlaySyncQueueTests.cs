using Circle_Tracker;
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
    public async Task FlushPending_LargeBatch_ChunksCorrectly()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 120);

        int appendCallCount = 0;
        var sheetsAppender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) =>
        {
            appendCallCount++;
            rows.Count.Should().BeLessOrEqualTo(50);
            return Task.CompletedTask;
        });

        using var queue = new OfflinePlaySyncQueue(
            dbManager,
            null,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true,
            sheetsAppender);

        var result = await queue.FlushPendingQueueAsync();

        result.IsSuccess.Should().BeTrue();
        result.SyncedCount.Should().Be(120);
        appendCallCount.Should().Be(3);

        await using var verifyConn = await dbManager.CreateConnectionAsync();
        var syncedCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Synced';");
        syncedCount.Should().Be(120);
    }

    [Fact]
    public async Task FlushPending_PartialBatchFailure_PreviousBatchesSynced()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 120);

        int appendCallCount = 0;
        var sheetsAppender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) =>
        {
            appendCallCount++;
            if (appendCallCount == 2)
            {
                throw new Exception("Simulated Sheets API failure");
            }
            return Task.CompletedTask;
        });

        using var queue = new OfflinePlaySyncQueue(
            dbManager,
            null,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true,
            sheetsAppender);

        var result = await queue.FlushPendingQueueAsync();

        result.IsSuccess.Should().BeTrue();
        result.SyncedCount.Should().Be(50);

        await using var verifyConn = await dbManager.CreateConnectionAsync();
        var syncedCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Synced';");
        var pendingCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Pending';");

        syncedCount.Should().Be(50);
        pendingCount.Should().Be(70);
    }

    [Fact]
    public async Task FlushPending_NoPendingPlays_ReturnsSuccessWithZeroCount()
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

    [Fact]
    public async Task FlushPending_SheetsNotReady_ReturnsFailure()
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
    public async Task StopBackgroundSync_GracefulShutdown_CompletesCleanly()
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

        queue.StartBackgroundSync(TimeSpan.FromSeconds(10));

        await Task.Delay(100);

        queue.StopBackgroundSync();

        await Task.Delay(100);
    }

    [Fact]
    public async Task BackgroundSync_FindsPendingPlays_FlushesAutomatically()
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

        await SeedPendingPlaysAsync(dbManager, 5);

        queue.StartBackgroundSync(TimeSpan.FromMilliseconds(200));

        await Task.Delay(500);

        await using var verifyConn = await dbManager.CreateConnectionAsync();
        var syncedCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Synced';");

        queue.StopBackgroundSync();

        syncedCount.Should().Be(5);
    }

    [Fact]
    public async Task Dispose_StopsBackgroundSync()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();

        var sheetsService = CreateMockSheetsService();
        var queue = new OfflinePlaySyncQueue(
            dbManager,
            sheetsService,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true);

        queue.StartBackgroundSync(TimeSpan.FromSeconds(10));

        await Task.Delay(100);

        queue.Dispose();

        await Task.Delay(100);
    }

    [Fact]
    public async Task FlushPendingQueueAsync_WithLargeBatch_UpdatesAllRowsInBatches()
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
    public async Task FlushPendingQueueAsync_WhenDatabaseFailsMidBatch_PreviousBatchesStillSynced()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 120);

        int appendCallCount = 0;
        var sheetsAppender = new Func<IList<IList<object>>, CancellationToken, Task>(async (rows, ct) =>
        {
            appendCallCount++;
            if (appendCallCount == 2)
            {
                await using var triggerConn = await dbManager.CreateConnectionAsync();
                await triggerConn.ExecuteAsync(@"
                    CREATE TRIGGER fail_mid_batch
                    BEFORE UPDATE ON plays
                    WHEN NEW.sync_status = 'Synced'
                    BEGIN
                        SELECT RAISE(ABORT, 'Simulated database disruption mid batch');
                    END;");
            }
        });

        using var queue = new OfflinePlaySyncQueue(
            dbManager,
            null,
            "test-spreadsheet-id",
            "Sheet1",
            () => ",",
            () => true,
            sheetsAppender);

        var result = await queue.FlushPendingQueueAsync();

        result.IsSuccess.Should().BeTrue();
        result.SyncedCount.Should().Be(50);

        await using var verifyConn = await dbManager.CreateConnectionAsync();
        var syncedCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Synced';");
        var pendingCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Pending';");

        syncedCount.Should().Be(50);
        pendingCount.Should().Be(70);
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
    private static async Task SeedPlayWithModsAsync(SqliteDatabaseManager dbManager, int modsBitfield)
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
                @SessionId, @Timestamp, 20001, 2001, '',
                'Mod Test Map', '', '', '',
                @ModsBitfield, 'NM', 180, 5.5, 2.5, 2.8, 4.0, 9.0, 8.0, 6.0,
                500, 450, 40, 10, 0, 98.5, 1,
                1, 120, 1, 0, 0, 'Test',
                'Pending'
            );";

        await conn.ExecuteAsync(insertSql, new { SessionId = sessionId, Timestamp = timestampStr, ModsBitfield = modsBitfield });
    }

    [Fact]
    public async Task BuildRowData_HiddenModSet_RowContainsHdFlag()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPlayWithModsAsync(dbManager, (int)Circle_Tracker.OsuMods.Hidden);

        IList<object>? capturedRow = null;
        var appender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) =>
        {
            capturedRow = rows[0];
            return Task.CompletedTask;
        });

        using var queue = new OfflinePlaySyncQueue(dbManager, null, "id", "Sheet1", () => ",", () => true, appender);

        await queue.FlushPendingQueueAsync();

        capturedRow.Should().NotBeNull();
        capturedRow![2].Should().Be("1");
    }

    [Fact]
    public async Task BuildRowData_NoModsSet_AllModColumnsEmpty()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPlayWithModsAsync(dbManager, 0);

        IList<object>? capturedRow = null;
        var appender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) =>
        {
            capturedRow = rows[0];
            return Task.CompletedTask;
        });

        using var queue = new OfflinePlaySyncQueue(dbManager, null, "id", "Sheet1", () => ",", () => true, appender);

        await queue.FlushPendingQueueAsync();

        capturedRow.Should().NotBeNull();
        capturedRow![2].Should().Be("");
        capturedRow[3].Should().Be("");
        capturedRow[4].Should().Be("");
        capturedRow[18].Should().Be("");
        capturedRow[19].Should().Be("");
        capturedRow[20].Should().Be("");
    }

    [Fact]
    public async Task BuildRowData_MultipleModsSet_CorrectFlags()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        int bitfield = (int)(Circle_Tracker.OsuMods.Hidden | Circle_Tracker.OsuMods.HardRock | Circle_Tracker.OsuMods.DoubleTime);
        await SeedPlayWithModsAsync(dbManager, bitfield);

        IList<object>? capturedRow = null;
        var appender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) =>
        {
            capturedRow = rows[0];
            return Task.CompletedTask;
        });

        using var queue = new OfflinePlaySyncQueue(dbManager, null, "id", "Sheet1", () => ",", () => true, appender);

        await queue.FlushPendingQueueAsync();

        capturedRow.Should().NotBeNull();
        capturedRow![2].Should().Be("1");
        capturedRow[3].Should().Be("1");
        capturedRow[4].Should().Be("1");
        capturedRow[18].Should().Be("");
        capturedRow[19].Should().Be("");
        capturedRow[20].Should().Be("");
    }

    [Fact]
    public async Task BuildRowData_NightcoreSet_DtColumnIsEmpty()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPlayWithModsAsync(dbManager, (int)Circle_Tracker.OsuMods.Nightcore);

        IList<object>? capturedRow = null;
        var appender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) =>
        {
            capturedRow = rows[0];
            return Task.CompletedTask;
        });

        using var queue = new OfflinePlaySyncQueue(dbManager, null, "id", "Sheet1", () => ",", () => true, appender);

        await queue.FlushPendingQueueAsync();

        capturedRow.Should().NotBeNull();
        capturedRow![4].Should().Be("");
    }

    private static PlayEntryData BuildReconnectPlayData(int beatmapId)
    {
        return new PlayEntryData(
            BeatmapString: "Song [Hard]", BeatmapSetID: 1, BeatmapID: beatmapId,
            Hidden: false, Hardrock: false, Doubletime: false, EZ: false, Halftime: false, Flashlight: false,
            BeatmapBpm: 120, BeatmapAim: 1m, BeatmapSpeed: 1m, BeatmapStars: 3m,
            BeatmapCs: 4m, BeatmapAr: 8m, BeatmapOd: 7m,
            TotalBeatmapHits: 100, Accuracy: 98m,
            Play300c: 100, Play100c: 0, Play50c: 0, PlayMissc: 0,
            Complete: true, PlayTimeSeconds: 60, ModsString: "", PlayCount: 1, AccuracyReliable: true
        );
    }

    private static async Task LogPlayWithSheetsFailingAsync(SqliteDatabaseManager dbManager, int beatmapId)
    {
        var session = new SessionManager(dbManager);
        await session.InitializeAsync();
        await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
        await sqliteSink.InitializeAsync();
        var sheets = new Mock<ISheetsSink>();
        var sheetsPlay = sheets.As<IPlaySink>();
        sheetsPlay.Setup(s => s.SinkName).Returns("Google Sheets");
        sheetsPlay.Setup(s => s.IsReady).Returns(true);
        sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var composite = new CompositePlaySink();
        composite.AddSink(sqliteSink, () => true);
        composite.AddSink(sheetsPlay.Object, () => true);
        var context = new PlayContext(session.SessionId, false, 0, 0, "test", null, false);

        await composite.TryLogPlayAsync(BuildReconnectPlayData(beatmapId), context);
    }

    [Fact]
    public async Task EndToEnd_PlayLoggedWhileSheetsSyncFails_AppearsInPendingQueue()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();

        await LogPlayWithSheetsFailingAsync(dbManager, 910);

        using var queue = new OfflinePlaySyncQueue(dbManager, null, "id", "Sheet1", () => ",", () => false, null);
        int pending = await queue.GetPendingCountAsync();

        pending.Should().Be(1);
    }

    [Fact]
    public async Task EndToEnd_PendingPlay_FlushesOnReconnectAndMarksSynced()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await LogPlayWithSheetsFailingAsync(dbManager, 911);
        var appender = new Func<IList<IList<object>>, CancellationToken, Task>((rows, ct) => Task.CompletedTask);
        using var queue = new OfflinePlaySyncQueue(dbManager, null, "id", "Sheet1", () => ",", () => true, appender);

        var result = await queue.FlushPendingQueueAsync();

        result.SyncedCount.Should().Be(1);
    }

    private sealed class ScriptedQueueHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _script;
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ScriptedQueueHandler(Func<int, HttpResponseMessage> script) => _script = script;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _calls);
            return Task.FromResult(_script(call));
        }
    }

    private sealed class ScriptedQueueClientFactory : Google.Apis.Http.HttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public ScriptedQueueClientFactory(HttpMessageHandler handler) => _handler = handler;

        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => _handler;
    }

    private static SheetsService CreateScriptedQueueSheetsService(ScriptedQueueHandler handler)
    {
        return new SheetsService(new BaseClientService.Initializer
        {
            HttpClientFactory = new ScriptedQueueClientFactory(handler),
            ApplicationName = "CircleTrackerTests"
        });
    }

    private static HttpResponseMessage QueueErrorResponse(HttpStatusCode status, int code)
    {
        return new HttpResponseMessage
        {
            StatusCode = status,
            Content = new StringContent(
                $"{{\"error\":{{\"code\":{code},\"message\":\"Sheets failure\",\"status\":\"ERROR\"}}}}",
                Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage QueueAppendSuccessResponse()
    {
        return new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(
                "{\"spreadsheetId\":\"test-id\",\"tableRange\":\"Sheet1!A1:X1\",\"updates\":{}}",
                Encoding.UTF8,
                "application/json")
        };
    }

    [Fact]
    public async Task FlushPendingQueueAsync_Transient503_RetriesBatch()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 5);
        var handler = new ScriptedQueueHandler(call => call == 1
            ? QueueErrorResponse(HttpStatusCode.ServiceUnavailable, 503)
            : QueueAppendSuccessResponse());
        var sheetsService = CreateScriptedQueueSheetsService(handler);
        using var queue = new OfflinePlaySyncQueue(dbManager, sheetsService, "id", "Sheet1", () => ",", () => true);
        queue.RetryDelayProvider = (_, _) => Task.CompletedTask;

        var result = await queue.FlushPendingQueueAsync();

        result.SyncedCount.Should().Be(5);
    }

    [Fact]
    public async Task FlushPendingQueueAsync_Permanent400_SkipsBatchAndContinues()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 60);
        var handler = new ScriptedQueueHandler(call => call == 1
            ? QueueErrorResponse(HttpStatusCode.BadRequest, 400)
            : QueueAppendSuccessResponse());
        var sheetsService = CreateScriptedQueueSheetsService(handler);
        using var queue = new OfflinePlaySyncQueue(dbManager, sheetsService, "id", "Sheet1", () => ",", () => true);
        queue.RetryDelayProvider = (_, _) => Task.CompletedTask;

        var result = await queue.FlushPendingQueueAsync();

        result.SyncedCount.Should().Be(10);
    }
}
