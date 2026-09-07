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
)
{
    public decimal SessionAccuracy => SessionAverageAccuracy;
}

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
    BestPlayCard? BestPlay,
    decimal PassAccuracy = 0m
)
{
    public bool HasPasses => TotalPasses > 0;
    public string SessionAccuracyText => HasPasses ? $"{SessionAccuracy:F2}%" : "—";
    public string SessionStarsText => HasPasses ? $"{SessionAvgStars:F2}★" : "—";
    public string SessionBpmText => HasPasses ? $"{SessionAvgBpm:F0} BPM" : "—";
    public string DeltaStarsBadgeText => BaselineDeltaStars >= 0 
        ? $"+{BaselineDeltaStars:F2} ★ above 30D avg" 
        : $"{Math.Abs(BaselineDeltaStars):F2} ★ below 30D avg";
    public string DeltaPassRateBadgeText => BaselineDeltaPassRate >= 0 
        ? $"+{BaselineDeltaPassRate:F1}% above 30D avg" 
        : $"{Math.Abs(BaselineDeltaPassRate):F1}% below 30D avg";
    public string DeltaBpmBadgeText => BaselineDeltaBpm >= 0 
        ? $"+{BaselineDeltaBpm:F0} BPM above 30D avg" 
        : $"{Math.Abs(BaselineDeltaBpm):F0} BPM below 30D avg";
};

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
    decimal SessionAccuracy { get; }
    decimal BaselineDeltaAccuracy { get; }
    LiveSessionMetrics GetCurrentMetrics();
    Task OnPlayLoggedAsync(PlayEntryData play, PlayContext context);
    Task ProcessPlay(PlayEntryData play, PlayContext? context = null);
    event EventHandler<LiveSessionMetrics>? MetricsUpdated;
    event EventHandler<LiveSessionMetrics>? PlayProcessed;
    event EventHandler<PostPlayAchievement>? AchievementUnlocked;
    Task<SessionSummaryReport> GenerateSessionSummaryAsync(CancellationToken ct = default);
    void ResetSession();
}

public class LiveSessionTracker : ILiveSessionTracker
{
    private readonly ISessionAnalyticsService _sessionService;
    private readonly List<PlayEntryData> _sessionPlays = new();
    private readonly object _playsLock = new();
    private readonly Dictionary<string, DateTime> _lastAchievementTimes = new();
    private DateTime _sessionStartTime = DateTime.UtcNow;
    private decimal _sessionMaxPassStars = 0m;
    
    private RollingPeriodStats? _baseline30Day;

    public decimal SessionAccuracy { get; private set; } = 0m;
    public decimal BaselineDeltaAccuracy { get; private set; } = 0m;

    public event EventHandler<LiveSessionMetrics>? MetricsUpdated;
    public event EventHandler<LiveSessionMetrics>? PlayProcessed;
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
        var totalPlayTime = snapshot.Sum(p => p.PlayTimeSeconds);
        var passedPlays = snapshot.Where(p => p.Complete).ToList();
        var passCount = passedPlays.Count;
        decimal avgAccuracy = CalculateHitWeightedAccuracy(snapshot);
        decimal avgStars = 0m;
        double avgBpm = 0.0;
        decimal deltaAcc = 0m;
        decimal deltaStars = 0m;
        double deltaBpm = 0.0;
        if (passCount > 0)
        {
            avgStars = (decimal)passedPlays.Average(p => p.BeatmapStars);
            avgBpm = passedPlays.Average(p => p.BeatmapBpm);
            if (_baseline30Day != null)
            {
                deltaAcc = avgAccuracy - _baseline30Day.MeanAccuracy;
                deltaStars = avgStars - _baseline30Day.MeanStars;
                deltaBpm = avgBpm - _baseline30Day.MeanBpm;
            }
        }
        return new LiveSessionMetrics(
            SessionPlayCount: snapshot.Count,
            SessionPassCount: passCount,
            ActivePlayMinutes: Math.Round(totalPlayTime / 60.0, 1),
            SessionAverageAccuracy: Math.Round(avgAccuracy, 2),
            BaselineDeltaAccuracy: Math.Round(deltaAcc, 2),
            SessionAverageStars: Math.Round(avgStars, 2),
            BaselineDeltaStars: Math.Round(deltaStars, 2),
            SessionAverageBpm: Math.Round(avgBpm, 1),
            BaselineDeltaBpm: Math.Round(deltaBpm, 1)
        );
    }

    public async Task ProcessPlay(PlayEntryData play, PlayContext? context = null)
    {
        context ??= new PlayContext(
            SessionId: Guid.NewGuid().ToString(),
            IsReplay: false,
            RawMods: 0,
            CurrentGameMode: 0,
            DetectedClient: "lazer",
            SoundFilePath: null,
            SubmitSoundEnabled: false
        );
        await OnPlayLoggedAsync(play, context);
    }

    public async Task OnPlayLoggedAsync(PlayEntryData play, PlayContext context)
    {
        if (_baseline30Day == null)
        {
            await LoadBaselineAsync();
        }

        List<PlayEntryData> snapshot;
        lock (_playsLock)
        {
            _sessionPlays.Add(play);
            snapshot = _sessionPlays.ToList();
        }

        SessionAccuracy = CalculateHitWeightedAccuracy(snapshot);
        BaselineDeltaAccuracy = _baseline30Day != null ? SessionAccuracy - _baseline30Day.MeanAccuracy : 0m;

        CheckAchievements(play, context);

        var currentMetrics = GetCurrentMetrics();
        MetricsUpdated?.Invoke(this, currentMetrics);
        PlayProcessed?.Invoke(this, currentMetrics);
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
                BestPlay: null,
                PassAccuracy: 0m
            );
        }

        var passes = snapshot.Where(p => p.Complete).ToList();
        var passCount = passes.Count;
        var totalPlayTime = snapshot.Sum(p => p.PlayTimeSeconds);
        var passRate = snapshot.Count > 0 ? (passCount / (double)snapshot.Count) * 100.0 : 0;
        var deltaPassRate = _baseline30Day != null ? passRate - _baseline30Day.PassRatePercent : 0;

        decimal hitWeightedAcc = CalculateHitWeightedAccuracy(snapshot);
        decimal avgStars = 0m;
        double avgBpm = 0;
        decimal deltaAcc = 0m;
        decimal deltaStars = 0m;
        double deltaBpm = 0;

        if (passCount > 0)
        {
            avgStars = passes.Average(p => p.BeatmapStars);
            avgBpm = passes.Average(p => p.BeatmapBpm);

            deltaAcc = _baseline30Day != null ? hitWeightedAcc - _baseline30Day.MeanAccuracy : 0m;
            deltaStars = _baseline30Day != null ? avgStars - _baseline30Day.MeanStars : 0m;
            deltaBpm = _baseline30Day != null ? avgBpm - _baseline30Day.MeanBpm : 0;
        }

        decimal passAccuracy = passes.Count > 0
            ? passes.Average(p => p.Accuracy)
            : 0m;

        var bestPlay = GetBestPlay();

        return new SessionSummaryReport(
            TotalPlays: snapshot.Count,
            TotalPasses: passCount,
            ActivePlayMinutes: totalPlayTime / 60.0,
            SessionAccuracy: hitWeightedAcc,
            BaselineDeltaAccuracy: deltaAcc,
            SessionAvgStars: avgStars,
            BaselineDeltaStars: deltaStars,
            SessionAvgBpm: avgBpm,
            BaselineDeltaBpm: deltaBpm,
            PassRatePercent: passRate,
            BaselineDeltaPassRate: deltaPassRate,
            BestPlay: bestPlay,
            PassAccuracy: passAccuracy
        );
    }

    public void ResetSession()
    {
        lock (_playsLock)
        {
            _sessionPlays.Clear();
            _lastAchievementTimes.Clear();
        }
        _sessionStartTime = DateTime.UtcNow;
        SessionAccuracy = 0m;
        BaselineDeltaAccuracy = 0m;
        _sessionMaxPassStars = 0m;
    }

    private async Task LoadBaselineAsync(CancellationToken ct = default)
    {
        var allMetrics = await _sessionService.GetRollingAveragesAsync(ct);
        if (allMetrics.TryGetValue("30D", out var baseline))
        {
            _baseline30Day = baseline;
        }
    }

    private void CheckAchievements(PlayEntryData play, PlayContext context)
    {
        const int CooldownSeconds = 30;
        var now = DateTime.UtcNow;

        if (play.Complete && play.BeatmapStars > _sessionMaxPassStars)
        {
            string starAchievementKey = "StarRecordPass";
            if (ShouldFireAchievement(starAchievementKey, now, CooldownSeconds))
            {
                var achievement = new PostPlayAchievement(
                    Title: "New Star Rating Record Pass!",
                    Description: $"{play.BeatmapStars:F2}★",
                    AccentColorHex: "#facc15",
                    Timestamp: now
                );
                AchievementUnlocked?.Invoke(this, achievement);
                _lastAchievementTimes[starAchievementKey] = now;
            }
        }

        if (play.Complete && play.BeatmapStars > _sessionMaxPassStars)
            _sessionMaxPassStars = play.BeatmapStars;

        if (play.Complete && play.Accuracy >= 95m && play.BeatmapStars >= 5.0m && play.BeatmapStars <= 6.0m)
        {
            string comfortKey = "ComfortZoneClear";
            if (ShouldFireAchievement(comfortKey, now, CooldownSeconds))
            {
                var achievement = new PostPlayAchievement(
                    Title: "Comfort Zone Clear",
                    Description: $"{play.Accuracy:F1}% on {play.BeatmapStars:F1}★",
                    AccentColorHex: "#4ade80",
                    Timestamp: now
                );
                AchievementUnlocked?.Invoke(this, achievement);
                _lastAchievementTimes[comfortKey] = now;
            }
        }

        if (play.Complete && play.BeatmapBpm >= 220)
        {
            string speedKey = "SpeedPR";
            if (ShouldFireAchievement(speedKey, now, CooldownSeconds))
            {
                var achievement = new PostPlayAchievement(
                    Title: "Speed PR",
                    Description: $"Passed {play.BeatmapBpm:F0} BPM Stream Map",
                    AccentColorHex: "#7dd3fc",
                    Timestamp: now
                );
                AchievementUnlocked?.Invoke(this, achievement);
                _lastAchievementTimes[speedKey] = now;
            }
        }

        if (play.Complete && play.BeatmapAim >= 3.0m)
        {
            string aimKey = "AimRecord";
            if (ShouldFireAchievement(aimKey, now, CooldownSeconds))
            {
                var achievement = new PostPlayAchievement(
                    Title: "Aim Record",
                    Description: $"{play.BeatmapAim:F1}★ Aim Pass",
                    AccentColorHex: "#e11d48",
                    Timestamp: now
                );
                AchievementUnlocked?.Invoke(this, achievement);
                _lastAchievementTimes[aimKey] = now;
            }
        }
    }

    private bool ShouldFireAchievement(string achievementKey, DateTime now, int cooldownSeconds)
    {
        if (_lastAchievementTimes.TryGetValue(achievementKey, out var lastTime))
        {
            return (now - lastTime).TotalSeconds >= cooldownSeconds;
        }
        return true;
    }

    private static decimal CalculateHitWeightedAccuracy(List<PlayEntryData> plays)
    {
        var passedPlays = plays.Where(p => p.Complete).ToList();
        
        if (passedPlays.Count == 0)
        {
            return 0m;
        }

        long totalHits = passedPlays.Sum(p => (long)p.TotalHits);
        if (totalHits == 0)
        {
            return passedPlays.Average(p => p.Accuracy);
        }

        return (decimal)(passedPlays.Sum(p => (double)p.Accuracy * p.TotalHits) / totalHits);
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
