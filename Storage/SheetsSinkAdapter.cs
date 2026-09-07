using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage;

internal class SheetsSinkAdapter : IPlaySink
{
    private readonly ISheetsSink _sink;
    
    public string SinkName => "Google Sheets Adapter";
    public bool IsReady => _sink.SheetsApiReady;
    
    public Task InitializeAsync(bool silent = false, CancellationToken ct = default)
    {
        return _sink.InitGoogleAPIAsync(silent);
    }
    
    public Task TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct = default)
        => Task.CompletedTask;
    
    public SheetsSinkAdapter(ISheetsSink sink) => _sink = sink;
}
