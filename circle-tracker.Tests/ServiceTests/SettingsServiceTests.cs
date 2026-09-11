using Circle_Tracker.Services;
using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace CircleTracker.Tests.ServiceTests;

public class SettingsServiceTests
{
    [Fact]
    public void LoadSettings_NoFile_UsesDefaults()
    {
        string nonExistentFile = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json");
        string nonExistentOld = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.txt");

        var service = new SettingsService(nonExistentFile, nonExistentOld);

        service.EnableLocalLogging.Should().BeTrue();
        service.EnableGoogleSheetsLogging.Should().BeFalse();
        service.LocalDatabasePath.Should().BeEmpty();
        service.SpreadsheetId.Should().BeEmpty();
        service.SheetName.Should().Be("Raw Data");
        service.SubmitSoundEnabled.Should().BeTrue();
        service.SpreadsheetTimezoneVerified.Should().BeFalse();
        service.UseAltFuncSeparator.Should().BeFalse();
        service.Username.Should().BeEmpty();
        service.TosuHost.Should().Be("127.0.0.1");
        service.TosuPort.Should().Be(24050);
        service.DisableBackgroundAnimationsWhenUnfocused.Should().BeFalse();
    }

    [Fact]
    public void LoadSettings_ValidJson_LoadsAllProperties()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"test_valid_{Guid.NewGuid():N}.json");
        try
        {
            string json = """
            {
              "enableLocalLogging": false,
              "enableGoogleSheetsLogging": true,
              "localDatabasePath": "/custom/path/db.sqlite",
              "spreadsheetId": "custom_sheet_id",
              "sheetName": "CustomSheet",
              "submitSoundEnabled": false,
              "spreadsheetTimezoneVerified": true,
              "useAltFuncSeparator": true,
              "username": "osuPlayer",
              "tosuHost": "192.168.1.50",
              "tosuPort": 12345,
              "disableBackgroundAnimationsWhenUnfocused": true
            }
            """;
            File.WriteAllText(tempJson, json);

            var service = new SettingsService(tempJson);

            service.EnableLocalLogging.Should().BeFalse();
            service.EnableGoogleSheetsLogging.Should().BeTrue();
            service.LocalDatabasePath.Should().Be("/custom/path/db.sqlite");
            service.SpreadsheetId.Should().Be("custom_sheet_id");
            service.SheetName.Should().Be("CustomSheet");
            service.SubmitSoundEnabled.Should().BeFalse();
            service.SpreadsheetTimezoneVerified.Should().BeTrue();
            service.UseAltFuncSeparator.Should().BeTrue();
            service.Username.Should().Be("osuPlayer");
            service.TosuHost.Should().Be("192.168.1.50");
            service.TosuPort.Should().Be(12345);
            service.DisableBackgroundAnimationsWhenUnfocused.Should().BeTrue();
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { }
        }
    }

    [Fact]
    public void SaveSettings_RoundTrip_PreservesAllValues()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"test_save_{Guid.NewGuid():N}.json");
        try
        {
            var service1 = new SettingsService(tempJson);
            service1.EnableLocalLogging = false;
            service1.EnableGoogleSheetsLogging = true;
            service1.LocalDatabasePath = "/saved/db.sqlite";
            service1.SpreadsheetId = "saved_sheet_id";
            service1.SheetName = "SavedSheet";
            service1.SubmitSoundEnabled = false;
            service1.SpreadsheetTimezoneVerified = true;
            service1.UseAltFuncSeparator = true;
            service1.Username = "SavedUser";
            service1.TosuHost = "10.0.0.1";
            service1.TosuPort = 9999;
            service1.DisableBackgroundAnimationsWhenUnfocused = true;

            service1.SaveSettings();

            var service2 = new SettingsService(tempJson);
            service2.EnableLocalLogging.Should().Be(service1.EnableLocalLogging);
            service2.EnableGoogleSheetsLogging.Should().Be(service1.EnableGoogleSheetsLogging);
            service2.LocalDatabasePath.Should().Be(service1.LocalDatabasePath);
            service2.SpreadsheetId.Should().Be(service1.SpreadsheetId);
            service2.SheetName.Should().Be(service1.SheetName);
            service2.SubmitSoundEnabled.Should().Be(service1.SubmitSoundEnabled);
            service2.SpreadsheetTimezoneVerified.Should().Be(service1.SpreadsheetTimezoneVerified);
            service2.UseAltFuncSeparator.Should().Be(service1.UseAltFuncSeparator);
            service2.Username.Should().Be(service1.Username);
            service2.TosuHost.Should().Be(service1.TosuHost);
            service2.TosuPort.Should().Be(service1.TosuPort);
            service2.DisableBackgroundAnimationsWhenUnfocused.Should().Be(service1.DisableBackgroundAnimationsWhenUnfocused);
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { }
        }
    }

    [Fact]
    public void MigrateOldSettings_ValidTextFile_MigratesCorrectly()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"test_mig_{Guid.NewGuid():N}.json");
        string tempOldTxt = Path.Combine(Path.GetTempPath(), $"test_mig_{Guid.NewGuid():N}.txt");
        try
        {
            string[] oldLines =
            [
                "migrated_sheet_id",
                "MigratedSheet",
                "1",
                "1",
                "1",
                "MigratedUser",
                "192.168.0.2",
                "30000"
            ];
            File.WriteAllLines(tempOldTxt, oldLines);

            var service = new SettingsService(tempJson, tempOldTxt);

            service.SpreadsheetId.Should().Be("migrated_sheet_id");
            service.SheetName.Should().Be("MigratedSheet");
            service.SubmitSoundEnabled.Should().BeTrue();
            service.SpreadsheetTimezoneVerified.Should().BeTrue();
            service.UseAltFuncSeparator.Should().BeTrue();
            service.Username.Should().Be("MigratedUser");
            service.TosuHost.Should().Be("192.168.0.2");
            service.TosuPort.Should().Be(30000);
            File.Exists(tempJson).Should().BeTrue();
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { }
            try { if (File.Exists(tempOldTxt)) File.Delete(tempOldTxt); } catch { }
        }
    }

    [Fact]
    public void MigrateOldSettings_PartialFile_HandlesGracefully()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"test_partial_{Guid.NewGuid():N}.json");
        string tempOldTxt = Path.Combine(Path.GetTempPath(), $"test_partial_{Guid.NewGuid():N}.txt");
        try
        {
            string[] partialLines = ["partial_sheet_id", "PartialSheet"];
            File.WriteAllLines(tempOldTxt, partialLines);

            var service = new SettingsService(tempJson, tempOldTxt);

            service.SpreadsheetId.Should().Be("partial_sheet_id");
            service.SheetName.Should().Be("PartialSheet");
            service.TosuHost.Should().Be("127.0.0.1");
            service.TosuPort.Should().Be(24050);
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { }
            try { if (File.Exists(tempOldTxt)) File.Delete(tempOldTxt); } catch { }
        }
    }

    [Fact]
    public void LoadSettings_CorruptJson_UsesDefaults()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"test_corrupt_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempJson, "{ corrupt invalid json [[[");

            var service = new SettingsService(tempJson);

            service.TosuHost.Should().Be("127.0.0.1");
            service.TosuPort.Should().Be(24050);
            service.SheetName.Should().Be("Raw Data");
            service.SubmitSoundEnabled.Should().BeTrue();
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { }
        }
    }

    [Fact]
    public void GetFunctionSeparator_AltEnabled_ReturnsSemicolon()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"test_sep_{Guid.NewGuid():N}.json");
        var service = new SettingsService(tempJson);

        service.UseAltFuncSeparator = true;

        service.GetFunctionSeparator().Should().Be(";");
    }

    [Fact]
    public void GetFunctionSeparator_AltDisabled_ReturnsComma()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"test_sep_{Guid.NewGuid():N}.json");
        var service = new SettingsService(tempJson);

        service.UseAltFuncSeparator = false;

        service.GetFunctionSeparator().Should().Be(",");
    }

    [Fact]
    public void FindFile_ExistsInBaseDirectory_ReturnsAbsolutePath()
    {
        string fileName = $"test_{Guid.NewGuid():N}.tmp";
        string filePath = Path.Combine(AppContext.BaseDirectory, fileName);
        File.WriteAllText(filePath, "test");
        try
        {
            var result = SettingsService.FindFile(fileName);

            Path.IsPathRooted(result).Should().BeTrue();
            result.Should().Be(filePath);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void FindFile_NotInBaseDirectory_ReturnsExpectedFallback()
    {
        string fileName = $"nonexistent_{Guid.NewGuid():N}.tmp";
        string expected = Path.Combine(AppPaths.AppDataDirectory, fileName);

        var result = SettingsService.FindFile(fileName);

        result.Should().Be(expected);
    }

    [Fact]
    public void MissingSoundFile_FallsBackOrReports()
    {
        string fileName = $"missing_sound_{Guid.NewGuid():N}.wav";
        string expected = Path.Combine(AppPaths.AppDataDirectory, fileName);

        var result = SettingsService.FindFile(fileName);

        result.Should().Be(expected);
        File.Exists(result).Should().BeFalse();
    }

    [Fact]
    public void SettingsService_DefaultConstructor_UsesAppDataSettingsPath()
    {
        var service = new SettingsService();

        service.SettingsFilePath.Should().Be(AppPaths.SettingsPath);
    }

    [Fact]
    public void SettingsService_SaveThenLoad_RoundTripsViaNewPath()
    {
        string freshDir = Path.Combine(Path.GetTempPath(), $"ct_newpath_{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(freshDir, "user_settings.json");
        try
        {
            var writer = new SettingsService(settingsPath);
            writer.SpreadsheetId = "new_path_sheet";
            writer.SaveSettings();

            var reader = new SettingsService(settingsPath);

            reader.SpreadsheetId.Should().Be("new_path_sheet");
        }
        finally
        {
            try { Directory.Delete(freshDir, true); } catch { }
        }
    }

    [Fact]
    public void SettingsService_LoadSettings_DoesNotDependOnCWD()
    {
        string previousCwd = Environment.CurrentDirectory;
        string tempDir = Path.GetTempPath();
        Environment.CurrentDirectory = tempDir;
        string nonExistentFile = Path.Combine(tempDir, $"nonexistent_{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(nonExistentFile);

            service.EnableLocalLogging.Should().BeTrue();
            service.EnableGoogleSheetsLogging.Should().BeFalse();
        }
        finally
        {
            Environment.CurrentDirectory = previousCwd;
        }
    }
}
