using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Circle_Tracker.Analytics;
using Circle_Tracker.Storage;

namespace Circle_Tracker.Services;

public record LiveSessionMetrics(
    int SessionPlayCount,
    int SessionPassCount,
    double ActivePlayMinutes,
    decimal SessionAverageAccuracy,
    decimal BaselineDeltaAccuracy,
    decimal SessionAverageStars,
    decimal BaselineDeltaStars,
    double SessionAverageBpm,
    double BaselineDeltaBpm
);

public record PostPlayAchievement(
    string Title,
    string Description,
    string AccentColorHex,
    DateTime Timestamp
);

public record SessionSummaryReport(
    int TotalPlays,
    int TotalPasses,
    double ActivePlayMinutes,
    decimal SessionAccuracy,
    decimal BaselineDeltaAccuracy,
    decimal SessionAvgStars,
    decimal BaselineDeltaStars,
    double SessionAvgBpm,
    double BaselineDeltaBpm,
    double PassRatePercent,
    double BaselineDeltaPassRate,
    BestPlayCard? BestPlay
);

public record BestPlayCard(
    string BeatmapString,
    int BeatmapSetId,
    decimal Stars,
    decimal Accuracy,
    int TotalHits,
    int PlayTimeSeconds,
    string ModsString
);

public interface ILiveSessionTracker
{
    LiveSessionMetrics GetCurrentMetrics();
    Task OnPlayLoggedAsync(PlayEntryData play, PlayContext context);
    event EventHandler<LiveSessionMetrics>? MetricsUpdated;
    event EventHandler<PostPlayAchievement>? AchievementUnlocked;
    Task<SessionSummaryReport> GenerateSessionSummaryAsync(CancellationToken ct = default);
    void ResetSession();
}

public class LiveSessionTracker : ILiveSessionTracker
{
    private readonly ISessionAnalyticsService _sessionService;
    private readonly List<PlayEntryData> _sessionPlays = new();
    private readonly object _playsLock = new();
    private DateTime _sessionStartTime = DateTime.UtcNow;
    private decimal _sessionPeakAccuracy = 0m;
    private decimal _highestStarPass = 0m;
    private double _highestBpmPass = 0;
    private double _highestAimPass = 0;
    
    private RollingPeriodStats? _baseline30Day;

    public event EventHandler<LiveSessionMetrics>? MetricsUpdated;
    public event EventHandler<PostPlayAchievement>? AchievementUnlocked;

    public LiveSessionTracker(ISessionAnalyticsService sessionService)
    {
        _sessionService = sessionService;
    }

    public LiveSessionMetrics GetCurrentMetrics()
    {
        List<PlayEntryData> snapshot;
        lock (_playsLock) { snapshot = _sessionPlays.ToList(); }

        if (snapshot.Count == 0)
        {
            return new LiveSessionMetrics(
                SessionPlayCount: 0,
                SessionPassCount: 0,
                ActivePlayMinutes: 0,
                SessionAverageAccuracy: 0m,
                BaselineDeltaAccuracy: 0m,
                SessionAverageStars: 0m,
                BaselineDeltaStars: 0m,
                SessionAverageBpm: 0,
                BaselineDeltaBpm: 0
            );
        }

        var passCount = snapshot.Count(p => p.Complete);
        var totalPlayTime = snapshot.Sum(p => p.PlayTimeSeconds);
        var avgAccuracy = snapshot.Average(p => p.Accuracy);
        var avgStars = snapshot.Average(p => p.BeatmapStars);
        var avgBpm = snapshot.Average(p => p.BeatmapBpm);

        var deltaAcc = _baseline30Day != null ? avgAccuracy - _baseline30Day.MeanAccuracy : 0m;
        var deltaStars = _baseline30Day != null ? avgStars - _baseline30Day.MeanStars : 0m;
        var deltaBpm = _baseline30Day != null ? avgBpm - _baseline30Day.MeanBpm : 0;

        return new LiveSessionMetrics(
            SessionPlayCount: snapshot.Count,
            SessionPassCount: passCount,
            ActivePlayMinutes: totalPlayTime / 60.0,
            SessionAverageAccuracy: avgAccuracy,
            BaselineDeltaAccuracy: deltaAcc,
            SessionAverageStars: avgStars,
            BaselineDeltaStars: deltaStars,
            SessionAverageBpm: avgBpm,
            BaselineDeltaBpm: deltaBpm
        );
    }

    public async Task OnPlayLoggedAsync(PlayEntryData play, PlayContext context)
    {
        if (_baseline30Day == null)
        {
            await LoadBaselineAsync();
        }

        lock (_playsLock)
        {
            _sessionPlays.Add(play);
        }

        if (play.Accuracy > _sessionPeakAccuracy)
        {
            _sessionPeakAccuracy = play.Accuracy;
        }

        await CheckAchievementsAsync(play, context);

        MetricsUpdated?.Invoke(this, GetCurrentMetrics());
    }

    public async Task<SessionSummaryReport> GenerateSessionSummaryAsync(CancellationToken ct = default)
    {
        if (_baseline30Day == null)
        {
            await LoadBaselineAsync(ct);
        }

        List<PlayEntryData> snapshot;
        lock (_playsLock) { snapshot = _sessionPlays.ToList(); }

        if (snapshot.Count == 0)
        {
            return new SessionSummaryReport(
                TotalPlays: 0,
                TotalPasses: 0,
                ActivePlayMinutes: 0,
                SessionAccuracy: 0m,
                BaselineDeltaAccuracy: 0m,
                SessionAvgStars: 0m,
                BaselineDeltaStars: 0m,
                SessionAvgBpm: 0,
                BaselineDeltaBpm: 0,
                PassRatePercent: 0,
                BaselineDeltaPassRate: 0,
                BestPlay: null
            );
        }

        var passes = snapshot.Where(p => p.Complete).ToList();
        var passCount = passes.Count;
        var totalPlayTime = snapshot.Sum(p => p.PlayTimeSeconds);
        var avgAccuracy = snapshot.Average(p => p.Accuracy);
        var avgStars = snapshot.Average(p => p.BeatmapStars);
        var avgBpm = snapshot.Average(p => p.BeatmapBpm);
        var passRate = snapshot.Count > 0 ? (passCount / (double)snapshot.Count) * 100.0 : 0;

        var deltaAcc = _baseline30Day != null ? avgAccuracy - _baseline30Day.MeanAccuracy : 0m;
        var deltaStars = _baseline30Day != null ? avgStars - _baseline30Day.MeanStars : 0m;
        var deltaBpm = _baseline30Day != null ? avgBpm - _baseline30Day.MeanBpm : 0;
        var deltaPassRate = _baseline30Day != null ? passRate - _baseline30Day.PassRatePercent : 0;

        var bestPlay = GetBestPlay();

        return new SessionSummaryReport(
            TotalPlays: snapshot.Count,
            TotalPasses: passCount,
            ActivePlayMinutes: totalPlayTime / 60.0,
            SessionAccuracy: avgAccuracy,
            BaselineDeltaAccuracy: deltaAcc,
            SessionAvgStars: avgStars,
            BaselineDeltaStars: deltaStars,
            SessionAvgBpm: avgBpm,
            BaselineDeltaBpm: deltaBpm,
            PassRatePercent: passRate,
            BaselineDeltaPassRate: deltaPassRate,
            BestPlay: bestPlay
        );
    }

    public void ResetSession()
    {
        lock (_playsLock)
        {
            _sessionPlays.Clear();
        }
        _sessionStartTime = DateTime.UtcNow;
        _sessionPeakAccuracy = 0m;
        _highestStarPass = 0m;
        _highestBpmPass = 0;
        _highestAimPass = 0;
    }

    private async Task LoadBaselineAsync(CancellationToken ct = default)
    {
        var allMetrics = await _sessionService.GetRollingAveragesAsync(ct);
        if (allMetrics.TryGetValue("30D", out var baseline))
        {
            _baseline30Day = baseline;
        }
    }

    private async Task CheckAchievementsAsync(PlayEntryData play, PlayContext context)
    {
        if (play.Complete && play.BeatmapStars > _highestStarPass)
        {
            _highestStarPass = play.BeatmapStars;
            var achievement = new PostPlayAchievement(
                Title: "New Star Rating Record Pass!",
                Description: $"{play.BeatmapStars:F2}★",
                AccentColorHex: "#facc15",
                Timestamp: DateTime.UtcNow
            );
            AchievementUnlocked?.Invoke(this, achievement);
        }

        if (play.Complete && play.Accuracy >= 95m && play.BeatmapStars >= 5.0m && play.BeatmapStars <= 6.0m)
        {
            var achievement = new PostPlayAchievement(
                Title: "Comfort Zone Clear",
                Description: $"{play.Accuracy:F1}% on {play.BeatmapStars:F1}★",
                AccentColorHex: "#4ade80",
                Timestamp: DateTime.UtcNow
            );
            AchievementUnlocked?.Invoke(this, achievement);
        }

        if (play.Complete && play.BeatmapBpm >= 220 && play.BeatmapBpm > _highestBpmPass)
        {
            _highestBpmPass = play.BeatmapBpm;
            var achievement = new PostPlayAchievement(
                Title: "Speed PR",
                Description: $"Passed {play.BeatmapBpm:F0} BPM Stream Map",
                AccentColorHex: "#7dd3fc",
                Timestamp: DateTime.UtcNow
            );
            AchievementUnlocked?.Invoke(this, achievement);
        }

        if (play.Complete && play.BeatmapAim >= 3.0m && play.BeatmapAim > (decimal)_highestAimPass)
        {
            _highestAimPass = (double)play.BeatmapAim;
            var achievement = new PostPlayAchievement(
                Title: "Aim Record",
                Description: $"{play.BeatmapAim:F1}★ Aim Pass",
                AccentColorHex: "#e11d48",
                Timestamp: DateTime.UtcNow
            );
            AchievementUnlocked?.Invoke(this, achievement);
        }

        await Task.CompletedTask;
    }


    private BestPlayCard? GetBestPlay()
    {
        List<PlayEntryData> snapshot;
        lock (_playsLock) { snapshot = _sessionPlays.ToList(); }

        var completedPlays = snapshot.Where(p => p.Complete).ToList();
        if (completedPlays.Count == 0)
        {
            return null;
        }

        var bestPlay = completedPlays.OrderByDescending(p => p.BeatmapStars).ThenByDescending(p => p.Accuracy).First();

        return new BestPlayCard(
            BeatmapString: bestPlay.BeatmapString,
            BeatmapSetId: bestPlay.BeatmapSetID,
            Stars: bestPlay.BeatmapStars,
            Accuracy: bestPlay.Accuracy,
            TotalHits: bestPlay.TotalBeatmapHits,
            PlayTimeSeconds: bestPlay.PlayTimeSeconds,
            ModsString: bestPlay.ModsString
        );
    }
}
