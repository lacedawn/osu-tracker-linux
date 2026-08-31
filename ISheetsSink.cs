using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public interface ISheetsSink
    {
        bool SheetsApiReady { get; }
        bool SpreadsheetTimezoneVerified { get; set; }
        bool UseAltFuncSeparator { get; set; }
        string SpreadsheetId { get; set; }
        string SheetName { get; set; }
        int SheetRows { get; }
        Action? OnSettingsChanged { get; set; }

        void InitGoogleAPI(bool silent = false);

        Task TryAppendPlayEntry(PlayEntryData data, bool isReplay, int rawMods, int currentGameMode,
            DateTime lastPostTime, Action<DateTime> setLastPostTime,
            string soundFilePath, bool submitSoundEnabled,
            CancellationToken ct = default);
    }
}
