using Circle_Tracker;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Dapper;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.StorageTests
{
    public class PlayQueryEngineTests
    {
        private static async Task<(SqliteDatabaseManager Db, SqlitePlayQueryEngine Engine)> CreateTestEnvironmentAsync()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();
            var engine = new SqlitePlayQueryEngine(dbManager);
            return (dbManager, engine);
        }

        private static async Task InsertPlayDirectAsync(
            SqliteDatabaseManager db,
            string timestamp,
            int beatmapId = 1,
            int beatmapSetId = 1,
            string beatmapString = "Artist - Title [Diff]",
            string beatmapTitle = "Title",
            string beatmapArtist = "Artist",
            string beatmapVersion = "Diff",
            int modsBitfield = 0,
            string modsString = "",
            int bpm = 180,
            double stars = 5.0,
            double accuracy = 98.0,
            bool complete = true,
            int h300 = 100,
            int h100 = 10,
            int h50 = 0,
            int misses = 0,
            int totalHits = 110,
            int playTimeSeconds = 60,
            int consecutivePlayCount = 1,
            bool isReplay = false)
        {
            await using var conn = await db.CreateConnectionAsync();
            await conn.ExecuteAsync(@"
                INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_checksum,
                    beatmap_string, beatmap_title, beatmap_artist, beatmap_version,
                    mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp,
                    total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable,
                    is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                VALUES (NULL, @ts, @bid, @bsid, '',
                    @bs, @bt, @ba, @bv,
                    @mods, @ms, @bpm, @stars, 2.5, 2.5, 4.0, 9.0, 8.0, 6.0,
                    @th, @h3, @h1, @h5, @hm, @acc, 1,
                    @comp, @pts, @cpc, 0, @rep, 'osu!stable');",
                new
                {
                    ts = timestamp,
                    bid = beatmapId,
                    bsid = beatmapSetId,
                    bs = beatmapString,
                    bt = beatmapTitle,
                    ba = beatmapArtist,
                    bv = beatmapVersion,
                    mods = modsBitfield,
                    ms = modsString,
                    bpm,
                    stars,
                    th = totalHits,
                    h3 = h300,
                    h1 = h100,
                    h5 = h50,
                    hm = misses,
                    acc = accuracy,
                    comp = complete ? 1 : 0,
                    pts = playTimeSeconds,
                    cpc = consecutivePlayCount,
                    rep = isReplay ? 1 : 0
                });
        }

        [Fact]
        public async Task DateRangePreset_Last7Days_FiltersCorrectly()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            DateTime now = DateTime.UtcNow;

            await InsertPlayDirectAsync(db, now.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), beatmapId: 1, beatmapTitle: "Today Play");
            await InsertPlayDirectAsync(db, now.AddDays(-3).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), beatmapId: 2, beatmapTitle: "3 Days Ago");
            await InsertPlayDirectAsync(db, now.AddDays(-45).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), beatmapId: 3, beatmapTitle: "45 Days Ago");

            var result = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                DatePreset = DateRangePreset.Last7Days,
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            result.TotalCount.Should().Be(2);
            result.Items.Should().HaveCount(2);
            result.Items.Should().Contain(p => p.BeatmapTitle == "Today Play");
            result.Items.Should().Contain(p => p.BeatmapTitle == "3 Days Ago");
            result.Items.Should().NotContain(p => p.BeatmapTitle == "45 Days Ago");
        }

        [Fact]
        public async Task BitwiseModFiltering_NoModOnly_ReturnsOnlyNoModPlays()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            await InsertPlayDirectAsync(db, ts, beatmapId: 1, modsBitfield: 0, modsString: "");
            await InsertPlayDirectAsync(db, ts, beatmapId: 2, modsBitfield: 8, modsString: "HD");
            await InsertPlayDirectAsync(db, ts, beatmapId: 3, modsBitfield: 24, modsString: "HDHR");
            await InsertPlayDirectAsync(db, ts, beatmapId: 4, modsBitfield: 72, modsString: "HDDT");
            await InsertPlayDirectAsync(db, ts, beatmapId: 5, modsBitfield: 2, modsString: "EZ");

            var result = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                ModMode = ModFilterMode.NoModOnly,
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            result.TotalCount.Should().Be(1);
            result.Items.Single().ModsBitfield.Should().Be(0);
        }

        [Fact]
        public async Task BitwiseModFiltering_ContainsAll_ReturnsPlaysWithHD()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            await InsertPlayDirectAsync(db, ts, beatmapId: 1, modsBitfield: 0);
            await InsertPlayDirectAsync(db, ts, beatmapId: 2, modsBitfield: 8);
            await InsertPlayDirectAsync(db, ts, beatmapId: 3, modsBitfield: 24);
            await InsertPlayDirectAsync(db, ts, beatmapId: 4, modsBitfield: 72);
            await InsertPlayDirectAsync(db, ts, beatmapId: 5, modsBitfield: 2);

            var result = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                ModMode = ModFilterMode.ContainsAll,
                RequiredModsBitfield = 8,
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            result.TotalCount.Should().Be(3);
            result.Items.Select(p => p.ModsBitfield).Should().BeEquivalentTo(new[] { 8, 24, 72 });
        }

        [Fact]
        public async Task BitwiseModFiltering_ExactMatch_ReturnsOnlyExactHDDT()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            await InsertPlayDirectAsync(db, ts, beatmapId: 1, modsBitfield: 0);
            await InsertPlayDirectAsync(db, ts, beatmapId: 2, modsBitfield: 8);
            await InsertPlayDirectAsync(db, ts, beatmapId: 3, modsBitfield: 24);
            await InsertPlayDirectAsync(db, ts, beatmapId: 4, modsBitfield: 72);
            await InsertPlayDirectAsync(db, ts, beatmapId: 5, modsBitfield: 2);

            var result = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                ModMode = ModFilterMode.ExactMatch,
                RequiredModsBitfield = 72,
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            result.TotalCount.Should().Be(1);
            result.Items.Single().ModsBitfield.Should().Be(72);
        }

        [Fact]
        public async Task BitwiseModFiltering_ExcludedMods_FiltersOutHR()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            await InsertPlayDirectAsync(db, ts, beatmapId: 1, modsBitfield: 0);
            await InsertPlayDirectAsync(db, ts, beatmapId: 2, modsBitfield: 8);
            await InsertPlayDirectAsync(db, ts, beatmapId: 3, modsBitfield: 24);
            await InsertPlayDirectAsync(db, ts, beatmapId: 4, modsBitfield: 72);
            await InsertPlayDirectAsync(db, ts, beatmapId: 5, modsBitfield: 2);

            var result = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                ExcludedModsBitfield = 16,
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            result.TotalCount.Should().Be(4);
            result.Items.Should().NotContain(p => p.ModsBitfield == 24);
        }

        [Fact]
        public async Task NumericBounding_StarRange_FiltersCorrectly()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            await InsertPlayDirectAsync(db, ts, beatmapId: 1, stars: 3.0);
            await InsertPlayDirectAsync(db, ts, beatmapId: 2, stars: 4.5);
            await InsertPlayDirectAsync(db, ts, beatmapId: 3, stars: 5.0);
            await InsertPlayDirectAsync(db, ts, beatmapId: 4, stars: 5.8);
            await InsertPlayDirectAsync(db, ts, beatmapId: 5, stars: 6.0);
            await InsertPlayDirectAsync(db, ts, beatmapId: 6, stars: 7.5);

            var result = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                MinStars = 5.0,
                MaxStars = 6.0,
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            result.TotalCount.Should().Be(3);
            result.Items.Should().OnlyContain(p => p.Stars >= 5.0m && p.Stars <= 6.0m);
        }

        [Fact]
        public async Task TextSearch_CaseInsensitiveAndSpecialCharsSafe()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            await InsertPlayDirectAsync(db, ts, beatmapId: 1, beatmapArtist: "Camellia", beatmapTitle: "Crystallize", beatmapString: "Camellia - Crystallize [Expert]");
            await InsertPlayDirectAsync(db, ts, beatmapId: 2, beatmapArtist: "CAMELLIA", beatmapTitle: "Exit This Earth's Atomosphere", beatmapString: "CAMELLIA - Exit This Earth's Atomosphere [Hard]");
            await InsertPlayDirectAsync(db, ts, beatmapId: 3, beatmapArtist: "DragonForce", beatmapTitle: "Through the Fire", beatmapString: "DragonForce - Through the Fire [Insane]");

            var result = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                SearchQuery = "camellia",
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            result.TotalCount.Should().Be(2);
            result.Items.Should().OnlyContain(p => p.BeatmapArtist.Contains("amellia", StringComparison.OrdinalIgnoreCase));

            var specialResult = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                SearchQuery = "100%_test",
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });
            specialResult.TotalCount.Should().Be(0);
        }

        [Fact]
        public async Task Pagination_CorrectlySlicesAndReportsTotals()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            DateTime baseTime = DateTime.UtcNow;

            for (int i = 1; i <= 25; i++)
            {
                await InsertPlayDirectAsync(db,
                    baseTime.AddMinutes(-i).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    beatmapId: i,
                    accuracy: 90.0 + (i * 0.2),
                    stars: 4.0 + (i * 0.1));
            }

            var page2 = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                Page = 2,
                PageSize = 10,
                SortBy = PlaySortField.Timestamp,
                Order = SortOrder.Descending,
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            page2.Items.Should().HaveCount(10);
            page2.TotalCount.Should().Be(25);
            page2.PageNumber.Should().Be(2);
            page2.PageSize.Should().Be(10);
            page2.TotalPages.Should().Be(3);
            page2.HasNextPage.Should().BeTrue();
            page2.HasPreviousPage.Should().BeTrue();

            page2.Summary.TotalMatches.Should().Be(25);
            page2.Summary.AverageAccuracy.Should().BeGreaterThan(0);
            page2.Summary.TotalPasses.Should().Be(25);
            page2.Summary.PassRatePercent.Should().Be(100.0);
        }

        [Fact]
        public async Task SummaryOnly_ReturnsAggregateWithoutItems()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            await InsertPlayDirectAsync(db, ts, beatmapId: 1, accuracy: 98.0, playTimeSeconds: 120, totalHits: 500, complete: true);
            await InsertPlayDirectAsync(db, ts, beatmapId: 2, accuracy: 96.0, playTimeSeconds: 180, totalHits: 600, complete: false);

            var summary = await engine.GetSummaryOnlyAsync(new PlayQueryFilter
            {
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            summary.TotalMatches.Should().Be(2);
            summary.AverageAccuracy.Should().Be(97.0m);
            summary.TotalPlayTimeSeconds.Should().Be(300);
            summary.TotalHitsLogged.Should().Be(1100);
            summary.TotalPasses.Should().Be(1);
            summary.PassRatePercent.Should().Be(50.0);
        }

        [Fact]
        public async Task Autocomplete_ReturnsMatchingBeatmapStrings()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            await InsertPlayDirectAsync(db, ts, beatmapId: 1, beatmapString: "Camellia - Crystallize [Expert]");
            await InsertPlayDirectAsync(db, ts, beatmapId: 2, beatmapString: "Camellia - Exit [Hard]");
            await InsertPlayDirectAsync(db, ts, beatmapId: 3, beatmapString: "DragonForce - Fire [Insane]");

            var results = await engine.AutocompleteSearchAsync("camellia");
            results.Should().HaveCount(2);
            results.Should().OnlyContain(s => s.Contains("Camellia", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task EmptyDatabase_ReturnsEmptyResultsWithZeroSummary()
        {
            var (_, engine) = await CreateTestEnvironmentAsync();

            var result = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            result.TotalCount.Should().Be(0);
            result.Items.Should().BeEmpty();
            result.Summary.TotalMatches.Should().Be(0);
            result.Summary.AverageAccuracy.Should().Be(0.0m);
            result.Summary.PassRatePercent.Should().Be(0.0);
        }

        [Fact]
        public async Task PassStatusFilter_OnlyPasses_FiltersCorrectly()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            await InsertPlayDirectAsync(db, ts, beatmapId: 1, complete: true);
            await InsertPlayDirectAsync(db, ts, beatmapId: 2, complete: false);
            await InsertPlayDirectAsync(db, ts, beatmapId: 3, complete: true);

            var result = await engine.QueryPlaysAsync(new PlayQueryFilter
            {
                PassStatus = PassStatusFilter.PassesOnly,
                ReplayFilter = ReplayFilterOption.IncludeReplays
            });

            result.TotalCount.Should().Be(2);
            result.Items.Should().OnlyContain(p => p.IsComplete);
        }

        private static async Task SeedPlaysBatchAsync(
            SqliteDatabaseManager db,
            int count,
            int beatmapId,
            int beatmapSetId,
            string beatmapString,
            int playTimeSeconds = 60)
        {
            await using var conn = await db.CreateConnectionAsync();
            using var transaction = conn.BeginTransaction();
            const string sql = @"
                INSERT INTO plays (session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_checksum,
                    beatmap_string, beatmap_title, beatmap_artist, beatmap_version,
                    mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp,
                    total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable,
                    is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client)
                VALUES (NULL, '2026-01-01T00:00:00.000Z', @bid, @bsid, '',
                    @bs, @bs, 'Artist', 'Diff',
                    0, '', 180, 5.0, 2.5, 2.5, 4.0, 9.0, 8.0, 6.0,
                    100, 100, 0, 0, 0, 100.0, 1,
                    1, @pts, 1, 0, 0, 'osu!stable');";

            for (int i = 0; i < count; i++)
            {
                await conn.ExecuteAsync(sql, new { bid = beatmapId, bsid = beatmapSetId, bs = beatmapString, pts = playTimeSeconds }, transaction);
            }
            transaction.Commit();
        }

        [Fact]
        public async Task GetMostGrindedBeatmapsAsync_WithMoreThan500Plays_AggregatesAllRecordsWithoutClamping()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();

            await SeedPlaysBatchAsync(db, 700, 101, 1001, "Freedom Dive");
            await SeedPlaysBatchAsync(db, 400, 102, 1002, "Blue Zenith");
            await SeedPlaysBatchAsync(db, 100, 103, 1003, "The Big Black");

            var results = await engine.GetMostGrindedBeatmapsAsync(limit: 5);

            results.Should().HaveCount(3);
            results[0].BeatmapId.Should().Be(101);
            results[0].TotalAttempts.Should().Be(700);
            results[1].TotalAttempts.Should().Be(400);
        }

        [Fact]
        public async Task GetMostGrindedBeatmapsAsync_WithEmptyDatabase_ReturnsEmptyListWithoutException()
        {
            var (_, engine) = await CreateTestEnvironmentAsync();

            var results = await engine.GetMostGrindedBeatmapsAsync();

            results.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public async Task GetMostGrindedBeatmapsAsync_CorrectlyCalculatesCumulativeHours()
        {
            var (db, engine) = await CreateTestEnvironmentAsync();

            await SeedPlaysBatchAsync(db, 10, 201, 2001, "Test Map", playTimeSeconds: 360);

            var results = await engine.GetMostGrindedBeatmapsAsync();

            results[0].CumulativeHours.Should().BeApproximately(1.0, 0.01);
        }
    }
}
