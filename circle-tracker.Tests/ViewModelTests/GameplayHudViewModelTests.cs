using Avalonia.Headless.XUnit;
using Circle_Tracker.ViewModels;
using FluentAssertions;
using Xunit;
using static Circle_Tracker.Tracker;

namespace Circle_Tracker.Tests.ViewModelTests;

public class GameplayHudViewModelTests
{
    [AvaloniaFact]
    public void Should_UpdateHitCountsAndAccuracy_When_SnapshotApplied()
    {
        var viewModel = new GameplayHudViewModel();
        var snapshot = new TrackerSnapshot(
            IsPlaying: true,
            IsReplay: false,
            DetectedClient: "tosu",
            BeatmapString: "Artist - Title [Insane]",
            BeatmapTitle: "Title",
            BeatmapArtist: "Artist",
            BeatmapVersion: "Insane",
            BeatmapId: 100,
            BeatmapSetId: 200,
            BeatmapHp: 5.0m,
            BeatmapStars: 5.50m,
            BeatmapAim: 2.5m,
            BeatmapSpeed: 2.5m,
            BeatmapCs: 4.0m,
            BeatmapAr: 10.0m,
            BeatmapOd: 10.0m,
            BeatmapBpm: 210,
            TotalBeatmapHits: 600,
            Play300c: 500,
            Play100c: 80,
            Play50c: 15,
            PlayMissc: 5,
            Accuracy: 98.75m,
            Time: 120,
            ModsString: "HDHR",
            GameStateLabel: "PLAYING",
            SheetsApiReady: true,
            MemoryReadError: false,
            PlayingSeconds: 120,
            IdleSeconds: 30,
            PlayCount: 12,
            DatabaseReady: true,
            LocalPlayCount: 50
        );

        viewModel.UpdateFromSnapshot(snapshot);

        viewModel.Hits300.Should().Be("500");
        viewModel.Hits100.Should().Be("80");
        viewModel.Hits50.Should().Be("15");
        viewModel.HitsMiss.Should().Be("5");
        viewModel.TotalObjectsText.Should().Be("Total: 600");
        viewModel.AccuracyText.Should().Be("98.75%");
        viewModel.AccuracyBrush.Should().Be(AppBrushes.GreenBrush);
        viewModel.StatArBrush.Should().Be(AppBrushes.GreenBrush);
        viewModel.StatOdBrush.Should().Be(AppBrushes.GreenBrush);
        viewModel.StatBpmBrush.Should().Be(AppBrushes.OrangeBrush);
        viewModel.StatMods.Should().Be("+HDHR");
        viewModel.StatModsBrush.Should().Be(AppBrushes.PinkBrush);
        viewModel.PlayCountBadge.Should().Be("Play #12");
        viewModel.SessionTimeText.Should().Be("Play: 2m  •  Idle: 0m  •  Efficiency: 80%");

        var perfectSnapshot = snapshot with { Accuracy = 100.0m };
        viewModel.UpdateFromSnapshot(perfectSnapshot);
        viewModel.AccuracyBrush.Should().Be(AppBrushes.GoldBrush);

        var lowAccSnapshot = snapshot with { Accuracy = 92.50m };
        viewModel.UpdateFromSnapshot(lowAccSnapshot);
        viewModel.AccuracyBrush.Should().Be(AppBrushes.WhiteBrush);
    }

    [AvaloniaFact]
    public void Should_SetAppropriateGameStateBrush_For_PlayingResultsAndReplay()
    {
        var viewModel = new GameplayHudViewModel();
        var baseSnapshot = new TrackerSnapshot(
            IsPlaying: false,
            IsReplay: false,
            DetectedClient: "",
            BeatmapString: "",
            BeatmapTitle: "",
            BeatmapArtist: "",
            BeatmapVersion: "",
            BeatmapId: 0,
            BeatmapSetId: 0,
            BeatmapHp: 0m,
            BeatmapStars: 0m,
            BeatmapAim: 0m,
            BeatmapSpeed: 0m,
            BeatmapCs: 0m,
            BeatmapAr: 0m,
            BeatmapOd: 0m,
            BeatmapBpm: 0,
            TotalBeatmapHits: 0,
            Play300c: 0,
            Play100c: 0,
            Play50c: 0,
            PlayMissc: 0,
            Accuracy: 0m,
            Time: 0,
            ModsString: "",
            GameStateLabel: "IDLE",
            SheetsApiReady: false,
            MemoryReadError: false,
            PlayingSeconds: 0,
            IdleSeconds: 0
        );

        viewModel.UpdateFromSnapshot(baseSnapshot with { GameStateLabel = "PLAYING" });
        viewModel.GameState.Should().Be("PLAYING");
        viewModel.GameStateBrush.Should().Be(AppBrushes.GreenBrush);

        viewModel.UpdateFromSnapshot(baseSnapshot with { GameStateLabel = "RESULTS" });
        viewModel.GameState.Should().Be("RESULTS");
        viewModel.GameStateBrush.Should().Be(AppBrushes.CyanBrush);

        viewModel.UpdateFromSnapshot(baseSnapshot with { GameStateLabel = "REPLAY" });
        viewModel.GameState.Should().Be("REPLAY");
        viewModel.GameStateBrush.Should().Be(AppBrushes.OrangeBrush);

        viewModel.UpdateFromSnapshot(baseSnapshot with { GameStateLabel = "SONG_SELECT" });
        viewModel.GameState.Should().Be("SONG_SELECT");
        viewModel.GameStateBrush.Should().Be(AppBrushes.MutedBrush);
    }
}
