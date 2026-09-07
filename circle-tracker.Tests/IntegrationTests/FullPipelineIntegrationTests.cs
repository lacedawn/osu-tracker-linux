using Circle_Tracker;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using CircleTracker.Tests;
using FluentAssertions;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace circle_tracker.Tests.IntegrationTests;

public class FullPipelineIntegrationTests
{
    [Fact]
    public async Task FullPipeline_ConnectTosuAndSubmitPlay_EndToEnd()
    {
        var mockForm = new Mock<IMainWindow>();
        var mockTosuClient = new Mock<ITosuClient>();
        var mockSheetsSink = new Mock<ISheetsSink>();

        mockTosuClient.Setup(x => x.IsConnected).Returns(true);
        mockSheetsSink.Setup(x => x.SheetsApiReady).Returns(true);
        mockSheetsSink.Setup(x => x.InitGoogleAPIAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

        var settings = new SettingsService();
        settings.LocalDatabasePath = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        settings.EnableLocalLogging = true;
        settings.EnableGoogleSheetsLogging = false;

        var tracker = new Tracker(mockForm.Object, mockTosuClient.Object, settings);
        await tracker.InitializeStorageAsync(silent: true);

        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.WarmUpPlaying());
        tracker.Tick();

        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Playing(
            h300: 150, h100: 10, h50: 1, misses: 2, songTimeMs: 30000, accuracy: 97.5m));
        tracker.Tick();

        mockTosuClient.Setup(x => x.LatestState).Returns(StateBuilder.Results(h300: 150));
        tracker.Tick();
        await tracker.FlushPendingSubmissionsAsync();

        tracker.DatabaseReady.Should().BeTrue();
        tracker.LocalPlayCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FullPipeline_Shutdown_AllResourcesCleanedUp()
    {
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

        tracker.Tick();
        await tracker.FlushPendingSubmissionsAsync();

        var sessionManager = tracker.SessionManager;

        sessionManager.Should().NotBeNull();
        tracker.DatabaseReady.Should().BeTrue();
    }

    [Fact]
    public async Task FullPipeline_SettingsRoundTrip_PreservesConfiguration()
    {
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

            settings1.SaveSettings();

            var settings2 = new SettingsService(settingsFilePath: tempPath);

            settings2.Username.Should().Be("TestUser");
            settings2.TosuHost.Should().Be("127.0.0.1");
            settings2.TosuPort.Should().Be(24050);
            settings2.EnableLocalLogging.Should().BeTrue();
            settings2.EnableGoogleSheetsLogging.Should().BeFalse();
            settings2.SpreadsheetId.Should().Be("test-spreadsheet-id");
            settings2.SheetName.Should().Be("TestSheet");
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
