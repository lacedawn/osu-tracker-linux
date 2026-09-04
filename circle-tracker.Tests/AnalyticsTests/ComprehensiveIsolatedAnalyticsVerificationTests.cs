using Circle_Tracker;
using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Dapper;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace CircleTracker.Tests.AnalyticsTests;

public class ComprehensiveIsolatedAnalyticsVerificationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private string? _tempDbPath;
    private SqliteDatabaseManager _dbManager = null!;
    private SkillAnalyticsService _skillService = null!;
    private SessionAnalyticsService _sessionService = null!;
    private CachedAnalyticsService _cachedAnalytics = null!;
    private SqlitePlayQueryEngine _queryEngine = null!;
    private List<RawPlay> _rawPlays = new();

    public class RawPlay
    {
        public long id { get; set; }
        public string? session_id { get; set; }
        public string timestamp { get; set; } = "";
        public int beatmap_id { get; set; }
        public int beatmap_set_id { get; set; }
        public string beatmap_string { get; set; } = "";
        public int mods_bitfield { get; set; }
        public string mods_string { get; set; } = "";
        public int bpm { get; set; }
        public double stars { get; set; }
        public double aim { get; set; }
        public double speed { get; set; }
        public double cs { get; set; }
        public double ar { get; set; }
        public double od { get; set; }
        public double hp { get; set; }
        public int total_hits { get; set; }
        public int hit_300 { get; set; }
        public int hit_100 { get; set; }
        public int hit_50 { get; set; }
        public int hit_miss { get; set; }
        public double accuracy { get; set; }
        public int is_complete { get; set; }
        public int play_time_seconds { get; set; }
        public int consecutive_play_count { get; set; }

        public string beatmap_name => beatmap_string;
        public int play_300_count => hit_300;
        public int play_100_count => hit_100;
        public int play_50_count => hit_50;
        public int play_miss_count => hit_miss;
    }

    public ComprehensiveIsolatedAnalyticsVerificationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        string? existingDb = DiscoverUserDatabase();
        if (existingDb != null && File.Exists(existingDb))
        {
            _tempDbPath = Path.Combine(Path.GetTempPath(), $"ct_verify_{Guid.NewGuid():N}.db");
            File.Copy(existingDb, _tempDbPath, overwrite: true);
            _dbManager = new SqliteDatabaseManager($"Data Source={_tempDbPath}");
            await _dbManager.InitializeAsync();
        }
        else
        {
            _dbManager = new SqliteDatabaseManager($"Data Source=VerifyMemoryDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            await _dbManager.InitializeAsync();
            await SeedRealisticPlayHistoryAsync(_dbManager);
        }

        _skillService = new SkillAnalyticsService(_dbManager);
        _sessionService = new SessionAnalyticsService(_dbManager);
        _cachedAnalytics = new CachedAnalyticsService(_skillService, _sessionService);
        _queryEngine = new SqlitePlayQueryEngine(_dbManager);

        await using var conn = await _dbManager.CreateConnectionAsync();
        _rawPlays = (await conn.QueryAsync<RawPlay>("SELECT * FROM plays")).ToList();
    }

    public Task DisposeAsync()
    {
        if (_tempDbPath != null && File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
        return Task.CompletedTask;
    }

    private static string? DiscoverUserDatabase()
    {
        string? envPath = Environment.GetEnvironmentVariable("CIRCLE_TRACKER_DB_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath)) return envPath;
        string localDb = Path.Combine(Directory.GetCurrentDirectory(), "circle_tracker.db");
        if (File.Exists(localDb)) return localDb;
        string appDirDb = Path.Combine(AppContext.BaseDirectory, "circle_tracker.db");
        if (File.Exists(appDirDb)) return appDirDb;
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "circle-tracker", "circle_tracker.db");
        if (File.Exists(appData)) return appData;
        string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "circle-tracker", "circle_tracker.db");
        if (File.Exists(localAppData)) return localAppData;
        return null;
    }

    [Fact]
    public async Task StarMasteryCurve_MatchesIndependentMathematicalGroundTruth()
    {
        var curve = await _skillService.GetStarMasteryCurveAsync();
        foreach (var bracket in curve)
        {
            var matchingPlays = _rawPlays
                .Where(p => p.stars >= bracket.MinStars && (bracket.MaxStars >= 10.0 ? p.stars >= bracket.MinStars : p.stars < bracket.MaxStars))
                .ToList();
            int expectedAttempts = matchingPlays.Count;
            var passedPlays = matchingPlays.Where(p => p.is_complete == 1).ToList();
            int expectedPasses = passedPlays.Count;

            bracket.TotalAttempts.Should().Be(expectedAttempts, $"Attempt count mismatch in bracket {bracket.MinStars}-{bracket.MaxStars}");
            bracket.Passes.Should().Be(expectedPasses, $"Pass count mismatch in bracket {bracket.MinStars}-{bracket.MaxStars}");

            if (matchingPlays.Count > 0)
            {
                var sortedAccs = matchingPlays.Select(p => p.accuracy).OrderBy(a => a).ToList();
                int idx = (int)((sortedAccs.Count - 1) * 0.90);
                idx = Math.Clamp(idx, 0, sortedAccs.Count - 1);
                double expectedP90 = Math.Round(sortedAccs[idx], 1);
                Math.Abs((double)bracket.P90Accuracy - expectedP90).Should().BeLessThanOrEqualTo(0.5,
                    $"P90 accuracy mismatch in bracket {bracket.MinStars}-{bracket.MaxStars}. Expected: {expectedP90}, Actual: {bracket.P90Accuracy}");
            }
        }
    }

    [Fact]
    public async Task AimSpeedBias_MatchesIndependentMathematicalGroundTruth()
    {
        var bias = await _skillService.GetAimSpeedProfileAsync();
        bias.Should().NotBeNull();

        var validPlays = _rawPlays.Where(p => p.aim > 0 && p.speed > 0).ToList();
        if (validPlays.Count == 0) return;

        var aimPlays = validPlays.Where(p => p.aim >= 1.20 * p.speed).ToList();
        var speedPlays = validPlays.Where(p => p.speed >= 1.20 * p.aim && !(p.aim >= 1.20 * p.speed)).ToList();
        var balancedPlays = validPlays.Where(p => !(p.aim >= 1.20 * p.speed) && !(p.speed >= 1.20 * p.aim)).ToList();

        bias.AimDominantPlays.Should().Be(aimPlays.Count);
        bias.SpeedDominantPlays.Should().Be(speedPlays.Count);
        bias.BalancedPlays.Should().Be(balancedPlays.Count);

        int totalBiased = aimPlays.Count + speedPlays.Count;
        double expectedAimPct = totalBiased > 0 ? Math.Round(100.0 * aimPlays.Count / totalBiased, 1) : 50.0;
        double expectedSpeedPct = totalBiased > 0 ? Math.Round(100.0 * speedPlays.Count / totalBiased, 1) : 50.0;

        Math.Abs(bias.AimBiasPercent - expectedAimPct).Should().BeLessThanOrEqualTo(1.0, "Aim percentage ratio mismatch");
        Math.Abs(bias.SpeedBiasPercent - expectedSpeedPct).Should().BeLessThanOrEqualTo(1.0, "Speed percentage ratio mismatch");

        if (aimPlays.Count > 0)
        {
            double expectedAimAcc = Math.Round(aimPlays.Average(p => p.accuracy), 1);
            Math.Abs((double)bias.AimAvgAcc - expectedAimAcc).Should().BeLessThanOrEqualTo(0.5, "Aim average accuracy mismatch");
        }
        if (speedPlays.Count > 0)
        {
            double expectedSpeedAcc = Math.Round(speedPlays.Average(p => p.accuracy), 1);
            Math.Abs((double)bias.SpeedAvgAcc - expectedSpeedAcc).Should().BeLessThanOrEqualTo(0.5, "Speed average accuracy mismatch");
        }
    }

    [Fact]
    public async Task OdPrecisionTiers_MatchesIndependentHitWindowFormulas()
    {
        var tiers = await _skillService.GetOdAccuracyCurveAsync();
        tiers.Should().NotBeEmpty();

        foreach (var tier in tiers)
        {
            double refOd = tier.MaxOd > 10.5 ? 11.0 : tier.MaxOd;
            double expectedWindowMs = Math.Round(80.0 - 6.0 * refOd, 1);
            Math.Abs(tier.HitWindow300Ms - expectedWindowMs).Should().BeLessThanOrEqualTo(1.0,
                $"Hit window formula mismatch for OD tier {tier.TierName}");

            Func<RawPlay, bool> predicate = tier.TierName switch
            {
                "OD <= 8.0" => p => p.od <= 8.05,
                "OD 8.1 - 9.0" => p => p.od > 8.05 && p.od <= 9.05,
                "OD 9.1 - 9.7" => p => p.od > 9.05 && p.od <= 9.75,
                "OD 9.8 - 10.0" => p => p.od > 9.75 && p.od <= 10.05,
                _ => p => p.od > 10.05
            };
            int expectedCount = _rawPlays.Count(predicate);
            tier.TotalPlays.Should().Be(expectedCount, $"Play count mismatch for OD tier {tier.TierName}");
        }
    }

    [Fact]
    public async Task BpmSpeedBrackets_MatchesIndependentMathematicalGroundTruth()
    {
        var brackets = await _skillService.GetBpmSpeedCeilingsAsync();
        brackets.Should().NotBeEmpty();

        foreach (var bracket in brackets)
        {
            var matchingPlays = _rawPlays
                .Where(p => p.bpm >= bracket.MinBpm && (bracket.MaxBpm >= int.MaxValue / 2 ? true : p.bpm <= bracket.MaxBpm))
                .ToList();

            bracket.PlayCount.Should().Be(matchingPlays.Count, $"Play count mismatch for BPM {bracket.Label}");

            if (matchingPlays.Count > 0)
            {
                decimal expectedAcc = Math.Round((decimal)matchingPlays.Average(p => p.accuracy), 1);
                Math.Abs((double)bracket.MeanAccuracy - (double)expectedAcc).Should().BeLessThanOrEqualTo(0.5, $"Accuracy mismatch for BPM {bracket.Label}");

                long totalMisses = matchingPlays.Sum(p => (long)p.hit_miss);
                long totalHits = matchingPlays.Sum(p => (long)p.total_hits);
                double expectedMissRate = totalHits > 0 ? Math.Round(100.0 * totalMisses / totalHits, 2) : 0.0;
                Math.Abs(bracket.MissesPerHundredHits - expectedMissRate).Should().BeLessThanOrEqualTo(0.5, $"Miss density mismatch for BPM {bracket.Label}");
            }
        }
    }

    [Fact]
    public async Task RollingPeriodStats_MatchesIndependentTimeWindowCalculations()
    {
        var rolling = await _sessionService.GetRollingAveragesAsync();
        DateTime now = DateTime.UtcNow;

        DateTime earliestDate = _rawPlays.Count > 0
            ? _rawPlays.Select(p => DateTime.TryParse(p.timestamp, null, DateTimeStyles.RoundtripKind, out var d) ? d : (DateTime.TryParse(p.timestamp, out var d2) ? d2 : now)).Min()
            : now;
        double historyDays = Math.Max(0, (now - earliestDate).TotalDays);

        foreach (var periodDays in new[] { 7, 30, 90 })
        {
            string key = $"{periodDays}D";
            rolling.Should().ContainKey(key);
            var stats = rolling[key];

            DateTime cutoff = now.AddDays(-periodDays);
            var windowPlays = _rawPlays
                .Where(p => (DateTime.TryParse(p.timestamp, null, DateTimeStyles.RoundtripKind, out var d) ? d : (DateTime.TryParse(p.timestamp, out var d2) ? d2 : DateTime.MinValue)) >= cutoff)
                .ToList();

            stats.TotalPlays.Should().Be(windowPlays.Count, $"Total plays mismatch for period {key}");

            if (windowPlays.Count > 0)
            {
                int expectedActiveDays = windowPlays
                    .Select(p => p.timestamp.Length >= 10 ? p.timestamp.Substring(0, 10) : p.timestamp)
                    .Distinct()
                    .Count();
                double expectedPlaysPerDay = expectedActiveDays > 0 ? Math.Round((double)windowPlays.Count / expectedActiveDays, 1) : 0.0;
                Math.Abs(stats.PlaysPerActiveDay - expectedPlaysPerDay).Should().BeLessThanOrEqualTo(0.5, $"Plays/day mismatch for period {key}");

                long totalHits = windowPlays.Sum(p => (long)p.total_hits);
                if (totalHits > 0)
                {
                    decimal expectedWeightedAcc = Math.Round((decimal)(windowPlays.Sum(p => p.accuracy * p.total_hits) / totalHits), 2);
                    Math.Abs(stats.MeanAccuracy - expectedWeightedAcc).Should().BeLessThanOrEqualTo(0.5m, $"Weighted accuracy mismatch for period {key}");
                }

                int passes = windowPlays.Count(p => p.is_complete == 1);
                double expectedPassRate = Math.Round(100.0 * passes / windowPlays.Count, 2);
                Math.Abs(stats.PassRatePercent - expectedPassRate).Should().BeLessThanOrEqualTo(0.5, $"Pass rate mismatch for period {key}");
            }

            bool expectedSufficient = periodDays == 7 ? windowPlays.Count > 0 : historyDays >= periodDays;
            stats.HasSufficientData.Should().Be(expectedSufficient, $"Sufficiency flag mismatch for period {key}");
        }
    }

    [Fact]
    public async Task TopChokes_MatchesIndependentChokeDetection()
    {
        var chokes = await _sessionService.GetTopChokeMapsAsync(limit: 10);

        var independentChokes = _rawPlays
            .Where(p => p.beatmap_id > 0)
            .GroupBy(p => p.beatmap_id)
            .Select(g =>
            {
                var chokesList = g.Where(p => (p.hit_miss == 1 || p.hit_miss == 2) && p.accuracy >= 95.0).ToList();
                int chokeCount = chokesList.Count;
                int totalAttempts = g.Count();
                int maxConsecutive = g.Max(p => p.consecutive_play_count);
                int minMisses = chokeCount > 0 ? chokesList.Min(p => p.hit_miss) : 0;
                decimal bestAcc = chokeCount > 0 ? Math.Round((decimal)chokesList.Max(p => p.accuracy), 2) : 0m;
                return new
                {
                    BeatmapId = g.Key,
                    TotalAttempts = totalAttempts,
                    MaxConsecutive = maxConsecutive,
                    ChokeCount = chokeCount,
                    MinMisses = minMisses,
                    BestAcc = bestAcc
                };
            })
            .Where(c => c.ChokeCount > 0 && (c.MaxConsecutive >= 3 || c.TotalAttempts >= 5))
            .OrderByDescending(c => c.ChokeCount)
            .ThenByDescending(c => c.BestAcc)
            .Take(10)
            .ToList();

        chokes.Count.Should().Be(independentChokes.Count);
        for (int i = 0; i < chokes.Count; i++)
        {
            chokes[i].BeatmapId.Should().Be(independentChokes[i].BeatmapId);
            chokes[i].ChokeCount.Should().Be(independentChokes[i].ChokeCount);
            chokes[i].MinMisses.Should().Be(independentChokes[i].MinMisses);
        }
    }

    [Fact]
    public async Task SessionBaselineComparison_MatchesIndependentMathematicalGroundTruth()
    {
        string sessionId = "test-session-baseline";
        var comparison = await _sessionService.CompareSessionToBaselineAsync(sessionId);

        var sessionPlays = _rawPlays.Where(p => p.session_id == sessionId).ToList();
        DateTime cutoff = DateTime.UtcNow.AddDays(-30);
        var baselinePlays = _rawPlays.Where(p => DateTime.TryParse(p.timestamp, null, DateTimeStyles.RoundtripKind, out var d) && d >= cutoff).ToList();

        comparison.SessionPlays.Should().Be(sessionPlays.Count);
        if (sessionPlays.Count > 0)
        {
            decimal expectedAcc = Math.Round((decimal)sessionPlays.Average(p => p.accuracy), 2);
            decimal expectedStars = Math.Round((decimal)sessionPlays.Average(p => p.stars), 2);
            int passes = sessionPlays.Count(p => p.is_complete == 1);
            double expectedPassRate = Math.Round(100.0 * passes / sessionPlays.Count, 2);
            double expectedMinutes = Math.Round(sessionPlays.Sum(p => p.play_time_seconds) / 60.0, 1);

            Math.Abs(comparison.SessionAcc - expectedAcc).Should().BeLessThanOrEqualTo(0.5m);
            Math.Abs(comparison.SessionStars - expectedStars).Should().BeLessThanOrEqualTo(0.5m);
            Math.Abs(comparison.SessionPassRate - expectedPassRate).Should().BeLessThanOrEqualTo(0.5);
            Math.Abs(comparison.SessionActiveMinutes - expectedMinutes).Should().BeLessThanOrEqualTo(0.5);
        }

        if (baselinePlays.Count > 0)
        {
            decimal expectedBaseAcc = Math.Round((decimal)baselinePlays.Average(p => p.accuracy), 2);
            decimal expectedBaseStars = Math.Round((decimal)baselinePlays.Average(p => p.stars), 2);
            int basePasses = baselinePlays.Count(p => p.is_complete == 1);
            double expectedBasePassRate = Math.Round(100.0 * basePasses / baselinePlays.Count, 2);

            Math.Abs(comparison.Baseline30DAcc - expectedBaseAcc).Should().BeLessThanOrEqualTo(0.5m);
            Math.Abs(comparison.Baseline30DStars - expectedBaseStars).Should().BeLessThanOrEqualTo(0.5m);
            Math.Abs(comparison.Baseline30DPassRate - expectedBasePassRate).Should().BeLessThanOrEqualTo(0.5);

            Math.Abs(comparison.DeltaAcc - (comparison.SessionAcc - comparison.Baseline30DAcc)).Should().BeLessThanOrEqualTo(0.01m);
            Math.Abs(comparison.DeltaStars - (comparison.SessionStars - comparison.Baseline30DStars)).Should().BeLessThanOrEqualTo(0.01m);
            Math.Abs(comparison.DeltaPassRate - (comparison.SessionPassRate - comparison.Baseline30DPassRate)).Should().BeLessThanOrEqualTo(0.01);
        }
    }

    [Fact]
    public async Task CachedAnalyticsService_CrossReferencesIdenticalToDirectServices()
    {
        var directCurve = await _skillService.GetStarMasteryCurveAsync();
        var cachedCurve = await _cachedAnalytics.GetStarMasteryCurveAsync();
        cachedCurve.Should().BeEquivalentTo(directCurve);

        var directProfile = await _skillService.GetAimSpeedProfileAsync();
        var cachedProfile = await _cachedAnalytics.GetAimSpeedProfileAsync();
        cachedProfile.Should().BeEquivalentTo(directProfile);

        var directRolling = await _sessionService.GetRollingAveragesAsync();
        var cachedRolling = await _cachedAnalytics.GetRollingAveragesAsync();
        cachedRolling.Should().BeEquivalentTo(directRolling);

        var directChokes = await _sessionService.GetTopChokeMapsAsync(10);
        var cachedChokes = await _cachedAnalytics.GetTopChokeMapsAsync(10);
        cachedChokes.Should().BeEquivalentTo(directChokes);
    }

    [Fact]
    public async Task ExplorerQuery_SummaryStatistics_MatchesIndependentAggregation()
    {
        var filter = new PlayQueryFilter
        {
            Page = 1,
            PageSize = 50
        };
        var result = await _queryEngine.QueryPlaysAsync(filter);

        result.TotalCount.Should().Be(_rawPlays.Count);
        int expectedPages = (int)Math.Ceiling((double)_rawPlays.Count / 50);
        result.TotalPages.Should().Be(expectedPages);

        if (_rawPlays.Count > 0)
        {
            decimal expectedAvgAcc = Math.Round((decimal)_rawPlays.Average(p => p.accuracy), 2);
            decimal expectedAvgStars = Math.Round((decimal)_rawPlays.Average(p => p.stars), 2);
            int expectedPlayTime = _rawPlays.Sum(p => p.play_time_seconds);

            Math.Abs(result.Summary.AverageAccuracy - expectedAvgAcc).Should().BeLessThanOrEqualTo(0.5m);
            Math.Abs(result.Summary.AverageStars - expectedAvgStars).Should().BeLessThanOrEqualTo(0.5m);
            result.Summary.TotalPlayTimeSeconds.Should().Be(expectedPlayTime);
        }
    }

    [Theory]
    [InlineData(0, ModFilterMode.NoModOnly)]
    [InlineData(8, ModFilterMode.ContainsAll)]
    [InlineData(16, ModFilterMode.ContainsAll)]
    [InlineData(64, ModFilterMode.ContainsAll)]
    public async Task ExplorerModFiltering_MatchesIndependentBitfieldMatching(int requiredBit, ModFilterMode mode)
    {
        var filter = new PlayQueryFilter
        {
            Page = 1,
            PageSize = 5000,
            ModMode = mode,
            RequiredModsBitfield = requiredBit > 0 ? requiredBit : null
        };
        var result = await _queryEngine.QueryPlaysAsync(filter);
        List<RawPlay> expected;
        if (mode == ModFilterMode.NoModOnly)
            expected = _rawPlays.Where(p => p.mods_bitfield == 0).ToList();
        else
            expected = _rawPlays.Where(p => (p.mods_bitfield & requiredBit) == requiredBit).ToList();

        result.TotalCount.Should().Be(expected.Count, $"Explorer mod filter count mismatch for mod bitfield {requiredBit}");
    }

    [Fact]
    public async Task LiveSessionTracker_CalculatesStatsStrictlyFromPassedMaps()
    {
        var tracker = new LiveSessionTracker(_sessionService);
        var context = new PlayContext(Guid.NewGuid().ToString(), false, 0, 0, "tosu", null, false);

        for (int i = 0; i < 3; i++)
        {
            var retryPlay = new PlayEntryData("Map", 1, 1, false, false, false, false, false, false, 240, 4m, 4m, 7m, 4m, 9m, 8m, 100, 70m, 30, 0, 0, 10, false, 20, "NM", 1, true);
            await tracker.OnPlayLoggedAsync(retryPlay, context);
        }

        var pass1 = new PlayEntryData("Map", 1, 1, false, false, false, false, false, false, 180, 2m, 2m, 5m, 4m, 9m, 8m, 100, 98m, 98, 2, 0, 0, true, 120, "NM", 1, true);
        var pass2 = new PlayEntryData("Map", 1, 1, false, false, false, false, false, false, 200, 3m, 3m, 5.5m, 4m, 9m, 8m, 100, 100m, 100, 0, 0, 0, true, 120, "NM", 1, true);
        await tracker.OnPlayLoggedAsync(pass1, context);
        await tracker.OnPlayLoggedAsync(pass2, context);

        var metrics = tracker.GetCurrentMetrics();

        metrics.SessionPlayCount.Should().Be(5);
        metrics.SessionPassCount.Should().Be(2);

        metrics.SessionAverageAccuracy.Should().Be(99.0m);
        metrics.SessionAverageStars.Should().Be(5.25m);
        metrics.SessionAverageBpm.Should().Be(190.0);
    }

    private static async Task SeedRealisticPlayHistoryAsync(SqliteDatabaseManager db)
    {
        await using var conn = await db.CreateConnectionAsync();
        var rand = new Random(42);
        var now = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO sessions (id, start_time, total_plays, playing_seconds, idle_seconds, efficiency_percent, client_version)
            VALUES ('test-session-baseline', @startTime, 6, 720, 100, 87.8, 'osu!stable');",
            new { startTime = now.AddDays(-10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") });

        var playsToInsert = new List<object>();

        for (int p = 0; p < 6; p++)
        {
            var date = now.AddDays(-10).AddHours(p);
            bool isChoke = p < 3;
            int misses = isChoke ? 1 : 0;
            decimal acc = isChoke ? 97.5m : 99.0m;
            playsToInsert.Add(new
            {
                SessionId = "test-session-baseline",
                Timestamp = date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                BeatmapId = 10,
                BeatmapSetId = 10,
                BeatmapChecksum = (string?)null,
                BeatmapString = "Choke Map 1 [Diff 10]",
                BeatmapTitle = "Choke Map 1",
                BeatmapArtist = "Artist",
                BeatmapVersion = "Diff 10",
                ModsBitfield = 0,
                ModsString = "NM",
                Bpm = 185,
                Stars = 5.6,
                Aim = 2.8,
                Speed = 2.8,
                Cs = 4.0,
                Ar = 9.0,
                Od = 8.5,
                Hp = 6.0,
                TotalHits = 500,
                Hit300 = 490,
                Hit100 = 9,
                Hit50 = 0,
                HitMiss = misses,
                Accuracy = (double)acc,
                AccuracyReliable = 1,
                IsComplete = 1,
                PlayTimeSeconds = 120,
                ConsecutivePlayCount = p + 1,
                GameMode = 0,
                IsReplay = 0,
                DetectedClient = "osu!stable"
            });
        }

        for (int p = 0; p < 5; p++)
        {
            var date = now.AddDays(-5).AddHours(p);
            bool isChoke = p < 2;
            int misses = isChoke ? 2 : 0;
            decimal acc = isChoke ? 96.2m : 98.5m;
            playsToInsert.Add(new
            {
                SessionId = (string?)null,
                Timestamp = date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                BeatmapId = 11,
                BeatmapSetId = 11,
                BeatmapChecksum = (string?)null,
                BeatmapString = "Choke Map 2 [Diff 11]",
                BeatmapTitle = "Choke Map 2",
                BeatmapArtist = "Artist",
                BeatmapVersion = "Diff 11",
                ModsBitfield = 8,
                ModsString = "HD",
                Bpm = 205,
                Stars = 6.2,
                Aim = 3.5,
                Speed = 2.5,
                Cs = 4.2,
                Ar = 9.3,
                Od = 9.0,
                Hp = 6.0,
                TotalHits = 450,
                Hit300 = 430,
                Hit100 = 18,
                Hit50 = 0,
                HitMiss = misses,
                Accuracy = (double)acc,
                AccuracyReliable = 1,
                IsComplete = 1,
                PlayTimeSeconds = 110,
                ConsecutivePlayCount = p + 1,
                GameMode = 0,
                IsReplay = 0,
                DetectedClient = "osu!stable"
            });
        }

        for (int i = 0; i < 190; i++)
        {
            var date = now.AddDays(-rand.Next(0, 45)).AddMinutes(-rand.Next(0, 1440));
            double stars = 4.0 + rand.NextDouble() * 4.5;
            int bpm = 150 + rand.Next(0, 110);
            double acc = 88.0 + rand.NextDouble() * 11.5;
            bool complete = rand.NextDouble() > 0.35;
            int misses = complete ? rand.Next(0, 3) : rand.Next(3, 15);
            int rawMods = rand.NextDouble() switch
            {
                > 0.8 => 8,
                > 0.65 => 16,
                > 0.5 => 64,
                _ => 0
            };
            double od = 7.0 + rand.NextDouble() * 3.0;
            double aim = (stars * 0.4) + rand.NextDouble() * (stars * 0.4);
            double speed = stars - aim + (rand.NextDouble() * 0.4 - 0.2);
            if (aim <= 0) aim = 1.0;
            if (speed <= 0) speed = 1.0;

            playsToInsert.Add(new
            {
                SessionId = (string?)null,
                Timestamp = date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                BeatmapId = 100 + i,
                BeatmapSetId = 1000 + i,
                BeatmapChecksum = (string?)null,
                BeatmapString = $"Artist - Title [Diff {i}]",
                BeatmapTitle = $"Title {i}",
                BeatmapArtist = "Artist",
                BeatmapVersion = $"Diff {i}",
                ModsBitfield = rawMods,
                ModsString = rawMods == 0 ? "NM" : "Mod",
                Bpm = bpm,
                Stars = stars,
                Aim = aim,
                Speed = speed,
                Cs = 4.0,
                Ar = 9.0,
                Od = od,
                Hp = 5.0,
                TotalHits = 300,
                Hit300 = 280,
                Hit100 = 15,
                Hit50 = 2,
                HitMiss = misses,
                Accuracy = acc,
                AccuracyReliable = 1,
                IsComplete = complete ? 1 : 0,
                PlayTimeSeconds = complete ? 120 : 35,
                ConsecutivePlayCount = 1,
                GameMode = 0,
                IsReplay = 0,
                DetectedClient = "osu!stable"
            });
        }

        const string insertSql = @"
            INSERT INTO plays (
                session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_checksum,
                beatmap_string, beatmap_title, beatmap_artist, beatmap_version,
                mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp,
                total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable,
                is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client
            ) VALUES (
                @SessionId, @Timestamp, @BeatmapId, @BeatmapSetId, @BeatmapChecksum,
                @BeatmapString, @BeatmapTitle, @BeatmapArtist, @BeatmapVersion,
                @ModsBitfield, @ModsString, @Bpm, @Stars, @Aim, @Speed, @Cs, @Ar, @Od, @Hp,
                @TotalHits, @Hit300, @Hit100, @Hit50, @HitMiss, @Accuracy, @AccuracyReliable,
                @IsComplete, @PlayTimeSeconds, @ConsecutivePlayCount, @GameMode, @IsReplay, @DetectedClient
            );";

        await conn.ExecuteAsync(insertSql, playsToInsert);
    }
}
