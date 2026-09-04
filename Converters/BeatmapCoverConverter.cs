using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Circle_Tracker.Services;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Circle_Tracker.Converters;

public class BeatmapCoverConverter : IValueConverter
{
    public static readonly AttachedProperty<int> BeatmapSetIdProperty =
        AvaloniaProperty.RegisterAttached<BeatmapCoverConverter, Image, int>(
            "BeatmapSetId",
            0);

    public static event EventHandler<(int BeatmapSetId, Bitmap? Bitmap)>? CoverLoaded;

    static BeatmapCoverConverter()
    {
        BeatmapSetIdProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Image image)
            {
                OnBeatmapSetIdChanged(image, args.NewValue.GetValueOrDefault());
            }
        });
    }

    public static int GetBeatmapSetId(Image element)
    {
        return element.GetValue(BeatmapSetIdProperty);
    }

    public static void SetBeatmapSetId(Image element, int value)
    {
        element.SetValue(BeatmapSetIdProperty, value);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int beatmapSetId && beatmapSetId > 0)
        {
            var cached = BeatmapCoverCache.Instance.GetFromMemory(beatmapSetId);
            if (cached != null)
            {
                return cached;
            }

            if (parameter is Image image)
            {
                _ = LoadIntoImageAsync(image, beatmapSetId);
            }
            else
            {
                _ = BeatmapCoverCache.Instance.GetCoverAsync(beatmapSetId);
            }

            return null;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static void OnBeatmapSetIdChanged(Image image, int beatmapSetId)
    {
        if (beatmapSetId <= 0)
        {
            image.Source = null;
            return;
        }

        var cached = BeatmapCoverCache.Instance.GetFromMemory(beatmapSetId);
        if (cached != null)
        {
            image.Source = cached;
            return;
        }

        image.Source = null;
        _ = LoadIntoImageAsync(image, beatmapSetId);
    }

    private static async Task LoadIntoImageAsync(Image image, int beatmapSetId)
    {
        try
        {
            var bitmap = await BeatmapCoverCache.Instance.GetCoverAsync(beatmapSetId);
            if (bitmap != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (GetBeatmapSetId(image) == beatmapSetId || GetBeatmapSetId(image) == 0)
                    {
                        image.Source = bitmap;
                    }
                    CoverLoaded?.Invoke(null, (beatmapSetId, bitmap));
                });
            }
        }
        catch
        {
        }
    }
}
