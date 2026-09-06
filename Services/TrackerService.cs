using System;
using System.Threading;
using System.Threading.Tasks;
using Circle_Tracker.Analytics;
using Circle_Tracker.Storage;
using Circle_Tracker.Sync;
using Microsoft.Extensions.Logging;

namespace Circle_Tracker.Services;

public class TrackerService : ITrackerService, IMainWindow
{
    private static readonly ILogger<TrackerService> _log = AppLogger.For<TrackerService>();
    private readonly Tracker _tracker;
    private readonly ISettingsService _settings;

    public int IdleSeconds => _tracker.IdleSeconds;
    public int PlayingSeconds => _tracker.PlayingSeconds;
    public bool EnableLocalLogging
    {
        get => _settings.EnableLocalLogging;
        set => _settings.EnableLocalLogging = value;
    }
    public bool EnableGoogleSheetsLogging
    {
        get => _settings.EnableGoogleSheetsLogging;
        set => _settings.EnableGoogleSheetsLogging = value;
    }
    public string LocalDatabasePath
    {
        get => _settings.LocalDatabasePath;
        set => _settings.LocalDatabasePath = value;
    }
    public string TosuHost
    {
        get => _settings.TosuHost;
        set => _settings.TosuHost = value;
    }
    public int TosuPort
    {
        get => _settings.TosuPort;
        set => _settings.TosuPort = value;
    }
    public bool SubmitSoundEnabled
    {
        get => _settings.SubmitSoundEnabled;
        set => _settings.SubmitSoundEnabled = value;
    }
    public bool UseAltFuncSeparator
    {
        get => _settings.UseAltFuncSeparator;
        set => _settings.UseAltFuncSeparator = value;
    }
    public string SpreadsheetId
    {
        get => _settings.SpreadsheetId;
        set => _settings.SpreadsheetId = value;
    }
    public string SheetName
    {
        get => _settings.SheetName;
        set => _settings.SheetName = value;
    }
    public bool DatabaseReady => _tracker.DatabaseReady;
    public int LocalPlayCount => _tracker.LocalPlayCount;
    public bool SheetsApiReady => _tracker.SheetsApiReady;
    public SessionManager SessionManager => _tracker.SessionManager;
    public IPlaySink? PlaySink => _tracker.PlaySink;

    public event EventHandler<(PlayEntryData Data, PlayContext Context)>? PlayLogged
    {
        add => _tracker.PlayLogged += value;
        remove => _tracker.PlayLogged -= value;
    }

    public TrackerService(ITosuClient tosuClient, ISettingsService? settings = null)
    {
        _settings = settings ?? new SettingsService();
        _tracker = new Tracker(this, tosuClient, _settings);
    }

    public TrackerService(ITosuClient tosuClient, IPlaySink playSink, SessionManager? sessionManager = null, ISheetsSink? sheetsSink = null, ISettingsService? settings = null)
    {
        _settings = settings ?? new SettingsService();
        _tracker = new Tracker(this, tosuClient, playSink, sessionManager, sheetsSink, settings: _settings);
    }

    public void SaveSettings() => _settings.SaveSettings();
    public void TickWrapper() => _tracker.TickWrapper();
    public void TickEverySecond() => _tracker.TickEverySecond();
    public TrackerSnapshot GetSnapshot() => _tracker.GetSnapshot();
    public Task InitializeStorageAsync(bool silent = false, CancellationToken ct = default) => _tracker.InitializeStorageAsync(silent, ct);
    public Task InitGoogleAPIAsync(bool silent = false) => _tracker.InitGoogleAPIAsync(silent);
    public Task FlushPendingSubmissionsAsync(CancellationToken ct = default) => _tracker.SubmissionService.FlushPendingSubmissionsAsync(ct);
    public ISessionAnalyticsService? GetSessionAnalyticsService() => _tracker.GetSessionAnalyticsService();

    void IMainWindow.SetCredentialsFound(bool found) { }
    void IMainWindow.SetSheetsApiReady(bool val) { }
    void IMainWindow.UpdateTime() { }
    void IMainWindow.StopUpdateTimer() { }
    void IMainWindow.ShowMessage(string message, string title) { }
    Task<bool> IMainWindow.ShowYesNoDialog(string message, string title) => Task.FromResult(false);
}
