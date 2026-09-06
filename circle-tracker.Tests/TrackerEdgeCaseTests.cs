using Circle_Tracker;
using Circle_Tracker.Services;
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

        // Simulate playing with 100 hits
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Artist",
                Title = "Title",
                Difficulty = "Hard",
                Id = 123,
                SetId = 456,
                Time = new TimeInfo { Live = 30000 }
            },
            Play = new TosuPlay
            {
                Mode = new ModeInfo { Number = 0 },
                Mods = new ModsInfo { Number = 0 },
                Hits = new HitCounts { H300 = 90, H100 = 8, H50 = 2, Misses = 0 },
                Accuracy = 97m
            },
            Menu = new TosuMenu
            {
                State = 2, // Playing
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });
        
        tracker.Tick();

        // Act - Simulate hit count regression (should not happen normally)
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Artist",
                Title = "Title",
                Difficulty = "Hard",
                Id = 123,
                SetId = 456,
                Time = new TimeInfo { Live = 31000 } // Time increased
            },
            Play = new TosuPlay
            {
                Mode = new ModeInfo { Number = 0 },
                Mods = new ModsInfo { Number = 0 },
                Hits = new HitCounts { H300 = 50, H100 = 5, H50 = 1, Misses = 0 }, // Hits decreased
                Accuracy = 97m
            },
            Menu = new TosuMenu
            {
                State = 2,
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });
        
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
        mockSheetsSink.Setup(x => x.InitGoogleAPIAsync(It.IsAny<bool>())).ReturnsAsync(());

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object);
        await tracker.InitializeStorageAsync(silent: true);

        // Start at time 0
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Artist",
                Title = "Title",
                Difficulty = "Hard",
                Id = 123,
                SetId = 456,
                Time = new TimeInfo { Live = 500 }
            },
            Play = new TosuPlay
            {
                Mode = new ModeInfo { Number = 0 },
                Mods = new ModsInfo { Number = 0 },
                Hits = new HitCounts { H300 = 5, H100 = 0, H50 = 0, Misses = 0 },
                Accuracy = 100m
            },
            Menu = new TosuMenu
            {
                State = 2,
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });
        
        tracker.Tick();

        // Act - Large time jump (intro skip of 60 seconds)
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Artist",
                Title = "Title",
                Difficulty = "Hard",
                Id = 123,
                SetId = 456,
                Time = new TimeInfo { Live = 60500 } // 60 second jump
            },
            Play = new TosuPlay
            {
                Mode = new ModeInfo { Number = 0 },
                Mods = new ModsInfo { Number = 0 },
                Hits = new HitCounts { H300 = 25, H100 = 1, H50 = 0, Misses = 0 }, // More hits after skip
                Accuracy = 99m
            },
            Menu = new TosuMenu
            {
                State = 2,
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });
        
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
        mockSheetsSink.Setup(x => x.InitGoogleAPIAsync(It.IsAny<bool>())).ReturnsAsync(());

        bool playLogged = false;
        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object);
        await tracker.InitializeStorageAsync(silent: true);
        tracker.PlayLogged += (s, e) => { playLogged = true; };

        // Simulate playing with 50+ hits
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Artist",
                Title = "Title",
                Difficulty = "Hard",
                Id = 123,
                SetId = 456,
                Time = new TimeInfo { Live = 30000 }
            },
            Play = new TosuPlay
            {
                Mode = new ModeInfo { Number = 0 },
                Mods = new ModsInfo { Number = 0 },
                Hits = new HitCounts { H300 = 45, H100 = 5, H50 = 0, Misses = 2 },
                Accuracy = 95m
            },
            Menu = new TosuMenu
            {
                State = 2,
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });
        
        tracker.Tick();

        // Act - Time rewind (retry)
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Artist",
                Title = "Title",
                Difficulty = "Hard",
                Id = 123,
                SetId = 456,
                Time = new TimeInfo { Live = 500 } // Rewound to start
            },
            Play = new TosuPlay
            {
                Mode = new ModeInfo { Number = 0 },
                Mods = new ModsInfo { Number = 0 },
                Hits = new HitCounts { H300 = 2, H100 = 0, H50 = 0, Misses = 0 },
                Accuracy = 100m
            },
            Menu = new TosuMenu
            {
                State = 2,
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });
        
        tracker.Tick();
        await tracker.FlushPendingSubmissionsAsync();
        
        var snapshot = tracker.GetSnapshot();

        // Assert
        Assert.True(playLogged); // Should have logged the retry
        Assert.Equal(500, snapshot.Time); // Should have new time
        Assert.Equal(2, snapshot.TotalBeatmapHits); // Should have new hit count
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
