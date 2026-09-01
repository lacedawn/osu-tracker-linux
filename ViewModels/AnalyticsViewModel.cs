using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Circle_Tracker.Analytics;
using Circle_Tracker.Storage.Querying;

namespace Circle_Tracker.ViewModels;

public class AnalyticsViewModel : INotifyPropertyChanged
{
    private readonly ISkillAnalyticsService _skillService;
    private readonly ISessionAnalyticsService _sessionService;
    private readonly IPlayQueryEngine _queryEngine;
    
    private int _selectedTabIndex = -1;

    private async Task InvokeOnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Background);
    }
    private bool _isLoading;
    private CancellationTokenSource? _loadCts;
    
    private ObservableCollection<StarMasteryBracket> _starMasteryBrackets = new();
    private AimSpeedBias? _aimSpeedBias;
    private ObservableCollection<OdPrecisionTier> _odPrecisionTiers = new();
    private ObservableCollection<BpmSpeedBracket> _bpmSpeedBrackets = new();
    
    private ObservableCollection<FatigueCurvePoint> _fatigueCurve = new();
    private string _optimalWindowText = "";
    
    private RollingPeriodMetrics? _rolling7Day;
    private RollingPeriodMetrics? _rolling30Day;
    private RollingPeriodMetrics? _rolling90Day;
    
    private ObservableCollection<ChokeCard> _topChokes = new();
    private ObservableCollection<GrindCard> _mostGrinded = new();
    
    private ObservableCollection<PlayRecord> _playHistory = new();
    private int _currentPage = 1;
    private int _pageSize = 50;
    private int _totalPages;
    private int _totalMatches;
    private double _subsetAvgAccuracy;
    private double _subsetAvgStars;
    private int _subsetTotalPlaytime;
    
    private string _searchText = "";
    private PlayQueryFilter _currentFilter;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AnalyticsViewModel(
        ISkillAnalyticsService skillService,
        ISessionAnalyticsService sessionService,
        IPlayQueryEngine queryEngine)
    {
        _skillService = skillService;
        _sessionService = sessionService;
        _queryEngine = queryEngine;
        _currentFilter = new PlayQueryFilter { Page = 1, PageSize = _pageSize };
        
        NextPageCommand = new RelayCommand(async () => await NextPageAsync(), () => CurrentPage < TotalPages);
        PreviousPageCommand = new RelayCommand(async () => await PreviousPageAsync(), () => CurrentPage > 1);
        ExportCsvCommand = new RelayCommand(async () => await ExportCsvAsync());
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex != value)
            {
                _selectedTabIndex = value;
                OnPropertyChanged();
                _ = LoadTabDataAsync(value);
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public ObservableCollection<StarMasteryBracket> StarMasteryBrackets => _starMasteryBrackets;
    public AimSpeedBias? AimSpeedBias => _aimSpeedBias;
    public ObservableCollection<OdPrecisionTier> OdPrecisionTiers => _odPrecisionTiers;
    public ObservableCollection<BpmSpeedBracket> BpmSpeedBrackets => _bpmSpeedBrackets;
    
    public ObservableCollection<FatigueCurvePoint> FatigueCurve => _fatigueCurve;
    public string OptimalWindowText { get => _optimalWindowText; set { _optimalWindowText = value; OnPropertyChanged(); } }
    
    public RollingPeriodMetrics? Rolling7Day { get => _rolling7Day; set { _rolling7Day = value; OnPropertyChanged(); } }
    public RollingPeriodMetrics? Rolling30Day { get => _rolling30Day; set { _rolling30Day = value; OnPropertyChanged(); } }
    public RollingPeriodMetrics? Rolling90Day { get => _rolling90Day; set { _rolling90Day = value; OnPropertyChanged(); } }
    
    public ObservableCollection<ChokeCard> TopChokes => _topChokes;
    public ObservableCollection<GrindCard> MostGrinded => _mostGrinded;
    
    public ObservableCollection<PlayRecord> PlayHistory => _playHistory;
    public int CurrentPage { get => _currentPage; set { _currentPage = value; OnPropertyChanged(); } }
    public int PageSize { get => _pageSize; set { _pageSize = value; OnPropertyChanged(); } }
    public int TotalPages { get => _totalPages; set { _totalPages = value; OnPropertyChanged(); } }
    public int TotalMatches { get => _totalMatches; set { _totalMatches = value; OnPropertyChanged(); } }
    public double SubsetAvgAccuracy { get => _subsetAvgAccuracy; set { _subsetAvgAccuracy = value; OnPropertyChanged(); } }
    public double SubsetAvgStars { get => _subsetAvgStars; set { _subsetAvgStars = value; OnPropertyChanged(); } }
    public int SubsetTotalPlaytime { get => _subsetTotalPlaytime; set { _subsetTotalPlaytime = value; OnPropertyChanged(); } }
    
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                _ = ApplyFiltersAsync();
            }
        }
    }

    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadTabDataAsync(0, ct);
    }

    private async Task LoadTabDataAsync(int tabIndex, CancellationToken ct = default)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _loadCts.Token);

        IsLoading = true;
        try
        {
            switch (tabIndex)
            {
                case 0:
                    await LoadSkillAnalyticsAsync(linkedCts.Token);
                    break;
                case 1:
                    await LoadSessionDynamicsAsync(linkedCts.Token);
                    break;
                case 2:
                    await LoadTrendsAsync(linkedCts.Token);
                    break;
                case 3:
                    await LoadChokesAsync(linkedCts.Token);
                    break;
                case 4:
                    await LoadPlayHistoryAsync(linkedCts.Token);
                    break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSkillAnalyticsAsync(CancellationToken ct)
    {
        var starMastery = await Task.Run(() => _skillService.GetStarMasteryCurveAsync(ct), ct);
        var aimSpeed = await Task.Run(() => _skillService.GetAimSpeedProfileAsync(ct), ct);
        var odPrecision = await Task.Run(() => _skillService.GetOdAccuracyCurveAsync(ct), ct);
        var bpmSpeed = await Task.Run(() => _skillService.GetBpmSpeedCeilingsAsync(ct), ct);

        Action updateUI = () =>
        {
            _starMasteryBrackets.Clear();
            foreach (var bracket in starMastery)
            {
                _starMasteryBrackets.Add(new StarMasteryBracket
                {
                    Label = $"{bracket.MinStars:F1}-{bracket.MaxStars:F1}★",
                    MinStar = bracket.MinStars,
                    MaxStar = bracket.MaxStars,
                    PassCount = bracket.Passes,
                    AttemptCount = bracket.TotalAttempts,
                    P90Accuracy = (double)bracket.P90Accuracy,
                    ProgressPercent = (double)bracket.P90Accuracy,
                    ZoneColor = bracket.P90Accuracy >= 95 ? "#4ade80" : (bracket.P90Accuracy >= 90 ? "#fb923c" : "#f87171"),
                    ZoneLabel = bracket.P90Accuracy >= 95 ? "Comfort" : (bracket.P90Accuracy >= 90 ? "Push" : "Pass-Only")
                });
            }

            _aimSpeedBias = new AimSpeedBias
            {
                AimPercentage = aimSpeed.AimBiasPercent,
                SpeedPercentage = aimSpeed.SpeedBiasPercent,
                AimAvgAccuracy = (double)aimSpeed.AimAvgAcc,
                SpeedAvgAccuracy = (double)aimSpeed.SpeedAvgAcc
            };
            OnPropertyChanged(nameof(AimSpeedBias));

            _odPrecisionTiers.Clear();
            foreach (var tier in odPrecision)
            {
                _odPrecisionTiers.Add(new OdPrecisionTier
                {
                    Label = $"OD {tier.MinOd:F1}-{tier.MaxOd:F1}",
                    MinOd = tier.MinOd,
                    MaxOd = tier.MaxOd,
                    AvgAccuracy = (double)tier.MeanAccuracy,
                    HitWindow300Ms = tier.HitWindow300Ms
                });
            }

            _bpmSpeedBrackets.Clear();
            foreach (var bracket in bpmSpeed)
            {
                _bpmSpeedBrackets.Add(new BpmSpeedBracket
                {
                    Label = $"{bracket.MinBpm}-{bracket.MaxBpm} BPM",
                    MinBpm = bracket.MinBpm,
                    MaxBpm = bracket.MaxBpm,
                    AvgAccuracy = (double)bracket.MeanAccuracy,
                    MissDensityPer100 = bracket.MissesPerHundredHits
                });
            }
        };

        if (Dispatcher.UIThread.CheckAccess())
            updateUI();
        else
            await Dispatcher.UIThread.InvokeAsync(updateUI, DispatcherPriority.Background);
    }

    private async Task LoadSessionDynamicsAsync(CancellationToken ct)
    {
        var fatigue = await Task.Run(() => _sessionService.GetSessionFatigueCurveAsync(ct), ct);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _fatigueCurve.Clear();
            foreach (var point in fatigue)
            {
                _fatigueCurve.Add(new FatigueCurvePoint
                {
                    TimeLabel = point.TimeRangeLabel,
                    AccuracyDelta = (double)point.AccDeltaFromSessionAvg
                });
            }

            var bestBucket = fatigue.OrderByDescending(p => p.MeanAccuracy).FirstOrDefault();
            OptimalWindowText = bestBucket != null 
                ? $"Peak Performance: {bestBucket.TimeRangeLabel}"
                : "Insufficient data";
        }, DispatcherPriority.Background);
    }

    private async Task LoadTrendsAsync(CancellationToken ct)
    {
        var allMetrics = await Task.Run(() => _sessionService.GetRollingAveragesAsync(ct), ct);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (allMetrics.TryGetValue("7day", out var metrics7))
            {
                Rolling7Day = new RollingPeriodMetrics
                {
                    Days = 7,
                    AvgStars = (double)metrics7.MeanStars,
                    WeightedAccuracy = (double)metrics7.MeanAccuracy,
                    DailyPlayCount = metrics7.TotalPlays / (double)metrics7.PeriodDays,
                    DailyActiveHours = metrics7.TotalActiveHours / metrics7.PeriodDays,
                    PassRatePercent = metrics7.PassRatePercent,
                    AvgBpm = metrics7.MeanBpm
                };
            }

            if (allMetrics.TryGetValue("30day", out var metrics30))
            {
                Rolling30Day = new RollingPeriodMetrics
                {
                    Days = 30,
                    AvgStars = (double)metrics30.MeanStars,
                    WeightedAccuracy = (double)metrics30.MeanAccuracy,
                    DailyPlayCount = metrics30.TotalPlays / (double)metrics30.PeriodDays,
                    DailyActiveHours = metrics30.TotalActiveHours / metrics30.PeriodDays,
                    PassRatePercent = metrics30.PassRatePercent,
                    AvgBpm = metrics30.MeanBpm
                };
            }

            if (allMetrics.TryGetValue("90day", out var metrics90))
            {
                Rolling90Day = new RollingPeriodMetrics
                {
                    Days = 90,
                    AvgStars = (double)metrics90.MeanStars,
                    WeightedAccuracy = (double)metrics90.MeanAccuracy,
                    DailyPlayCount = metrics90.TotalPlays / (double)metrics90.PeriodDays,
                    DailyActiveHours = metrics90.TotalActiveHours / metrics90.PeriodDays,
                    PassRatePercent = metrics90.PassRatePercent,
                    AvgBpm = metrics90.MeanBpm
                };
            }
        }, DispatcherPriority.Background);
    }

    private async Task LoadChokesAsync(CancellationToken ct)
    {
        var chokes = await Task.Run(() => _sessionService.GetTopChokeMapsAsync(10, ct), ct);

        var grinded = await Task.Run(async () =>
        {
            var filter = new PlayQueryFilter { PageSize = 5000 };
            var result = await _queryEngine.QueryPlaysAsync(filter, ct);
            return result.Items.GroupBy(p => p.BeatmapId)
                .Select(g => new GrindCard
                {
                    BeatmapString = g.First().BeatmapString,
                    BeatmapSetId = g.First().BeatmapSetId,
                    TotalAttempts = g.Count(),
                    CumulativeHours = g.Sum(p => p.PlayTimeSeconds) / 3600.0
                })
                .OrderByDescending(c => c.TotalAttempts)
                .Take(10)
                .ToList();
        }, ct);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _topChokes.Clear();
            foreach (var choke in chokes)
            {
                _topChokes.Add(new ChokeCard
                {
                    BeatmapString = choke.BeatmapString,
                    BeatmapSetId = choke.BeatmapSetId,
                    BeatmapId = choke.BeatmapId,
                    MinMisses = choke.MinMisses,
                    BestAccuracy = (double)choke.BestChokeAcc,
                    ModsString = "NM"
                });
            }

            _mostGrinded.Clear();
            foreach (var grind in grinded)
                _mostGrinded.Add(grind);
        }, DispatcherPriority.Background);
    }

    private async Task LoadPlayHistoryAsync(CancellationToken ct)
    {
        _currentFilter = _currentFilter with { Page = CurrentPage, PageSize = PageSize };
        if (!string.IsNullOrWhiteSpace(SearchText))
            _currentFilter = _currentFilter with { SearchQuery = SearchText };

        var result = await Task.Run(() => _queryEngine.QueryPlaysAsync(_currentFilter, ct), ct);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _playHistory.Clear();
            foreach (var play in result.Items)
                _playHistory.Add(play);

            TotalMatches = result.TotalCount;
            TotalPages = result.TotalPages;
            SubsetAvgAccuracy = (double)result.Summary.AverageAccuracy;
            SubsetAvgStars = (double)result.Summary.AverageStars;
            SubsetTotalPlaytime = result.Summary.TotalPlayTimeSeconds;
        }, DispatcherPriority.Background);
    }

    private async Task NextPageAsync()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            await LoadPlayHistoryAsync(default);
        }
    }

    private async Task PreviousPageAsync()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            await LoadPlayHistoryAsync(default);
        }
    }

    private async Task ApplyFiltersAsync()
    {
        CurrentPage = 1;
        await LoadPlayHistoryAsync(default);
    }

    private async Task ExportCsvAsync()
    {
        await Task.CompletedTask;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class StarMasteryBracket
{
    public string Label { get; set; } = "";
    public double MinStar { get; set; }
    public double MaxStar { get; set; }
    public int PassCount { get; set; }
    public int AttemptCount { get; set; }
    public double P90Accuracy { get; set; }
    public double ProgressPercent { get; set; }
    public string ZoneColor { get; set; } = "";
    public string ZoneLabel { get; set; } = "";
}

public class AimSpeedBias
{
    public double AimPercentage { get; set; }
    public double SpeedPercentage { get; set; }
    public double AimAvgAccuracy { get; set; }
    public double SpeedAvgAccuracy { get; set; }
}

public class OdPrecisionTier
{
    public string Label { get; set; } = "";
    public double MinOd { get; set; }
    public double MaxOd { get; set; }
    public double AvgAccuracy { get; set; }
    public double HitWindow300Ms { get; set; }
}

public class BpmSpeedBracket
{
    public string Label { get; set; } = "";
    public int MinBpm { get; set; }
    public int MaxBpm { get; set; }
    public double AvgAccuracy { get; set; }
    public double MissDensityPer100 { get; set; }
}

public class FatigueCurvePoint
{
    public string TimeLabel { get; set; } = "";
    public double AccuracyDelta { get; set; }
}

public class RollingPeriodMetrics
{
    public int Days { get; set; }
    public double AvgStars { get; set; }
    public double WeightedAccuracy { get; set; }
    public double DailyPlayCount { get; set; }
    public double DailyActiveHours { get; set; }
    public double PassRatePercent { get; set; }
    public double AvgBpm { get; set; }
}

public class ChokeCard
{
    public string BeatmapString { get; set; } = "";
    public int BeatmapSetId { get; set; }
    public int BeatmapId { get; set; }
    public int MinMisses { get; set; }
    public double BestAccuracy { get; set; }
    public string ModsString { get; set; } = "";
    public string CoverUrl => BeatmapSetId > 0 ? $"https://assets.ppy.sh/beatmaps/{BeatmapSetId}/covers/cover.jpg" : "";
}

public class GrindCard
{
    public string BeatmapString { get; set; } = "";
    public int BeatmapSetId { get; set; }
    public int TotalAttempts { get; set; }
    public double CumulativeHours { get; set; }
    public string CoverUrl => BeatmapSetId > 0 ? $"https://assets.ppy.sh/beatmaps/{BeatmapSetId}/covers/cover.jpg" : "";
}

public class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public async void Execute(object? parameter) => await _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
