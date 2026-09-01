using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Circle_Tracker.Converters;

public class PercentToWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percentValue)
        {
            return percentValue;
        }
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
