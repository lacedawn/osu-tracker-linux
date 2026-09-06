using System.Text.Json.Serialization;

namespace Circle_Tracker
{
    public class UserSettings
    {
        [JsonPropertyName("enableLocalLogging")] public bool EnableLocalLogging { get; set; } = true;
        [JsonPropertyName("enableGoogleSheetsLogging")] public bool EnableGoogleSheetsLogging { get; set; } = false;
        [JsonPropertyName("localDatabasePath")] public string LocalDatabasePath { get; set; } = "";
        [JsonPropertyName("spreadsheetId")] public string SpreadsheetId { get; set; } = "";
        [JsonPropertyName("sheetName")] public string SheetName { get; set; } = "Raw Data";
        [JsonPropertyName("submitSoundEnabled")] public bool SubmitSoundEnabled { get; set; } = true;
        [JsonPropertyName("spreadsheetTimezoneVerified")] public bool SpreadsheetTimezoneVerified { get; set; } = false;
        [JsonPropertyName("useAltFuncSeparator")] public bool UseAltFuncSeparator { get; set; } = false;
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("tosuHost")] public string TosuHost { get; set; } = "127.0.0.1";
        [JsonPropertyName("tosuPort")] public int TosuPort { get; set; } = 24050;
        [JsonPropertyName("disableBackgroundAnimationsWhenUnfocused")] public bool DisableBackgroundAnimationsWhenUnfocused { get; set; } = false;
        [JsonPropertyName("updateRepository")] public string UpdateRepository { get; set; } = "lacedawn/osu-tracker-linux";
        public static bool GlobalDisableBackgroundAnimationsWhenUnfocused { get; set; } = false;
    }
}
