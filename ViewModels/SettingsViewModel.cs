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
        CredentialsFound = File.Exists(Path.Combine(AppContext.BaseDirectory, "credentials.json"));
        CredentialsStatusText = CredentialsFound ? "Found" : "Missing";
        CredentialsStatusBrush = CredentialsFound ? AppBrushes.GreenBrush : AppBrushes.RedBrush;
    }

    public async Task ConnectSheetsAsync()
    {
        try
        {
            _statusCallback?.Invoke("Connecting to Sheets...");
            if (_tracker != null)
            {
                await _tracker.InitGoogleAPIAsync();
                SheetsConnected = _tracker.SheetsApiReady;
                SheetsStatusText = _tracker.SheetsApiReady ? "Sheets: Connected" : "Sheets: Not connected";
                SheetsStatusBrush = _tracker.SheetsApiReady ? AppBrushes.GreenBrush : AppBrushes.RedBrush;
                _statusCallback?.Invoke(_tracker.SheetsApiReady ? "Sheets connected" : "Sheets connection failed");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to connect to Sheets");
            _statusCallback?.Invoke("Sheets connection failed");
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
                _statusCallback?.Invoke("Sheets API not connected");
                return;
            }

            string spreadsheetId = _tracker.SpreadsheetId;
            string sheetName = _tracker.SheetName;
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                _statusCallback?.Invoke("Missing Spreadsheet ID");
                return;
            }

            var sheetsService = GoogleSheetsManager.CreateSheetsService();
            if (sheetsService == null)
            {
                _statusCallback?.Invoke("Failed to create Google Sheets service");
                return;
            }

            if (_dbManager == null)
            {
                _statusCallback?.Invoke("Database manager not available");
                return;
            }

            _statusCallback?.Invoke("Importing sheets data...");
            var importer = new GoogleSheetsHistoricalImporter(sheetsService, _dbManager);
            var result = await importer.ImportFromSpreadsheetAsync(spreadsheetId, sheetName, null, CancellationToken.None);

            if (result.Success)
            {
                _statusCallback?.Invoke($"Imported {result.SyncedCount} plays");
            }
            else
            {
                _statusCallback?.Invoke("Import failed");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to import from Sheets");
            _statusCallback?.Invoke("Import error");
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
