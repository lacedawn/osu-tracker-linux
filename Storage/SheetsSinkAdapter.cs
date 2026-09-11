using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage;

internal class SheetsSinkAdapter : IPlaySink, ISheetsSink
{
    private readonly ISheetsSink _sink;

    public string SinkName => "Google Sheets Adapter";
    public bool IsReady => _sink.SheetsApiReady;
    public bool SheetsApiReady => _sink.SheetsApiReady;
    public bool SpreadsheetTimezoneVerified
    {
        get => _sink.SpreadsheetTimezoneVerified;
        set => _sink.SpreadsheetTimezoneVerified = value;
    }
    public bool UseAltFuncSeparator
    {
        get => _sink.UseAltFuncSeparator;
        set => _sink.UseAltFuncSeparator = value;
    }
    public string SpreadsheetId
    {
        get => _sink.SpreadsheetId;
        set => _sink.SpreadsheetId = value;
    }
    public string SheetName
    {
        get => _sink.SheetName;
        set => _sink.SheetName = value;
    }
    public int SheetRows => _sink.SheetRows;
    public Action? OnSettingsChanged
    {
        get => _sink.OnSettingsChanged;
        set => _sink.OnSettingsChanged = value;
    }

    public Task InitializeAsync(bool silent = false, CancellationToken ct = default)
    {
        return _sink.InitGoogleAPIAsync(silent);
    }

    public Task InitGoogleAPIAsync(bool silent = false)
    {
        return _sink.InitGoogleAPIAsync(silent);
    }

    public Task<bool> TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct = default)
    {
        return _sink.TryLogPlayAsync(data, context, ct);
    }

    async Task IPlaySink.TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct)
    {
        await _sink.TryLogPlayAsync(data, context, ct);
    }

    public SheetsSinkAdapter(ISheetsSink sink) => _sink = sink;
}
