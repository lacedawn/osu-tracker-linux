using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage
{
    public interface IPlaySink
    {
        string SinkName { get; }
        bool IsReady { get; }
        Task InitializeAsync(bool silent = false, CancellationToken ct = default);
        Task TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct = default);
    }

    public record PlayContext(
        string SessionId,
        bool IsReplay,
        int RawMods,
        int CurrentGameMode,
        string DetectedClient,
        string SoundFilePath,
        bool SubmitSoundEnabled,
        bool? SheetsSyncSucceeded = null
    );
}
