using Avalonia.Threading;
using Circle_Tracker.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using static Circle_Tracker.Tracker;

namespace Circle_Tracker.ViewModels;

public class SessionLiveCardViewModel : ViewModelBase, IDisposable
{
    private bool _liveSessionCardVisible;
    private string _deltaAccuracyText = "+0.00%";
    private string _deltaStarsText = "+0.00★";
    private string _deltaBpmText = "+0 BPM";
    private string _sessionStatsText = "0 plays • 0 passes • 0.0 min active";
    private string _sessionElapsedText = "0m session";

    private bool _achievementBannerVisible;
    private string _achievementTitle = "";
    private string _achievementDescription = "";
    private string _achievementAccentColor = "#facc15";
    private CancellationTokenSource? _achievementBannerCts;

    public bool LiveSessionCardVisible
    {
        get => _liveSessionCardVisible;
        set => SetProperty(ref _liveSessionCardVisible, value);
    }

    public string DeltaAccuracyText
    {
        get => _deltaAccuracyText;
        set => SetProperty(ref _deltaAccuracyText, value);
    }

    public string DeltaStarsText
    {
        get => _deltaStarsText;
        set => SetProperty(ref _deltaStarsText, value);
    }

    public string DeltaBpmText
    {
        get => _deltaBpmText;
        set => SetProperty(ref _deltaBpmText, value);
    }

    public string SessionStatsText
    {
        get => _sessionStatsText;
        set => SetProperty(ref _sessionStatsText, value);
    }

    public string SessionElapsedText
    {
        get => _sessionElapsedText;
        set => SetProperty(ref _sessionElapsedText, value);
    }

    public bool AchievementBannerVisible
    {
        get => _achievementBannerVisible;
        set => SetProperty(ref _achievementBannerVisible, value);
    }

    public string AchievementTitle
    {
        get => _achievementTitle;
        set => SetProperty(ref _achievementTitle, value);
    }

    public string AchievementDescription
    {
        get => _achievementDescription;
        set => SetProperty(ref _achievementDescription, value);
    }

    public string AchievementAccentColor
    {
        get => _achievementAccentColor;
        set => SetProperty(ref _achievementAccentColor, value);
    }

    public void UpdateFromSnapshot(TrackerSnapshot snapshot)
    {
        int totalSeconds = snapshot.PlayingSeconds + snapshot.IdleSeconds;
        int totalMin = totalSeconds / 60;
        SessionElapsedText = $"{totalMin}m session";
        if (snapshot.PlayCount > 0 && !LiveSessionCardVisible)
        {
            LiveSessionCardVisible = true;
        }
    }

    public void UpdateFromMetrics(LiveSessionMetrics metrics, DateTime? sessionStartTime = null)
    {
        if (metrics.SessionPlayCount == 0)
        {
            LiveSessionCardVisible = false;
            return;
        }

        LiveSessionCardVisible = true;

        DeltaAccuracyText = metrics.BaselineDeltaAccuracy >= 0
            ? $"+{metrics.BaselineDeltaAccuracy:F2}%"
            : $"{metrics.BaselineDeltaAccuracy:F2}%";

        DeltaStarsText = metrics.BaselineDeltaStars >= 0
            ? $"+{metrics.BaselineDeltaStars:F2}★"
            : $"{metrics.BaselineDeltaStars:F2}★";

        DeltaBpmText = metrics.BaselineDeltaBpm >= 0
            ? $"+{metrics.BaselineDeltaBpm:F0} BPM"
            : $"{metrics.BaselineDeltaBpm:F0} BPM";

        SessionStatsText = $"{metrics.SessionPlayCount} plays • {metrics.SessionPassCount} passes • {metrics.ActivePlayMinutes:F1} min active";

        if (sessionStartTime.HasValue)
        {
            var wallClockMinutes = (DateTime.UtcNow - sessionStartTime.Value).TotalMinutes;
            SessionElapsedText = $"{wallClockMinutes:F0}m session";
        }
    }

    public void ShowAchievement(PostPlayAchievement achievement)
    {
        AchievementTitle = achievement.Title;
        AchievementDescription = achievement.Description;
        AchievementAccentColor = achievement.AccentColorHex;
        AchievementBannerVisible = true;

        _achievementBannerCts?.Cancel();
        _achievementBannerCts = new CancellationTokenSource();
        var token = _achievementBannerCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(4000, token);
                if (!token.IsCancellationRequested)
                {
                    if (Dispatcher.UIThread.CheckAccess())
                    {
                        AchievementBannerVisible = false;
                    }
                    else
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => AchievementBannerVisible = false);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void ResetSession()
    {
        LiveSessionCardVisible = false;
        DeltaAccuracyText = "+0.00%";
        DeltaStarsText = "+0.00★";
        DeltaBpmText = "+0 BPM";
        SessionStatsText = "0 plays • 0 passes • 0.0 min active";
        SessionElapsedText = "0m session";
        AchievementBannerVisible = false;
    }

    public void Dispose()
    {
        _achievementBannerCts?.Cancel();
        _achievementBannerCts?.Dispose();
    }
}
