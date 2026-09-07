using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Circle_Tracker.Analytics;
using Circle_Tracker.Storage;
using Circle_Tracker.Sync;
using Dapper;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;

namespace Circle_Tracker.Services;

public class TrackerService : ITrackerService, IMainWindow
{
    public static readonly TimeSpan OfflineSyncInterval = TimeSpan.FromMinutes(5);

    private static readonly ILogger<TrackerService> _log = AppLogger.For<TrackerService>();
    private readonly Tracker _tracker;
    private readonly ISettingsService _settings;
    private readonly Func<IOfflinePlaySyncQueue>? _syncQueueFactory;
    private readonly object _syncQueueLock = new();
    private IOfflinePlaySyncQueue? _syncQueue;

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

    public IOfflinePlaySyncQueue? OfflineSyncQueue
    {
        get
        {
            lock (_syncQueueLock)
            {
                return _syncQueue;
            }
        }
    }

    public TrackerService(ITosuClient tosuClient, ISettingsService? settings = null, Func<IOfflinePlaySyncQueue>? syncQueueFactory = null)
    {
        _settings = settings ?? new SettingsService();
        _syncQueueFactory = syncQueueFactory;
        _tracker = new Tracker(this, tosuClient, _settings);
    }

    public TrackerService(ITosuClient tosuClient, IPlaySink playSink, SessionManager? sessionManager = null, ISheetsSink? sheetsSink = null, ISettingsService? settings = null, Func<IOfflinePlaySyncQueue>? syncQueueFactory = null)
    {
        _settings = settings ?? new SettingsService();
        _syncQueueFactory = syncQueueFactory;
        _tracker = new Tracker(this, new TrackerOptions(tosuClient, PlaySink: playSink, SessionManager: sessionManager, SheetsSink: sheetsSink, Settings: _settings));
    }

    public void SaveSettings() => _settings.SaveSettings();
    public void TickWrapper() => _tracker.TickWrapper();
    public void TickEverySecond()
    {
        _tracker.TickEverySecond();
        RefreshOfflineSyncState();
    }
    public TrackerSnapshot GetSnapshot() => _tracker.GetSnapshot();
    public Task InitializeStorageAsync(bool silent = false, CancellationToken ct = default) => _tracker.InitializeStorageAsync(silent, ct);
    public async Task InitGoogleAPIAsync(bool silent = false)
    {
        await _tracker.InitGoogleAPIAsync(silent).ConfigureAwait(false);
        RefreshOfflineSyncState();
    }
    public Task FlushPendingSubmissionsAsync(CancellationToken ct = default) => _tracker.SubmissionService.FlushPendingSubmissionsAsync(ct);
    public ISessionAnalyticsService? GetSessionAnalyticsService() => _tracker.GetSessionAnalyticsService();

    public async Task SyncOfflinePlaysToSheetsAsync(CancellationToken ct = default)
    {
        if (_tracker.SessionManager.GetDatabaseManager() is not SqliteDatabaseManager sqliteDb)
            return;

        var sheetsService = GoogleSheetsManager.CreateSheetsService();
        if (sheetsService == null)
            return;

        string spreadsheetId = _settings.SpreadsheetId;
        string sheetName = _settings.SheetName;
        string separator = _settings.UseAltFuncSeparator ? ";" : ",";

        async Task AppendBatchAsync(IList<IList<object>> rows, CancellationToken innerCt)
        {
            string range = $"{sheetName}!A:A";
            var valueRange = new ValueRange { Values = rows };
            var request = sheetsService.Spreadsheets.Values.Append(valueRange, spreadsheetId, range);
            request.ValueInputOption = Google.Apis.Sheets.v4.SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            await request.ExecuteAsync(innerCt).ConfigureAwait(false);
        }

        await using var queue = new OfflinePlaySyncQueue(
            sqliteDb, sheetsService, spreadsheetId, sheetName,
            () => separator, () => _tracker.SheetsApiReady, AppendBatchAsync);

        await queue.FlushPendingQueueAsync(ct).ConfigureAwait(false);
    }

    public void RefreshOfflineSyncState()
    {
        if (_tracker.SheetsApiReady)
            EnsureOfflineSyncStarted();
        else
            DetachOfflineSyncQueue();
    }

    public async Task FlushOfflineSyncAsync(CancellationToken ct = default)
    {
        IOfflinePlaySyncQueue? queue = OfflineSyncQueue;

        if (queue != null)
        {
            await queue.FlushPendingQueueAsync(ct).ConfigureAwait(false);
            RefreshOfflineSyncState();
        }
        else
        {
            await SyncOfflinePlaysToSheetsAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<int> GetPendingSyncCountAsync(CancellationToken ct = default)
    {
        try
        {
            IOfflinePlaySyncQueue? queue = OfflineSyncQueue;

            if (queue != null)
                return await queue.GetPendingCountAsync(ct).ConfigureAwait(false);

            IDatabaseManager dbManager = _tracker.SessionManager.GetDatabaseManager();
            await using var conn = await dbManager.CreateConnectionAsync(ct).ConfigureAwait(false);
            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM plays WHERE sync_status = 'Pending';").ConfigureAwait(false);
        }
        catch
        {
            return 0;
        }
    }

    public async Task StopOfflineSyncAsync(CancellationToken ct = default)
    {
        IOfflinePlaySyncQueue? queue = TakeOfflineSyncQueue();

        if (queue == null)
            return;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            await queue.StopBackgroundSyncAsync().WaitAsync(TimeSpan.FromSeconds(5), timeoutCts.Token).ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await queue.FlushPendingQueueAsync(ct).ConfigureAwait(false);
        }
        catch
        {
        }

        DisposeSyncQueue(queue);
    }

    private void EnsureOfflineSyncStarted()
    {
        lock (_syncQueueLock)
        {
            if (_syncQueue != null)
                return;
        }

        IOfflinePlaySyncQueue? queue = CreateOfflineSyncQueue();

        if (queue == null)
            return;

        lock (_syncQueueLock)
        {
            if (_syncQueue != null)
            {
                DisposeSyncQueue(queue);
                return;
            }

            _syncQueue = queue;
        }

        queue.StartBackgroundSync(OfflineSyncInterval);
        _log.LogInformation("Offline sync queue started with interval {Interval}", OfflineSyncInterval);
    }

    private IOfflinePlaySyncQueue? CreateOfflineSyncQueue()
    {
        try
        {
            if (_syncQueueFactory != null)
                return _syncQueueFactory();

            IDatabaseManager dbManager = _tracker.SessionManager.GetDatabaseManager();
            SheetsService? sheetsService = GoogleSheetsManager.CreateSheetsService();

            if (sheetsService == null)
                return null;

            return new OfflinePlaySyncQueue(
                dbManager,
                sheetsService,
                _settings.SpreadsheetId,
                _settings.SheetName,
                _settings.GetFunctionSeparator,
                () => _tracker.SheetsApiReady);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to create offline sync queue");
            return null;
        }
    }

    private void DetachOfflineSyncQueue()
    {
        IOfflinePlaySyncQueue? queue = TakeOfflineSyncQueue();

        if (queue == null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await queue.StopBackgroundSyncAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            DisposeSyncQueue(queue);
        });
    }

    private IOfflinePlaySyncQueue? TakeOfflineSyncQueue()
    {
        lock (_syncQueueLock)
        {
            IOfflinePlaySyncQueue? queue = _syncQueue;
            _syncQueue = null;
            return queue;
        }
    }

    private static void DisposeSyncQueue(IOfflinePlaySyncQueue queue)
    {
        try
        {
            if (queue is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else if (queue is IDisposable disposable)
                disposable.Dispose();
        }
        catch
        {
        }
    }

    void IMainWindow.SetCredentialsFound(bool found) { }
    void IMainWindow.SetSheetsApiReady(bool val) { }
    void IMainWindow.UpdateTime() { }
    void IMainWindow.StopUpdateTimer() { }
    void IMainWindow.ShowMessage(string message, string title) { }
    Task<bool> IMainWindow.ShowYesNoDialog(string message, string title) => Task.FromResult(false);
}
