using Circle_Tracker;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Circle_Tracker.Sync;
using Dapper;
using FluentAssertions;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.ServiceTests;

public class TrackerServiceOfflineSyncTests
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
            BeatmapId = 20000 + i,
            BeatmapSetId = 2000 + i
        });

        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(insertSql, plays, tx);
        tx.Commit();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(10).ConfigureAwait(false);
        }

        return condition();
    }

    private static TrackerService CreateService(
        SqliteDatabaseManager dbManager,
        Mock<ISheetsSink> sheetsSink,
        Func<IOfflinePlaySyncQueue>? syncQueueFactory = null)
    {
        var mockTosuClient = new Mock<ITosuClient>();
        var mockPlaySink = new Mock<IPlaySink>();
        var sessionManager = new SessionManager(dbManager);
        var settings = new SettingsService();

        return new TrackerService(
            mockTosuClient.Object,
            mockPlaySink.Object,
            sessionManager,
            sheetsSink.Object,
            settings,
            syncQueueFactory);
    }

    [Fact]
    public async Task InitGoogleAPIAsync_WhenSheetsReady_StartsBackgroundSync()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        var mockSheetsSink = new Mock<ISheetsSink>();
        mockSheetsSink.Setup(s => s.SheetsApiReady).Returns(true);
        mockSheetsSink.Setup(s => s.InitGoogleAPIAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

        var mockQueue = new Mock<IOfflinePlaySyncQueue>();
        var service = CreateService(dbManager, mockSheetsSink, () => mockQueue.Object);

        await service.InitGoogleAPIAsync(silent: true);

        mockQueue.Verify(q => q.StartBackgroundSync(TrackerService.OfflineSyncInterval), Times.Once);
    }

    [Fact]
    public async Task RefreshOfflineSyncState_WhenSheetsNotReady_DetachesQueue()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        bool sheetsReady = true;
        var mockSheetsSink = new Mock<ISheetsSink>();
        mockSheetsSink.Setup(s => s.SheetsApiReady).Returns(() => sheetsReady);

        bool stopped = false;
        var mockQueue = new Mock<IOfflinePlaySyncQueue>();
        mockQueue.Setup(q => q.StopBackgroundSyncAsync()).Callback(() => stopped = true).Returns(Task.CompletedTask);

        var service = CreateService(dbManager, mockSheetsSink, () => mockQueue.Object);

        service.RefreshOfflineSyncState();

        sheetsReady = false;

        service.RefreshOfflineSyncState();

        await WaitUntilAsync(() => Volatile.Read(ref stopped), TimeSpan.FromSeconds(5));

        Volatile.Read(ref stopped).Should().BeTrue();
    }

    [Fact]
    public async Task FlushOfflineSyncAsync_WithActiveQueue_DelegatesFlush()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        var mockSheetsSink = new Mock<ISheetsSink>();
        mockSheetsSink.Setup(s => s.SheetsApiReady).Returns(true);

        var mockQueue = new Mock<IOfflinePlaySyncQueue>();
        mockQueue.Setup(q => q.FlushPendingQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(true, 3, 0, null));
        var service = CreateService(dbManager, mockSheetsSink, () => mockQueue.Object);

        service.RefreshOfflineSyncState();

        await service.FlushOfflineSyncAsync();

        mockQueue.Verify(q => q.FlushPendingQueueAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingSyncCountAsync_WithActiveQueue_ReturnsQueueCount()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        var mockSheetsSink = new Mock<ISheetsSink>();
        mockSheetsSink.Setup(s => s.SheetsApiReady).Returns(true);

        var mockQueue = new Mock<IOfflinePlaySyncQueue>();
        mockQueue.Setup(q => q.GetPendingCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        var service = CreateService(dbManager, mockSheetsSink, () => mockQueue.Object);

        service.RefreshOfflineSyncState();

        int count = await service.GetPendingSyncCountAsync();

        count.Should().Be(7);
    }

    [Fact]
    public async Task GetPendingSyncCountAsync_WithoutQueue_CountsDatabaseRows()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        await SeedPendingPlaysAsync(dbManager, 4);

        var mockSheetsSink = new Mock<ISheetsSink>();
        mockSheetsSink.Setup(s => s.SheetsApiReady).Returns(false);
        var service = CreateService(dbManager, mockSheetsSink);

        int count = await service.GetPendingSyncCountAsync();

        count.Should().Be(4);
    }

    [Fact]
    public async Task StopOfflineSyncAsync_WithActiveQueue_StopsAndFlushes()
    {
        using var dbManager = await CreateInitializedDbManagerAsync();
        var mockSheetsSink = new Mock<ISheetsSink>();
        mockSheetsSink.Setup(s => s.SheetsApiReady).Returns(true);

        var mockQueue = new Mock<IOfflinePlaySyncQueue>();
        mockQueue.Setup(q => q.StopBackgroundSyncAsync()).Returns(Task.CompletedTask);
        mockQueue.Setup(q => q.FlushPendingQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(true, 0, 0, null));
        var service = CreateService(dbManager, mockSheetsSink, () => mockQueue.Object);

        service.RefreshOfflineSyncState();

        await service.StopOfflineSyncAsync();

        mockQueue.Verify(q => q.FlushPendingQueueAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
