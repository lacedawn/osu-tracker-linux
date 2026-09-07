using Circle_Tracker;
using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using CircleTracker.Tests;
using Dapper;
using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace circle_tracker.Tests.IntegrationTests;

public class FullPipelinePlayLoggingIntegrationTests
{
    private sealed class PipelineHarness : IAsyncDisposable
    {
        public SqliteDatabaseManager DbManager { get; }
        public LocalSqlitePlaySink SqliteSink { get; }
        public Mock<ITosuClient> TosuClient { get; }
        public Mock<ISheetsSink> SheetsSink { get; }
        public Mock<IPlaySink> SheetsPlaySink { get; }
        public LiveSessionTracker LiveSessionTracker { get; }
        public Tracker Tracker { get; }
        public Task LiveSessionTask { get; set; } = Task.CompletedTask;

        public PipelineHarness(
            SqliteDatabaseManager dbManager,
            LocalSqlitePlaySink sqliteSink,
            Mock<ITosuClient> tosuClient,
            Mock<ISheetsSink> sheetsSink,
            Mock<IPlaySink> sheetsPlaySink,
            LiveSessionTracker liveSessionTracker,
            Tracker tracker)
        {
            DbManager = dbManager;
            SqliteSink = sqliteSink;
            TosuClient = tosuClient;
            SheetsSink = sheetsSink;
            SheetsPlaySink = sheetsPlaySink;
            LiveSessionTracker = liveSessionTracker;
            Tracker = tracker;
        }

        public async ValueTask DisposeAsync()
        {
            await SqliteSink.DisposeAsync();
            await DbManager.DisposeAsync();
        }
    }

    private static async Task<PipelineHarness> CreateHarnessAsync()
    {
        string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var dbManager = new SqliteDatabaseManager(connStr);
        await dbManager.InitializeAsync();

        var sessionManager = new SessionManager(dbManager);
        await sessionManager.InitializeAsync();

        var sqliteSink = new LocalSqlitePlaySink(dbManager);
        await sqliteSink.InitializeAsync(silent: true);

        var mockWindow = new Mock<IMainWindow>();
        mockWindow.Setup(w => w.ShowYesNoDialog(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var mockClient = new Mock<ITosuClient>();
        mockClient.Setup(c => c.IsConnected).Returns(true);
        mockClient.Setup(c => c.CalculatePpAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PpCalcResult?)null);

        var mockSheets = new Mock<ISheetsSink>();
        mockSheets.Setup(s => s.SheetsApiReady).Returns(true);
        mockSheets.Setup(s => s.InitGoogleAPIAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

        var sheetsPlaySink = mockSheets.As<IPlaySink>();
        sheetsPlaySink.Setup(s => s.SinkName).Returns("Google Sheets");
        sheetsPlaySink.Setup(s => s.IsReady).Returns(true);
        sheetsPlaySink.Setup(s => s.InitializeAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sheetsPlaySink.Setup(s => s.TryLogPlayAsync(
                It.IsAny<PlayEntryData>(),
                It.IsAny<PlayContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockSessionAnalytics = new Mock<ISessionAnalyticsService>();
        mockSessionAnalytics.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>());

        var liveSessionTracker = new LiveSessionTracker(mockSessionAnalytics.Object);

        var composite = new CompositePlaySink(new[]
        {
            new SinkRegistration(sqliteSink),
            new SinkRegistration(sheetsPlaySink.Object)
        });

        var tracker = new Tracker(
            mockWindow.Object,
            new TrackerOptions(
                mockClient.Object,
                PlaySink: composite,
                SessionManager: sessionManager,
                SheetsSink: mockSheets.Object));

        await tracker.InitializeStorageAsync(silent: true);

        var harness = new PipelineHarness(
            dbManager,
            sqliteSink,
            mockClient,
            mockSheets,
            sheetsPlaySink,
            liveSessionTracker,
            tracker);

        tracker.PlayLogged += (sender, args) =>
        {
            harness.LiveSessionTask = liveSessionTracker.OnPlayLoggedAsync(args.Data, args.Context);
        };

        return harness;
    }

    [Fact]
    public async Task FullPipeline_PlayInPlayingState_IsLoggedToSqliteAndSheetsAndTriggersLiveSession()
    {
        await using var harness = await CreateHarnessAsync();

        harness.TosuClient.Setup(c => c.LatestState).Returns(StateBuilder.WarmUpPlaying(checksum: "e2e-pipeline"));
        harness.Tracker.Tick();
        harness.TosuClient.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 50, songTimeMs: 30000, checksum: "e2e-pipeline"));
        harness.Tracker.Tick();
        harness.TosuClient.Setup(c => c.LatestState).Returns(StateBuilder.Results(h300: 50, checksum: "e2e-pipeline"));
        harness.Tracker.Tick();

        await harness.Tracker.FlushPendingSubmissionsAsync();
        await harness.LiveSessionTask;

        await using var conn = await harness.DbManager.CreateConnectionAsync();
        int rowCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays;");
        int isComplete = await conn.ExecuteScalarAsync<int>("SELECT is_complete FROM plays;");
        int hit300 = await conn.ExecuteScalarAsync<int>("SELECT hit_300 FROM plays;");

        rowCount.Should().Be(1);
        isComplete.Should().Be(1);
        hit300.Should().Be(50);
        harness.SheetsPlaySink.Verify(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.LiveSessionTracker.GetCurrentMetrics().SessionPlayCount.Should().Be(1);
        harness.LiveSessionTracker.GetCurrentMetrics().SessionPassCount.Should().Be(1);
    }

    [Fact]
    public async Task FullPipeline_ReplayDetected_IsNotLogged()
    {
        await using var harness = await CreateHarnessAsync();

        harness.TosuClient.Setup(c => c.LatestState).Returns(StateBuilder.WarmUpPlaying(checksum: "e2e-replay"));
        harness.Tracker.Tick();
        harness.TosuClient.Setup(c => c.LatestState).Returns(StateBuilder.Playing(
            h300: 50, songTimeMs: 30000, playerName: "someone_else", profileName: "lacedawn", checksum: "e2e-replay"));
        harness.Tracker.Tick();
        harness.TosuClient.Setup(c => c.LatestState).Returns(StateBuilder.Build(
            7, h300: 50, playerName: "someone_else", profileName: "lacedawn", checksum: "e2e-replay"));
        harness.Tracker.Tick();

        await harness.Tracker.FlushPendingSubmissionsAsync();
        await harness.LiveSessionTask;

        await using var conn = await harness.DbManager.CreateConnectionAsync();
        int rowCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays;");

        rowCount.Should().Be(0);
        harness.LiveSessionTracker.GetCurrentMetrics().SessionPlayCount.Should().Be(0);
    }
}
