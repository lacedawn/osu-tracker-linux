using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Circle_Tracker.Services;

public class SettingsService : ISettingsService
{
    private static readonly ILogger<SettingsService> _log = AppLogger.For<SettingsService>();

    public const string DefaultTosuHost = "127.0.0.1";
    public const int DefaultTosuPort = 24050;

    private bool _disableBackgroundAnimationsWhenUnfocused;

    public string SettingsFilePath { get; }
    public string OldSettingsFilePath { get; }
    public string SoundFilePath => FindFile(Path.Combine("assets", "sectionpass.wav"));

    public bool EnableLocalLogging { get; set; } = true;
    public bool EnableGoogleSheetsLogging { get; set; } = false;
    public string LocalDatabasePath { get; set; } = "";
    public string TosuHost { get; set; } = DefaultTosuHost;
    public int TosuPort { get; set; } = DefaultTosuPort;
    public bool SubmitSoundEnabled { get; set; } = true;
    public string Username { get; set; } = "";

    public bool DisableBackgroundAnimationsWhenUnfocused
    {
        get => _disableBackgroundAnimationsWhenUnfocused;
        set => _disableBackgroundAnimationsWhenUnfocused = value;
    }

    public string SpreadsheetId { get; set; } = "";
    public string SheetName { get; set; } = "Raw Data";
    public bool UseAltFuncSeparator { get; set; } = false;
    public bool SpreadsheetTimezoneVerified { get; set; } = false;
    public string UpdateRepository { get; set; } = Updater.DefaultRepository;

    public event Action? SettingsChanged;

    public SettingsService() : this(null, null)
    {
    }

    public SettingsService(string? settingsFilePath = null, string? oldSettingsFilePath = null)
    {
        if (settingsFilePath == null && oldSettingsFilePath == null)
        {
            AppPaths.MigrateLegacyFiles();
        }
        SettingsFilePath = settingsFilePath ?? AppPaths.SettingsPath;
        OldSettingsFilePath = oldSettingsFilePath ?? AppPaths.LegacyTxtPath;
        LoadSettings();
    }

    public static string FindFile(string relativePath)
    {
        return AppPaths.ResolveShippedAssetPath(relativePath);
    }

    public string GetFunctionSeparator() => UseAltFuncSeparator ? ";" : ",";

    public static string SanitizeTosuHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return DefaultTosuHost;
        }

        string trimmed = host.Trim();

        if (trimmed.Length == 0)
        {
            return DefaultTosuHost;
        }

        foreach (char c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                return DefaultTosuHost;
            }
        }

        return trimmed;
    }

    public static int SanitizeTosuPort(int port)
    {
        if (port < 1 || port > 65535)
        {
            return DefaultTosuPort;
        }

        return port;
    }

    public static int SanitizeTosuPortText(string? portText)
    {
        if (!int.TryParse(portText?.Trim(), out int port))
        {
            return DefaultTosuPort;
        }

        return SanitizeTosuPort(port);
    }

    public void SaveSettings()
    {
        try
        {
            TosuHost = SanitizeTosuHost(TosuHost);
            TosuPort = SanitizeTosuPort(TosuPort);
            UpdateRepository = Updater.ResolveRepository(UpdateRepository);
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
                DisableBackgroundAnimationsWhenUnfocused = DisableBackgroundAnimationsWhenUnfocused,
                UpdateRepository = UpdateRepository
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
        TosuHost = DefaultTosuHost;
        TosuPort = DefaultTosuPort;
        DisableBackgroundAnimationsWhenUnfocused = false;
        UpdateRepository = Updater.DefaultRepository;

        if (!File.Exists(SettingsFilePath))
        {
            if (File.Exists(OldSettingsFilePath))
            {
                MigrateOldSettings(OldSettingsFilePath);
            }
            else if (File.Exists(AppPaths.OldLegacyTxtPath))
            {
                MigrateOldSettings(AppPaths.OldLegacyTxtPath);
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
                TosuHost = SanitizeTosuHost(settings.TosuHost);
                TosuPort = SanitizeTosuPort(settings.TosuPort);
                DisableBackgroundAnimationsWhenUnfocused = settings.DisableBackgroundAnimationsWhenUnfocused;
                UpdateRepository = Updater.ResolveRepository(settings.UpdateRepository);
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
            if (lines.Length > 6) TosuHost = SanitizeTosuHost(lines[6]);
            if (lines.Length > 7) TosuPort = SanitizeTosuPortText(lines[7]);
            SaveSettings();
            _log.LogInformation("Migrated settings from old text format to JSON");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to migrate old settings");
        }
    }
}
