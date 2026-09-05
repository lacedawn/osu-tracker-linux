using Avalonia.Media;
using static Circle_Tracker.Tracker;

namespace Circle_Tracker.ViewModels;

public class GameplayHudViewModel : ViewModelBase
{
    private string _gameState = "IDLE";
    private IBrush _gameStateBrush = AppBrushes.MutedBrush;

    private string _statCs = "0.0";
    private string _statAr = "0.0";
    private string _statOd = "0.0";
    private string _statHp = "0.0";
    private string _statBpm = "0";
    private string _statMods = "None";
    private IBrush _statArBrush = AppBrushes.WhiteBrush;
    private IBrush _statOdBrush = AppBrushes.WhiteBrush;
    private IBrush _statBpmBrush = AppBrushes.WhiteBrush;
    private IBrush _statModsBrush = AppBrushes.MutedBrush;

    private string _hits300 = "0";
    private string _hits100 = "0";
    private string _hits50 = "0";
    private string _hitsMiss = "0";
    private string _totalObjectsText = "Total: 0";

    private string _accuracyText = "100.00%";
    private IBrush _accuracyBrush = AppBrushes.GoldBrush;
    private string _playCountBadge = "Play #0";
    private string _sessionTimeText = "Play: 0m  •  Idle: 0m  •  Efficiency: 0%";

    public string GameState
    {
        get => _gameState;
        set => SetProperty(ref _gameState, value);
    }

    public IBrush GameStateBrush
    {
        get => _gameStateBrush;
        set => SetProperty(ref _gameStateBrush, value);
    }

    public string StatCs
    {
        get => _statCs;
        set => SetProperty(ref _statCs, value);
    }

    public string StatAr
    {
        get => _statAr;
        set => SetProperty(ref _statAr, value);
    }

    public string StatOd
    {
        get => _statOd;
        set => SetProperty(ref _statOd, value);
    }

    public string StatHp
    {
        get => _statHp;
        set => SetProperty(ref _statHp, value);
    }

    public string StatBpm
    {
        get => _statBpm;
        set => SetProperty(ref _statBpm, value);
    }

    public string StatMods
    {
        get => _statMods;
        set => SetProperty(ref _statMods, value);
    }

    public IBrush StatArBrush
    {
        get => _statArBrush;
        set => SetProperty(ref _statArBrush, value);
    }

    public IBrush StatOdBrush
    {
        get => _statOdBrush;
        set => SetProperty(ref _statOdBrush, value);
    }

    public IBrush StatBpmBrush
    {
        get => _statBpmBrush;
        set => SetProperty(ref _statBpmBrush, value);
    }

    public IBrush StatModsBrush
    {
        get => _statModsBrush;
        set => SetProperty(ref _statModsBrush, value);
    }

    public string Hits300
    {
        get => _hits300;
        set => SetProperty(ref _hits300, value);
    }

    public string Hits100
    {
        get => _hits100;
        set => SetProperty(ref _hits100, value);
    }

    public string Hits50
    {
        get => _hits50;
        set => SetProperty(ref _hits50, value);
    }

    public string HitsMiss
    {
        get => _hitsMiss;
        set => SetProperty(ref _hitsMiss, value);
    }

    public string TotalObjectsText
    {
        get => _totalObjectsText;
        set => SetProperty(ref _totalObjectsText, value);
    }

    public string AccuracyText
    {
        get => _accuracyText;
        set => SetProperty(ref _accuracyText, value);
    }

    public IBrush AccuracyBrush
    {
        get => _accuracyBrush;
        set => SetProperty(ref _accuracyBrush, value);
    }

    public string PlayCountBadge
    {
        get => _playCountBadge;
        set => SetProperty(ref _playCountBadge, value);
    }

    public string SessionTimeText
    {
        get => _sessionTimeText;
        set => SetProperty(ref _sessionTimeText, value);
    }

    public void UpdateFromSnapshot(TrackerSnapshot snapshot)
    {
        GameState = snapshot.GameStateLabel;
        GameStateBrush = snapshot.GameStateLabel switch
        {
            "PLAYING" => AppBrushes.GreenBrush,
            "RESULTS" => AppBrushes.CyanBrush,
            "REPLAY" => AppBrushes.OrangeBrush,
            _ => AppBrushes.MutedBrush
        };

        StatCs = snapshot.BeatmapCs.ToString("0.0");
        StatAr = snapshot.BeatmapAr.ToString("0.0");
        StatArBrush = snapshot.BeatmapAr >= 10.0m ? AppBrushes.GreenBrush : AppBrushes.WhiteBrush;

        StatOd = snapshot.BeatmapOd.ToString("0.0");
        StatOdBrush = snapshot.BeatmapOd >= 10.0m ? AppBrushes.GreenBrush : AppBrushes.WhiteBrush;

        StatHp = snapshot.BeatmapHp.ToString("0.0");
        StatBpm = snapshot.BeatmapBpm.ToString();
        StatBpmBrush = snapshot.BeatmapBpm >= 200 ? AppBrushes.OrangeBrush : AppBrushes.WhiteBrush;

        StatMods = !string.IsNullOrEmpty(snapshot.ModsString) ? $"+{snapshot.ModsString}" : "None";
        StatModsBrush = !string.IsNullOrEmpty(snapshot.ModsString) ? AppBrushes.PinkBrush : AppBrushes.MutedBrush;

        Hits300 = snapshot.Play300c.ToString();
        Hits100 = snapshot.Play100c.ToString();
        Hits50 = snapshot.Play50c.ToString();
        HitsMiss = snapshot.PlayMissc.ToString();
        TotalObjectsText = $"Total: {snapshot.TotalBeatmapHits}";

        AccuracyText = $"{snapshot.Accuracy:0.00}%";
        if (snapshot.Accuracy >= 100.0m)
        {
            AccuracyBrush = AppBrushes.GoldBrush;
        }
        else if (snapshot.Accuracy > 95.0m)
        {
            AccuracyBrush = AppBrushes.GreenBrush;
        }
        else
        {
            AccuracyBrush = AppBrushes.WhiteBrush;
        }

        PlayCountBadge = $"Play #{snapshot.PlayCount}";
        UpdateSessionTime(snapshot.PlayingSeconds, snapshot.IdleSeconds);
    }

    public void UpdateSessionTime(int playingSeconds, int idleSeconds)
    {
        float total = playingSeconds + idleSeconds;
        float eff = total > 0 ? 100f * playingSeconds / total : 0f;
        int playingMin = playingSeconds / 60;
        int idleMin = idleSeconds / 60;
        SessionTimeText = $"Play: {playingMin}m  •  Idle: {idleMin}m  •  Efficiency: {(int)eff}%";
    }
}
