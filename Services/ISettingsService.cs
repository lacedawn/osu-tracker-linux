using System;

namespace Circle_Tracker.Services;

public interface ISettingsService
{
    bool EnableLocalLogging { get; set; }
    bool EnableGoogleSheetsLogging { get; set; }
    string LocalDatabasePath { get; set; }
    string TosuHost { get; set; }
    int TosuPort { get; set; }
    bool SubmitSoundEnabled { get; set; }
    string Username { get; set; }
    bool DisableBackgroundAnimationsWhenUnfocused { get; set; }
    string SpreadsheetId { get; set; }
    string SheetName { get; set; }
    bool UseAltFuncSeparator { get; set; }
    bool SpreadsheetTimezoneVerified { get; set; }
    string SettingsFilePath { get; }
    string SoundFilePath { get; }

    event Action? SettingsChanged;

    void SaveSettings();
    void LoadSettings();
    void MigrateOldSettings(string oldPath);
    string GetFunctionSeparator();
}
