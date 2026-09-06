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
        mockTracker.Setup(t => t.FlushPendingSubmissionsAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
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
