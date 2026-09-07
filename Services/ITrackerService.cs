using System;
using System.Threading;
using System.Threading.Tasks;
using Circle_Tracker.Storage;

namespace Circle_Tracker.Services;

public interface ITrackerService
{
    int IdleSeconds { get; }
    int PlayingSeconds { get; }
    bool EnableLocalLogging { get; set; }
    bool EnableGoogleSheetsLogging { get; set; }
    string LocalDatabasePath { get; set; }
    string TosuHost { get; set; }
    int TosuPort { get; set; }
    bool SubmitSoundEnabled { get; set; }
    bool UseAltFuncSeparator { get; set; }
    string SpreadsheetId { get; set; }
    string SheetName { get; set; }
    bool DatabaseReady { get; }
    int LocalPlayCount { get; }
    bool SheetsApiReady { get; }
    SessionManager SessionManager { get; }
    IPlaySink? PlaySink { get; }
    
    event EventHandler<(PlayEntryData Data, PlayContext Context)>? PlayLogged;
    
    void SaveSettings();
    void TickWrapper();
    void TickEverySecond();
    TrackerSnapshot GetSnapshot();
    Task InitializeStorageAsync(bool silent = false, CancellationToken ct = default);
    Task InitGoogleAPIAsync(bool silent = false);
    Task FlushPendingSubmissionsAsync(CancellationToken ct = default);
    Task SyncOfflinePlaysToSheetsAsync(CancellationToken ct = default);
    Analytics.ISessionAnalyticsService? GetSessionAnalyticsService();
}
