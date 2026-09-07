using Circle_Tracker;
using FluentAssertions;
using Xunit;

namespace CircleTracker.Tests.Models;

public class PlayEntryDataTests
{
    private static PlayEntryData BuildSample(
        int beatmapId = 129891,
        decimal accuracy = 99.45m,
        bool complete = true)
        => new PlayEntryData(
            BeatmapString: "xi - FREEDOM DiVE [FOUR DIMENSIONS]",
            BeatmapSetID: 39804,
            BeatmapID: beatmapId,
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
            Accuracy: accuracy,
            Play300c: 1950,
            Play100c: 33,
            Play50c: 0,
            PlayMissc: 0,
            Complete: complete,
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

    [Fact]
    public void PlayEntryData_DefaultConstruction_HasExpectedDefaults()
    {
        var data = BuildSample();

        data.BeatmapID.Should().Be(129891);
        data.BeatmapSetID.Should().Be(39804);
        data.BeatmapString.Should().Be("xi - FREEDOM DiVE [FOUR DIMENSIONS]");
        data.Hidden.Should().BeTrue();
        data.Hardrock.Should().BeTrue();
        data.Doubletime.Should().BeFalse();
        data.EZ.Should().BeFalse();
        data.Halftime.Should().BeFalse();
        data.Flashlight.Should().BeFalse();
        data.BeatmapBpm.Should().Be(222);
        data.BeatmapStars.Should().Be(7.82m);
        data.Accuracy.Should().Be(99.45m);
        data.Play300c.Should().Be(1950);
        data.Play100c.Should().Be(33);
        data.Play50c.Should().Be(0);
        data.PlayMissc.Should().Be(0);
        data.Complete.Should().BeTrue();
        data.PlayTimeSeconds.Should().Be(255);
        data.ModsString.Should().Be("HDHR");
        data.PlayCount.Should().Be(3);
        data.AccuracyReliable.Should().BeTrue();
        data.BeatmapTitle.Should().Be("FREEDOM DiVE");
        data.BeatmapArtist.Should().Be("xi");
        data.BeatmapVersion.Should().Be("FOUR DIMENSIONS");
        data.BeatmapHp.Should().Be(7.0m);
        data.BeatmapChecksum.Should().Be("d41d8cd98f00b204e9800998ecf8427e");
    }

    [Fact]
    public void PlayEntryData_Equality_SameValuesAreEqual()
    {
        var first = BuildSample();
        var second = BuildSample();

        first.Should().Be(second);
    }

    [Fact]
    public void PlayEntryData_Equality_DifferentBeatmapId_AreNotEqual()
    {
        var first = BuildSample(beatmapId: 100);
        var second = BuildSample(beatmapId: 200);

        first.Should().NotBe(second);
    }
}
