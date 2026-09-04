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
using Circle_Tracker.Sync;
using Microsoft.Extensions.Logging;

namespace Circle_Tracker.ViewModels;

public class AnalyticsViewModel : INotifyPropertyChanged
{
    private static readonly ILogger<AnalyticsViewModel> _log = AppLogger.For<AnalyticsViewModel>();

    private readonly ISkillAnalyticsService _skillService;
    private readonly ISessionAnalyticsService _sessionService;
    private readonly IPlayQueryEngine _queryEngine;
    private readonly IDataExportService? _exportService;

    public Func<Task<string?>>? RequestSaveFilePathAsync { get; set; }
    
    private int _selectedTabIndex = -1;
    private HeadToHeadComparison? _sessionBaseline;

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
    private ObservableCollection<OdPrecisionTier> _odPrecisionTiers = new();
    private ObservableCollection<BpmSpeedBracket> _bpmSpeedBrackets = new();
    
    private RollingPeriodMetrics? _rolling7Day;
    private RollingPeriodMetrics? _rolling30Day;
    private RollingPeriodMetrics? _rolling90Day;
    
    private ObservableCollection<PlayRecord> _sessionPlays = new();
    public ObservableCollection<PlayRecord> SessionPlays => _sessionPlays;
    private double _sessionPlaysPerHour;
    public double SessionPlaysPerHour
    {
        get => _sessionPlaysPerHour;
        set { _sessionPlaysPerHour = value; OnPropertyChanged(); }
    }

    private ObservableCollection<DailyTrendItem> _dailyTrends = new();
    public ObservableCollection<DailyTrendItem> DailyTrends => _dailyTrends;

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
        IPlayQueryEngine queryEngine,
        IDataExportService? exportService = null)
    {
        _skillService = skillService;
        _sessionService = sessionService;
        _queryEngine = queryEngine;
        _exportService = exportService ?? ((queryEngine as SqlitePlayQueryEngine)?.DbManager is { } db
            ? new DataExportService(db, queryEngine)
            : null);
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
    public ObservableCollection<OdPrecisionTier> OdPrecisionTiers => _odPrecisionTiers;
    public ObservableCollection<BpmSpeedBracket> BpmSpeedBrackets => _bpmSpeedBrackets;

    public HeadToHeadComparison? SessionBaseline
    {
        get => _sessionBaseline;
        set { _sessionBaseline = value; OnPropertyChanged(); }
    }
    
    public RollingPeriodMetrics? Rolling7Day { get => _rolling7Day; set { _rolling7Day = value; OnPropertyChanged(); } }
    public RollingPeriodMetrics? Rolling30Day { get => _rolling30Day; set { _rolling30Day = value; OnPropertyChanged(); } }
    public RollingPeriodMetrics? Rolling90Day { get => _rolling90Day; set { _rolling90Day = value; OnPropertyChanged(); } }
    
    public ObservableCollection<ChokeCard> TopChokes => _topChokes;
    public ObservableCollection<GrindCard> MostGrinded => _mostGrinded;
    
    public ObservableCollection<PlayRecord> PlayHistory => _playHistory;
    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage != value)
            {
                _currentPage = value;
                OnPropertyChanged();
                UpdatePaginationCanExecute();
            }
        }
    }
    public int PageSize { get => _pageSize; set { _pageSize = value; OnPropertyChanged(); } }
    public int TotalPages
    {
        get => _totalPages;
        set
        {
            if (_totalPages != value)
            {
                _totalPages = value;
                OnPropertyChanged();
                UpdatePaginationCanExecute();
            }
        }
    }
    public int TotalMatches { get => _totalMatches; set { _totalMatches = value; OnPropertyChanged(); } }
    public double SubsetAvgAccuracy { get => _subsetAvgAccuracy; set { _subsetAvgAccuracy = value; OnPropertyChanged(); } }
    public double SubsetAvgStars { get => _subsetAvgStars; set { _subsetAvgStars = value; OnPropertyChanged(); } }
    public int SubsetTotalPlaytime { get => _subsetTotalPlaytime; set { _subsetTotalPlaytime = value; OnPropertyChanged(); } }

    private void UpdatePaginationCanExecute()
    {
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool _filterNm;
    private bool _filterHd;
    private bool _filterHr;
    private bool _filterDt;
    private bool _filterFl;
    private bool _filterEz;
    private bool _filterHt;

    public bool IsFilterNm
    {
        get => _filterNm;
        set
        {
            if (_filterNm != value)
            {
                _filterNm = value;
                if (value)
                {
                    _filterHd = _filterHr = _filterDt = _filterFl = _filterEz = _filterHt = false;
                    RaiseModPropertiesChanged();
                }
                OnPropertyChanged();
                _ = ApplyModFiltersAsync();
            }
        }
    }

    public bool IsFilterHd { get => _filterHd; set => SetMod(ref _filterHd, value); }
    public bool IsFilterHr { get => _filterHr; set => SetMod(ref _filterHr, value); }
    public bool IsFilterDt { get => _filterDt; set => SetMod(ref _filterDt, value); }
    public bool IsFilterFl { get => _filterFl; set => SetMod(ref _filterFl, value); }
    public bool IsFilterEz { get => _filterEz; set => SetMod(ref _filterEz, value); }
    public bool IsFilterHt { get => _filterHt; set => SetMod(ref _filterHt, value); }

    private void SetMod(ref bool field, bool value)
    {
        if (field != value)
        {
            field = value;
            if (value) _filterNm = false;
            OnPropertyChanged(nameof(IsFilterNm));
            OnPropertyChanged();
            _ = ApplyModFiltersAsync();
        }
    }

    private void RaiseModPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsFilterHd));
        OnPropertyChanged(nameof(IsFilterHr));
        OnPropertyChanged(nameof(IsFilterDt));
        OnPropertyChanged(nameof(IsFilterFl));
        OnPropertyChanged(nameof(IsFilterEz));
        OnPropertyChanged(nameof(IsFilterHt));
    }

    private async Task ApplyModFiltersAsync()
    {
        int bitfield = 0;
        if (_filterHd) bitfield |= (1 << 3);
        if (_filterHr) bitfield |= (1 << 4);
        if (_filterDt) bitfield |= (1 << 6);
        if (_filterEz) bitfield |= (1 << 1);
        if (_filterHt) bitfield |= (1 << 8);
        if (_filterFl) bitfield |= (1 << 10);

        ModFilterMode mode = ModFilterMode.Any;
        int? req = null;
        if (_filterNm)
        {
            mode = ModFilterMode.NoModOnly;
        }
        else if (bitfield > 0)
        {
            mode = ModFilterMode.ContainsAll;
            req = bitfield;
        }

        _currentFilter = _currentFilter with
        {
            ModMode = mode,
            RequiredModsBitfield = req
        };
        CurrentPage = 1;
        await LoadPlayHistoryAsync(default);
    }
    
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
        var odPrecision = await Task.Run(() => _skillService.GetOdAccuracyCurveAsync(ct), ct);
        var bpmSpeed = await Task.Run(() => _skillService.GetBpmSpeedCeilingsAsync(ct), ct);

        Action updateUI = () =>
        {
            _starMasteryBrackets.Clear();
            foreach (var bracket in starMastery)
            {
                string label = bracket.MaxStars >= 99.0 
                    ? $"{bracket.MinStars:F1}+★" 
                    : $"{bracket.MinStars:F1}–{bracket.MinStars + 0.4:F1}★";
                string subtext = bracket.TotalAttempts > 0 
                    ? $"{bracket.Passes}/{bracket.TotalAttempts} passed ({bracket.PassRatePercent:F0}%)" 
                    : "No plays";
                string zoneColor = bracket.SkillZone switch
                {
                    "Comfort" => "#4ade80",
                    "Push" => "#fb923c",
                    "Pass-Only" => "#f87171",
                    _ => "#94a3b8"
                };
                _starMasteryBrackets.Add(new StarMasteryBracket
                {
                    Label = label,
                    MinStar = bracket.MinStars,
                    MaxStar = bracket.MaxStars,
                    PassCount = bracket.Passes,
                    AttemptCount = bracket.TotalAttempts,
                    MeanAccuracy = (double)bracket.MeanAccuracy,
                    PassRatePercent = bracket.PassRatePercent,
                    ProgressPercent = bracket.Passes > 0 ? (double)bracket.MeanAccuracy : 0.0,
                    ZoneColor = zoneColor,
                    ZoneLabel = bracket.SkillZone,
                    Subtext = subtext
                });
            }

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
                string label = bracket.MaxBpm >= int.MaxValue / 2
                    ? $"{bracket.MinBpm}+ BPM"
                    : $"{bracket.MinBpm}-{bracket.MaxBpm} BPM";
                    
                _bpmSpeedBrackets.Add(new BpmSpeedBracket
                {
                    Label = label,
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
        try
        {
            var recent = await _queryEngine.QueryPlaysAsync(new PlayQueryFilter { PageSize = 1 }, ct);
            string? latestSession = recent?.Items?.FirstOrDefault()?.SessionId;
            if (!string.IsNullOrEmpty(latestSession))
            {
                var comparison = await Task.Run(() => _sessionService.CompareSessionToBaselineAsync(latestSession, ct), ct);
                var sessionPlaysResult = await Task.Run(() => _queryEngine.QueryPlaysAsync(new PlayQueryFilter 
                { 
                    SessionId = latestSession, 
                    PageSize = 500 
                }, ct), ct);

                double activeHours = comparison.SessionActiveMinutes / 60.0;
                double playsPerHour = activeHours > 0.05 ? Math.Round(comparison.SessionPlays / activeHours, 1) : 0.0;

                await InvokeOnUIThread(() =>
                {
                    SessionBaseline = comparison;
                    SessionPlaysPerHour = playsPerHour;
                    _sessionPlays.Clear();
                    if (sessionPlaysResult?.Items != null)
                    {
                        foreach (var play in sessionPlaysResult.Items)
                        {
                            _sessionPlays.Add(play);
                        }
                    }
                });
            }
            else
            {
                await InvokeOnUIThread(() =>
                {
                    SessionBaseline = null;
                    SessionPlaysPerHour = 0.0;
                    _sessionPlays.Clear();
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load session dynamics");
        }
    }

    private async Task LoadTrendsAsync(CancellationToken ct)
    {
        var allMetrics = await Task.Run(() => _sessionService.GetRollingAveragesAsync(ct), ct);
        var dailyItems = await Task.Run(() => _sessionService.GetDailyActivityLogAsync(14, ct), ct);

        await InvokeOnUIThread(() =>
        {
            if (allMetrics.TryGetValue("7D", out var metrics7))
            {
                Rolling7Day = new RollingPeriodMetrics
                {
                    Days = 7,
                    AvgStars = (double)metrics7.MeanStars,
                    WeightedAccuracy = (double)metrics7.MeanAccuracy,
                    DailyPlayCount = metrics7.PlaysPerActiveDay,
                    DailyActiveHours = metrics7.HoursPerActiveDay,
                    PassRatePercent = metrics7.PassRatePercent,
                    AvgBpm = metrics7.MeanBpm,
                    HasSufficientData = metrics7.HasSufficientData,
                    DateRangeText = metrics7.DateRangeText,
                    HistoryDaysAvailable = metrics7.HistoryDaysAvailable
                };
            }

            if (allMetrics.TryGetValue("30D", out var metrics30))
            {
                Rolling30Day = new RollingPeriodMetrics
                {
                    Days = 30,
                    AvgStars = (double)metrics30.MeanStars,
                    WeightedAccuracy = (double)metrics30.MeanAccuracy,
                    DailyPlayCount = metrics30.PlaysPerActiveDay,
                    DailyActiveHours = metrics30.HoursPerActiveDay,
                    PassRatePercent = metrics30.PassRatePercent,
                    AvgBpm = metrics30.MeanBpm,
                    HasSufficientData = metrics30.HasSufficientData,
                    DateRangeText = metrics30.DateRangeText,
                    HistoryDaysAvailable = metrics30.HistoryDaysAvailable
                };
            }

            if (allMetrics.TryGetValue("90D", out var metrics90))
            {
                Rolling90Day = new RollingPeriodMetrics
                {
                    Days = 90,
                    AvgStars = (double)metrics90.MeanStars,
                    WeightedAccuracy = (double)metrics90.MeanAccuracy,
                    DailyPlayCount = metrics90.PlaysPerActiveDay,
                    DailyActiveHours = metrics90.HoursPerActiveDay,
                    PassRatePercent = metrics90.PassRatePercent,
                    AvgBpm = metrics90.MeanBpm,
                    HasSufficientData = metrics90.HasSufficientData,
                    DateRangeText = metrics90.DateRangeText,
                    HistoryDaysAvailable = metrics90.HistoryDaysAvailable
                };
            }

            _dailyTrends.Clear();
            if (dailyItems != null)
            {
                foreach (var item in dailyItems)
                {
                    _dailyTrends.Add(item);
                }
            }
        });
    }

    private async Task LoadChokesAsync(CancellationToken ct)
    {
        var chokes = await Task.Run(() => _sessionService.GetTopChokeMapsAsync(10, ct), ct);
        var grinded = await _queryEngine.GetMostGrindedBeatmapsAsync(10, ct);

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
            {
                _mostGrinded.Add(new GrindCard
                {
                    BeatmapString = grind.BeatmapString,
                    BeatmapSetId = grind.BeatmapSetId,
                    TotalAttempts = grind.TotalAttempts,
                    CumulativeHours = grind.CumulativeHours
                });
            }
        }, DispatcherPriority.Background);
    }

    private async Task LoadPlayHistoryAsync(CancellationToken ct)
    {
        string? query = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        _currentFilter = _currentFilter with
        {
            Page = CurrentPage,
            PageSize = PageSize,
            SearchQuery = query
        };

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
            UpdatePaginationCanExecute();
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
        try
        {
            if (RequestSaveFilePathAsync == null || _exportService == null) return;
            string? destination = await RequestSaveFilePathAsync();
            if (string.IsNullOrWhiteSpace(destination)) return;
            IsLoading = true;
            await _exportService.ExportPlaysAsync(destination, ExportFormat.Csv, _currentFilter);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to export plays to CSV");
        }
        finally
        {
            IsLoading = false;
        }
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
    public double MeanAccuracy { get; set; }
    public double PassRatePercent { get; set; }
    public double ProgressPercent { get; set; }
    public string ZoneColor { get; set; } = "";
    public string ZoneLabel { get; set; } = "";
    public string Subtext { get; set; } = "";
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


public class RollingPeriodMetrics
{
    public int Days { get; set; }
    public double AvgStars { get; set; }
    public double WeightedAccuracy { get; set; }
    public double DailyPlayCount { get; set; }
    public double DailyActiveHours { get; set; }
    public double PassRatePercent { get; set; }
    public double AvgBpm { get; set; }
    public bool HasSufficientData { get; set; } = true;
    public string DateRangeText { get; set; } = "";
    public int HistoryDaysAvailable { get; set; }
    public int DaysRemaining => Math.Max(0, Days - HistoryDaysAvailable);
    public double ProgressPercent => Days > 0 ? Math.Min(100.0, Math.Round(100.0 * HistoryDaysAvailable / Days, 1)) : 0.0;
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

    public void RaiseCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
    }
}
