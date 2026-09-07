using Circle_Tracker;
using Circle_Tracker.Services;
using FluentAssertions;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.ServiceTests;

public class BeatmapStateTrackerTests
{
    [Fact]
    public void UpdateModsFromBitfield_HDDT_SetsCorrectFlags()
    {
        var tracker = new BeatmapStateTracker(Mock.Of<ITosuClient>());
        int mods = (1 << 3) | (1 << 6);

        tracker.UpdateModsFromBitfield(mods);

        tracker.Hidden.Should().BeTrue();
        tracker.Doubletime.Should().BeTrue();
        tracker.Hardrock.Should().BeFalse();
        tracker.EZ.Should().BeFalse();
        tracker.Halftime.Should().BeFalse();
        tracker.Flashlight.Should().BeFalse();
        tracker.Auto.Should().BeFalse();
    }

    [Fact]
    public void UpdateModsFromBitfield_None_AllFalse()
    {
        var tracker = new BeatmapStateTracker(Mock.Of<ITosuClient>());

        tracker.UpdateModsFromBitfield(0);

        tracker.Hidden.Should().BeFalse();
        tracker.Doubletime.Should().BeFalse();
        tracker.Hardrock.Should().BeFalse();
        tracker.EZ.Should().BeFalse();
        tracker.Halftime.Should().BeFalse();
        tracker.Flashlight.Should().BeFalse();
        tracker.Auto.Should().BeFalse();
    }

    [Fact]
    public void UpdateModsFromBitfield_Nightcore_SetsDTTrue()
    {
        var tracker = new BeatmapStateTracker(Mock.Of<ITosuClient>());
        int mods = 1 << 9;

        tracker.UpdateModsFromBitfield(mods);

        tracker.Doubletime.Should().BeTrue();
    }

    [Fact]
    public void GetModsString_MultipleMods_ReturnsCorrectString()
    {
        var tracker = new BeatmapStateTracker(Mock.Of<ITosuClient>());
        tracker.UpdateModsFromBitfield((1 << 3) | (1 << 6));

        var result = tracker.GetModsString();

        result.Should().Be("HDDT");
    }

    [Fact]
    public void GetModsString_NoMods_ReturnsEmpty()
    {
        var tracker = new BeatmapStateTracker(Mock.Of<ITosuClient>());
        tracker.UpdateModsFromBitfield(0);

        var result = tracker.GetModsString();

        result.Should().BeEmpty();
    }

    [Fact]
    public void UpdateBeatmapFromState_ValidState_SetsAllProperties()
    {
        var tracker = new BeatmapStateTracker(Mock.Of<ITosuClient>());
        var state = new TosuState
        {
            Beatmap = new TosuBeatmap
            {
                Id = 12345,
                Set = 67890,
                Title = "Test Title",
                Artist = "Test Artist",
                Version = "Insane",
                Checksum = "test_checksum_hash",
                Time = new TosuBeatmapTime { FirstObject = 1500 },
                Stats = new TosuBeatmapStats
                {
                    Hp = new TosuStatValue { Converted = 5.5m },
                    Bpm = new TosuBpm { Common = 180 },
                    Stars = new TosuStars { Total = 6.25m, Aim = 3.1m, Speed = 2.8m },
                    Cs = new TosuStatValue { Converted = 4.0m },
                    Ar = new TosuStatValue { Converted = 9.3m },
                    Od = new TosuStatValue { Converted = 8.5m }
                }
            }
        };

        tracker.UpdateBeatmapFromState(state);

        tracker.BeatmapID.Should().Be(12345);
        tracker.BeatmapSetID.Should().Be(67890);
        tracker.BeatmapTitle.Should().Be("Test Title");
        tracker.BeatmapArtist.Should().Be("Test Artist");
        tracker.BeatmapVersion.Should().Be("Insane");
        tracker.BeatmapString.Should().Be("Test Artist - Test Title [Insane]");
        tracker.BeatmapHp.Should().Be(5.5m);
        tracker.BeatmapBpm.Should().Be(180);
        tracker.FirstHitObjectTime.Should().Be(1500);
        tracker.BeatmapStars.Should().Be(6.25m);
        tracker.BeatmapAim.Should().Be(3.1m);
        tracker.BeatmapSpeed.Should().Be(2.8m);
        tracker.BeatmapCs.Should().Be(4.0m);
        tracker.BeatmapAr.Should().Be(9.3m);
        tracker.BeatmapOd.Should().Be(8.5m);
        tracker.CurrentBeatmapChecksum.Should().Be("test_checksum_hash");
    }

    [Fact]
    public void UpdateBeatmapFromState_NullBeatmap_DoesNotThrow()
    {
        var tracker = new BeatmapStateTracker(Mock.Of<ITosuClient>());
        var state = new TosuState { Beatmap = null };

        var act = () => tracker.UpdateBeatmapFromState(state);

        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateBeatmapFromState_NullState_DoesNotThrow()
    {
        var tracker = new BeatmapStateTracker(Mock.Of<ITosuClient>());

        var act = () => tracker.UpdateBeatmapFromState(null!);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task UpdateDifficultyFromPpApi_DifficultyPresent_SetsDifficultyProperties()
    {
        var mockTosu = new Mock<ITosuClient>();
        mockTosu.Setup(c => c.CalculatePpAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PpCalcResult
            {
                Difficulty = new PpDifficulty
                {
                    Aim = 3.2m,
                    Speed = 2.7m,
                    Stars = 5.9m,
                    Ar = 9.2m,
                    Od = 8.6m,
                    Cs = 4.1m,
                    Hp = 5.8m,
                    Bpm = 190,
                    ClockRate = 1.5
                }
            });
        var tracker = new BeatmapStateTracker(mockTosu.Object);

        await tracker.UpdateDifficultyFromPpApi(64);

        tracker.BeatmapAim.Should().Be(3.2m);
        tracker.BeatmapSpeed.Should().Be(2.7m);
        tracker.BeatmapStars.Should().Be(5.9m);
        tracker.BeatmapAr.Should().Be(9.2m);
        tracker.BeatmapOd.Should().Be(8.6m);
        tracker.BeatmapCs.Should().Be(4.1m);
        tracker.BeatmapHp.Should().Be(5.8m);
        tracker.BeatmapBpm.Should().Be(190);
        tracker.LastClockRate.Should().Be(1.5f);
    }

    [Fact]
    public async Task CancelledPpLookup_LeavesPreviousDifficultyIntact()
    {
        var mockTosu = new Mock<ITosuClient>();
        mockTosu.Setup(c => c.CalculatePpAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PpCalcResult
            {
                Difficulty = new PpDifficulty { Stars = 5.9m }
            });
        var tracker = new BeatmapStateTracker(mockTosu.Object);
        tracker.BeatmapStars = 4.2m;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await tracker.UpdateDifficultyFromPpApi(0, cts.Token);

        tracker.BeatmapStars.Should().Be(4.2m);
    }

    [Fact]
    public async Task StalePpResponse_AfterMapChange_DoesNotOverwriteCurrentMapDifficulty()
    {
        var gate = new TaskCompletionSource<PpCalcResult?>();
        var mockTosu = new Mock<ITosuClient>();
        mockTosu.Setup(c => c.CalculatePpAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        var tracker = new BeatmapStateTracker(mockTosu.Object);
        tracker.CurrentBeatmapChecksum = "map-a";

        var pending = tracker.UpdateDifficultyFromPpApi(0);

        tracker.CurrentBeatmapChecksum = "map-b";
        tracker.BeatmapStars = 7.1m;
        gate.SetResult(new PpCalcResult
        {
            Difficulty = new PpDifficulty { Stars = 5.9m }
        });
        await pending;

        tracker.BeatmapStars.Should().Be(7.1m);
    }

    [Fact]
    public async Task RapidMapChanges_KeepLatestDifficultyWithoutThrowing()
    {
        var mockTosu = new Mock<ITosuClient>();
        mockTosu.Setup(c => c.CalculatePpAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PpCalcResult
            {
                Difficulty = new PpDifficulty { Stars = 5.9m }
            });
        var tracker = new BeatmapStateTracker(mockTosu.Object);

        for (int i = 0; i < 20; i++)
        {
            tracker.CurrentBeatmapChecksum = $"map-{i}";
            tracker.FireUpdateDifficultyFromPpApi(0);
        }

        for (int spin = 0; spin < 1000 && tracker.BeatmapStars == 0; spin++)
        {
            await Task.Yield();
        }

        tracker.BeatmapStars.Should().Be(5.9m);
    }
}
