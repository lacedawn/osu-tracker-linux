using Avalonia.Controls;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Circle_Tracker.Converters;

public class DoubleToGridLengthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double val = 0.0;
        if (value is double d) val = d;
        else if (value is decimal dec) val = (double)dec;
        else if (value is float f) val = f;
        else if (value is int i) val = i;
        val = Math.Max(0.1, val);
        return new GridLength(val, GridUnitType.Star);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
