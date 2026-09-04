using Circle_Tracker;
using Circle_Tracker.Analytics;
using Circle_Tracker.Storage;
using Dapper;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.AnalyticsTests
{
    public class SessionAnalyticsServiceTests
    {
        private static async Task<(SqliteDatabaseManager Db, LocalSqlitePlaySink Sink, CachedAnalyticsService Analytics, SessionManager Session)> CreateTestEnvironmentAsync()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();

            var skillService = new SkillAnalyticsService(dbManager);
            var sessionService = new SessionAnalyticsService(dbManager);
            var cachedAnalytics = new CachedAnalyticsService(skillService, sessionService);

            var sink = new LocalSqlitePlaySink(dbManager, () => cachedAnalytics.InvalidateCache());
            await sink.InitializeAsync();

            var session = new SessionManager(dbManager);
            await session.InitializeAsync();

            return (dbManager, sink, cachedAnalytics, session);
        }

        private static PlayEntryData CreatePlay(
            int beatmapId = 1,
            int beatmapSetId = 1,
            string title = "Song",
            double stars = 5.0,
            int bpm = 180,
            decimal accuracy = 98.0m,
            bool complete = true,
            int h300 = 100,
            int h100 = 10,
            int misses = 0,
            int totalHits = 110,
            int playTimeSeconds = 60,
            int playCount = 1)
        {
            return new PlayEntryData(
                BeatmapString: $"{title} [Hard]",
                BeatmapSetID: beatmapSetId,
                BeatmapID: beatmapId,
                Hidden: false,
                Hardrock: false,
                Doubletime: false,
                EZ: false,
                Halftime: false,
                Flashlight: false,
                BeatmapBpm: bpm,
                BeatmapAim: 2.5m,
                BeatmapSpeed: 2.5m,
                BeatmapStars: (decimal)stars,
                BeatmapCs: 4.0m,
                BeatmapAr: 9.0m,
                BeatmapOd: 8.0m,
                TotalBeatmapHits: totalHits,
                Accuracy: accuracy,
                Play300c: h300,
                Play100c: h100,
                Play50c: 0,
                PlayMissc: misses,
                Complete: complete,
                PlayTimeSeconds: playTimeSeconds,
                ModsString: "",
                PlayCount: playCount,
                AccuracyReliable: true
            );
        }


        [Fact]
        public async Task RollingMovingAverages_PartitionsWindowsCorrectly()
        {
            var (db, _, analytics, _) = await CreateTestEnvironmentAsync();

            DateTime now = DateTime.UtcNow;

            await using (var conn = await db.CreateConnectionAsync())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_string, beatmap_title, beatmap_artist, beatmap_version, mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp, total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable, is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                    VALUES (NULL, @t1, 1, 1, 'Recent Song', 'Recent Song', 'Artist', 'Diff', 0, '', 180, 5.0, 2.5, 2.5, 4.0, 9.0, 8.0, 6.0, 100, 98, 2, 0, 0, 99.0, 1, 1, 120, 1, 0, 0, 'osu!stable');

                    INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_string, beatmap_title, beatmap_artist, beatmap_version, mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp, total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable, is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                    VALUES (NULL, @t2, 2, 2, 'Medium Song', 'Medium Song', 'Artist', 'Diff', 0, '', 200, 6.0, 3.0, 3.0, 4.0, 9.0, 8.0, 6.0, 100, 95, 5, 0, 0, 97.0, 1, 1, 180, 1, 0, 0, 'osu!stable');

                    INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_string, beatmap_title, beatmap_artist, beatmap_version, mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp, total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable, is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                    VALUES (NULL, @t3, 3, 3, 'Old Song', 'Old Song', 'Artist', 'Diff', 0, '', 220, 7.0, 3.5, 3.5, 4.0, 9.0, 8.0, 6.0, 100, 90, 10, 0, 0, 95.0, 1, 0, 240, 1, 0, 0, 'osu!stable');",
                    new
                    {
                        t1 = now.AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        t2 = now.AddDays(-20).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        t3 = now.AddDays(-60).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    });
            }

            var rolling = await analytics.GetRollingAveragesAsync();

            rolling.Should().ContainKeys("7D", "30D", "90D");

            var r7 = rolling["7D"];
            r7.TotalPlays.Should().Be(1);
            r7.MeanAccuracy.Should().Be(99.0m);
            r7.MeanStars.Should().Be(5.0m);
            r7.PassRatePercent.Should().Be(100.0);
            r7.MeanBpm.Should().Be(180.0);

            var r30 = rolling["30D"];
            r30.TotalPlays.Should().Be(2);
            r30.MeanAccuracy.Should().Be(98.0m);
            r30.MeanStars.Should().Be(5.5m);
            r30.PassRatePercent.Should().Be(100.0);
            r30.MeanBpm.Should().Be(190.0);

            var r90 = rolling["90D"];
            r90.TotalPlays.Should().Be(3);
            r90.MeanAccuracy.Should().Be(98.0m);
            r90.MeanStars.Should().Be(5.5m);
            r90.PassRatePercent.Should().BeApproximately(66.67, 0.01);
            r90.MeanBpm.Should().Be(190.0);
        }

        [Fact]
        public async Task ChokeDetection_IdentifiesNearFcHeartbreaksOnGrindedMaps()
        {
            var (db, sink, analytics, session) = await CreateTestEnvironmentAsync();
            var context = new PlayContext(session.SessionId, false, 0, 0, "osu!stable", "", false);

            for (int i = 1; i <= 4; i++)
            {
                await sink.TryLogPlayAsync(CreatePlay(beatmapId: 101, beatmapSetId: 50, title: "Grind Map", accuracy: 92.0m, misses: 5, playCount: i), context);
            }
            await sink.TryLogPlayAsync(CreatePlay(beatmapId: 101, beatmapSetId: 50, title: "Grind Map", accuracy: 98.5m, misses: 1, playCount: 5), context);

            await sink.TryLogPlayAsync(CreatePlay(beatmapId: 202, beatmapSetId: 60, title: "Non Grinded Choke", accuracy: 99.0m, misses: 1, playCount: 1), context);

            var chokes = await analytics.GetTopChokeMapsAsync();

            chokes.Should().HaveCount(1);
            var topChoke = chokes.First();
            topChoke.BeatmapId.Should().Be(101);
            topChoke.BeatmapSetId.Should().Be(50);
            topChoke.ChokeCount.Should().Be(1);
            topChoke.BestChokeAcc.Should().Be(98.5m);
            topChoke.MinMisses.Should().Be(1);
            topChoke.TotalMapAttempts.Should().Be(5);
        }

        [Fact]
        public async Task HeadToHead_ComparesSessionAgainst30DayBaseline()
        {
            var (db, _, analytics, _) = await CreateTestEnvironmentAsync();

            DateTime now = DateTime.UtcNow;
            string targetSessionId = "target_session";
            string otherSessionId = "other_session";

            await using (var conn = await db.CreateConnectionAsync())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO sessions (id, start_time, end_time, total_plays, playing_seconds, idle_seconds)
                    VALUES (@s1, @t1, @t2, 2, 240, 60);

                    INSERT INTO sessions (id, start_time, end_time, total_plays, playing_seconds, idle_seconds)
                    VALUES (@target, @t3, @t4, 1, 180, 30);",
                    new
                    {
                        s1 = otherSessionId,
                        target = targetSessionId,
                        t1 = now.AddDays(-10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        t2 = now.AddDays(-5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        t3 = now.AddMinutes(-30).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        t4 = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    });

                await conn.ExecuteAsync(@"
                    INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_string, beatmap_title, beatmap_artist, beatmap_version, mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp, total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable, is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                    VALUES (@s1, @t1, 1, 1, 'Baseline Song 1', 'Title', 'Artist', 'Diff', 0, '', 180, 5.0, 2.5, 2.5, 4.0, 9.0, 8.0, 6.0, 100, 95, 5, 0, 0, 96.0, 1, 1, 120, 1, 0, 0, 'osu!stable');

                    INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_string, beatmap_title, beatmap_artist, beatmap_version, mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp, total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable, is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                    VALUES (@s1, @t2, 2, 2, 'Baseline Song 2', 'Title', 'Artist', 'Diff', 0, '', 180, 5.0, 2.5, 2.5, 4.0, 9.0, 8.0, 6.0, 100, 90, 10, 0, 0, 94.0, 1, 0, 120, 1, 0, 0, 'osu!stable');

                    INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_string, beatmap_title, beatmap_artist, beatmap_version, mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp, total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable, is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                    VALUES (@target, @t3, 3, 3, 'Session Song 1', 'Title', 'Artist', 'Diff', 0, '', 180, 6.0, 3.0, 3.0, 4.0, 9.0, 8.0, 6.0, 100, 99, 1, 0, 0, 99.0, 1, 1, 180, 1, 0, 0, 'osu!stable');",
                    new
                    {
                        s1 = otherSessionId,
                        target = targetSessionId,
                        t1 = now.AddDays(-10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        t2 = now.AddDays(-5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        t3 = now.AddMinutes(-30).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    });
            }

            var h2h = await analytics.CompareSessionToBaselineAsync(targetSessionId);

            h2h.SessionPlays.Should().Be(1);
            h2h.SessionAcc.Should().Be(99.0m);
            h2h.SessionStars.Should().Be(6.0m);
            h2h.SessionPassRate.Should().Be(100.0);
            h2h.SessionActiveMinutes.Should().Be(3.0);

            h2h.Baseline30DAcc.Should().Be(97.50m);
            h2h.DeltaAcc.Should().Be(1.50m);
        }

        [Fact]
        public async Task CachingAndInvalidation_ServesCachedResultsUntilInvalidated()
        {
            var (db, sink, analytics, session) = await CreateTestEnvironmentAsync();
            var context = new PlayContext(session.SessionId, false, 0, 0, "osu!stable", "", false);

            await sink.TryLogPlayAsync(CreatePlay(stars: 5.0, accuracy: 98.0m), context);

            var firstCall = await analytics.GetStarMasteryCurveAsync();
            var b5First = firstCall.Single(b => b.MinStars == 5.0 && b.MaxStars == 5.5);
            b5First.TotalAttempts.Should().Be(1);

            await using (var conn = await db.CreateConnectionAsync())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_string, beatmap_title, beatmap_artist, beatmap_version, mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp, total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable, is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                    VALUES (@sid, @ts, 2, 2, 'Direct DB Play', 'Title', 'Artist', 'Diff', 0, '', 180, 5.0, 2.5, 2.5, 4.0, 9.0, 8.0, 6.0, 100, 100, 0, 0, 0, 100.0, 1, 1, 60, 1, 0, 0, 'osu!stable');",
                    new { sid = session.SessionId, ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") });
            }

            var secondCallCached = await analytics.GetStarMasteryCurveAsync();
            var b5Second = secondCallCached.Single(b => b.MinStars == 5.0 && b.MaxStars == 5.5);
            b5Second.TotalAttempts.Should().Be(1);

            analytics.InvalidateCache();

            var thirdCallFresh = await analytics.GetStarMasteryCurveAsync();
            var b5Third = thirdCallFresh.Single(b => b.MinStars == 5.0 && b.MaxStars == 5.5);
            b5Third.TotalAttempts.Should().Be(2);
        }

        [Fact]
        public async Task DailyActivityLog_AggregatesDaysCorrectly()
        {
            var (db, _, analytics, _) = await CreateTestEnvironmentAsync();
            DateTime now = DateTime.UtcNow;

            await using (var conn = await db.CreateConnectionAsync())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_string, beatmap_title, beatmap_artist, beatmap_version, mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp, total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable, is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                    VALUES (NULL, @t1, 1, 1, 'Song 1', 'Title', 'Artist', 'Diff', 0, '', 180, 5.0, 2.5, 2.5, 4.0, 9.0, 8.0, 6.0, 100, 98, 2, 0, 0, 98.0, 1, 1, 120, 1, 0, 0, 'osu!stable');

                    INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_string, beatmap_title, beatmap_artist, beatmap_version, mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp, total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable, is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                    VALUES (NULL, @t2, 2, 2, 'Song 2', 'Title', 'Artist', 'Diff', 0, '', 180, 6.0, 3.0, 3.0, 4.0, 9.0, 8.0, 6.0, 100, 90, 10, 0, 0, 90.0, 1, 0, 60, 1, 0, 0, 'osu!stable');",
                    new
                    {
                        t1 = now.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        t2 = now.AddDays(-1).AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    });
            }

            var daily = await analytics.GetDailyActivityLogAsync(14);
            daily.Should().HaveCount(1);
            var day = daily[0];
            day.TotalAttempts.Should().Be(2);
            day.Passes.Should().Be(1);
            day.PassRatePercent.Should().Be(50.0);
            day.AvgAccuracy.Should().Be(98.0m);
            day.AvgStars.Should().Be(5.0m);
            day.ActiveMinutes.Should().Be(3.0);
        }
    }
}
