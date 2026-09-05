using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Circle_Tracker.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using static Circle_Tracker.Tracker;

namespace Circle_Tracker.ViewModels;

public class BeatmapBannerViewModel : ViewModelBase
{
    private readonly BeatmapCoverCache _coverCache;
    private string _beatmapTitle = "No beatmap detected";
    private string _beatmapArtist = "-";
    private string _beatmapVersion = "-";
    private string _beatmapStars = "★ 0.00";
    private bool _bannerTrianglesVisible = true;
    private Bitmap? _coverImage;
    private int _currentBeatmapSetId;
    private CancellationTokenSource? _coverLoadCts;

    public event EventHandler<Bitmap?>? CoverImageChanged;

    public BeatmapBannerViewModel(BeatmapCoverCache? coverCache = null)
    {
        _coverCache = coverCache ?? BeatmapCoverCache.Instance;
    }

    public string BeatmapTitle
    {
        get => _beatmapTitle;
        set => SetProperty(ref _beatmapTitle, value);
    }

    public string BeatmapArtist
    {
        get => _beatmapArtist;
        set => SetProperty(ref _beatmapArtist, value);
    }

    public string BeatmapVersion
    {
        get => _beatmapVersion;
        set => SetProperty(ref _beatmapVersion, value);
    }

    public string BeatmapStars
    {
        get => _beatmapStars;
        set => SetProperty(ref _beatmapStars, value);
    }

    public bool BannerTrianglesVisible
    {
        get => _bannerTrianglesVisible;
        set => SetProperty(ref _bannerTrianglesVisible, value);
    }

    public Bitmap? CoverImage
    {
        get => _coverImage;
        set
        {
            if (SetProperty(ref _coverImage, value))
            {
                CoverImageChanged?.Invoke(this, value);
            }
        }
    }

    public void UpdateFromSnapshot(TrackerSnapshot snapshot)
    {
        BeatmapTitle = !string.IsNullOrEmpty(snapshot.BeatmapTitle)
            ? snapshot.BeatmapTitle
            : (!string.IsNullOrEmpty(snapshot.BeatmapString) ? snapshot.BeatmapString : "No beatmap detected");
        BeatmapArtist = !string.IsNullOrEmpty(snapshot.BeatmapArtist) ? snapshot.BeatmapArtist : "-";
        BeatmapVersion = !string.IsNullOrEmpty(snapshot.BeatmapVersion) ? snapshot.BeatmapVersion : "-";
        BeatmapStars = $"★ {snapshot.BeatmapStars:0.00}";
        BannerTrianglesVisible = string.IsNullOrEmpty(snapshot.BeatmapTitle) && string.IsNullOrEmpty(snapshot.BeatmapString);

        LoadCoverImage(snapshot.BeatmapSetId);
    }

    public void LoadCoverImage(int beatmapSetId)
    {
        if (beatmapSetId <= 0)
        {
            _currentBeatmapSetId = 0;
            _coverLoadCts?.Cancel();
            CoverImage = null;
            return;
        }

        if (_currentBeatmapSetId == beatmapSetId && CoverImage != null)
        {
            return;
        }

        _currentBeatmapSetId = beatmapSetId;
        _coverLoadCts?.Cancel();

        var cached = _coverCache.GetFromMemory(beatmapSetId);
        if (cached != null)
        {
            CoverImage = cached;
            return;
        }

        _coverLoadCts = new CancellationTokenSource();
        var token = _coverLoadCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var bitmap = await _coverCache.GetCoverAsync(beatmapSetId, token);
                if (!token.IsCancellationRequested && _currentBeatmapSetId == beatmapSetId)
                {
                    if (Dispatcher.UIThread.CheckAccess())
                    {
                        if (_currentBeatmapSetId == beatmapSetId)
                        {
                            CoverImage = bitmap;
                        }
                    }
                    else
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (_currentBeatmapSetId == beatmapSetId)
                            {
                                CoverImage = bitmap;
                            }
                        });
                    }
                }
            }
            catch
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    if (_currentBeatmapSetId == beatmapSetId)
                    {
                        CoverImage = null;
                    }
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_currentBeatmapSetId == beatmapSetId)
                        {
                            CoverImage = null;
                        }
                    });
                }
            }
        }, token);
    }
}
