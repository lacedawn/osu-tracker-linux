using Circle_Tracker;
using Circle_Tracker.Storage;
using Dapper;
using FluentAssertions;
using Moq;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.StorageTests
{
    public class CompositePlaySinkTests
    {
        [Fact]
        public void IsReady_WhenSQLiteReadyAndSheetsNotReady_ReturnsTrue()
        {
            var sqliteSink = new Mock<IPlaySink>();
            sqliteSink.Setup(s => s.IsReady).Returns(true);
            var sheetsSink = new Mock<IPlaySink>();
            sheetsSink.Setup(s => s.IsReady).Returns(false);
            var composite = new CompositePlaySink();
            composite.AddSink(sqliteSink.Object);
            composite.AddSink(sheetsSink.Object);

            var isReady = composite.IsReady;

            isReady.Should().BeTrue();
        }

        [Fact]
        public void IsReady_WhenAllSinksNotReady_ReturnsFalse()
        {
            var sink1 = new Mock<IPlaySink>();
            sink1.Setup(s => s.IsReady).Returns(false);
            var sink2 = new Mock<IPlaySink>();
            sink2.Setup(s => s.IsReady).Returns(false);
            var composite = new CompositePlaySink();
            composite.AddSink(sink1.Object);
            composite.AddSink(sink2.Object);

            var isReady = composite.IsReady;

            isReady.Should().BeFalse();
        }

        [Fact]
        public void AllSinksReady_WhenAllReady_ReturnsTrue()
        {
            var sink1 = new Mock<IPlaySink>();
            sink1.Setup(s => s.IsReady).Returns(true);
            var sink2 = new Mock<IPlaySink>();
            sink2.Setup(s => s.IsReady).Returns(true);
            var composite = new CompositePlaySink();
            composite.AddSink(sink1.Object);
            composite.AddSink(sink2.Object);

            var allReady = composite.AllSinksReady;

            allReady.Should().BeTrue();
        }

        [Fact]
        public void AllSinksReady_WhenOneSinkNotReady_ReturnsFalse()
        {
            var sink1 = new Mock<IPlaySink>();
            sink1.Setup(s => s.IsReady).Returns(true);
            var sink2 = new Mock<IPlaySink>();
            sink2.Setup(s => s.IsReady).Returns(false);
            var composite = new CompositePlaySink();
            composite.AddSink(sink1.Object);
            composite.AddSink(sink2.Object);

            var allReady = composite.AllSinksReady;

            allReady.Should().BeFalse();
        }

        [Fact]
        public void IsReady_DisabledSinksAreIgnored()
        {
            var enabledSink = new Mock<IPlaySink>();
            enabledSink.Setup(s => s.IsReady).Returns(true);
            var disabledSink = new Mock<IPlaySink>();
            disabledSink.Setup(s => s.IsReady).Returns(false);
            var composite = new CompositePlaySink();
            composite.AddSink(enabledSink.Object, () => true);
            composite.AddSink(disabledSink.Object, () => false);

            var isReady = composite.IsReady;
            var allReady = composite.AllSinksReady;

            isReady.Should().BeTrue();
            allReady.Should().BeTrue();
        }

        private static PlayEntryData BuildCompositePlayData(int beatmapId, string clientId = "")
        {
            return new PlayEntryData(
                BeatmapString: "Artist - Title [Hard]",
                BeatmapSetID: 10,
                BeatmapID: beatmapId,
                Hidden: false,
                Hardrock: false,
                Doubletime: false,
                EZ: false,
                Halftime: false,
                Flashlight: false,
                BeatmapBpm: 120,
                BeatmapAim: 2m,
                BeatmapSpeed: 2m,
                BeatmapStars: 3m,
                BeatmapCs: 4m,
                BeatmapAr: 8m,
                BeatmapOd: 7m,
                TotalBeatmapHits: 150,
                Accuracy: 98m,
                Play300c: 140,
                Play100c: 10,
                Play50c: 0,
                PlayMissc: 0,
                Complete: true,
                PlayTimeSeconds: 60,
                ModsString: "",
                PlayCount: 1,
                AccuracyReliable: true,
                ClientId: clientId
            );
        }

        private static PlayContext BuildCompositePlayContext(string sessionId)
        {
            return new PlayContext(
                SessionId: sessionId,
                IsReplay: false,
                RawMods: 0,
                CurrentGameMode: 0,
                DetectedClient: "osu!stable",
                SoundFilePath: null,
                SubmitSoundEnabled: false
            );
        }

        private static (Mock<ISheetsSink> Sheets, Mock<IPlaySink> SheetsPlay) CreateSheetsDouble(bool isReady)
        {
            var sheets = new Mock<ISheetsSink>();
            var sheetsPlay = sheets.As<IPlaySink>();

            sheetsPlay.Setup(s => s.SinkName).Returns("Google Sheets");
            sheetsPlay.Setup(s => s.IsReady).Returns(isReady);

            return (sheets, sheetsPlay);
        }

        [Fact]
        public async Task TryLogPlayAsync_SheetsReturnsFalse_MarksPending()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var (sheets, sheetsPlay) = CreateSheetsDouble(isReady: true);
            sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var composite = new CompositePlaySink();
            composite.AddSink(sqliteSink, () => true);
            composite.AddSink(sheetsPlay.Object, () => true);

            await composite.TryLogPlayAsync(BuildCompositePlayData(701), BuildCompositePlayContext(session.SessionId));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 701;");

            status.Should().Be("Pending");
        }

        [Fact]
        public async Task TryLogPlayAsync_SheetsReturnsTrue_MarksSynced()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var (sheets, sheetsPlay) = CreateSheetsDouble(isReady: true);
            sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var composite = new CompositePlaySink();
            composite.AddSink(sqliteSink, () => true);
            composite.AddSink(sheetsPlay.Object, () => true);

            await composite.TryLogPlayAsync(BuildCompositePlayData(702), BuildCompositePlayContext(session.SessionId));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 702;");

            status.Should().Be("Synced");
        }

        [Fact]
        public async Task TryLogPlayAsync_SheetsThrows_MarksPending()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var (sheets, sheetsPlay) = CreateSheetsDouble(isReady: true);
            sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Google Sheets network timeout"));

            var composite = new CompositePlaySink();
            composite.AddSink(sqliteSink, () => true);
            composite.AddSink(sheetsPlay.Object, () => true);

            await composite.TryLogPlayAsync(BuildCompositePlayData(703), BuildCompositePlayContext(session.SessionId));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 703;");

            status.Should().Be("Pending");
        }

        private static async Task<bool> SlowSheetsResponseAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return false;
        }

        [Fact]
        public async Task TryLogPlayAsync_SheetsSlow_SQLiteStillWritesFast()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var (sheets, sheetsPlay) = CreateSheetsDouble(isReady: true);
            sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                .Returns(SlowSheetsResponseAsync);

            var composite = new CompositePlaySink { SheetsAttemptTimeout = TimeSpan.FromMilliseconds(200) };
            composite.AddSink(sqliteSink, () => true);
            composite.AddSink(sheetsPlay.Object, () => true);
            var stopwatch = Stopwatch.StartNew();

            await composite.TryLogPlayAsync(BuildCompositePlayData(704), BuildCompositePlayContext(session.SessionId));

            stopwatch.Stop();

            await using var conn = await dbManager.CreateConnectionAsync();
            int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays WHERE beatmap_id = 704;");

            count.Should().Be(1);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task TryLogPlayAsync_NoSheetsSink_MarksSyncedNotPending()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var composite = new CompositePlaySink();
            composite.AddSink(sqliteSink, () => true);

            await composite.TryLogPlayAsync(BuildCompositePlayData(705), BuildCompositePlayContext(session.SessionId));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 705;");

            status.Should().Be("Synced");
        }

        [Fact]
        public async Task Submit_WhenSheetsConfiguredButNotReady_RowStaysPending()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var (sheets, sheetsPlay) = CreateSheetsDouble(isReady: false);
            sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var composite = new CompositePlaySink();
            composite.AddSink(sqliteSink, () => true);
            composite.AddSink(sheetsPlay.Object, () => true);

            await composite.TryLogPlayAsync(BuildCompositePlayData(706), BuildCompositePlayContext(session.SessionId));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 706;");

            status.Should().Be("Pending");
        }

        [Fact]
        public async Task Submit_WhenSheetsSucceeds_RowMarkedSynced()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var (sheets, sheetsPlay) = CreateSheetsDouble(isReady: true);
            sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var composite = new CompositePlaySink();
            composite.AddSink(sqliteSink, () => true);
            composite.AddSink(sheetsPlay.Object, () => true);

            await composite.TryLogPlayAsync(BuildCompositePlayData(707), BuildCompositePlayContext(session.SessionId));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 707;");

            status.Should().Be("Synced");
        }

        [Fact]
        public async Task Submit_SameClientIdTwice_SecondAppendSkipped()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var (sheets, sheetsPlay) = CreateSheetsDouble(isReady: true);
            sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var composite = new CompositePlaySink();
            composite.AddSink(sqliteSink, () => true);
            composite.AddSink(sheetsPlay.Object, () => true);
            var data = BuildCompositePlayData(708, clientId: "idem-client-1");

            await composite.TryLogPlayAsync(data, BuildCompositePlayContext(session.SessionId));

            await composite.TryLogPlayAsync(data, BuildCompositePlayContext(session.SessionId));

            sheets.Verify(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        private static async Task<bool> GatedSheetsAppendAsync(TaskCompletionSource<bool> entered, TaskCompletionSource<bool> release)
        {
            entered.TrySetResult(true);

            await release.Task;

            return true;
        }

        private static async Task<bool> CancelHonouringSheetsAppendAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);

            return true;
        }

        [Fact]
        public async Task Timeout_WhenContinuationSucceeds_ReportsSingleSuccess()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var (sheets, sheetsPlay) = CreateSheetsDouble(isReady: true);
            sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => GatedSheetsAppendAsync(entered, release));

            var composite = new CompositePlaySink { SheetsAttemptTimeout = TimeSpan.Zero };
            composite.AddSink(sqliteSink, () => true);
            composite.AddSink(sheetsPlay.Object, () => true);

            Task submit = composite.TryLogPlayAsync(BuildCompositePlayData(709, clientId: "timeout-client-1"), BuildCompositePlayContext(session.SessionId));

            await entered.Task;

            release.TrySetResult(true);

            await submit;

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 709;");

            status.Should().Be("Synced");
            sheets.Verify(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Timeout_WhenSheetsHonoursCancellation_ReportsPending()
        {
            string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var (sheets, sheetsPlay) = CreateSheetsDouble(isReady: true);
            sheets.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                .Returns((PlayEntryData _, PlayContext _, CancellationToken ct) => CancelHonouringSheetsAppendAsync(ct));

            var composite = new CompositePlaySink { SheetsAttemptTimeout = TimeSpan.Zero };
            composite.AddSink(sqliteSink, () => true);
            composite.AddSink(sheetsPlay.Object, () => true);

            await composite.TryLogPlayAsync(BuildCompositePlayData(710, clientId: "timeout-client-2"), BuildCompositePlayContext(session.SessionId));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 710;");

            status.Should().Be("Pending");
        }
    }
}
