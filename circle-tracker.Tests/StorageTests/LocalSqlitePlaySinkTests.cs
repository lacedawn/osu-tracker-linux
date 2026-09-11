using Circle_Tracker;
using Circle_Tracker.Storage;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.StorageTests
{
    public class LocalSqlitePlaySinkTests
    {
        [Fact]
        public async Task InMemoryIsolation_InitializesSchemaAndMigrations_WithoutTouchingDisk()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);

            await dbManager.InitializeAsync();

            dbManager.IsHealthy.Should().BeTrue();

            await using var conn = await dbManager.CreateConnectionAsync();
            int migrationVersion = await conn.ExecuteScalarAsync<int>("SELECT MAX(version) FROM schema_migrations;");
            migrationVersion.Should().Be(2);

            int sessionsTableCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='sessions';");
            sessionsTableCount.Should().Be(1);

            int playsTableCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='plays';");
            playsTableCount.Should().Be(1);

            var indexes = (await conn.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='plays';")).ToList();

            indexes.Should().Contain("idx_plays_timestamp");
            indexes.Should().Contain("idx_plays_beatmap_id");
            indexes.Should().Contain("idx_plays_checksum");
            indexes.Should().Contain("idx_plays_stars");
            indexes.Should().Contain("idx_plays_mods");
            indexes.Should().Contain("idx_plays_complete");
            indexes.Should().Contain("idx_plays_session");
        }

        [Fact]
        public async Task DataIntegrityTest_LogsFullPlayEntry_AllColumnsMatchExactValues()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();

            await using var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            var sessionManager = new SessionManager(dbManager);
            await sessionManager.InitializeAsync();

            var data = new PlayEntryData(
                BeatmapString: "xi - FREEDOM DiVE [FOUR DIMENSIONS]",
                BeatmapSetID: 39804,
                BeatmapID: 129891,
                Hidden: true,
                Hardrock: true,
                Doubletime: false,
                EZ: false,
                Halftime: false,
                Flashlight: false,
                BeatmapBpm: 222,
                BeatmapAim: 3.85m,
                BeatmapSpeed: 4.12m,
                BeatmapStars: 7.82m,
                BeatmapCs: 4.0m,
                BeatmapAr: 10.0m,
                BeatmapOd: 10.0m,
                TotalBeatmapHits: 1983,
                Accuracy: 99.45m,
                Play300c: 1950,
                Play100c: 33,
                Play50c: 0,
                PlayMissc: 0,
                Complete: true,
                PlayTimeSeconds: 255,
                ModsString: "HDHR",
                PlayCount: 3,
                AccuracyReliable: true,
                BeatmapTitle: "FREEDOM DiVE",
                BeatmapArtist: "xi",
                BeatmapVersion: "FOUR DIMENSIONS",
                BeatmapHp: 7.0m,
                BeatmapChecksum: "d41d8cd98f00b204e9800998ecf8427e"
            );

            var context = new PlayContext(
                SessionId: sessionManager.SessionId,
                IsReplay: false,
                RawMods: 24,
                CurrentGameMode: 0,
                DetectedClient: "osu!stable",
                SoundFilePath: "",
                SubmitSoundEnabled: false
            );

            await sink.TryLogPlayAsync(data, context);

            await using var conn = await dbManager.CreateConnectionAsync();
            var row = await conn.QuerySingleAsync<dynamic>("SELECT * FROM plays WHERE beatmap_id = @id;", new { id = 129891 });

            ((string)row.session_id).Should().Be(sessionManager.SessionId);
            ((long)row.beatmap_id).Should().Be(129891);
            ((long)row.beatmap_set_id).Should().Be(39804);
            ((string)row.beatmap_checksum).Should().Be("d41d8cd98f00b204e9800998ecf8427e");
            ((string)row.beatmap_string).Should().Be("xi - FREEDOM DiVE [FOUR DIMENSIONS]");
            ((string)row.beatmap_title).Should().Be("FREEDOM DiVE");
            ((string)row.beatmap_artist).Should().Be("xi");
            ((string)row.beatmap_version).Should().Be("FOUR DIMENSIONS");
            ((long)row.mods_bitfield).Should().Be(24);
            ((string)row.mods_string).Should().Be("HDHR");
            ((long)row.bpm).Should().Be(222);
            ((double)row.stars).Should().BeApproximately(7.82, 0.001);
            ((double)row.aim).Should().BeApproximately(3.85, 0.001);
            ((double)row.speed).Should().BeApproximately(4.12, 0.001);
            ((double)row.cs).Should().BeApproximately(4.0, 0.001);
            ((double)row.ar).Should().BeApproximately(10.0, 0.001);
            ((double)row.od).Should().BeApproximately(10.0, 0.001);
            ((double)row.hp).Should().BeApproximately(7.0, 0.001);
            ((long)row.total_hits).Should().Be(1983);
            ((long)row.hit_300).Should().Be(1950);
            ((long)row.hit_100).Should().Be(33);
            ((long)row.hit_50).Should().Be(0);
            ((long)row.hit_miss).Should().Be(0);
            ((double)row.accuracy).Should().BeApproximately(99.45, 0.001);
            ((long)row.accuracy_reliable).Should().Be(1);
            ((long)row.is_complete).Should().Be(1);
            ((long)row.play_time_seconds).Should().Be(255);
            ((long)row.consecutive_play_count).Should().Be(3);
            ((long)row.game_mode).Should().Be(0);
            ((long)row.is_replay).Should().Be(0);
            ((string)row.detected_client).Should().Be("osu!stable");
        }

        [Fact]
        public async Task CompositeSinkResilience_WhenGoogleSheetsThrows_SqliteStillCommitsSuccessfully()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();

            await using var sqliteSink = new LocalSqlitePlaySink(dbManager);
            await sqliteSink.InitializeAsync();

            var failingSheetsSink = new Mock<IPlaySink>();
            failingSheetsSink.Setup(s => s.SinkName).Returns("Google Sheets");
            failingSheetsSink.Setup(s => s.IsReady).Returns(true);
            failingSheetsSink.Setup(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()))
                             .ThrowsAsync(new HttpRequestException("Google Sheets network timeout"));

            var compositeSink = new CompositePlaySink();
            compositeSink.AddSink(sqliteSink, () => true);
            compositeSink.AddSink(failingSheetsSink.Object, () => true);

            var session = new SessionManager(dbManager);
            await session.InitializeAsync();

            var data = new PlayEntryData(
                BeatmapString: "Test Artist - Test Title [Hard]",
                BeatmapSetID: 100,
                BeatmapID: 200,
                Hidden: false,
                Hardrock: false,
                Doubletime: false,
                EZ: false,
                Halftime: false,
                Flashlight: false,
                BeatmapBpm: 120,
                BeatmapAim: 2.0m,
                BeatmapSpeed: 2.0m,
                BeatmapStars: 3.5m,
                BeatmapCs: 4.0m,
                BeatmapAr: 8.0m,
                BeatmapOd: 7.0m,
                TotalBeatmapHits: 150,
                Accuracy: 98.0m,
                Play300c: 140,
                Play100c: 10,
                Play50c: 0,
                PlayMissc: 0,
                Complete: true,
                PlayTimeSeconds: 60,
                ModsString: "",
                PlayCount: 1,
                AccuracyReliable: true
            );

            var context = new PlayContext(
                SessionId: session.SessionId,
                IsReplay: false,
                RawMods: 0,
                CurrentGameMode: 0,
                DetectedClient: "osu!lazer",
                SoundFilePath: "",
                SubmitSoundEnabled: false
            );

            var act = async () => await compositeSink.TryLogPlayAsync(data, context);
            await act.Should().NotThrowAsync();

            await using var conn = await dbManager.CreateConnectionAsync();
            int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays WHERE beatmap_id = 200;");
            count.Should().Be(1);
        }

        [Fact]
        public async Task SessionRollup_AggregatesPlayTimeIdleTimeAndPlayCountCorrectly()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();

            await using var session = new SessionManager(dbManager);
            await session.InitializeAsync();

            await session.UpdateStatsAsync(playingSeconds: 150, idleSeconds: 50, clientVersion: "osu!stable");
            await session.IncrementPlaysAsync();
            await session.IncrementPlaysAsync();
            await session.IncrementPlaysAsync();
            await session.EndSessionAsync();

            await using var conn = await dbManager.CreateConnectionAsync();
            var row = await conn.QuerySingleAsync<dynamic>("SELECT * FROM sessions WHERE id = @id;", new { id = session.SessionId });

            ((string)row.id).Should().Be(session.SessionId);
            ((long)row.total_plays).Should().Be(3);
            ((long)row.playing_seconds).Should().Be(150);
            ((long)row.idle_seconds).Should().Be(50);
            ((double)row.efficiency_percent).Should().BeApproximately(75.0, 0.01);
            ((string)row.client_version).Should().Be("osu!stable");
            ((string)row.end_time).Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task ConcurrencyTest_50ConcurrentPlays_ZeroLockBusyErrorsUnderWalMode()
        {
            string tempDbFile = Path.Combine(Path.GetTempPath(), $"ct_concurrency_{Guid.NewGuid():N}.db");
            try
            {
                await using var dbManager = new SqliteDatabaseManager(tempDbFile);
                await dbManager.InitializeAsync();

                await using var sink = new LocalSqlitePlaySink(dbManager);
                await sink.InitializeAsync();

                var sessionManager = new SessionManager(dbManager);
                await sessionManager.InitializeAsync();
                string sessionId = sessionManager.SessionId;
                var tasks = new List<Task>();

                for (int i = 0; i < 50; i++)
                {
                    int index = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        var data = new PlayEntryData(
                            BeatmapString: $"Concurrent Song {index} [Insane]",
                            BeatmapSetID: 1000 + index,
                            BeatmapID: 2000 + index,
                            Hidden: (index % 2) == 0,
                            Hardrock: (index % 3) == 0,
                            Doubletime: (index % 4) == 0,
                            EZ: false,
                            Halftime: false,
                            Flashlight: false,
                            BeatmapBpm: 180,
                            BeatmapAim: 3.0m,
                            BeatmapSpeed: 3.0m,
                            BeatmapStars: 5.5m,
                            BeatmapCs: 4.0m,
                            BeatmapAr: 9.0m,
                            BeatmapOd: 8.0m,
                            TotalBeatmapHits: 500,
                            Accuracy: 98.5m,
                            Play300c: 480,
                            Play100c: 20,
                            Play50c: 0,
                            PlayMissc: 0,
                            Complete: true,
                            PlayTimeSeconds: 90,
                            ModsString: "",
                            PlayCount: 1,
                            AccuracyReliable: true
                        );

                        var context = new PlayContext(
                            SessionId: sessionId,
                            IsReplay: false,
                            RawMods: 0,
                            CurrentGameMode: 0,
                            DetectedClient: "osu!stable",
                            SoundFilePath: "",
                            SubmitSoundEnabled: false
                        );

                        await sink.TryLogPlayAsync(data, context);
                    }));
                }

                await Task.WhenAll(tasks);

                await using var conn = await dbManager.CreateConnectionAsync();
                int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays WHERE session_id = @sid;", new { sid = sessionId });
                count.Should().Be(50);
            }
            finally
            {
                if (File.Exists(tempDbFile))
                {
                    try { File.Delete(tempDbFile); } catch { }
                }
                string wal = $"{tempDbFile}-wal";
                if (File.Exists(wal))
                {
                    try { File.Delete(wal); } catch { }
                }
                string shm = $"{tempDbFile}-shm";
                if (File.Exists(shm))
                {
                    try { File.Delete(shm); } catch { }
                }
            }
        }

        [Fact]
        public async Task Migrations_WhenRunMultipleTimes_AreIdempotent()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);

            await dbManager.InitializeAsync();
            await dbManager.InitializeAsync();

            await using var conn = await dbManager.CreateConnectionAsync();
            int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations;");
            count.Should().Be(2);
        }

        [Fact]
        public async Task CompositeSink_WhenSinkDisabled_DoesNotDispatchToDisabledSink()
        {
            var enabledSink = new Mock<IPlaySink>();
            enabledSink.Setup(s => s.SinkName).Returns("EnabledSink");
            enabledSink.Setup(s => s.IsReady).Returns(true);

            var disabledSink = new Mock<IPlaySink>();
            disabledSink.Setup(s => s.SinkName).Returns("DisabledSink");
            disabledSink.Setup(s => s.IsReady).Returns(true);

            var composite = new CompositePlaySink();
            composite.AddSink(enabledSink.Object, () => true);
            composite.AddSink(disabledSink.Object, () => false);

            var data = new PlayEntryData(
                BeatmapString: "Title", BeatmapSetID: 1, BeatmapID: 1,
                Hidden: false, Hardrock: false, Doubletime: false, EZ: false, Halftime: false, Flashlight: false,
                BeatmapBpm: 120, BeatmapAim: 1, BeatmapSpeed: 1, BeatmapStars: 1,
                BeatmapCs: 4, BeatmapAr: 8, BeatmapOd: 8,
                TotalBeatmapHits: 50, Accuracy: 100,
                Play300c: 50, Play100c: 0, Play50c: 0, PlayMissc: 0,
                Complete: true, PlayTimeSeconds: 30, ModsString: "", PlayCount: 1, AccuracyReliable: true
            );

            var context = new PlayContext(
                SessionId: "session1", IsReplay: false, RawMods: 0, CurrentGameMode: 0,
                DetectedClient: "osu!stable", SoundFilePath: "", SubmitSoundEnabled: false
            );

            await composite.TryLogPlayAsync(data, context);

            var expectedContext = context with { SheetsSyncSucceeded = true };
            enabledSink.Verify(s => s.TryLogPlayAsync(data, expectedContext, It.IsAny<CancellationToken>()), Times.Once);
            disabledSink.Verify(s => s.TryLogPlayAsync(It.IsAny<PlayEntryData>(), It.IsAny<PlayContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task DatabaseManager_ExecuteInTransaction_RollsBackOnError()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();

            var act = async () =>
            {
                await dbManager.ExecuteInTransactionAsync(async (conn, tx) =>
                {
                    await conn.ExecuteAsync(
                        "INSERT INTO sessions (id, start_time, total_plays) VALUES ('tx_test', '2026-01-01', 1);",
                        transaction: tx);
                    throw new InvalidOperationException("Force rollback");
                });
            };

            await act.Should().ThrowAsync<InvalidOperationException>();

            await using var verifyConn = await dbManager.CreateConnectionAsync();
            int count = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sessions WHERE id = 'tx_test';");
            count.Should().Be(0);
        }
        [Fact]
        public async Task Dispose_WithPendingPlay_DoesNotThrow()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            var data = new PlayEntryData(
                BeatmapString: "Song [Hard]", BeatmapSetID: 1, BeatmapID: 1,
                Hidden: false, Hardrock: false, Doubletime: false, EZ: false, Halftime: false, Flashlight: false,
                BeatmapBpm: 120, BeatmapAim: 1m, BeatmapSpeed: 1m, BeatmapStars: 3m,
                BeatmapCs: 4m, BeatmapAr: 8m, BeatmapOd: 7m,
                TotalBeatmapHits: 100, Accuracy: 98m,
                Play300c: 100, Play100c: 0, Play50c: 0, PlayMissc: 0,
                Complete: true, PlayTimeSeconds: 60, ModsString: "", PlayCount: 1, AccuracyReliable: true
            );
            var context = new PlayContext(
                SessionId: "s1", IsReplay: false, RawMods: 0, CurrentGameMode: 0,
                DetectedClient: "test", SoundFilePath: null, SubmitSoundEnabled: false
            );

            _ = Task.Run(() => sink.TryLogPlayAsync(data, context));

            var act = () => sink.Dispose();

            act.Should().NotThrow();
            await dbManager.DisposeAsync();
        }

        [Fact]
        public async Task DisposeAsync_WithNoPendingPlays_CompletesQuickly()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();

            var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            var sw = System.Diagnostics.Stopwatch.StartNew();

            await sink.DisposeAsync();

            sw.Stop();
            sw.Elapsed.TotalMilliseconds.Should().BeLessThan(1000);
        }

        [Fact]
        public async Task Dispose_CalledTwice_DoesNotThrow()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();

            var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            var act = () =>
            {
                sink.Dispose();
                sink.Dispose();
            };

            act.Should().NotThrow();
        }

        [Fact]
        public async Task TryLogPlayAsync_AfterDispose_DoesNotHang()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();

            var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();
            sink.Dispose();

            var data = new PlayEntryData(
                BeatmapString: "Song [Hard]", BeatmapSetID: 1, BeatmapID: 1,
                Hidden: false, Hardrock: false, Doubletime: false, EZ: false, Halftime: false, Flashlight: false,
                BeatmapBpm: 120, BeatmapAim: 1m, BeatmapSpeed: 1m, BeatmapStars: 3m,
                BeatmapCs: 4m, BeatmapAr: 8m, BeatmapOd: 7m,
                TotalBeatmapHits: 100, Accuracy: 98m,
                Play300c: 100, Play100c: 0, Play50c: 0, PlayMissc: 0,
                Complete: true, PlayTimeSeconds: 60, ModsString: "", PlayCount: 1, AccuracyReliable: true
            );
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var context = new PlayContext(
                SessionId: "s1", IsReplay: false, RawMods: 0, CurrentGameMode: 0,
                DetectedClient: "test", SoundFilePath: null, SubmitSoundEnabled: false
            );

            var act = async () => await sink.TryLogPlayAsync(data, context, cts.Token);

            await act.Should().ThrowAsync<Exception>();
        }

        private static PlayEntryData BuildSyncStatusPlayData(int beatmapId)
        {
            return new PlayEntryData(
                BeatmapString: "Song [Hard]", BeatmapSetID: 1, BeatmapID: beatmapId,
                Hidden: false, Hardrock: false, Doubletime: false, EZ: false, Halftime: false, Flashlight: false,
                BeatmapBpm: 120, BeatmapAim: 1m, BeatmapSpeed: 1m, BeatmapStars: 3m,
                BeatmapCs: 4m, BeatmapAr: 8m, BeatmapOd: 7m,
                TotalBeatmapHits: 100, Accuracy: 98m,
                Play300c: 100, Play100c: 0, Play50c: 0, PlayMissc: 0,
                Complete: true, PlayTimeSeconds: 60, ModsString: "", PlayCount: 1, AccuracyReliable: true
            );
        }

        private static PlayContext BuildSyncStatusContext(string sessionId, bool? sheetsSyncSucceeded)
        {
            return new PlayContext(
                SessionId: sessionId, IsReplay: false, RawMods: 0, CurrentGameMode: 0,
                DetectedClient: "test", SoundFilePath: null, SubmitSoundEnabled: false,
                SheetsSyncSucceeded: sheetsSyncSucceeded
            );
        }

        [Fact]
        public async Task InsertPlay_WithoutSheetsContext_StoresSyncedStatus()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            await sink.TryLogPlayAsync(BuildSyncStatusPlayData(901), BuildSyncStatusContext(session.SessionId, null));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 901;");

            status.Should().Be("Synced");
        }

        [Fact]
        public async Task InsertPlay_WithSheetsSyncSucceededTrue_StoresSyncedStatus()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            await sink.TryLogPlayAsync(BuildSyncStatusPlayData(902), BuildSyncStatusContext(session.SessionId, true));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 902;");

            status.Should().Be("Synced");
        }

        [Fact]
        public async Task InsertPlay_WithSheetsSyncSucceededTrue_StoresSyncedTimestamp()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            await sink.TryLogPlayAsync(BuildSyncStatusPlayData(903), BuildSyncStatusContext(session.SessionId, true));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? syncedAt = await conn.ExecuteScalarAsync<string?>("SELECT synced_at FROM plays WHERE beatmap_id = 903;");

            syncedAt.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task InsertPlay_WithSheetsSyncSucceededFalse_StoresPendingStatus()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            await sink.TryLogPlayAsync(BuildSyncStatusPlayData(904), BuildSyncStatusContext(session.SessionId, false));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? status = await conn.ExecuteScalarAsync<string>("SELECT sync_status FROM plays WHERE beatmap_id = 904;");

            status.Should().Be("Pending");
        }

        [Fact]
        public async Task InsertPlay_WithoutSheetsContext_StoresSyncedTimestamp()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            await using var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var session = new SessionManager(dbManager);
            await session.InitializeAsync();
            await using var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            await sink.TryLogPlayAsync(BuildSyncStatusPlayData(905), BuildSyncStatusContext(session.SessionId, null));

            await using var conn = await dbManager.CreateConnectionAsync();
            string? syncedAt = await conn.ExecuteScalarAsync<string?>("SELECT synced_at FROM plays WHERE beatmap_id = 905;");

            syncedAt.Should().NotBeNullOrEmpty();
        }
    }
}
