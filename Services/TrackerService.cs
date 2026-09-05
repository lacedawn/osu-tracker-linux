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

    public int IdleSeconds => _tracker.IdleSeconds;
    public int PlayingSeconds => _tracker.PlayingSeconds;
    public bool EnableLocalLogging
    {
        get => _tracker.EnableLocalLogging;
        set => _tracker.EnableLocalLogging = value;
    }
    public bool EnableGoogleSheetsLogging
    {
        get => _tracker.EnableGoogleSheetsLogging;
        set => _tracker.EnableGoogleSheetsLogging = value;
    }
    public string LocalDatabasePath
    {
        get => _tracker.LocalDatabasePath;
        set => _tracker.LocalDatabasePath = value;
    }
    public string TosuHost
    {
        get => _tracker.TosuHost;
        set => _tracker.TosuHost = value;
    }
    public int TosuPort
    {
        get => _tracker.TosuPort;
        set => _tracker.TosuPort = value;
    }
    public bool SubmitSoundEnabled
    {
        get => _tracker.SubmitSoundEnabled;
        set => _tracker.SubmitSoundEnabled = value;
    }
    public bool UseAltFuncSeparator
    {
        get => _tracker.UseAltFuncSeparator;
        set => _tracker.UseAltFuncSeparator = value;
    }
    public string SpreadsheetId
    {
        get => _tracker.SpreadsheetId;
        set => _tracker.SpreadsheetId = value;
    }
    public string SheetName
    {
        get => _tracker.SheetName;
        set => _tracker.SheetName = value;
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

    public TrackerService(ITosuClient tosuClient)
    {
        _tracker = new Tracker(this, tosuClient);
    }

    public TrackerService(ITosuClient tosuClient, IPlaySink playSink, SessionManager? sessionManager = null, ISheetsSink? sheetsSink = null)
    {
        _tracker = new Tracker(this, tosuClient, playSink, sessionManager, sheetsSink);
    }

    public void SaveSettings() => _tracker.SaveSettings();
    public void TickWrapper() => _tracker.TickWrapper();
    public void TickEverySecond() => _tracker.TickEverySecond();
    public TrackerSnapshot GetSnapshot() => _tracker.GetSnapshot();
    public Task InitializeStorageAsync(bool silent = false, CancellationToken ct = default) => _tracker.InitializeStorageAsync(silent, ct);
    public Task InitGoogleAPIAsync(bool silent = false) => _tracker.InitGoogleAPIAsync(silent);
    public Task FlushPendingSubmissionsAsync(CancellationToken ct = default) => _tracker.FlushPendingSubmissionsAsync(ct);
    public ISessionAnalyticsService? GetSessionAnalyticsService() => _tracker.GetSessionAnalyticsService();

    void IMainWindow.SetCredentialsFound(bool found) { }
    void IMainWindow.SetSheetsApiReady(bool val) { }
    void IMainWindow.UpdateTime() { }
    void IMainWindow.StopUpdateTimer() { }
    void IMainWindow.ShowMessage(string message, string title) { }
    Task<bool> IMainWindow.ShowYesNoDialog(string message, string title) => Task.FromResult(false);
}
