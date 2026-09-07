using Avalonia.Media;
using Avalonia.Threading;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Circle_Tracker.Sync;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using static Circle_Tracker.Tracker;

namespace Circle_Tracker.ViewModels;

public class SettingsViewModel : ViewModelBase, IDisposable
{
    private static readonly ILogger<SettingsViewModel> _log = AppLogger.For<SettingsViewModel>();

    private readonly ITrackerService? _tracker;
    private readonly ITosuClient? _tosuClient;
    private readonly IDatabaseManager? _dbManager;
    private readonly Action<string>? _statusCallback;
    private CancellationTokenSource? _reconnectDebounce;

    private bool _tosuConnected;
    private string _tosuStatusText = "tosu: Connecting...";
    private IBrush _tosuStatusBrush = AppBrushes.RedBrush;

    private bool _databaseReady;
    private string _dbStatusText = "DB: Ready";
    private IBrush _dbStatusBrush = AppBrushes.GreenBrush;
    private int _localPlayCount;

    private bool _sheetsConnected;
    private string _sheetsStatusText = "Sheets: Not connected";
    private IBrush _sheetsStatusBrush = AppBrushes.RedBrush;

    private bool _credentialsFound;
    private string _credentialsStatusText = "Missing";
    private IBrush _credentialsStatusBrush = AppBrushes.RedBrush;

    private string _sheetsOperationStatus = "";
    private IBrush _sheetsOperationStatusBrush = AppBrushes.MutedBrush;

    private bool _enableLocalLogging = true;
    private string _localDatabasePath = "";
    private bool _submitSoundEnabled = true;
    private string _tosuHost = "127.0.0.1";
    private string _tosuPortText = "24050";
    private bool _startupLaunch;
    private bool _enableSheetsLogging;
    private string _spreadsheetId = "";
    private string _sheetName = "Raw Data";
    private bool _useAltFuncSeparator;
    private int _pendingSyncCount;

    public SettingsViewModel(
        ITrackerService? tracker = null,
        ITosuClient? tosuClient = null,
        IDatabaseManager? dbManager = null,
        Action<string>? statusCallback = null)
    {
        _tracker = tracker;
        _tosuClient = tosuClient;
        _dbManager = dbManager;
        _statusCallback = statusCallback;

        ConnectSheetsCommand = new RelayCommand(async () => await ConnectSheetsAsync());
        ImportSheetsCommand = new RelayCommand(async () => await ImportSheetsAsync());
        SyncToSheetsCommand = new RelayCommand(async () => await SyncToSheetsAsync());

        LoadSettingsFromTracker();
        CheckCredentials();

        if (_tosuClient != null)
        {
            _tosuClient.ConnectionStateChanged += OnTosuConnectionStateChanged;
        }
    }

    public bool TosuConnected
    {
        get => _tosuConnected;
        set => SetProperty(ref _tosuConnected, value);
    }

    public string TosuStatusText
    {
        get => _tosuStatusText;
        set => SetProperty(ref _tosuStatusText, value);
    }

    public IBrush TosuStatusBrush
    {
        get => _tosuStatusBrush;
        set => SetProperty(ref _tosuStatusBrush, value);
    }

    public bool DatabaseReady
    {
        get => _databaseReady;
        set => SetProperty(ref _databaseReady, value);
    }

    public string DbStatusText
    {
        get => _dbStatusText;
        set => SetProperty(ref _dbStatusText, value);
    }

    public IBrush DbStatusBrush
    {
        get => _dbStatusBrush;
        set => SetProperty(ref _dbStatusBrush, value);
    }

    public int LocalPlayCount
    {
        get => _localPlayCount;
        set => SetProperty(ref _localPlayCount, value);
    }

    public int PendingSyncCount
    {
        get => _pendingSyncCount;
        private set
        {
            if (SetProperty(ref _pendingSyncCount, value))
            {
                OnPropertyChanged(nameof(PendingSyncText));
                OnPropertyChanged(nameof(HasPendingSync));
            }
        }
    }

    public string PendingSyncText => _pendingSyncCount > 0 ? $"{_pendingSyncCount} plays pending sync" : "";

    public bool HasPendingSync => _pendingSyncCount > 0;

    public bool SheetsConnected
    {
        get => _sheetsConnected;
        set => SetProperty(ref _sheetsConnected, value);
    }

    public string SheetsStatusText
    {
        get => _sheetsStatusText;
        set => SetProperty(ref _sheetsStatusText, value);
    }

    public IBrush SheetsStatusBrush
    {
        get => _sheetsStatusBrush;
        set => SetProperty(ref _sheetsStatusBrush, value);
    }

    public bool CredentialsFound
    {
        get => _credentialsFound;
        set => SetProperty(ref _credentialsFound, value);
    }

    public string CredentialsStatusText
    {
        get => _credentialsStatusText;
        set => SetProperty(ref _credentialsStatusText, value);
    }

    public IBrush CredentialsStatusBrush
    {
        get => _credentialsStatusBrush;
        set => SetProperty(ref _credentialsStatusBrush, value);
    }

    public string SheetsOperationStatus
    {
        get => _sheetsOperationStatus;
        set
        {
            if (SetProperty(ref _sheetsOperationStatus, value))
            {
                OnPropertyChanged(nameof(HasSheetsOperationStatus));
            }
        }
    }

    public bool HasSheetsOperationStatus => !string.IsNullOrWhiteSpace(_sheetsOperationStatus);

    public IBrush SheetsOperationStatusBrush
    {
        get => _sheetsOperationStatusBrush;
        set => SetProperty(ref _sheetsOperationStatusBrush, value);
    }

    public bool EnableLocalLogging
    {
        get => _enableLocalLogging;
        set
        {
            if (SetProperty(ref _enableLocalLogging, value) && _tracker != null)
            {
                _tracker.EnableLocalLogging = value;
                _tracker.SaveSettings();
            }
        }
    }

    public string LocalDatabasePath
    {
        get => _localDatabasePath;
        set
        {
            if (SetProperty(ref _localDatabasePath, value) && _tracker != null)
            {
                _tracker.LocalDatabasePath = value;
                _tracker.SaveSettings();
            }
        }
    }

    public bool SubmitSoundEnabled
    {
        get => _submitSoundEnabled;
        set
        {
            if (SetProperty(ref _submitSoundEnabled, value) && _tracker != null)
            {
                _tracker.SubmitSoundEnabled = value;
                _tracker.SaveSettings();
            }
        }
    }

    public string TosuHost
    {
        get => _tosuHost;
        set
        {
            if (SetProperty(ref _tosuHost, value) && _tracker != null)
            {
                _tracker.TosuHost = value;
                _tracker.SaveSettings();
                if (_tosuClient != null && !string.IsNullOrWhiteSpace(value) && !value.Contains(' '))
                {
                    _tosuClient.Host = value.Trim();
                    DebounceReconnect();
                }
            }
        }
    }

    public string TosuPortText
    {
        get => _tosuPortText;
        set
        {
            if (SetProperty(ref _tosuPortText, value) && int.TryParse(value, out int port) && _tracker != null)
            {
                _tracker.TosuPort = port;
                _tracker.SaveSettings();
                if (_tosuClient != null && port >= 1 && port <= 65535)
                {
                    _tosuClient.Port = port;
                    DebounceReconnect();
                }
            }
        }
    }

    public bool StartupLaunch
    {
        get => _startupLaunch;
        set
        {
            if (SetProperty(ref _startupLaunch, value))
            {
                if (value)
                {
                    AutostartHelper.CreateAutostart();
                }
                else
                {
                    AutostartHelper.DeleteAutostart();
                }
            }
        }
    }

    public bool EnableSheetsLogging
    {
        get => _enableSheetsLogging;
        set
        {
            if (SetProperty(ref _enableSheetsLogging, value) && _tracker != null)
            {
                _tracker.EnableGoogleSheetsLogging = value;
                _tracker.SaveSettings();
            }
        }
    }

    public string SpreadsheetId
    {
        get => _spreadsheetId;
        set
        {
            if (SetProperty(ref _spreadsheetId, value) && _tracker != null)
            {
                _tracker.SpreadsheetId = value;
                _tracker.SaveSettings();
            }
        }
    }

    public string SheetName
    {
        get => _sheetName;
        set
        {
            if (SetProperty(ref _sheetName, value) && _tracker != null)
            {
                _tracker.SheetName = value;
                _tracker.SaveSettings();
            }
        }
    }

    public bool UseAltFuncSeparator
    {
        get => _useAltFuncSeparator;
        set
        {
            if (SetProperty(ref _useAltFuncSeparator, value) && _tracker != null)
            {
                _tracker.UseAltFuncSeparator = value;
                _tracker.SaveSettings();
            }
        }
    }

    public ICommand ConnectSheetsCommand { get; }
    public ICommand ImportSheetsCommand { get; }
    public ICommand SyncToSheetsCommand { get; }

    public void UpdateFromSnapshot(TrackerSnapshot snapshot)
    {
        bool isTosuConnected = _tosuClient != null ? _tosuClient.IsConnected : !snapshot.MemoryReadError;
        TosuConnected = isTosuConnected;
        TosuStatusText = isTosuConnected ? $"tosu: {snapshot.DetectedClient}" : "tosu: Connecting...";
        TosuStatusBrush = isTosuConnected ? AppBrushes.GreenBrush : AppBrushes.RedBrush;

        DatabaseReady = snapshot.DatabaseReady;
        DbStatusText = snapshot.DatabaseReady ? $"DB: {snapshot.LocalPlayCount} plays" : "DB: Error";
        DbStatusBrush = snapshot.DatabaseReady ? AppBrushes.GreenBrush : AppBrushes.RedBrush;
        LocalPlayCount = snapshot.LocalPlayCount;

        SheetsConnected = snapshot.SheetsApiReady;
        SheetsStatusText = snapshot.SheetsApiReady ? "Sheets: Connected" : "Sheets: Not connected";
        SheetsStatusBrush = snapshot.SheetsApiReady ? AppBrushes.GreenBrush : AppBrushes.RedBrush;
    }

    public void CheckCredentials()
    {
        CredentialsFound = AppPaths.CredentialsExist();
        CredentialsStatusText = CredentialsFound ? "Found" : "Missing";
        CredentialsStatusBrush = CredentialsFound ? AppBrushes.GreenBrush : AppBrushes.RedBrush;
    }

    public async Task RefreshPendingSyncCountAsync(CancellationToken ct = default)
    {
        try
        {
            if (_tracker == null)
                return;

            PendingSyncCount = await _tracker.GetPendingSyncCountAsync(ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void SetOperationStatus(string message, IBrush brush)
    {
        SheetsOperationStatus = message;
        SheetsOperationStatusBrush = brush;
        _statusCallback?.Invoke(message);
    }

    public async Task ConnectSheetsAsync()
    {
        try
        {
            SetOperationStatus("Connecting to Sheets...", AppBrushes.MutedBrush);
            if (_tracker != null)
            {
                await _tracker.InitGoogleAPIAsync();
                SheetsConnected = _tracker.SheetsApiReady;
                SheetsStatusText = _tracker.SheetsApiReady ? "Sheets: Connected" : "Sheets: Not connected";
                SheetsStatusBrush = _tracker.SheetsApiReady ? AppBrushes.GreenBrush : AppBrushes.RedBrush;
                if (_tracker.SheetsApiReady)
                {
                    SetOperationStatus("✓ Sheets connected", AppBrushes.GreenBrush);
                }
                else
                {
                    SetOperationStatus("✗ Sheets connection failed", AppBrushes.RedBrush);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to connect to Sheets");
            SetOperationStatus("✗ Sheets connection failed", AppBrushes.RedBrush);
        }
    }

    public async Task ImportSheetsAsync()
    {
        try
        {
            if (_tracker == null) return;
            if (!_tracker.SheetsApiReady)
            {
                await _tracker.InitGoogleAPIAsync();
            }

            if (!_tracker.SheetsApiReady)
            {
                SetOperationStatus("Sheets API not connected", AppBrushes.RedBrush);
                return;
            }

            string spreadsheetId = _tracker.SpreadsheetId;
            string sheetName = _tracker.SheetName;
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                SetOperationStatus("Missing Spreadsheet ID", AppBrushes.RedBrush);
                return;
            }

            var sheetsService = GoogleSheetsManager.CreateSheetsService();
            if (sheetsService == null)
            {
                SetOperationStatus("Failed to create Google Sheets service", AppBrushes.RedBrush);
                return;
            }

            if (_dbManager == null)
            {
                SetOperationStatus("Database manager not available", AppBrushes.RedBrush);
                return;
            }

            SetOperationStatus("Importing sheets data...", AppBrushes.MutedBrush);
            var importer = new GoogleSheetsHistoricalImporter(sheetsService, _dbManager);
            var result = await importer.ImportFromSpreadsheetAsync(spreadsheetId, sheetName, null, CancellationToken.None);

            if (result.Success)
            {
                SetOperationStatus($"✓ Imported {result.SyncedCount} plays", AppBrushes.GreenBrush);
            }
            else
            {
                SetOperationStatus("✗ Import failed", AppBrushes.RedBrush);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to import from Sheets");
            SetOperationStatus("✗ Import error", AppBrushes.RedBrush);
        }
    }

    public async Task SyncToSheetsAsync()
    {
        try
        {
            if (_tracker == null) return;
            if (!_tracker.SheetsApiReady)
            {
                await _tracker.InitGoogleAPIAsync();
            }

            if (!_tracker.SheetsApiReady)
            {
                SetOperationStatus("Sheets API not connected", AppBrushes.RedBrush);
                return;
            }

            string spreadsheetId = _tracker.SpreadsheetId;
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                SetOperationStatus("Missing Spreadsheet ID", AppBrushes.RedBrush);
                return;
            }

            if (_dbManager == null)
            {
                SetOperationStatus("Database manager not available", AppBrushes.RedBrush);
                return;
            }

            SetOperationStatus("Syncing to Sheets...", AppBrushes.MutedBrush);
            await _tracker.SyncOfflinePlaysToSheetsAsync();
            await RefreshPendingSyncCountAsync().ConfigureAwait(false);
            SetOperationStatus("✓ Sync completed", AppBrushes.GreenBrush);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to sync to Sheets");
            SetOperationStatus("✗ Sync failed", AppBrushes.RedBrush);
        }
    }

    private void LoadSettingsFromTracker()
    {
        if (_tracker == null) return;
        _enableLocalLogging = _tracker.EnableLocalLogging;
        _enableSheetsLogging = _tracker.EnableGoogleSheetsLogging;
        _localDatabasePath = _tracker.LocalDatabasePath;
        _sheetName = _tracker.SheetName;
        _spreadsheetId = _tracker.SpreadsheetId;
        _submitSoundEnabled = _tracker.SubmitSoundEnabled;
        _useAltFuncSeparator = _tracker.UseAltFuncSeparator;
        _tosuHost = _tracker.TosuHost;
        _tosuPortText = _tracker.TosuPort.ToString();
        _startupLaunch = AutostartHelper.AutostartExists();
    }

    private void OnTosuConnectionStateChanged(object? sender, bool connected)
    {
        void Apply()
        {
            TosuConnected = connected;
            TosuStatusBrush = connected ? AppBrushes.GreenBrush : AppBrushes.RedBrush;
            TosuStatusText = connected ? "tosu: Connected" : "tosu: Connecting...";
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void DebounceReconnect()
    {
        _reconnectDebounce?.Cancel();
        _reconnectDebounce = new CancellationTokenSource();
        var token = _reconnectDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, token);
                if (!token.IsCancellationRequested && _tosuClient != null)
                {
                    await _tosuClient.ReconnectAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to reconnect to tosu");
            }
        });
    }

    public void Dispose()
    {
        _reconnectDebounce?.Cancel();
        _reconnectDebounce?.Dispose();
        if (_tosuClient != null)
        {
            _tosuClient.ConnectionStateChanged -= OnTosuConnectionStateChanged;
        }
    }
}
