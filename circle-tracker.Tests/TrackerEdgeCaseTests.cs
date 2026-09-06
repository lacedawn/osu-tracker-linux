using Circle_Tracker;
using Circle_Tracker.Services;
using CircleTracker.Tests;
using Moq;
using System.Threading.Tasks;
using Xunit;

namespace circle_tracker.Tests;

public class TrackerEdgeCaseTests
{
    [Fact]
    public void Tracker_DisconnectedTosuClient_HandlesGracefully()
    {
        // Arrange
        var mockForm = new Mock<IMainWindow>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockSheetsSink = new Mock<ISheetsSink>();
        
        mockTosuClient.Setup(x => x.IsConnected).Returns(false);
        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(false);

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object);

        // Act
        tracker.Tick();
        var snapshot = tracker.GetSnapshot();

        // Assert
        Assert.Equal("Disconnected", snapshot.DetectedClient);
        Assert.False(snapshot.IsPlaying);
    }

    [Fact]
    public void Tracker_NullLatestState_HandlesGracefully()
    {
        // Arrange
        var mockForm = new Mock<IMainWindow>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockSheetsSink = new Mock<ISheetsSink>();
        
        mockTosuClient.Setup(x => x.IsConnected).Returns(true);
        mockTosuClient.Setup(x => x.LatestState).Returns((TosuState?)null);
        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(false);

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object);

        // Act
        tracker.Tick();
        var snapshot = tracker.GetSnapshot();

        // Assert
        Assert.Equal("Connecting...", snapshot.DetectedClient);
        Assert.False(snapshot.IsPlaying);
    }

    [Fact]
    public void Tracker_HitCountRegression_LogsWarning()
    {
        // Arrange
        var mockForm = new Mock<IMainWindow>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockSheetsSink = new Mock<ISheetsSink>();
        
        mockTosuClient.Setup(x => x.IsConnected).Returns(true);
        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(false);

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object);

        // Warm up first playing tick
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.WarmUpPlaying());
        tracker.Tick();

        // Simulate playing with 100 hits
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Playing(
            h300: 90, h100: 8, h50: 2, misses: 0, songTimeMs: 30000, accuracy: 97m));
        tracker.Tick();

        // Act - Simulate hit count regression (should not happen normally)
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Playing(
            h300: 50, h100: 5, h50: 1, misses: 0, songTimeMs: 31000, accuracy: 97m));
        tracker.Tick();
        var snapshot = tracker.GetSnapshot();

        // Assert - Tracker should not update hits when they regress without time rewind
        Assert.Equal(100, snapshot.TotalBeatmapHits); // Should remain at previous value
    }

    [Fact]
    public async Task Tracker_LargeTimeJump_HandlesIntroSkip()
    {
        // Arrange
        var mockForm = new Mock<IMainWindow>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockSheetsSink = new Mock<ISheetsSink>();
        
        mockTosuClient.Setup(x => x.IsConnected).Returns(true);
        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(false);
        mockSheetsSink.Setup(x => x.InitGoogleAPIAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object);
        await tracker.InitializeStorageAsync(silent: true);

        // Warm up first playing tick
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.WarmUpPlaying());
        tracker.Tick();

        // Start at time 500
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Playing(
            h300: 5, h100: 0, h50: 0, misses: 0, songTimeMs: 500, accuracy: 100m));
        tracker.Tick();

        // Act - Large time jump (intro skip of 60 seconds)
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Playing(
            h300: 25, h100: 1, h50: 0, misses: 0, songTimeMs: 60500, accuracy: 99m));
        tracker.Tick();
        var snapshot = tracker.GetSnapshot();

        // Assert - Should accept the new hit count despite large time delta
        Assert.Equal(26, snapshot.TotalBeatmapHits);
        Assert.Equal(60500, snapshot.Time);
    }

    [Fact]
    public async Task Tracker_TimeRewind_SubmitsRetryAndResets()
    {
        // Arrange
        var mockForm = new Mock<IMainWindow>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockSheetsSink = new Mock<ISheetsSink>();
        
        mockTosuClient.Setup(x => x.IsConnected).Returns(true);
        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(false);
        mockSheetsSink.Setup(x => x.InitGoogleAPIAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

        bool playLogged = false;
        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object);
        await tracker.InitializeStorageAsync(silent: true);
        tracker.PlayLogged += (s, e) => { playLogged = true; };

        // Warm up first playing tick
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.WarmUpPlaying());
        tracker.Tick();

        // Simulate playing with 50+ hits
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Playing(
            h300: 45, h100: 5, h50: 0, misses: 2, songTimeMs: 30000, accuracy: 95m));
        tracker.Tick();

        // Act - Time rewind (retry)
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Playing(
            h300: 2, h100: 0, h50: 0, misses: 0, songTimeMs: 500, accuracy: 100m));
        tracker.Tick();
        await tracker.FlushPendingSubmissionsAsync();
        
        var snapshot = tracker.GetSnapshot();

        // Assert
        Assert.True(playLogged); // Should have logged the retry
        Assert.Equal(500, snapshot.Time); // Should have new time
        Assert.Equal(0, snapshot.TotalBeatmapHits); // Should have reset hit count on retry
    }

    [Fact]
    public void Tracker_TickWrapper_PreventsReentrantCalls()
    {
        // Arrange
        var mockForm = new Mock<IMainWindow>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockSheetsSink = new Mock<ISheetsSink>();
        
        mockTosuClient.Setup(x => x.IsConnected).Returns(false);
        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(false);

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object);

        // Act - Call TickWrapper multiple times rapidly
        tracker.TickWrapper();
        tracker.TickWrapper();
        tracker.TickWrapper();

        // Assert - Should not throw or deadlock
        var snapshot = tracker.GetSnapshot();
        Assert.NotNull(snapshot);
    }
}
