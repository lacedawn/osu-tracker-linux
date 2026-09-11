using Circle_Tracker.Storage;
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

        Task InitGoogleAPIAsync(bool silent = false);
        Task<bool> TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct = default);
    }
}
