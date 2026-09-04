using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Circle_Tracker;
using Circle_Tracker.Analytics;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Circle_Tracker.ViewModels;
using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(CircleTracker.Tests.ViewModelTests.AnalyticsViewModelTestAppBuilder))]

namespace CircleTracker.Tests.ViewModelTests;

public class AnalyticsViewModelTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class AnalyticsViewModelTests
{
    [AvaloniaFact]
    public async Task InitializeAsync_WithEmptyDatabase_DoesNotThrow()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        skillService.Setup(s => s.GetStarMasteryCurveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Circle_Tracker.Analytics.StarMasteryBracket>)new List<Circle_Tracker.Analytics.StarMasteryBracket>());
        skillService.Setup(s => s.GetAimSpeedProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AimSpeedProfile(0, 0m, 0, 0, 0m, 0, 0, 0m, 0, 0, 0));
        skillService.Setup(s => s.GetOdAccuracyCurveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OdAccuracyTier>)new List<OdAccuracyTier>());
        skillService.Setup(s => s.GetBpmSpeedCeilingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BpmBracketStats>)new List<BpmBracketStats>());

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        await viewModel.InitializeAsync();

        viewModel.IsLoading.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task TabSwitching_CancelsObsoleteBackgroundTasks()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        var taskStartedCount = 0;
        var taskCompletedCount = 0;

        skillService.Setup(s => s.GetStarMasteryCurveAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                taskStartedCount++;
                await Task.Delay(100, ct);
                taskCompletedCount++;
                return (IReadOnlyList<Circle_Tracker.Analytics.StarMasteryBracket>)new List<Circle_Tracker.Analytics.StarMasteryBracket>();
            });

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        viewModel.SelectedTabIndex = 0;
        await Task.Delay(10);
        viewModel.SelectedTabIndex = 1;
        await Task.Delay(200);

        taskStartedCount.Should().Be(1);
    }

    [AvaloniaFact]
    public async Task FilterBinding_UpdatesQueryAndRefreshesData()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        var queryCallCount = 0;
        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .Callback<PlayQueryFilter, CancellationToken>((filter, ct) =>
            {
                queryCallCount++;
                filter.SearchQuery.Should().NotBeNullOrEmpty();
            })
            .ReturnsAsync(new PagedResult<PlayRecord>(
                new List<PlayRecord>(),
                0,
                1,
                50,
                new PlayFilterSummary(0, 0m, 0m, 0, 0, 0, 0)
            ));

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);
        viewModel.SelectedTabIndex = 4;
        await Task.Delay(100);

        queryCallCount = 0;
        viewModel.SearchText = "Test Map";
        await Task.Delay(100);

        queryCallCount.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public async Task PaginationCommands_NavigateBetweenPages()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PlayRecord>(
                Enumerable.Range(1, 50).Select(i => new PlayRecord(
                    Id: i,
                    SessionId: null,
                    TimestampUtc: DateTime.UtcNow,
                    BeatmapId: i,
                    BeatmapSetId: i,
                    BeatmapChecksum: null,
                    BeatmapString: $"Map {i}",
                    BeatmapTitle: $"Title {i}",
                    BeatmapArtist: $"Artist {i}",
                    BeatmapVersion: "Normal",
                    ModsBitfield: 0,
                    ModsString: "NM",
                    Bpm: 180,
                    Stars: 5.0m,
                    Aim: 2.5m,
                    Speed: 2.5m,
                    Cs: 4.0m,
                    Ar: 9.0m,
                    Od: 8.0m,
                    Hp: 5.0m,
                    TotalHits: 300,
                    Hit300: 280,
                    Hit100: 15,
                    Hit50: 3,
                    HitMiss: 2,
                    Accuracy: 95.0m,
                    AccuracyReliable: true,
                    IsComplete: true,
                    PlayTimeSeconds: 120,
                    ConsecutivePlayCount: 1,
                    GameMode: 0,
                    IsReplay: false,
                    DetectedClient: "lazer"
                )).ToList(),
                150,
                1,
                50,
                new PlayFilterSummary(150, 95.0m, 5.0m, 18000, 45000, 140, 93.3)
            ));

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);
        viewModel.SelectedTabIndex = 4;
        await Task.Delay(100);

        viewModel.CurrentPage.Should().Be(1);
        viewModel.TotalPages.Should().Be(3);

        viewModel.NextPageCommand.Execute(null);
        await Task.Delay(100);

        viewModel.CurrentPage.Should().Be(2);

        viewModel.PreviousPageCommand.Execute(null);
        await Task.Delay(100);

        viewModel.CurrentPage.Should().Be(1);
    }

    [AvaloniaFact]
    public void PropertyChanged_FiresForObservableProperties()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        var propertyChangedFired = false;
        viewModel.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(viewModel.IsLoading))
                propertyChangedFired = true;
        };

        viewModel.SelectedTabIndex = 1;

        propertyChangedFired.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task LoadPlayHistory_PopulatesObservableCollection()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        var plays = Enumerable.Range(1, 25).Select(i => new PlayRecord(
            Id: i,
            SessionId: null,
            TimestampUtc: DateTime.UtcNow.AddMinutes(-i),
            BeatmapId: i,
            BeatmapSetId: i,
            BeatmapChecksum: null,
            BeatmapString: $"Test Map {i}",
            BeatmapTitle: $"Title {i}",
            BeatmapArtist: $"Artist {i}",
            BeatmapVersion: "Hard",
            ModsBitfield: 0,
            ModsString: "NM",
            Bpm: 180,
            Stars: 5.5m,
            Aim: 2.7m,
            Speed: 2.8m,
            Cs: 4.0m,
            Ar: 9.5m,
            Od: 8.5m,
            Hp: 5.5m,
            TotalHits: 462,
            Hit300: 400,
            Hit100: 50,
            Hit50: 10,
            HitMiss: 2,
            Accuracy: 96.0m + i * 0.1m,
            AccuracyReliable: true,
            IsComplete: true,
            PlayTimeSeconds: 135,
            ConsecutivePlayCount: 1,
            GameMode: 0,
            IsReplay: false,
            DetectedClient: "stable"
        )).ToList();

        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PlayRecord>(
                plays,
                25,
                1,
                50,
                new PlayFilterSummary(25, 96.5m, 5.5m, 3000, 11550, 25, 100.0)
            ));

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);
        viewModel.SelectedTabIndex = 4;
        
        await Task.Delay(500);

        viewModel.PlayHistory.Count.Should().Be(25);
        viewModel.TotalMatches.Should().Be(25);
        viewModel.SubsetAvgAccuracy.Should().BeApproximately(96.5, 0.1);
    }

    [AvaloniaFact]
    public async Task RapidTabSwitching_DoesNotCauseRaceConditions()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        skillService.Setup(s => s.GetStarMasteryCurveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Circle_Tracker.Analytics.StarMasteryBracket>)new List<Circle_Tracker.Analytics.StarMasteryBracket>());
        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>());
        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PlayRecord>(
                new List<PlayRecord>(),
                0,
                1,
                50,
                new PlayFilterSummary(0, 0m, 0m, 0, 0, 0, 0)
            ));

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        for (int i = 0; i < 5; i++)
        {
            viewModel.SelectedTabIndex = i % 5;
            await Task.Delay(5);
        }

        await Task.Delay(200);

        viewModel.IsLoading.Should().BeFalse();
    }
}
