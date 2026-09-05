using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Circle_Tracker;
using Circle_Tracker.Analytics;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Circle_Tracker.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
        await Task.Delay(350);

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

    [Fact]
    public void PassFailConverters_ReturnCorrectValues()
    {
        var textConv = new Circle_Tracker.Converters.PassFailTextConverter();
        textConv.Convert(true, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture).Should().Be("PASS");
        textConv.Convert(false, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture).Should().Be("RETRY");
        textConv.Convert(null, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture).Should().Be("RETRY");

        var bgConv = new Circle_Tracker.Converters.PassFailBackgroundConverter();
        bgConv.Convert(true, typeof(Avalonia.Media.IBrush), null, System.Globalization.CultureInfo.InvariantCulture).Should().NotBeNull();
        bgConv.Convert(false, typeof(Avalonia.Media.IBrush), null, System.Globalization.CultureInfo.InvariantCulture).Should().NotBeNull();

        var fgConv = new Circle_Tracker.Converters.PassFailForegroundConverter();
        fgConv.Convert(true, typeof(Avalonia.Media.IBrush), null, System.Globalization.CultureInfo.InvariantCulture).Should().NotBeNull();
        fgConv.Convert(false, typeof(Avalonia.Media.IBrush), null, System.Globalization.CultureInfo.InvariantCulture).Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task SearchFilterReset_ClearingSearchText_ClearsQuery()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        PlayQueryFilter? lastFilter = null;
        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .Callback<PlayQueryFilter, CancellationToken>((f, _) => lastFilter = f)
            .ReturnsAsync(new PagedResult<PlayRecord>(new List<PlayRecord>(), 0, 1, 50, new PlayFilterSummary(0, 0, 0, 0, 0, 0, 0)));

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);
        viewModel.SelectedTabIndex = 4;
        await Task.Delay(50);

        viewModel.SearchText = "freedom dive";
        await Task.Delay(350);
        lastFilter.Should().NotBeNull();
        lastFilter!.SearchQuery.Should().Be("freedom dive");

        viewModel.SearchText = "";
        await Task.Delay(350);
        lastFilter.Should().NotBeNull();
        lastFilter!.SearchQuery.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task ModFilters_MutexAndBitfieldBehavior()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        PlayQueryFilter? lastFilter = null;
        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .Callback<PlayQueryFilter, CancellationToken>((f, _) => lastFilter = f)
            .ReturnsAsync(new PagedResult<PlayRecord>(new List<PlayRecord>(), 0, 1, 50, new PlayFilterSummary(0, 0, 0, 0, 0, 0, 0)));

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);
        viewModel.SelectedTabIndex = 4;
        await Task.Delay(50);

        // Turn on NM
        viewModel.IsFilterNm = true;
        await Task.Delay(50);
        lastFilter!.ModMode.Should().Be(ModFilterMode.NoModOnly);

        // Turn on HD and DT -> NM should automatically be unchecked
        viewModel.IsFilterHd = true;
        viewModel.IsFilterNm.Should().BeFalse();
        viewModel.IsFilterDt = true;
        await Task.Delay(50);

        lastFilter!.ModMode.Should().Be(ModFilterMode.ContainsAll);
        // HD (1 << 3 = 8) | DT (1 << 6 = 64) = 72
        lastFilter.RequiredModsBitfield.Should().Be((1 << 3) | (1 << 6));

        // Turn NM back on -> HD and DT should be unchecked
        viewModel.IsFilterNm = true;
        viewModel.IsFilterHd.Should().BeFalse();
        viewModel.IsFilterDt.Should().BeFalse();
        await Task.Delay(50);
        lastFilter!.ModMode.Should().Be(ModFilterMode.NoModOnly);
    }

    [AvaloniaFact]
    public async Task PaginationCommands_CanExecute_UpdatesOnPageCountChanges()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PlayRecord>(
                new List<PlayRecord>(),
                150,
                1,
                50,
                new PlayFilterSummary(150, 95.0m, 5.0m, 18000, 45000, 140, 93.3)
            ));

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        // Before loading plays, TotalPages is 0, CurrentPage is 1
        viewModel.NextPageCommand.CanExecute(null).Should().BeFalse();
        viewModel.PreviousPageCommand.CanExecute(null).Should().BeFalse();

        // Switch to tab 4 to load play history
        viewModel.SelectedTabIndex = 4;
        await Task.Delay(100);

        // Now TotalPages is 3, CurrentPage is 1
        viewModel.NextPageCommand.CanExecute(null).Should().BeTrue();
        viewModel.PreviousPageCommand.CanExecute(null).Should().BeFalse();

        viewModel.CurrentPage = 2;
        viewModel.NextPageCommand.CanExecute(null).Should().BeTrue();
        viewModel.PreviousPageCommand.CanExecute(null).Should().BeTrue();

        viewModel.CurrentPage = 3;
        viewModel.NextPageCommand.CanExecute(null).Should().BeFalse();
        viewModel.PreviousPageCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task ExportCsvCommand_CallsDataExportService()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();
        var exportService = new Mock<Circle_Tracker.Sync.IDataExportService>();

        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PlayRecord>(new List<PlayRecord>(), 0, 1, 50, new PlayFilterSummary(0, 0, 0, 0, 0, 0, 0)));

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object, exportService.Object);
        viewModel.RequestSaveFilePathAsync = () => Task.FromResult<string?>("/tmp/test_export.csv");

        viewModel.ExportCsvCommand.Execute(null);
        await Task.Delay(100);

        exportService.Verify(e => e.ExportPlaysAsync(
            "/tmp/test_export.csv",
            Circle_Tracker.Sync.ExportFormat.Csv,
            It.IsAny<PlayQueryFilter>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [AvaloniaFact]
    public async Task SessionDynamics_PopulatesSessionPlaysAndPace()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        var samplePlay = new PlayRecord(1, "test_session", DateTime.UtcNow, 100, 200, "hash", "Title [Hard]", "Title", "Artist", "Hard", 0, "", 180, 5.0m, 2.5m, 2.5m, 4m, 9m, 8m, 6m, 100, 95, 5, 0, 0, 98.5m, true, true, 120, 1, 0, false, "osu!stable");

        queryEngine.Setup(q => q.QueryPlaysAsync(It.Is<PlayQueryFilter>(f => f.PageSize == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PlayRecord>(new List<PlayRecord> { samplePlay }, 1, 1, 1, new PlayFilterSummary(1, 98.5m, 5.0m, 120, 100, 1, 100.0)));

        sessionService.Setup(s => s.CompareSessionToBaselineAsync("test_session", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HeadToHeadComparison(98.0m, 96.0m, 2.0m, 5.5m, 5.0m, 0.5m, 90.0, 80.0, 10.0, 10, 30.0));

        queryEngine.Setup(q => q.QueryPlaysAsync(It.Is<PlayQueryFilter>(f => f.SessionId == "test_session"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PlayRecord>(new List<PlayRecord> { samplePlay }, 1, 1, 500, new PlayFilterSummary(1, 98.5m, 5.0m, 120, 100, 1, 100.0)));

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        viewModel.SelectedTabIndex = 1;
        await Task.Delay(200);

        viewModel.SessionBaseline.Should().NotBeNull();
        viewModel.SessionBaseline!.SessionPlays.Should().Be(10);
        viewModel.SessionPlays.Should().HaveCount(1);
        viewModel.SessionPlaysPerHour.Should().Be(20.0);
    }

    [AvaloniaFact]
    public async Task Trends_PopulatesDailyTrendsLog()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        var dailyItems = new List<DailyTrendItem>
        {
            new("2026-09-04", 10, 8, 80.0, 98.5m, 5.2m, 25.0),
            new("2026-09-03", 5, 4, 80.0, 97.0m, 5.0m, 15.0)
        };

        sessionService.Setup(s => s.GetDailyActivityLogAsync(14, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dailyItems);

        sessionService.Setup(s => s.GetRollingAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RollingPeriodStats>
            {
                ["7D"] = new(7, 10, 0.5, 98m, 5m, 80, 180, 5, 0.5, true, "Sep 01 – Sep 04", 10),
                ["30D"] = new(30, 20, 1.0, 97m, 5m, 75, 180, 5, 0.5, true, "Aug 24 – Sep 04", 11),
                ["90D"] = new(90, 20, 1.0, 97m, 5m, 75, 180, 5, 0.5, false, "Aug 24 – Sep 04", 11)
            });

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        viewModel.SelectedTabIndex = 2;
        await Task.Delay(200);

        viewModel.DailyTrends.Should().HaveCount(2);
        viewModel.DailyTrends[0].DateString.Should().Be("2026-09-04");
        viewModel.DailyTrends[0].Passes.Should().Be(8);
        viewModel.DailyTrends[0].PassRatePercent.Should().Be(80.0);
    }

    [AvaloniaFact]
    public async Task SelectedTabIndex_ChokesTab_LoadsMostGrindedBeatmaps()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        sessionService.Setup(s => s.GetTopChokeMapsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChokeMapRecord>
            {
                new(101, 1001, "Freedom Dive", "", 1, 98.0m, 1, 10)
            });

        var grindedList = new List<GrindedBeatmapSummary>
        {
            new(101, 1001, "Freedom Dive", 700, 5.5)
        };
        queryEngine.Setup(q => q.GetMostGrindedBeatmapsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grindedList);

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        viewModel.SelectedTabIndex = 3;
        await Task.Delay(200);

        viewModel.MostGrinded.Should().HaveCount(1);
        viewModel.MostGrinded[0].BeatmapString.Should().Be("Freedom Dive");
        viewModel.MostGrinded[0].BeatmapSetId.Should().Be(1001);
        viewModel.MostGrinded[0].TotalAttempts.Should().Be(700);
        viewModel.MostGrinded[0].CumulativeHours.Should().Be(5.5);
    }

    [AvaloniaFact]
    public async Task SearchText_RapidKeystrokes_DebouncesAndDispatchesOnlyFinalQuery()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        PlayQueryFilter? capturedFilter = null;
        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .Returns<PlayQueryFilter, CancellationToken>(async (filter, ct) =>
            {
                capturedFilter = filter;
                await Task.Delay(10, ct);
                return new PagedResult<PlayRecord>(
                    new List<PlayRecord>(),
                    0,
                    1,
                    50,
                    new PlayFilterSummary(0, 0m, 0m, 0, 0, 0, 0)
                );
            });

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        viewModel.SearchText = "f";
        await Task.Delay(50);
        viewModel.SearchText = "fr";
        await Task.Delay(50);
        viewModel.SearchText = "free";
        await Task.Delay(50);
        viewModel.SearchText = "freedom";

        await Task.Delay(450);

        queryEngine.Verify(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()), Times.Once());
        capturedFilter.Should().NotBeNull();
        capturedFilter!.SearchText.Should().Be("freedom");
    }

    [AvaloniaFact]
    public async Task SearchText_WhenViewModelDisposedOrCancelled_AbortsInFlightSearch()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        var queryExecuted = false;
        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .Returns<PlayQueryFilter, CancellationToken>(async (filter, ct) =>
            {
                await Task.Delay(50, ct);
                queryExecuted = true;
                return new PagedResult<PlayRecord>(
                    new List<PlayRecord>(),
                    0,
                    1,
                    50,
                    new PlayFilterSummary(0, 0m, 0m, 0, 0, 0, 0)
                );
            });

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        viewModel.SearchText = "test";
        await Task.Delay(20);
        viewModel.Dispose();
        await Task.Delay(350);

        queryExecuted.Should().BeFalse();
        queryEngine.Verify(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [AvaloniaFact]
    public async Task SearchText_WhenUpdatedWithinDebounceWindow_CancelsPreviousSearch()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        var queriedFilters = new List<PlayQueryFilter>();
        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .Returns<PlayQueryFilter, CancellationToken>(async (filter, ct) =>
            {
                queriedFilters.Add(filter);
                await Task.Delay(10, ct);
                return new PagedResult<PlayRecord>(
                    new List<PlayRecord>(),
                    0,
                    1,
                    50,
                    new PlayFilterSummary(0, 0m, 0m, 0, 0, 0, 0)
                );
            });

        var viewModel = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);

        viewModel.SearchText = "initial";
        await Task.Delay(50);
        viewModel.SearchText = "updated";
        await Task.Delay(450);

        queriedFilters.Should().ContainSingle();
        queriedFilters[0].SearchText.Should().Be("updated");
    }

    [Fact]
    public void Should_ReturnDistinctInstances_When_ResolvedFromServiceProviderMultipleTimes()
    {
        var services = new ServiceCollection();
        
        var mockSkillService = new Mock<ISkillAnalyticsService>();
        var mockSessionService = new Mock<ISessionAnalyticsService>();
        var mockQueryEngine = new Mock<IPlayQueryEngine>();
        
        services.AddTransient<AnalyticsViewModel>(sp => 
            new AnalyticsViewModel(mockSkillService.Object, mockSessionService.Object, mockQueryEngine.Object));
        
        var provider = services.BuildServiceProvider();
        
        var vm1 = provider.GetService<AnalyticsViewModel>();
        var vm2 = provider.GetService<AnalyticsViewModel>();
        
        vm1.Should().NotBeNull();
        vm2.Should().NotBeNull();
        vm1.Should().NotBeSameAs(vm2);
    }

    [AvaloniaFact]
    public async Task Should_FunctionNormally_When_NewInstanceCreatedAfterPreviousInstanceDisposed()
    {
        var skillService = new Mock<ISkillAnalyticsService>();
        var sessionService = new Mock<ISessionAnalyticsService>();
        var queryEngine = new Mock<IPlayQueryEngine>();

        skillService.Setup(s => s.GetStarMasteryCurveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Circle_Tracker.Analytics.StarMasteryBracket>)new List<Circle_Tracker.Analytics.StarMasteryBracket>());
        skillService.Setup(s => s.GetOdAccuracyCurveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OdAccuracyTier>)new List<OdAccuracyTier>());
        skillService.Setup(s => s.GetBpmSpeedCeilingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BpmBracketStats>)new List<BpmBracketStats>());
        queryEngine.Setup(q => q.QueryPlaysAsync(It.IsAny<PlayQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PlayRecord>(
                new List<PlayRecord>(),
                0,
                1,
                50,
                new PlayFilterSummary(0, 0m, 0m, 0, 0, 0, 0)
            ));

        var vm1 = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);
        await vm1.InitializeAsync();
        vm1.IsLoading.Should().BeFalse();
        
        vm1.Dispose();

        var vm2 = new AnalyticsViewModel(skillService.Object, sessionService.Object, queryEngine.Object);
        
        var initAction = async () => await vm2.InitializeAsync();
        await initAction.Should().NotThrowAsync();
        
        vm2.IsLoading.Should().BeFalse();
        
        vm2.SearchText = "test query";
        await Task.Delay(350);
        
        queryEngine.Verify(q => q.QueryPlaysAsync(
            It.Is<PlayQueryFilter>(f => f.SearchText == "test query"), 
            It.IsAny<CancellationToken>()), Times.Once());
        
        vm2.Dispose();
    }
}
