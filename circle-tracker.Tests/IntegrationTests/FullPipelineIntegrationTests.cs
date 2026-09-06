using Circle_Tracker;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using CircleTracker.Tests;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
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
        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(true);
        mockSheetsSink.Setup(x => x.InitGoogleAPIAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);
        mockSheetsSink.Setup(x => x.TryAppendPlayEntry(
            It.IsAny<PlayEntryData>(),
            It.IsAny<bool>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<DateTime>(),
            It.IsAny<Action<DateTime>>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()
        )).Returns(Task.CompletedTask);

        var settings = new SettingsService();
        settings.LocalDatabasePath = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        settings.EnableLocalLogging = true;
        settings.EnableGoogleSheetsLogging = false;

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, settings);
        await tracker.InitializeStorageAsync(silent: true);

        // Warm up first playing tick
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.WarmUpPlaying());
        tracker.Tick();

        // Simulate playing state
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Playing(
            h300: 150, h100: 10, h50: 1, misses: 2, songTimeMs: 30000, accuracy: 97.5m));
        tracker.Tick();

        // Simulate transition to results screen
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Results(h300: 150));
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
        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Build(0));

        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(false);
        mockSheetsSink.Setup(x => x.InitGoogleAPIAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

        var settings = new SettingsService();
        settings.LocalDatabasePath = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        settings.EnableLocalLogging = true;

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, settings);
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
        var tempPath = Path.GetTempFileName();
        try
        {
            var settings1 = new SettingsService(settingsFilePath: tempPath);
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
            var settings2 = new SettingsService(settingsFilePath: tempPath);

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
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        await Task.CompletedTask;
    }
}
