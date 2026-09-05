using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Circle_Tracker.Converters;

public class DeltaToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double delta;

        switch (value)
        {
            case double d:
                delta = d;
                break;
            case decimal m:
                delta = (double)m;
                break;
            case float f:
                delta = f;
                break;
            case int i:
                delta = i;
                break;
            case long l:
                delta = l;
                break;
            default:
                delta = double.NaN;
                break;
        }

        Color color;
        if (double.IsNaN(delta) || delta == 0)
            color = Color.Parse("#8f87a3");
        else if (delta >= 0)
            color = Color.Parse("#4ade80");
        else
            color = Color.Parse("#f87171");

        if (targetType == typeof(Color) || targetType == typeof(Color?))
            return color;

        if (typeof(IBrush).IsAssignableFrom(targetType) || targetType == typeof(object))
            return new SolidColorBrush(color);

        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
