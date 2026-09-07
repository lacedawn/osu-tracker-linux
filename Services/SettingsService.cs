using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Circle_Tracker.Services;

public class SettingsService : ISettingsService
{
    private static readonly ILogger<SettingsService> _log = AppLogger.For<SettingsService>();

    private bool _disableBackgroundAnimationsWhenUnfocused;

    public string SettingsFilePath { get; }
    public string OldSettingsFilePath { get; }
    public string SoundFilePath => FindFile(Path.Combine("assets", "sectionpass.wav"));

    public bool EnableLocalLogging { get; set; } = true;
    public bool EnableGoogleSheetsLogging { get; set; } = false;
    public string LocalDatabasePath { get; set; } = "";
    public string TosuHost { get; set; } = "127.0.0.1";
    public int TosuPort { get; set; } = 24050;
    public bool SubmitSoundEnabled { get; set; } = true;
    public string Username { get; set; } = "";

    public bool DisableBackgroundAnimationsWhenUnfocused
    {
        get => _disableBackgroundAnimationsWhenUnfocused;
        set
        {
            _disableBackgroundAnimationsWhenUnfocused = value;
            UserSettings.GlobalDisableBackgroundAnimationsWhenUnfocused = value;
        }
    }

    public string SpreadsheetId { get; set; } = "";
    public string SheetName { get; set; } = "Raw Data";
    public bool UseAltFuncSeparator { get; set; } = false;
    public bool SpreadsheetTimezoneVerified { get; set; } = false;

    public event Action? SettingsChanged;

    public SettingsService() : this(null, null)
    {
    }

    public SettingsService(string? settingsFilePath = null, string? oldSettingsFilePath = null)
    {
        SettingsFilePath = settingsFilePath ?? Path.Combine(AppContext.BaseDirectory, "user_settings.json");
        OldSettingsFilePath = oldSettingsFilePath ?? Path.Combine(AppContext.BaseDirectory, "user_settings.txt");
        LoadSettings();
    }

    public static string FindFile(string relativePath)
    {
        string p1 = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(p1)) return p1;
        return p1;
    }

    public string GetFunctionSeparator() => UseAltFuncSeparator ? ";" : ",";

    public void SaveSettings()
    {
        try
        {
            var settings = new UserSettings
            {
                EnableLocalLogging = EnableLocalLogging,
                EnableGoogleSheetsLogging = EnableGoogleSheetsLogging,
                LocalDatabasePath = LocalDatabasePath,
                SpreadsheetId = SpreadsheetId,
                SheetName = SheetName,
                SubmitSoundEnabled = SubmitSoundEnabled,
                SpreadsheetTimezoneVerified = SpreadsheetTimezoneVerified,
                UseAltFuncSeparator = UseAltFuncSeparator,
                Username = Username,
                TosuHost = TosuHost,
                TosuPort = TosuPort,
                DisableBackgroundAnimationsWhenUnfocused = DisableBackgroundAnimationsWhenUnfocused
            };
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            string? dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
            SettingsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to save settings");
        }
    }

    public void LoadSettings()
    {
        EnableLocalLogging = true;
        EnableGoogleSheetsLogging = false;
        LocalDatabasePath = "";
        SpreadsheetId = "";
        SheetName = "Raw Data";
        SubmitSoundEnabled = true;
        SpreadsheetTimezoneVerified = false;
        UseAltFuncSeparator = false;
        Username = "";
        TosuHost = "127.0.0.1";
        TosuPort = 24050;
        DisableBackgroundAnimationsWhenUnfocused = false;

        if (!File.Exists(SettingsFilePath))
        {
            if (File.Exists(OldSettingsFilePath))
            {
                MigrateOldSettings(OldSettingsFilePath);
            }
            return;
        }

        try
        {
            string json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);
            if (settings != null)
            {
                EnableLocalLogging = settings.EnableLocalLogging;
                EnableGoogleSheetsLogging = settings.EnableGoogleSheetsLogging;
                LocalDatabasePath = settings.LocalDatabasePath ?? "";
                SpreadsheetId = settings.SpreadsheetId;
                SheetName = settings.SheetName;
                SubmitSoundEnabled = settings.SubmitSoundEnabled;
                SpreadsheetTimezoneVerified = settings.SpreadsheetTimezoneVerified;
                UseAltFuncSeparator = settings.UseAltFuncSeparator;
                Username = settings.Username;
                TosuHost = !string.IsNullOrWhiteSpace(settings.TosuHost) ? settings.TosuHost : "127.0.0.1";
                TosuPort = settings.TosuPort > 0 ? settings.TosuPort : 24050;
                DisableBackgroundAnimationsWhenUnfocused = settings.DisableBackgroundAnimationsWhenUnfocused;
                UserSettings.GlobalDisableBackgroundAnimationsWhenUnfocused = DisableBackgroundAnimationsWhenUnfocused;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load settings");
        }
    }

    public void MigrateOldSettings(string oldPath)
    {
        try
        {
            var lines = File.ReadAllLines(oldPath);
            if (lines.Length > 0) SpreadsheetId = lines[0];
            if (lines.Length > 1) SheetName = lines[1];
            if (lines.Length > 2) SubmitSoundEnabled = lines[2] == "1";
            if (lines.Length > 3) SpreadsheetTimezoneVerified = lines[3] == "1";
            if (lines.Length > 4) UseAltFuncSeparator = lines[4] == "1";
            if (lines.Length > 5) Username = lines[5];
            if (lines.Length > 6 && !string.IsNullOrWhiteSpace(lines[6])) TosuHost = lines[6];
            if (lines.Length > 7 && int.TryParse(lines[7], out int port) && port > 0) TosuPort = port;
            SaveSettings();
            _log.LogInformation("Migrated settings from old text format to JSON");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to migrate old settings");
        }
    }
}
