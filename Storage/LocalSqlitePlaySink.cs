using Dapper;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage
{
    public class LocalSqlitePlaySink : IPlaySink, IDisposable, IAsyncDisposable
    {
        private static readonly ILogger<LocalSqlitePlaySink> _log = AppLogger.For<LocalSqlitePlaySink>();

        private readonly IDatabaseManager _dbManager;
        private readonly Channel<(PlayEntryData Data, PlayContext Context, TaskCompletionSource<bool>? Completion)> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _workerTask;
        private int _totalPlaysRecorded = 0;
        private int _disposed = 0;

        public string SinkName => "Local SQLite";
        public bool IsReady => _dbManager.IsHealthy;
        public int TotalPlaysRecorded => _totalPlaysRecorded;
        public Action? OnPlayCommitted { get; set; }

        public LocalSqlitePlaySink(IDatabaseManager dbManager, Action? onPlayCommitted = null)
        {
            _dbManager = dbManager;
            OnPlayCommitted = onPlayCommitted;
            _channel = Channel.CreateUnbounded<(PlayEntryData, PlayContext, TaskCompletionSource<bool>?)>(
                new UnboundedChannelOptions { SingleReader = true });
            _workerTask = Task.Run(ProcessQueueAsync);
        }

        public async Task InitializeAsync(bool silent = false, CancellationToken ct = default)
        {
            try
            {
                await _dbManager.InitializeAsync(ct);
                await RefreshPlayCountAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to initialize LocalSqlitePlaySink");
                if (!silent) throw;
            }
        }

        private async Task RefreshPlayCountAsync(CancellationToken ct = default)
        {
            try
            {
                await using var conn = await _dbManager.CreateConnectionAsync(ct);
                _totalPlaysRecorded = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays;");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not query total plays count");
            }
        }

        public async Task TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_channel.Writer.TryWrite((data, context, tcs)))
            {
                await _channel.Writer.WriteAsync((data, context, tcs), ct);
            }
            await tcs.Task;
        }

        private async Task ProcessQueueAsync()
        {
            var reader = _channel.Reader;
            try
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out var item))
                    {
                        try
                        {
                            await InsertPlayAsync(item.Data, item.Context, _cts.Token).ConfigureAwait(false);
                            Interlocked.Increment(ref _totalPlaysRecorded);
                            OnPlayCommitted?.Invoke();
                            if (item.Context.SubmitSoundEnabled && !string.IsNullOrEmpty(item.Context.SoundFilePath))
                            {
                                SoundHelper.PlaySound(item.Context.SoundFilePath);
                            }
                            item.Completion?.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Failed to insert play into SQLite");
                            item.Completion?.TrySetException(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "SQLite background write loop terminated unexpectedly");
            }
        }

        private async Task InsertPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct)
        {
            string syncStatus = DetermineSyncStatus(context);

            const string sql = @"
                INSERT INTO plays (
                    session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_checksum,
                    beatmap_string, beatmap_title, beatmap_artist, beatmap_version,
                    mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp,
                    total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable,
                    is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client,
                    sync_status, synced_at
                ) VALUES (
                    @SessionId, @Timestamp, @BeatmapId, @BeatmapSetId, @BeatmapChecksum,
                    @BeatmapString, @BeatmapTitle, @BeatmapArtist, @BeatmapVersion,
                    @ModsBitfield, @ModsString, @Bpm, @Stars, @Aim, @Speed, @Cs, @Ar, @Od, @Hp,
                    @TotalHits, @Hit300, @Hit100, @Hit50, @HitMiss, @Accuracy, @AccuracyReliable,
                    @IsComplete, @PlayTimeSeconds, @ConsecutivePlayCount, @GameMode, @IsReplay, @DetectedClient,
                    @SyncStatus, @SyncedAt
                );";

            var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var parameters = new
            {
                SessionId = string.IsNullOrWhiteSpace(context.SessionId) ? null : context.SessionId,
                Timestamp = nowUtc,
                BeatmapId = data.BeatmapID,
                BeatmapSetId = data.BeatmapSetID,
                BeatmapChecksum = data.BeatmapChecksum ?? "",
                BeatmapString = data.BeatmapString ?? "",
                BeatmapTitle = data.BeatmapTitle ?? "",
                BeatmapArtist = data.BeatmapArtist ?? "",
                BeatmapVersion = data.BeatmapVersion ?? "",
                ModsBitfield = context.RawMods,
                ModsString = data.ModsString ?? "",
                Bpm = data.BeatmapBpm,
                Stars = (double)data.BeatmapStars,
                Aim = (double)data.BeatmapAim,
                Speed = (double)data.BeatmapSpeed,
                Cs = (double)data.BeatmapCs,
                Ar = (double)data.BeatmapAr,
                Od = (double)data.BeatmapOd,
                Hp = (double)data.BeatmapHp,
                TotalHits = data.TotalBeatmapHits,
                Hit300 = data.Play300c,
                Hit100 = data.Play100c,
                Hit50 = data.Play50c,
                HitMiss = data.PlayMissc,
                Accuracy = (double)data.Accuracy,
                AccuracyReliable = data.AccuracyReliable ? 1 : 0,
                IsComplete = data.Complete ? 1 : 0,
                PlayTimeSeconds = data.PlayTimeSeconds,
                ConsecutivePlayCount = data.PlayCount,
                GameMode = context.CurrentGameMode,
                IsReplay = context.IsReplay ? 1 : 0,
                DetectedClient = context.DetectedClient ?? "",
                SyncStatus = syncStatus,
                SyncedAt = syncStatus == "Synced" ? nowUtc : (string?)null
            };

            await using var conn = await _dbManager.CreateConnectionAsync(ct);
            await conn.ExecuteAsync(sql, parameters);
        }

        private static string DetermineSyncStatus(PlayContext context)
        {
            return context.SheetsSyncSucceeded == false ? "Pending" : "Synced";
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _channel.Writer.TryComplete();
            try
            {
                _workerTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException) { }
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException) { }
            _cts.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _channel.Writer.TryComplete();
            try
            {
                await _workerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException) { }
            _cts.Dispose();
        }
    }
}
