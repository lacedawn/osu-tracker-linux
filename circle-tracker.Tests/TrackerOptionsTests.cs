using Circle_Tracker;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using FluentAssertions;
using Moq;
using Xunit;

namespace Circle_Tracker.Tests;

public class TrackerOptionsTests
{
    [Fact]
    public void TrackerOptions_DefaultNullables_AllNullByDefault()
    {
        var mockClient = new Mock<ITosuClient>();

        var options = new TrackerOptions(mockClient.Object);

        new object?[]
        {
            options.PlaySink,
            options.SessionManager,
            options.SheetsSink,
            options.GameStateManager,
            options.BeatmapStateTracker,
            options.PlaySubmissionService,
            options.Settings
        }.Should().OnlyContain(x => x == null);
    }

    [Fact]
    public void TrackerOptions_AllPropertiesSupplied_AllAccessible()
    {
        var mockClient = new Mock<ITosuClient>();
        var mockPlaySink = new Mock<IPlaySink>();
        var mockDbManager = new Mock<IDatabaseManager>();
        var mockSheetsSink = new Mock<ISheetsSink>();
        var mockGameState = new Mock<IGameStateManager>();
        var mockBeatmapState = new Mock<IBeatmapStateTracker>();
        var mockSubmissionService = new Mock<IPlaySubmissionService>();
        var mockSettings = new Mock<ISettingsService>();
        var sessionManager = new SessionManager(mockDbManager.Object);

        var options = new TrackerOptions(
            mockClient.Object,
            mockPlaySink.Object,
            sessionManager,
            mockSheetsSink.Object,
            mockGameState.Object,
            mockBeatmapState.Object,
            mockSubmissionService.Object,
            mockSettings.Object);

        options.Should().Be(new TrackerOptions(
            mockClient.Object,
            mockPlaySink.Object,
            sessionManager,
            mockSheetsSink.Object,
            mockGameState.Object,
            mockBeatmapState.Object,
            mockSubmissionService.Object,
            mockSettings.Object));
    }

    [Fact]
    public void Tracker_ConstructWithTrackerOptions_InitializesWithoutThrowing()
    {
        var mockWindow = new Mock<IMainWindow>();
        var mockClient = new Mock<ITosuClient>();

        var tracker = new Tracker(mockWindow.Object, new TrackerOptions(mockClient.Object));

        tracker.GetSnapshot().Should().NotBeNull();
    }
}
