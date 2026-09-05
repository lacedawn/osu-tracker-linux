using Avalonia.Headless.XUnit;
using Circle_Tracker.ViewModels;
using FluentAssertions;
using Xunit;
using static Circle_Tracker.Tracker;

namespace Circle_Tracker.Tests.ViewModelTests;

public class BeatmapBannerViewModelTests
{
    [AvaloniaFact]
    public void Should_FormatBeatmapTitleAndStars_Correctly()
    {
        var viewModel = new BeatmapBannerViewModel();
        var snapshot = new TrackerSnapshot(
            IsPlaying: false,
            IsReplay: false,
            DetectedClient: "",
            BeatmapString: "Camellia - crystallized [Crystal]",
            BeatmapTitle: "crystallized",
            BeatmapArtist: "Camellia",
            BeatmapVersion: "Crystal",
            BeatmapId: 101,
            BeatmapSetId: 202,
            BeatmapHp: 6.0m,
            BeatmapStars: 6.25m,
            BeatmapAim: 3.1m,
            BeatmapSpeed: 3.1m,
            BeatmapCs: 4.2m,
            BeatmapAr: 9.6m,
            BeatmapOd: 9.0m,
            BeatmapBpm: 220,
            TotalBeatmapHits: 1200,
            Play300c: 0,
            Play100c: 0,
            Play50c: 0,
            PlayMissc: 0,
            Accuracy: 100m,
            Time: 0,
            ModsString: "",
            GameStateLabel: "IDLE",
            SheetsApiReady: false,
            MemoryReadError: false,
            PlayingSeconds: 0,
            IdleSeconds: 0
        );

        viewModel.UpdateFromSnapshot(snapshot);

        viewModel.BeatmapStars.Should().Be("★ 6.25");
        viewModel.BeatmapTitle.Should().Be("crystallized");
        viewModel.BeatmapArtist.Should().Be("Camellia");
        viewModel.BeatmapVersion.Should().Be("Crystal");
        viewModel.BannerTrianglesVisible.Should().BeFalse();

        var fallbackSnapshot = snapshot with { BeatmapTitle = "", BeatmapString = "Fallback Artist - Fallback Title [Hard]" };
        viewModel.UpdateFromSnapshot(fallbackSnapshot);
        viewModel.BeatmapTitle.Should().Be("Fallback Artist - Fallback Title [Hard]");
        viewModel.BannerTrianglesVisible.Should().BeFalse();

        var emptySnapshot = snapshot with { BeatmapTitle = "", BeatmapString = "", BeatmapArtist = "", BeatmapVersion = "" };
        viewModel.UpdateFromSnapshot(emptySnapshot);
        viewModel.BeatmapTitle.Should().Be("No beatmap detected");
        viewModel.BeatmapArtist.Should().Be("-");
        viewModel.BeatmapVersion.Should().Be("-");
        viewModel.BannerTrianglesVisible.Should().BeTrue();
    }
}
