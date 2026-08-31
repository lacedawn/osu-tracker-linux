using Newtonsoft.Json;

namespace Circle_Tracker
{
    public class UserSettings
    {
        [JsonProperty("spreadsheetId")] public string SpreadsheetId { get; set; } = "";
        [JsonProperty("sheetName")] public string SheetName { get; set; } = "Raw Data";
        [JsonProperty("submitSoundEnabled")] public bool SubmitSoundEnabled { get; set; } = true;
        [JsonProperty("spreadsheetTimezoneVerified")] public bool SpreadsheetTimezoneVerified { get; set; } = false;
        [JsonProperty("useAltFuncSeparator")] public bool UseAltFuncSeparator { get; set; } = false;
        [JsonProperty("username")] public string Username { get; set; } = "";
        [JsonProperty("tosuHost")] public string TosuHost { get; set; } = "127.0.0.1";
        [JsonProperty("tosuPort")] public int TosuPort { get; set; } = 24050;
    }
}
