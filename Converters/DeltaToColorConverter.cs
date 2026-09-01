using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Circle_Tracker.Converters;

public class DeltaToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double delta)
        {
            if (delta >= 0)
                return new SolidColorBrush(Color.Parse("#4ade80"));
            else
                return new SolidColorBrush(Color.Parse("#f87171"));
        }
        return new SolidColorBrush(Color.Parse("#8f87a3"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
