using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Circle_Tracker;
using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.SessionTests;

public class LiveSessionTrackerTests
{
    [Fact]
    public async Task BaselineDeltaCalculation_WithSeededBaseline_CalculatesCorrectDeltas()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        
        var baseline = new RollingPeriodStats(
            PeriodDays: 30,
            TotalPlays: 300,
            TotalActiveHours: 25.0,
            MeanAccuracy: 96.0m,
            MeanStars: 5.5m,
            PassRatePercent: 70.0,
            MeanBpm: 180.0,
            PlaysPerActiveDay: 10.0,
            HoursPerActiveDay: 1.0
        );
        
        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>
            {
                ["30D"] = baseline
            });

        var tracker = new LiveSessionTracker(sessionService.Object);

        for (int i = 1; i <= 5; i++)
        {
            var play = new PlayEntryData(
                BeatmapString: $"Map {i}",
                BeatmapSetID: i,
                BeatmapID: i,
                Hidden: false,
                Hardrock: false,
                Doubletime: false,
                EZ: false,
                Halftime: false,
                Flashlight: false,
                BeatmapBpm: 190,
                BeatmapAim: 2.9m,
                BeatmapSpeed: 2.9m,
                BeatmapStars: 5.8m,
                BeatmapCs: 4.0m,
                BeatmapAr: 9.0m,
                BeatmapOd: 8.5m,
                TotalBeatmapHits: 400,
                Accuracy: 97.2m,
                Play300c: 380,
                Play100c: 15,
                Play50c: 3,
                PlayMissc: 2,
                Complete: true,
                PlayTimeSeconds: 120,
                ModsString: "NM",
                PlayCount: 1,
                AccuracyReliable: true,
                BeatmapTitle: $"Title {i}",
                BeatmapArtist: $"Artist {i}",
                BeatmapVersion: "Hard",
                BeatmapHp: 5.0m,
                BeatmapChecksum: ""
            );

            var context = new PlayContext(
                SessionId: Guid.NewGuid().ToString(),
                IsReplay: false,
                RawMods: 0,
                CurrentGameMode: 0,
                DetectedClient: "lazer",
                SoundFilePath: null,
                SubmitSoundEnabled: false
            );

            await tracker.OnPlayLoggedAsync(play, context);
        }

        var metrics = tracker.GetCurrentMetrics();

        metrics.BaselineDeltaAccuracy.Should().BeApproximately(1.2m, 0.1m);
        metrics.BaselineDeltaStars.Should().BeApproximately(0.3m, 0.1m);
    }

    [Fact]
    public async Task GetCurrentMetrics_AveragesOnlyCompletedPlays_ExcludesRetries()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        var baseline = new RollingPeriodStats(
            PeriodDays: 30,
            TotalPlays: 300,
            TotalActiveHours: 25.0,
            MeanAccuracy: 95.0m,
            MeanStars: 5.0m,
            PassRatePercent: 70.0,
            MeanBpm: 180.0,
            PlaysPerActiveDay: 10.0,
            HoursPerActiveDay: 1.0
        );

        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>
            {
                ["30D"] = baseline
            });

        var tracker = new LiveSessionTracker(sessionService.Object);

        var context = new PlayContext(
            SessionId: Guid.NewGuid().ToString(),
            IsReplay: false,
            RawMods: 0,
            CurrentGameMode: 0,
            DetectedClient: "lazer",
            SoundFilePath: null,
            SubmitSoundEnabled: false
        );

        // Incomplete play (retry)
        var retryPlay = new PlayEntryData(
            BeatmapString: "Retry Map",
            BeatmapSetID: 1,
            BeatmapID: 1,
            Hidden: false,
            Hardrock: false,
            Doubletime: false,
            EZ: false,
            Halftime: false,
            Flashlight: false,
            BeatmapBpm: 220,
            BeatmapAim: 3.5m,
            BeatmapSpeed: 3.5m,
            BeatmapStars: 7.0m,
            BeatmapCs: 4.0m,
            BeatmapAr: 9.5m,
            BeatmapOd: 9.0m,
            TotalBeatmapHits: 400,
            Accuracy: 50.0m,
            Play300c: 20,
            Play100c: 5,
            Play50c: 2,
            PlayMissc: 10,
            Complete: false,
            PlayTimeSeconds: 20,
            ModsString: "NM",
            PlayCount: 1,
            AccuracyReliable: true,
            BeatmapTitle: "Retry",
            BeatmapArtist: "Artist",
            BeatmapVersion: "Expert",
            BeatmapHp: 5.0m,
            BeatmapChecksum: ""
        );

        await tracker.OnPlayLoggedAsync(retryPlay, context);

        var retryMetrics = tracker.GetCurrentMetrics();
        retryMetrics.SessionPlayCount.Should().Be(1);
        retryMetrics.SessionPassCount.Should().Be(0);
        retryMetrics.SessionAverageAccuracy.Should().Be(0m);
        retryMetrics.SessionAverageStars.Should().Be(0m);
        retryMetrics.SessionAverageBpm.Should().Be(0.0);
        retryMetrics.BaselineDeltaAccuracy.Should().Be(0m);
        retryMetrics.BaselineDeltaStars.Should().Be(0m);
        retryMetrics.BaselineDeltaBpm.Should().Be(0.0);

        // Passed play
        var passedPlay = new PlayEntryData(
            BeatmapString: "Passed Map",
            BeatmapSetID: 2,
            BeatmapID: 2,
            Hidden: false,
            Hardrock: false,
            Doubletime: false,
            EZ: false,
            Halftime: false,
            Flashlight: false,
            BeatmapBpm: 200,
            BeatmapAim: 3.0m,
            BeatmapSpeed: 3.0m,
            BeatmapStars: 6.0m,
            BeatmapCs: 4.0m,
            BeatmapAr: 9.0m,
            BeatmapOd: 8.5m,
            TotalBeatmapHits: 400,
            Accuracy: 98.0m,
            Play300c: 390,
            Play100c: 10,
            Play50c: 0,
            PlayMissc: 0,
            Complete: true,
            PlayTimeSeconds: 120,
            ModsString: "NM",
            PlayCount: 1,
            AccuracyReliable: true,
            BeatmapTitle: "Passed",
            BeatmapArtist: "Artist",
            BeatmapVersion: "Hard",
            BeatmapHp: 5.0m,
            BeatmapChecksum: ""
        );

        await tracker.OnPlayLoggedAsync(passedPlay, context);

        var finalMetrics = tracker.GetCurrentMetrics();
        finalMetrics.SessionPlayCount.Should().Be(2);
        finalMetrics.SessionPassCount.Should().Be(1);
        finalMetrics.SessionAverageAccuracy.Should().Be(98.0m);
        finalMetrics.SessionAverageStars.Should().Be(6.0m);
        finalMetrics.SessionAverageBpm.Should().Be(200.0);
        finalMetrics.BaselineDeltaAccuracy.Should().Be(3.0m);
        finalMetrics.BaselineDeltaStars.Should().Be(1.0m);
        finalMetrics.BaselineDeltaBpm.Should().Be(20.0);
    }



    [Fact]
    public async Task StarPassPRAchievement_WhenNewRecordSet_TriggersEvent()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>());

        var tracker = new LiveSessionTracker(sessionService.Object);

        var achievements = new List<PostPlayAchievement>();
        tracker.AchievementUnlocked += (sender, achievement) =>
        {
            achievements.Add(achievement);
        };

        var play1 = new PlayEntryData(
            BeatmapString: "Test 1",
            BeatmapSetID: 1,
            BeatmapID: 1,
            Hidden: false,
            Hardrock: false,
            Doubletime: false,
            EZ: false,
            Halftime: false,
            Flashlight: false,
            BeatmapBpm: 180,
            BeatmapAim: 3.1m,
            BeatmapSpeed: 3.1m,
            BeatmapStars: 6.2m,
            BeatmapCs: 4.0m,
            BeatmapAr: 9.5m,
            BeatmapOd: 9.0m,
            TotalBeatmapHits: 500,
            Accuracy: 96.8m,
            Play300c: 475,
            Play100c: 20,
            Play50c: 3,
            PlayMissc: 2,
            Complete: true,
            PlayTimeSeconds: 150,
            ModsString: "NM",
            PlayCount: 1,
            AccuracyReliable: true,
            BeatmapTitle: "Test 1",
            BeatmapArtist: "Artist 1",
            BeatmapVersion: "Hard",
            BeatmapHp: 5.5m,
            BeatmapChecksum: ""
        );

        var context1 = new PlayContext(
            SessionId: Guid.NewGuid().ToString(),
            IsReplay: false,
            RawMods: 0,
            CurrentGameMode: 0,
            DetectedClient: "lazer",
            SoundFilePath: null,
            SubmitSoundEnabled: false
        );

        await tracker.OnPlayLoggedAsync(play1, context1);

        var play2 = new PlayEntryData(
            BeatmapString: "Test 2",
            BeatmapSetID: 2,
            BeatmapID: 2,
            Hidden: false,
            Hardrock: false,
            Doubletime: false,
            EZ: false,
            Halftime: false,
            Flashlight: false,
            BeatmapBpm: 190,
            BeatmapAim: 3.0m,
            BeatmapSpeed: 3.75m,
            BeatmapStars: 6.75m,
            BeatmapCs: 4.2m,
            BeatmapAr: 9.8m,
            BeatmapOd: 9.5m,
            TotalBeatmapHits: 600,
            Accuracy: 97.1m,
            Play300c: 570,
            Play100c: 25,
            Play50c: 3,
            PlayMissc: 2,
            Complete: true,
            PlayTimeSeconds: 180,
            ModsString: "NM",
            PlayCount: 1,
            AccuracyReliable: true,
            BeatmapTitle: "Test 2",
            BeatmapArtist: "Artist 2",
            BeatmapVersion: "Insane",
            BeatmapHp: 6.0m,
            BeatmapChecksum: ""
        );

        var context2 = new PlayContext(
            SessionId: Guid.NewGuid().ToString(),
            IsReplay: false,
            RawMods: 0,
            CurrentGameMode: 0,
            DetectedClient: "lazer",
            SoundFilePath: null,
            SubmitSoundEnabled: false
        );

        await tracker.OnPlayLoggedAsync(play2, context2);

        var starAchievement = achievements.LastOrDefault(a => a.Title.Contains("New Star Rating Record"));
        starAchievement.Should().NotBeNull();
        starAchievement!.Description.Should().Contain("6.75");
        starAchievement.AccentColorHex.Should().Be("#facc15");
    }

    [Fact]
    public async Task SessionSummaryGeneration_WithMultiplePlays_ComputesAccurately()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        
        var baseline = new RollingPeriodStats(
            PeriodDays: 30,
            TotalPlays: 200,
            TotalActiveHours: 20.0,
            MeanAccuracy: 95.5m,
            MeanStars: 5.3m,
            PassRatePercent: 70.0,
            MeanBpm: 175.0,
            PlaysPerActiveDay: 10.0,
            HoursPerActiveDay: 1.0
        );
        
        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>
            {
                ["30D"] = baseline
            });

        var tracker = new LiveSessionTracker(sessionService.Object);

        for (int i = 1; i <= 10; i++)
        {
            var play = new PlayEntryData(
                BeatmapString: $"Map {i}",
                BeatmapSetID: i,
                BeatmapID: i,
                Hidden: false,
                Hardrock: false,
                Doubletime: false,
                EZ: false,
                Halftime: false,
                Flashlight: false,
                BeatmapBpm: 180,
                BeatmapAim: 2.8m,
                BeatmapSpeed: 2.8m,
                BeatmapStars: 5.6m,
                BeatmapCs: 4.0m,
                BeatmapAr: 9.0m,
                BeatmapOd: 8.5m,
                TotalBeatmapHits: 400,
                Accuracy: 96.5m,
                Play300c: 380,
                Play100c: 15,
                Play50c: 3,
                PlayMissc: 2,
                Complete: i <= 7,
                PlayTimeSeconds: 120,
                ModsString: "NM",
                PlayCount: 1,
                AccuracyReliable: true,
                BeatmapTitle: $"Title {i}",
                BeatmapArtist: $"Artist {i}",
                BeatmapVersion: "Hard",
                BeatmapHp: 5.0m,
                BeatmapChecksum: ""
            );

            var context = new PlayContext(
                SessionId: Guid.NewGuid().ToString(),
                IsReplay: false,
                RawMods: 0,
                CurrentGameMode: 0,
                DetectedClient: "lazer",
                SoundFilePath: null,
                SubmitSoundEnabled: false
            );

            await tracker.OnPlayLoggedAsync(play, context);
        }

        var summary = await tracker.GenerateSessionSummaryAsync();

        summary.TotalPlays.Should().Be(10);
        summary.TotalPasses.Should().Be(7);
        summary.ActivePlayMinutes.Should().BeApproximately(20.0, 0.5);
        summary.SessionAccuracy.Should().BeApproximately(96.5m, 0.1m);
        summary.SessionAvgStars.Should().BeApproximately(5.6m, 0.1m);
        summary.PassRatePercent.Should().Be(70.0);
        summary.BestPlay.Should().NotBeNull();
        summary.BestPlay!.Stars.Should().Be(5.6m);
    }

    [Fact]
    public void GetCurrentMetrics_WithNoPlays_ReturnsDefaultMetrics()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>());

        var tracker = new LiveSessionTracker(sessionService.Object);

        var metrics = tracker.GetCurrentMetrics();

        metrics.SessionPlayCount.Should().Be(0);
        metrics.SessionPassCount.Should().Be(0);
        metrics.ActivePlayMinutes.Should().Be(0);
    }

    [Fact]
    public async Task ComfortZoneAchievement_WhenConditionsMet_Triggers()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>());

        var tracker = new LiveSessionTracker(sessionService.Object);

        PostPlayAchievement? capturedAchievement = null;
        tracker.AchievementUnlocked += (sender, achievement) =>
        {
            capturedAchievement = achievement;
        };

        var play = new PlayEntryData(
            BeatmapString: "Comfort Zone Map",
            BeatmapSetID: 1,
            BeatmapID: 1,
            Hidden: false,
            Hardrock: false,
            Doubletime: false,
            EZ: false,
            Halftime: false,
            Flashlight: false,
            BeatmapBpm: 180,
            BeatmapAim: 2.85m,
            BeatmapSpeed: 2.85m,
            BeatmapStars: 5.7m,
            BeatmapCs: 4.0m,
            BeatmapAr: 9.0m,
            BeatmapOd: 8.5m,
            TotalBeatmapHits: 400,
            Accuracy: 98.8m,
            Play300c: 390,
            Play100c: 8,
            Play50c: 1,
            PlayMissc: 1,
            Complete: true,
            PlayTimeSeconds: 120,
            ModsString: "NM",
            PlayCount: 1,
            AccuracyReliable: true,
            BeatmapTitle: "Test",
            BeatmapArtist: "Test",
            BeatmapVersion: "Hard",
            BeatmapHp: 5.0m,
            BeatmapChecksum: ""
        );

        var context = new PlayContext(
            SessionId: Guid.NewGuid().ToString(),
            IsReplay: false,
            RawMods: 0,
            CurrentGameMode: 0,
            DetectedClient: "lazer",
            SoundFilePath: null,
            SubmitSoundEnabled: false
        );

        await tracker.OnPlayLoggedAsync(play, context);

        capturedAchievement.Should().NotBeNull();
        capturedAchievement!.Title.Should().Be("Comfort Zone Clear");
        capturedAchievement.AccentColorHex.Should().Be("#4ade80");
    }

    [Fact]
    public async Task SpeedPRAchievement_WhenHighBpmPassed_Triggers()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>());

        var tracker = new LiveSessionTracker(sessionService.Object);

        PostPlayAchievement? capturedAchievement = null;
        tracker.AchievementUnlocked += (sender, achievement) =>
        {
            capturedAchievement = achievement;
        };

        var play = new PlayEntryData(
            BeatmapString: "Speed Map",
            BeatmapSetID: 1,
            BeatmapID: 1,
            Hidden: false,
            Hardrock: false,
            Doubletime: false,
            EZ: false,
            Halftime: false,
            Flashlight: false,
            BeatmapBpm: 240,
            BeatmapAim: 2.5m,
            BeatmapSpeed: 4.0m,
            BeatmapStars: 6.5m,
            BeatmapCs: 4.0m,
            BeatmapAr: 9.5m,
            BeatmapOd: 9.0m,
            TotalBeatmapHits: 500,
            Accuracy: 95.2m,
            Play300c: 470,
            Play100c: 25,
            Play50c: 3,
            PlayMissc: 2,
            Complete: true,
            PlayTimeSeconds: 140,
            ModsString: "NM",
            PlayCount: 1,
            AccuracyReliable: true,
            BeatmapTitle: "Test",
            BeatmapArtist: "Test",
            BeatmapVersion: "Insane",
            BeatmapHp: 5.5m,
            BeatmapChecksum: ""
        );

        var context = new PlayContext(
            SessionId: Guid.NewGuid().ToString(),
            IsReplay: false,
            RawMods: 0,
            CurrentGameMode: 0,
            DetectedClient: "lazer",
            SoundFilePath: null,
            SubmitSoundEnabled: false
        );

        await tracker.OnPlayLoggedAsync(play, context);

        capturedAchievement.Should().NotBeNull();
        capturedAchievement!.Title.Should().Be("Speed PR");
        capturedAchievement.Description.Should().Contain("240");
        capturedAchievement.AccentColorHex.Should().Be("#7dd3fc");
    }

    private static PlayEntryData CreatePlayEntry(
        int totalHits,
        decimal accuracy,
        bool complete,
        int playTimeSeconds = 60)
    {
        return new PlayEntryData(
            BeatmapString: "Test Map",
            BeatmapSetID: 1,
            BeatmapID: 1,
            Hidden: false,
            Hardrock: false,
            Doubletime: false,
            EZ: false,
            Halftime: false,
            Flashlight: false,
            BeatmapBpm: 180,
            BeatmapAim: 2.5m,
            BeatmapSpeed: 2.5m,
            BeatmapStars: 5.0m,
            BeatmapCs: 4.0m,
            BeatmapAr: 9.0m,
            BeatmapOd: 8.0m,
            TotalBeatmapHits: totalHits,
            Accuracy: accuracy,
            Play300c: totalHits,
            Play100c: 0,
            Play50c: 0,
            PlayMissc: 0,
            Complete: complete,
            PlayTimeSeconds: playTimeSeconds,
            ModsString: "NM",
            PlayCount: 1,
            AccuracyReliable: true,
            BeatmapTitle: "Test Map",
            BeatmapArtist: "Artist",
            BeatmapVersion: "Normal",
            BeatmapHp: 5.0m,
            BeatmapChecksum: ""
        )
        {
            TotalHits = totalHits
        };
    }

    [Fact]
    public async Task GenerateSessionSummary_MixedRetriesAndFullPass_ComputesAccurateHitWeightedDelta()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        var baseline = new RollingPeriodStats(
            PeriodDays: 30,
            TotalPlays: 200,
            TotalActiveHours: 20.0,
            MeanAccuracy: 98.0m,
            MeanStars: 5.0m,
            PassRatePercent: 70.0,
            MeanBpm: 180.0,
            PlaysPerActiveDay: 10.0,
            HoursPerActiveDay: 1.0
        );

        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>
            {
                ["30D"] = baseline
            });

        var tracker = new LiveSessionTracker(sessionService.Object);

        for (int i = 0; i < 10; i++)
        {
            var retry = CreatePlayEntry(totalHits: 20, accuracy: 75.0m, complete: false, playTimeSeconds: 10);
            await tracker.ProcessPlay(retry);
        }

        var fullPass = CreatePlayEntry(totalHits: 1800, accuracy: 99.5m, complete: true, playTimeSeconds: 180);
        await tracker.ProcessPlay(fullPass);

        var report = await tracker.GenerateSessionSummaryAsync();

        report.SessionAccuracy.Should().BeApproximately(97.05m, 0.05m);
        report.BaselineDeltaAccuracy.Should().BeApproximately(-0.95m, 0.05m);
    }

    [Fact]
    public async Task GenerateSessionSummary_ZeroHitsSession_DoesNotThrowDivideByZero()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        var baseline = new RollingPeriodStats(
            PeriodDays: 30,
            TotalPlays: 100,
            TotalActiveHours: 10.0,
            MeanAccuracy: 98.0m,
            MeanStars: 5.0m,
            PassRatePercent: 70.0,
            MeanBpm: 180.0,
            PlaysPerActiveDay: 10.0,
            HoursPerActiveDay: 1.0
        );

        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>
            {
                ["30D"] = baseline
            });

        var tracker = new LiveSessionTracker(sessionService.Object);

        var failedPlay = CreatePlayEntry(totalHits: 0, accuracy: 0m, complete: false, playTimeSeconds: 5);
        await tracker.ProcessPlay(failedPlay);

        var report = await tracker.GenerateSessionSummaryAsync();

        report.SessionAccuracy.Should().Be(0m);
    }

    [Fact]
    public async Task ProcessPlay_RealTimeHudDelta_ReflectsWeightedCalculations()
    {
        var sessionService = new Mock<ISessionAnalyticsService>();
        var baseline = new RollingPeriodStats(
            PeriodDays: 30,
            TotalPlays: 200,
            TotalActiveHours: 20.0,
            MeanAccuracy: 98.0m,
            MeanStars: 5.0m,
            PassRatePercent: 70.0,
            MeanBpm: 180.0,
            PlaysPerActiveDay: 10.0,
            HoursPerActiveDay: 1.0
        );

        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>
            {
                ["30D"] = baseline
            });

        var tracker = new LiveSessionTracker(sessionService.Object);

        var play1 = CreatePlayEntry(totalHits: 100, accuracy: 92.0m, complete: false, playTimeSeconds: 20);
        await tracker.ProcessPlay(play1);

        tracker.SessionAccuracy.Should().BeApproximately(92.0m, 0.01m);
        tracker.BaselineDeltaAccuracy.Should().BeApproximately(-6.0m, 0.01m);

        var play2 = CreatePlayEntry(totalHits: 300, accuracy: 100.0m, complete: true, playTimeSeconds: 60);
        await tracker.ProcessPlay(play2);

        tracker.SessionAccuracy.Should().BeApproximately(98.0m, 0.01m);
        tracker.BaselineDeltaAccuracy.Should().BeApproximately(0.0m, 0.01m);

        var play3 = CreatePlayEntry(totalHits: 400, accuracy: 96.0m, complete: true, playTimeSeconds: 100);
        await tracker.ProcessPlay(play3);

        tracker.SessionAccuracy.Should().BeApproximately(97.0m, 0.01m);
        tracker.BaselineDeltaAccuracy.Should().BeApproximately(-1.0m, 0.01m);
    }
}
