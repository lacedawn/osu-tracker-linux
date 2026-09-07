using Circle_Tracker;
using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using FluentAssertions;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.ServiceTests;

public class LiveSessionTrackerTests
{
    private static Mock<ISessionAnalyticsService> MockAnalyticsService()
    {
        var mock = new Mock<ISessionAnalyticsService>();
        mock.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>());
        return mock;
    }

    private static PlayEntryData BuildPlay(decimal accuracy, bool complete, int totalBeatmapHits = 100)
        => new PlayEntryData(
            BeatmapString: "Test Song [Hard]",
            BeatmapSetID: 1,
            BeatmapID: 1,
            Hidden: false,
            Hardrock: false,
            Doubletime: false,
            EZ: false,
            Halftime: false,
            Flashlight: false,
            BeatmapBpm: 180,
            BeatmapAim: 2.0m,
            BeatmapSpeed: 2.0m,
            BeatmapStars: 5.0m,
            BeatmapCs: 4.0m,
            BeatmapAr: 9.0m,
            BeatmapOd: 8.0m,
            TotalBeatmapHits: totalBeatmapHits,
            Accuracy: accuracy,
            Play300c: 0,
            Play100c: 0,
            Play50c: 0,
            PlayMissc: 0,
            Complete: complete,
            PlayTimeSeconds: 60,
            ModsString: "",
            PlayCount: 1,
            AccuracyReliable: true
        );

    [Fact]
    public async Task GenerateSessionSummary_MixedCompleteness_PassAccuracyIsAverageOfPassesOnly()
    {
        var tracker = new LiveSessionTracker(MockAnalyticsService().Object);

        await tracker.ProcessPlay(BuildPlay(accuracy: 90m, complete: true));
        await tracker.ProcessPlay(BuildPlay(accuracy: 80m, complete: true));
        await tracker.ProcessPlay(BuildPlay(accuracy: 40m, complete: false));

        var report = await tracker.GenerateSessionSummaryAsync();

        report.PassAccuracy.Should().Be(85m);
    }

    [Fact]
    public async Task GenerateSessionSummary_AllPlaysIncomplete_PassAccuracyIsZero()
    {
        var tracker = new LiveSessionTracker(MockAnalyticsService().Object);

        await tracker.ProcessPlay(BuildPlay(accuracy: 70m, complete: false));
        await tracker.ProcessPlay(BuildPlay(accuracy: 60m, complete: false));

        var report = await tracker.GenerateSessionSummaryAsync();

        report.PassAccuracy.Should().Be(0m);
    }

    [Fact]
    public async Task GenerateSessionSummary_AllPlaysComplete_PassAccuracyEqualsSessionAccuracy()
    {
        var tracker = new LiveSessionTracker(MockAnalyticsService().Object);

        await tracker.ProcessPlay(BuildPlay(accuracy: 95m, complete: true, totalBeatmapHits: 100));
        await tracker.ProcessPlay(BuildPlay(accuracy: 95m, complete: true, totalBeatmapHits: 100));

        var report = await tracker.GenerateSessionSummaryAsync();

        report.PassAccuracy.Should().Be(95m);
        report.SessionAccuracy.Should().Be(95m);
    }

    [Fact]
    public async Task GenerateSessionSummary_ZeroPlays_ReturnsZeroPassAccuracy()
    {
        var tracker = new LiveSessionTracker(MockAnalyticsService().Object);

        var act = async () => await tracker.GenerateSessionSummaryAsync();
        await act.Should().NotThrowAsync();

        var report = await tracker.GenerateSessionSummaryAsync();

        report.PassAccuracy.Should().Be(0m);
    }
}
