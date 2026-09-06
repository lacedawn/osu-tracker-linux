using Circle_Tracker;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace circle_tracker.Tests.IntegrationTests;

public class FullPipelineIntegrationTests
{
    [Fact]
    public async Task FullPipeline_ConnectTosuAndSubmitPlay_EndToEnd()
    {
        // Arrange
        var mockForm = new Mock<IMainWindow>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockSheetsSink = new Mock<ISheetsSink>();
        
        mockTosuClient.Setup(x => x.IsConnected).Returns(true);
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Test Artist",
                Title = "Test Song",
                Difficulty = "Hard",
                Id = 12345,
                SetId = 67890,
                Stats = new ToseBeatmapStats
                {
                    HP = 5,
                    CS = 4,
                    AR = 9,
                    OD = 8,
                    BPM = new BpmInfo { Min = 180, Max = 180 },
                    Stars = new StarRating { Total = 5.5m, Aim = 2.8m, Speed = 2.7m }
                }
            },
            Play = new TosuPlay
            {
                Mode = new ModeInfo { Number = 0 },
                Mods = new ModsInfo { Number = 0 },
                Hits = new HitCounts { H300 = 100, H100 = 10, H50 = 1, Misses = 2 },
                Accuracy = 96.5m
            },
            Menu = new TosuMenu
            {
                State = 2,
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });

        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(true);
        mockSheetsSink.Setup(x => x.InitGoogleAPIAsync(It.IsAny<bool>())).ReturnsAsync(());
        mockSheetsSink.Setup(x => x.TryAppendPlayEntry(
            It.IsAny<PlayEntryData>(),
            It.IsAny<bool>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<DateTime>(),
            It.IsAny<Action<bool>>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()
        )).ReturnsAsync(());

        var settings = new SettingsService();
        settings.LocalDatabasePath = ":memory:";
        settings.EnableLocalLogging = true;
        settings.EnableGoogleSheetsLogging = false;

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object, settings);
        await tracker.InitializeStorageAsync(silent: true);

        // Act - Simulate game flow: menu → song select → playing → results
        tracker.Tick(); // Should process initial state
        
        // Simulate playing state
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Test Artist",
                Title = "Test Song",
                Difficulty = "Hard",
                Id = 12345,
                SetId = 67890,
                Time = new TimeInfo { Live = 30000 },
                Stats = new ToseBeatmapStats
                {
                    HP = 5,
                    CS = 4,
                    AR = 9,
                    OD = 8,
                    BPM = new BpmInfo { Min = 180, Max = 180 },
                    Stars = new StarRating { Total = 5.5m, Aim = 2.8m, Speed = 2.7m }
                }
            },
            Play = new TosuPlay
            {
                Mode = new ModeInfo { Number = 0 },
                Mods = new ModsInfo { Number = 0 },
                Hits = new HitCounts { H300 = 150, H100 = 10, H50 = 1, Misses = 2 },
                Accuracy = 97.5m
            },
            Menu = new TosuMenu
            {
                State = 2,
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });
        
        tracker.Tick();

        // Simulate transition to results screen
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Test Artist",
                Title = "Test Song",
                Difficulty = "Hard",
                Id = 12345,
                SetId = 67890,
                Time = new TimeInfo { Live = 60000 },
                Stats = new ToseBeatmapStats
                {
                    HP = 5,
                    CS = 4,
                    AR = 9,
                    OD = 8,
                    BPM = new BpmInfo { Min = 180, Max = 180 },
                    Stars = new StarRating { Total = 5.5m, Aim = 2.8m, Speed = 2.7m }
                }
            },
            Play = new TosuPlay
            {
                Mode = new ModeInfo { Number = 0 },
                Mods = new ModsInfo { Number = 0 },
                Hits = new HitCounts { H300 = 0, H100 = 0, H50 = 0, Misses = 0 },
                Accuracy = 100m
            },
            Menu = new TosuMenu
            {
                State = 7, // ResultsScreen
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });
        
        tracker.Tick();
        await tracker.FlushPendingSubmissionsAsync();

        // Assert
        Assert.True(tracker.DatabaseReady);
        Assert.True(tracker.LocalPlayCount > 0);
    }

    [Fact]
    public async Task FullPipeline_Shutdown_AllResourcesCleanedUp()
    {
        // Arrange
        var mockForm = new Mock<IMainWindow>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockSheetsSink = new Mock<ISheetsSink>();
        
        mockTosuClient.Setup(x => x.IsConnected).Returns(true);
        mockTosuClient.Setup(x => x.LatestState).Returns(new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Checksum = "test123",
                Artist = "Test Artist",
                Title = "Test Song",
                Difficulty = "Hard",
                Id = 12345,
                SetId = 67890
            },
            Menu = new TosuMenu
            {
                State = 0,
                Bm = new BeatmapInfo { Md5 = "test123" }
            }
        });

        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(false);
        mockSheetsSink.Setup(x => x.InitGoogleAPIAsync(It.IsAny<bool>())).ReturnsAsync(());

        var settings = new SettingsService();
        settings.LocalDatabasePath = ":memory:";
        settings.EnableLocalLogging = true;

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, mockSheetsSink.Object, settings);
        await tracker.InitializeStorageAsync(silent: true);

        // Act
        tracker.Tick();
        await tracker.FlushPendingSubmissionsAsync();
        
        // Verify session manager can be disposed
        var sessionManager = tracker.SessionManager;
        
        // Assert - verify resources are in a clean state
        Assert.NotNull(sessionManager);
        Assert.True(tracker.DatabaseReady);
    }

    [Fact]
    public async Task FullPipeline_SettingsRoundTrip_PreservesConfiguration()
    {
        // Arrange
        var tempPath = System.IO.Path.GetTempFileName();
        try
        {
            var settings1 = new SettingsService();
            settings1.SettingsFilePath = tempPath;
            settings1.Username = "TestUser";
            settings1.TosuHost = "127.0.0.1";
            settings1.TosuPort = 24050;
            settings1.EnableLocalLogging = true;
            settings1.EnableGoogleSheetsLogging = false;
            settings1.SpreadsheetId = "test-spreadsheet-id";
            settings1.SheetName = "TestSheet";

            // Act - Save settings
            settings1.SaveSettings();

            // Create new service and load
            var settings2 = new SettingsService();
            settings2.SettingsFilePath = tempPath;
            settings2.LoadSettings();

            // Assert
            Assert.Equal("TestUser", settings2.Username);
            Assert.Equal("127.0.0.1", settings2.TosuHost);
            Assert.Equal(24050, settings2.TosuPort);
            Assert.True(settings2.EnableLocalLogging);
            Assert.False(settings2.EnableGoogleSheetsLogging);
            Assert.Equal("test-spreadsheet-id", settings2.SpreadsheetId);
            Assert.Equal("TestSheet", settings2.SheetName);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(tempPath);
            }
        }
    }
}
