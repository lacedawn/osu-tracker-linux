using Circle_Tracker;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace Circle_Tracker.Services;

public static class AppPaths
{
    private static readonly ILogger _log = AppLogger.Factory.CreateLogger("AppPaths");

    public static string LegacyBaseDirectory => AppContext.BaseDirectory;

    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "circle-tracker");

    public static string SettingsPath => Path.Combine(AppDataDirectory, "user_settings.json");

    public static string LegacyJsonPath => Path.Combine(LegacyBaseDirectory, "user_settings.json");

    public static string LegacyTxtPath => Path.Combine(AppDataDirectory, "user_settings.txt");

    public static string OldLegacyTxtPath => Path.Combine(LegacyBaseDirectory, "user_settings.txt");

    public static string CredentialsPath => Path.Combine(AppDataDirectory, "credentials.json");

    public static string LegacyCredentialsPath => Path.Combine(LegacyBaseDirectory, "credentials.json");

    public static string DatabasePath => Path.Combine(AppDataDirectory, "circle_tracker.db");

    public static string LegacyDatabasePath => Path.Combine(LegacyBaseDirectory, "circle_tracker.db");

    public static string LogPath => Path.Combine(AppDataDirectory, "circle-tracker.log");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
    }

    public static void MigrateLegacyFiles()
    {
        MigrateLegacyFiles(LegacyBaseDirectory, AppDataDirectory);
    }

    internal static void MigrateLegacyFiles(string legacyDirectory, string targetDirectory)
    {
        MigrateLegacyFile(Path.Combine(legacyDirectory, "user_settings.json"), Path.Combine(targetDirectory, "user_settings.json"));
        MigrateLegacyFile(Path.Combine(legacyDirectory, "user_settings.txt"), Path.Combine(targetDirectory, "user_settings.txt"));
        MigrateLegacyFile(Path.Combine(legacyDirectory, "credentials.json"), Path.Combine(targetDirectory, "credentials.json"));
        MigrateLegacyFile(Path.Combine(legacyDirectory, "circle_tracker.db"), Path.Combine(targetDirectory, "circle_tracker.db"));
    }

    private static void MigrateLegacyFile(string sourcePath, string targetPath)
    {
        try
        {
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(sourcePath) || File.Exists(targetPath)) return;

            string? targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

            try
            {
                File.Move(sourcePath, targetPath);
            }
            catch (IOException)
            {
                File.Copy(sourcePath, targetPath);
                File.Delete(sourcePath);
            }

            _log.LogInformation("Migrated {File} to application data directory", Path.GetFileName(sourcePath));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to migrate {File}", Path.GetFileName(sourcePath));
        }
    }

    public static string FindCredentialsFile()
    {
        if (File.Exists(CredentialsPath)) return CredentialsPath;
        if (File.Exists(LegacyCredentialsPath)) return LegacyCredentialsPath;
        return CredentialsPath;
    }

    public static bool CredentialsExist()
    {
        return File.Exists(CredentialsPath) || File.Exists(LegacyCredentialsPath);
    }
}
