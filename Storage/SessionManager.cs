using Dapper;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage
{
    public class SessionManager : IDisposable, IAsyncDisposable
    {
        private static readonly ILogger<SessionManager> _log = AppLogger.For<SessionManager>();

        private readonly IDatabaseManager _dbManager;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private bool _isInitialized = false;

        public string SessionId { get; }
        public DateTime StartTimeUtc { get; }
        public DateTime? EndTimeUtc { get; private set; }
        public int TotalPlays { get; private set; }
        public int PlayingSeconds { get; private set; }
        public int IdleSeconds { get; private set; }
        public double EfficiencyPercent => (PlayingSeconds + IdleSeconds) > 0
            ? (100.0 * PlayingSeconds / (PlayingSeconds + IdleSeconds))
            : 0.0;
        public string ClientVersion { get; set; } = "Unknown";

        public SessionManager(IDatabaseManager dbManager, string? sessionId = null)
        {
            _dbManager = dbManager;
            SessionId = sessionId ?? Guid.NewGuid().ToString();
            StartTimeUtc = DateTime.UtcNow;
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_isInitialized) return;

            await _lock.WaitAsync(ct);
            try
            {
                if (_isInitialized) return;
                await UpsertSessionRowAsync(ct);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to initialize session {SessionId}", SessionId);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task UpdateStatsAsync(int playingSeconds, int idleSeconds, string? clientVersion = null, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                PlayingSeconds = playingSeconds;
                IdleSeconds = idleSeconds;
                if (!string.IsNullOrEmpty(clientVersion) && clientVersion != "Unknown" && clientVersion != "Connecting..." && clientVersion != "Disconnected")
                {
                    ClientVersion = clientVersion;
                }
                await UpsertSessionRowAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to update session stats for {SessionId}", SessionId);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task IncrementPlaysAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                TotalPlays++;
                await UpsertSessionRowAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to increment session play count for {SessionId}", SessionId);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task EndSessionAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                EndTimeUtc = DateTime.UtcNow;
                await UpsertSessionRowAsync(ct);
                _log.LogInformation("Session {SessionId} ended cleanly", SessionId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to end session {SessionId}", SessionId);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task UpsertSessionRowAsync(CancellationToken ct)
        {
            if (!_dbManager.IsHealthy) return;

            const string sql = @"
                INSERT INTO sessions (
                    id, start_time, end_time, total_plays,
                    playing_seconds, idle_seconds, efficiency_percent, client_version
                ) VALUES (
                    @Id, @StartTime, @EndTime, @TotalPlays,
                    @PlayingSeconds, @IdleSeconds, @EfficiencyPercent, @ClientVersion
                )
                ON CONFLICT(id) DO UPDATE SET
                    end_time = excluded.end_time,
                    total_plays = excluded.total_plays,
                    playing_seconds = excluded.playing_seconds,
                    idle_seconds = excluded.idle_seconds,
                    efficiency_percent = excluded.efficiency_percent,
                    client_version = CASE
                        WHEN excluded.client_version IS NOT NULL AND excluded.client_version != 'Unknown'
                        THEN excluded.client_version
                        ELSE sessions.client_version
                    END;";

            var parameters = new
            {
                Id = SessionId,
                StartTime = StartTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                EndTime = EndTimeUtc?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                TotalPlays = TotalPlays,
                PlayingSeconds = PlayingSeconds,
                IdleSeconds = IdleSeconds,
                EfficiencyPercent = EfficiencyPercent,
                ClientVersion = ClientVersion
            };

            await using var conn = await _dbManager.CreateConnectionAsync(ct);
            await conn.ExecuteAsync(sql, parameters);
        }

        public void Dispose()
        {
            try
            {
                EndSessionAsync().GetAwaiter().GetResult();
            }
            catch { }
            _lock.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await EndSessionAsync();
            }
            catch { }
            _lock.Dispose();
        }
    }
}
