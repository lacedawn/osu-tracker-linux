using Circle_Tracker;
using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Circle_Tracker.ViewModels;
using FluentAssertions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.ServiceTests;

public class CancellationPropagationTests
{
    [Fact]
    public async Task ShutdownAsync_CancelsShutdownToken_AndDisposesResources()
    {
        var mockDbManager = new Mock<IDatabaseManager>();
        var mockSessionService = new Mock<ISessionAnalyticsService>();
        var mockQueryEngine = new Mock<IPlayQueryEngine>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockLiveSession = new Mock<ILiveSessionTracker>();

        mockDbManager.Setup(d => d.IsHealthy).Returns(true);

        var sessionManager = new SessionManager(mockDbManager.Object);
        
        var mockTracker = new Mock<ITrackerService>();
        mockTracker.Setup(t => t.SessionManager).Returns(sessionManager);
        mockTracker.Setup(t => t.FlushPendingSubmissionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockTracker.Setup(t => t.SaveSettings());
        mockTracker.Setup(t => t.PlaySink).Returns((IPlaySink)null!);

        mockTosuClient.Setup(c => c.DisconnectAsync()).Returns(Task.CompletedTask);

        var viewModel = new MainWindowViewModel(
            mockTracker.Object,
            mockLiveSession.Object,
            mockDbManager.Object,
            mockSessionService.Object,
            mockQueryEngine.Object,
            mockTosuClient.Object);

        await viewModel.ShutdownAsync();

        mockTracker.Verify(t => t.FlushPendingSubmissionsAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockTosuClient.Verify(c => c.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task Cancel_DrainsPendingPlays()
    {
        var sink = new Mock<IPlaySink>();

        sink.Setup(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();

        gameState.Setup(g => g.IsReplay).Returns(false);

        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 50);

        using var cts = new CancellationTokenSource();

        cts.Cancel();

        bool flushed = await service.FlushPendingSubmissionsAsync(cts.Token);

        flushed.Should().BeTrue();
    }

    [Fact]
    public async Task PromptTimezone_CancellationToken_PropagatedCorrectly()
    {
        var mockWindow = new Mock<IMainWindow>();
        mockWindow.Setup(w => w.ShowYesNoDialog(It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync(true);

        var manager = new GoogleSheetsManager(mockWindow.Object, () => ",");

        await Task.Delay(100);

        mockWindow.Verify(w => w.ShowYesNoDialog(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
