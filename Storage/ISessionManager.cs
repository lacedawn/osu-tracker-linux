using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage;

public interface ISessionManager
{
    string SessionId { get; }
    DateTime StartTimeUtc { get; }
    DateTime? EndTimeUtc { get; }
    int TotalPlays { get; }
    int PlayingSeconds { get; }
    int IdleSeconds { get; }
    double EfficiencyPercent { get; }
    string ClientVersion { get; set; }
    IDatabaseManager GetDatabaseManager();
    Task InitializeAsync(CancellationToken ct = default);
    Task UpdateStatsAsync(int playingSeconds, int idleSeconds, string? clientVersion = null, CancellationToken ct = default);
    Task IncrementPlaysAsync(CancellationToken ct = default);
    Task EndSessionAsync(CancellationToken ct = default);
}
