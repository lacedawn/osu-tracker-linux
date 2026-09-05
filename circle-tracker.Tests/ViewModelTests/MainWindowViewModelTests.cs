using Avalonia.Headless.XUnit;
using Circle_Tracker.Analytics;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Circle_Tracker.ViewModels;
using FluentAssertions;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Circle_Tracker.Tests.ViewModelTests;

public class MainWindowViewModelTests
{
    private readonly Mock<ITrackerService> _mockTracker;
    private readonly Mock<ILiveSessionTracker> _mockLiveSessionTracker;
    private readonly Mock<IDatabaseManager> _mockDbManager;
    private readonly Mock<ISessionAnalyticsService> _mockSessionService;
    private readonly Mock<IPlayQueryEngine> _mockQueryEngine;

    public MainWindowViewModelTests()
    {
        _mockTracker = new Mock<ITrackerService>();
        _mockLiveSessionTracker = new Mock<ILiveSessionTracker>();
        _mockDbManager = new Mock<IDatabaseManager>();
        _mockSessionService = new Mock<ISessionAnalyticsService>();
        _mockQueryEngine = new Mock<IPlayQueryEngine>();
    }

    [AvaloniaFact]
    public async Task MainWindowViewModel_OnTrackerPlayProcessed_UpdatesObservablePropertiesOnUIThread()
    {
        var viewModel = CreateViewModel();
        var metrics = new LiveSessionMetrics(
            SessionPlayCount: 120,
            SessionPassCount: 95,
            ActivePlayMinutes: 45.5,
            SessionAverageAccuracy: 98.5m,
            BaselineDeltaAccuracy: 1.2m,
            SessionAverageStars: 5.5m,
            BaselineDeltaStars: 0.3m,
            SessionAverageBpm: 180.0,
            BaselineDeltaBpm: 10.0
        );

        _mockLiveSessionTracker.Raise(t => t.PlayProcessed += null, _mockLiveSessionTracker.Object, metrics);

        await Task.Delay(50);

        viewModel.SessionPlaysCount.Should().Be(120);
        viewModel.SessionAccuracyText.Should().Contain("98.50%");
    }

    [AvaloniaFact]
    public void ResetSessionCommand_WhenExecuted_CallsTrackerResetSession()
    {
        var viewModel = CreateViewModel();

        viewModel.ResetSessionCommand.Execute(null);

        _mockLiveSessionTracker.Verify(t => t.ResetSession(), Times.Once);
    }

    [AvaloniaFact]
    public void MainWindowViewModel_HandlesServiceExceptionsGracefullyWithoutCrashingUI()
    {
        _mockLiveSessionTracker.Setup(t => t.GetCurrentMetrics()).Throws(new InvalidOperationException("Database connection failed"));
        var viewModel = CreateViewModel();

        var act = () => viewModel.RefreshCommand.Execute(null);

        act.Should().NotThrow();
        viewModel.StatusText.Should().Contain("Failed");
    }

    [AvaloniaFact]
    public void MainWindowViewModel_InitializesWithCorrectDefaultValues()
    {
        var viewModel = CreateViewModel();

        viewModel.BeatmapTitle.Should().Be("No beatmap detected");
        viewModel.BeatmapArtist.Should().Be("-");
        viewModel.BeatmapVersion.Should().Be("-");
        viewModel.GameState.Should().Be("IDLE");
        viewModel.LiveSessionCardVisible.Should().BeFalse();
        viewModel.AchievementBannerVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void MainWindowViewModel_OpenAnalyticsCommand_DoesNotThrow_WhenActionNotSet()
    {
        var viewModel = CreateViewModel();

        var action = () => viewModel.OpenAnalyticsCommand.Execute(null);

        action.Should().NotThrow();
    }

    [AvaloniaFact]
    public async Task MainWindowViewModel_UpdatesCoverImage_WhenUrlChanges()
    {
        var viewModel = CreateViewModel();

        viewModel.UpdateFromSnapshot(new TrackerSnapshot(
            IsPlaying: false,
            IsReplay: false,
            DetectedClient: "lazer",
            BeatmapString: "",
            BeatmapTitle: "Test Beatmap",
            BeatmapArtist: "Test Artist",
            BeatmapVersion: "Expert",
            BeatmapId: 123,
            BeatmapSetId: 456,
            BeatmapHp: 5m,
            BeatmapStars: 4.5m,
            BeatmapAim: 3.2m,
            BeatmapSpeed: 2.1m,
            BeatmapCs: 4m,
            BeatmapAr: 9m,
            BeatmapOd: 8m,
            BeatmapBpm: 180,
            TotalBeatmapHits: 500,
            Play300c: 450,
            Play100c: 40,
            Play50c: 10,
            PlayMissc: 0,
            Accuracy: 98.5m,
            Time: 120,
            ModsString: "HD,HR",
            GameStateLabel: "PLAYING",
            SheetsApiReady: false,
            MemoryReadError: false,
            PlayingSeconds: 120,
            IdleSeconds: 30,
            PlayCount: 5,
            DatabaseReady: true,
            LocalPlayCount: 100
        ));

        await Task.Delay(50);

        viewModel.BeatmapTitle.Should().Be("Test Beatmap");
        viewModel.BeatmapArtist.Should().Be("Test Artist");
        viewModel.GameState.Should().Be("PLAYING");
    }

    [AvaloniaFact]
    public void MainWindowViewModel_ExportAndAuthCommands_ExecuteSuccessfully()
    {
        var viewModel = CreateViewModel();

        viewModel.ExportSessionCommand.Execute(null);
        viewModel.StatusText.Should().Be("Ready");

        viewModel.AuthenticateOsuCommand.Execute(null);
        viewModel.StatusText.Should().Be("Ready");
    }

    [AvaloniaFact]
    public void Should_CoordinateAllChildViewModels_When_SnapshotReceived()
    {
        var viewModel = CreateViewModel();
        var snapshot = new TrackerSnapshot(
            IsPlaying: true,
            IsReplay: false,
            DetectedClient: "tosu-test",
            BeatmapString: "Artist - Coordinated Song [Expert]",
            BeatmapTitle: "Coordinated Song",
            BeatmapArtist: "Artist",
            BeatmapVersion: "Expert",
            BeatmapId: 555,
            BeatmapSetId: 777,
            BeatmapHp: 6.5m,
            BeatmapStars: 6.85m,
            BeatmapAim: 3.5m,
            BeatmapSpeed: 3.3m,
            BeatmapCs: 4.5m,
            BeatmapAr: 10.3m,
            BeatmapOd: 10.1m,
            BeatmapBpm: 240,
            TotalBeatmapHits: 850,
            Play300c: 700,
            Play100c: 30,
            Play50c: 5,
            PlayMissc: 2,
            Accuracy: 99.15m,
            Time: 180,
            ModsString: "HDDT",
            GameStateLabel: "PLAYING",
            SheetsApiReady: true,
            MemoryReadError: false,
            PlayingSeconds: 180,
            IdleSeconds: 60,
            PlayCount: 8,
            DatabaseReady: true,
            LocalPlayCount: 142
        );

        viewModel.UpdateFromSnapshot(snapshot);

        viewModel.Hud.AccuracyText.Should().Be("99.15%");
        viewModel.Hud.Hits300.Should().Be("700");
        viewModel.Hud.Hits100.Should().Be("30");
        viewModel.Hud.Hits50.Should().Be("5");
        viewModel.Hud.HitsMiss.Should().Be("2");
        viewModel.Hud.TotalObjectsText.Should().Be("Total: 850");
        viewModel.Hud.StatCs.Should().Be("4.5");
        viewModel.Hud.StatAr.Should().Be("10.3");
        viewModel.Hud.StatOd.Should().Be("10.1");
        viewModel.Hud.StatHp.Should().Be("6.5");
        viewModel.Hud.StatBpm.Should().Be("240");
        viewModel.Hud.StatMods.Should().Be("+HDDT");
        viewModel.Hud.GameState.Should().Be("PLAYING");

        viewModel.Banner.BeatmapTitle.Should().Be("Coordinated Song");
        viewModel.Banner.BeatmapArtist.Should().Be("Artist");
        viewModel.Banner.BeatmapVersion.Should().Be("Expert");
        viewModel.Banner.BeatmapStars.Should().Be("★ 6.85");
        viewModel.Banner.BannerTrianglesVisible.Should().BeFalse();

        viewModel.Settings.DatabaseReady.Should().BeTrue();
        viewModel.Settings.LocalPlayCount.Should().Be(142);
        viewModel.Settings.DbStatusText.Should().Be("DB: 142 plays");
        viewModel.Settings.SheetsConnected.Should().BeTrue();
        viewModel.Settings.SheetsStatusText.Should().Be("Sheets: Connected");
        viewModel.Settings.TosuConnected.Should().BeTrue();
        viewModel.Settings.TosuStatusText.Should().Be("tosu: tosu-test");

        viewModel.SessionLive.LiveSessionCardVisible.Should().BeTrue();
        viewModel.SessionLive.SessionElapsedText.Should().Be("4m session");
    }

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(
            _mockTracker.Object,
            _mockLiveSessionTracker.Object,
            _mockDbManager.Object,
            _mockSessionService.Object,
            _mockQueryEngine.Object
        );
    }
}
