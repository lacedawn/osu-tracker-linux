using Circle_Tracker;
using Circle_Tracker.Analytics;
using Circle_Tracker.Storage;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.AnalyticsTests
{
    public class SkillAnalyticsServiceTests
    {
        private static async Task<(SqliteDatabaseManager Db, LocalSqlitePlaySink Sink, SessionManager Session)> CreateTestEnvironmentAsync()
        {
            string dbName = $"TestDb_{Guid.NewGuid():N}";
            string connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            var dbManager = new SqliteDatabaseManager(connStr);
            await dbManager.InitializeAsync();

            var sink = new LocalSqlitePlaySink(dbManager);
            await sink.InitializeAsync();

            var session = new SessionManager(dbManager);
            await session.InitializeAsync();

            return (dbManager, sink, session);
        }

        private static PlayEntryData CreatePlay(
            double stars = 5.0,
            double aim = 2.5,
            double speed = 2.5,
            double od = 8.0,
            int bpm = 180,
            decimal accuracy = 98.0m,
            bool complete = true,
            int h300 = 100,
            int h100 = 10,
            int misses = 2,
            int totalHits = 112)
        {
            return new PlayEntryData(
                BeatmapString: "Artist - Title [Diff]",
                BeatmapSetID: 1,
                BeatmapID: 1,
                Hidden: false,
                Hardrock: false,
                Doubletime: false,
                EZ: false,
                Halftime: false,
                Flashlight: false,
                BeatmapBpm: bpm,
                BeatmapAim: (decimal)aim,
                BeatmapSpeed: (decimal)speed,
                BeatmapStars: (decimal)stars,
                BeatmapCs: 4.0m,
                BeatmapAr: 9.0m,
                BeatmapOd: (decimal)od,
                TotalBeatmapHits: totalHits,
                Accuracy: accuracy,
                Play300c: h300,
                Play100c: h100,
                Play50c: 0,
                PlayMissc: misses,
                Complete: complete,
                PlayTimeSeconds: 60,
                ModsString: "",
                PlayCount: 1,
                AccuracyReliable: true
            );
        }

        [Fact]
        public async Task StarMastery_95PercentCutoff_ClassifiesComfortPushAndPassOnlyCorrectly()
        {
            var (db, sink, session) = await CreateTestEnvironmentAsync();
            var service = new SkillAnalyticsService(db);

            var context = new PlayContext(session.SessionId, false, 0, 0, "osu!stable", "", false);

            await sink.TryLogPlayAsync(CreatePlay(stars: 5.2, accuracy: 96.5m, complete: true), context);
            await sink.TryLogPlayAsync(CreatePlay(stars: 5.3, accuracy: 97.5m, complete: true), context);

            await sink.TryLogPlayAsync(CreatePlay(stars: 6.2, accuracy: 92.0m, complete: true), context);
            await sink.TryLogPlayAsync(CreatePlay(stars: 6.4, accuracy: 93.0m, complete: false), context);

            await sink.TryLogPlayAsync(CreatePlay(stars: 7.2, accuracy: 85.0m, complete: false), context);

            var brackets = await service.GetStarMasteryCurveAsync();

            var bracket5 = brackets.Single(b => b.MinStars == 5.0 && b.MaxStars == 5.5);
            bracket5.TotalAttempts.Should().Be(2);
            bracket5.Passes.Should().Be(2);
            bracket5.PassRatePercent.Should().Be(100.0);
            bracket5.MeanAccuracy.Should().Be(97.0m);
            bracket5.SkillZone.Should().Be("Comfort");

            var bracket6 = brackets.Single(b => b.MinStars == 6.0 && b.MaxStars == 6.5);
            bracket6.TotalAttempts.Should().Be(2);
            bracket6.Passes.Should().Be(1);
            bracket6.PassRatePercent.Should().Be(50.0);
            bracket6.MeanAccuracy.Should().Be(92.0m);
            bracket6.SkillZone.Should().Be("Push");

            var bracket7 = brackets.Single(b => b.MinStars == 7.0 && b.MaxStars == 7.5);
            bracket7.TotalAttempts.Should().Be(1);
            bracket7.Passes.Should().Be(0);
            bracket7.PassRatePercent.Should().Be(0.0);
            bracket7.MeanAccuracy.Should().Be(0.0m);
            bracket7.SkillZone.Should().Be("Unpassed");
        }

        [Fact]
        public async Task StarMastery_HighStarsAndPercentiles_CalculatesCorrectly()
        {
            var (db, sink, session) = await CreateTestEnvironmentAsync();
            var service = new SkillAnalyticsService(db);
            var context = new PlayContext(session.SessionId, false, 0, 0, "osu!stable", "", false);

            // Insert 10 plays for 8.0+ stars bracket: 8.1 to 9.2 stars
            var accs = new[] { 88.0m, 90.0m, 92.0m, 93.0m, 94.0m, 95.0m, 96.0m, 97.0m, 98.0m, 99.0m };
            foreach (var acc in accs)
            {
                await sink.TryLogPlayAsync(CreatePlay(stars: 8.5, accuracy: acc, complete: true), context);
            }

            var brackets = await service.GetStarMasteryCurveAsync();
            var b8 = brackets.Single(b => b.MinStars == 8.0 && b.MaxStars == 99.0);

            b8.TotalAttempts.Should().Be(10);
            b8.Passes.Should().Be(10);
            b8.PassRatePercent.Should().Be(100.0);
            b8.MeanAccuracy.Should().Be(94.2m);
            b8.SkillZone.Should().Be("Push");
        }


        [Fact]
        public async Task OdPrecision_CalculatesHitWindowAnd100sRatioCorrectly()
        {
            var (db, sink, session) = await CreateTestEnvironmentAsync();
            var service = new SkillAnalyticsService(db);
            var context = new PlayContext(session.SessionId, false, 0, 0, "osu!stable", "", false);

            await sink.TryLogPlayAsync(CreatePlay(od: 7.5, accuracy: 99.0m, h300: 200, h100: 10), context);
            await sink.TryLogPlayAsync(CreatePlay(od: 8.5, accuracy: 98.0m, h300: 180, h100: 20), context);
            await sink.TryLogPlayAsync(CreatePlay(od: 9.5, accuracy: 97.0m, h300: 150, h100: 30), context);
            await sink.TryLogPlayAsync(CreatePlay(od: 10.0, accuracy: 95.0m, h300: 100, h100: 40), context);
            await sink.TryLogPlayAsync(CreatePlay(od: 10.6, accuracy: 93.0m, h300: 80, h100: 50), context);

            var curve = await service.GetOdAccuracyCurveAsync();

            curve.Should().HaveCount(5);

            var tier1 = curve.Single(t => t.TierName == "OD <= 8.0");
            tier1.HitWindow300Ms.Should().Be(32.0);
            tier1.TotalPlays.Should().Be(1);
            tier1.MeanAccuracy.Should().Be(99.0m);
            tier1.Ratio100sTo300s.Should().Be(0.05);

            var tier2 = curve.Single(t => t.TierName == "OD 8.1 - 9.0");
            tier2.HitWindow300Ms.Should().Be(26.0);
            tier2.TotalPlays.Should().Be(1);
            tier2.MeanAccuracy.Should().Be(98.0m);

            var tier3 = curve.Single(t => t.TierName == "OD 9.1 - 9.7");
            tier3.HitWindow300Ms.Should().Be(21.8);
            tier3.TotalPlays.Should().Be(1);

            var tier4 = curve.Single(t => t.TierName == "OD 9.8 - 10.0");
            tier4.HitWindow300Ms.Should().Be(20.0);
            tier4.TotalPlays.Should().Be(1);

            var tier5 = curve.Single(t => t.TierName == "OD > 10.0");
            tier5.HitWindow300Ms.Should().Be(14.0);
            tier5.TotalPlays.Should().Be(1);
            tier5.Ratio100sTo300s.Should().Be(0.625);
        }

        [Fact]
        public async Task BpmSpeedCeilings_GroupsByBracketsAndCalculatesMissDensity()
        {
            var (db, sink, session) = await CreateTestEnvironmentAsync();
            var service = new SkillAnalyticsService(db);
            var context = new PlayContext(session.SessionId, false, 0, 0, "osu!stable", "", false);

            await sink.TryLogPlayAsync(CreatePlay(bpm: 160, accuracy: 99.0m, misses: 0, totalHits: 500), context);
            await sink.TryLogPlayAsync(CreatePlay(bpm: 180, accuracy: 98.0m, misses: 2, totalHits: 400), context);
            await sink.TryLogPlayAsync(CreatePlay(bpm: 200, accuracy: 97.0m, misses: 5, totalHits: 500), context);
            await sink.TryLogPlayAsync(CreatePlay(bpm: 220, accuracy: 95.0m, misses: 10, totalHits: 500), context);
            await sink.TryLogPlayAsync(CreatePlay(bpm: 240, accuracy: 93.0m, misses: 20, totalHits: 500), context);
            await sink.TryLogPlayAsync(CreatePlay(bpm: 260, accuracy: 88.0m, misses: 35, totalHits: 500), context);

            var stats = await service.GetBpmSpeedCeilingsAsync();

            stats.Should().HaveCount(6);

            var b1 = stats.Single(s => s.Label == "< 170 BPM");
            b1.PlayCount.Should().Be(1);
            b1.MeanAccuracy.Should().Be(99.0m);
            b1.MissesPerHundredHits.Should().Be(0.0);

            var b2 = stats.Single(s => s.Label == "170 - 189 BPM");
            b2.PlayCount.Should().Be(1);
            b2.MissesPerHundredHits.Should().Be(0.5);

            var b6 = stats.Single(s => s.Label == "250+ BPM");
            b6.PlayCount.Should().Be(1);
            b6.MeanAccuracy.Should().Be(88.0m);
            b6.MissesPerHundredHits.Should().Be(7.0);
        }

        [Fact]
        public async Task EmptyDatabase_ReturnsDefaultMetricsWithoutThrowing()
        {
            var (db, _, _) = await CreateTestEnvironmentAsync();
            var service = new SkillAnalyticsService(db);

            var mastery = await service.GetStarMasteryCurveAsync();
            mastery.Should().HaveCount(9);
            mastery.All(m => m.TotalAttempts == 0 && m.PassRatePercent == 0.0 && m.MeanAccuracy == 0.0m).Should().BeTrue();


            var od = await service.GetOdAccuracyCurveAsync();
            od.Should().HaveCount(5);
            od.All(t => t.TotalPlays == 0 && t.MeanAccuracy == 0.0m).Should().BeTrue();

            var bpm = await service.GetBpmSpeedCeilingsAsync();
            bpm.Should().HaveCount(6);
            bpm.All(b => b.PlayCount == 0 && b.MeanAccuracy == 0.0m).Should().BeTrue();
        }
    }
}
