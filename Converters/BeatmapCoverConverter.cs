using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace Circle_Tracker.Converters;

public class BeatmapCoverConverter : IValueConverter
{
    private static readonly HttpClient _httpClient = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int beatmapSetId && beatmapSetId > 0)
        {
            var url = $"https://assets.ppy.sh/beatmaps/{beatmapSetId}/covers/cover.jpg";
            return LoadImageAsync(url);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static async Task<Bitmap?> LoadImageAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync();
                return new Bitmap(stream);
            }
        }
        catch
        {
        }
        return null;
    }
}
