using Avalonia.Headless.XUnit;
using Circle_Tracker.Services;
using Circle_Tracker.ViewModels;
using FluentAssertions;
using Moq;
using System.Collections.Generic;
using Xunit;

namespace Circle_Tracker.Tests.ViewModelTests;

public class SettingsViewModelTests
{
    [AvaloniaFact]
    public void Should_PersistSettingsValues_When_PropertiesModified()
    {
        var mockTracker = new Mock<ITrackerService>();
        mockTracker.SetupAllProperties();
        mockTracker.Object.SubmitSoundEnabled = true;
        mockTracker.Object.EnableLocalLogging = true;
        var viewModel = new SettingsViewModel(mockTracker.Object);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (s, e) => changedProperties.Add(e.PropertyName);

        viewModel.LocalDatabasePath = "custom_tracker.db";
        viewModel.LocalDatabasePath.Should().Be("custom_tracker.db");
        mockTracker.VerifySet(t => t.LocalDatabasePath = "custom_tracker.db", Times.Once);
        mockTracker.Verify(t => t.SaveSettings(), Times.Once);
        changedProperties.Should().Contain(nameof(viewModel.LocalDatabasePath));

        viewModel.SubmitSoundEnabled = false;
        viewModel.SubmitSoundEnabled.Should().BeFalse();
        mockTracker.VerifySet(t => t.SubmitSoundEnabled = false, Times.Once);

        viewModel.SpreadsheetId = "test-spreadsheet-id-123";
        viewModel.SpreadsheetId.Should().Be("test-spreadsheet-id-123");
        mockTracker.VerifySet(t => t.SpreadsheetId = "test-spreadsheet-id-123", Times.Once);

        viewModel.SheetName = "Custom Sheet";
        viewModel.SheetName.Should().Be("Custom Sheet");
        mockTracker.VerifySet(t => t.SheetName = "Custom Sheet", Times.Once);

        viewModel.EnableSheetsLogging = true;
        viewModel.EnableSheetsLogging.Should().BeTrue();
        mockTracker.VerifySet(t => t.EnableGoogleSheetsLogging = true, Times.Once);

        viewModel.EnableLocalLogging = false;
        viewModel.EnableLocalLogging.Should().BeFalse();
        mockTracker.VerifySet(t => t.EnableLocalLogging = false, Times.Once);

        viewModel.UseAltFuncSeparator = true;
        viewModel.UseAltFuncSeparator.Should().BeTrue();
        mockTracker.VerifySet(t => t.UseAltFuncSeparator = true, Times.Once);

        viewModel.TosuPortText = "24055";
        viewModel.TosuPortText.Should().Be("24055");
        mockTracker.VerifySet(t => t.TosuPort = 24055, Times.Once);

        viewModel.TosuHost = "192.168.1.100";
        viewModel.TosuHost.Should().Be("192.168.1.100");
        mockTracker.VerifySet(t => t.TosuHost = "192.168.1.100", Times.Once);
    }
}
