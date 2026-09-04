using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Circle_Tracker.Converters;

public class PassFailTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is bool isComplete && isComplete) ? "PASS" : "RETRY";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class PassFailBackgroundConverter : IValueConverter
{
    private static readonly IBrush PassBrush = new SolidColorBrush(Color.Parse("#22c55e"));
    private static readonly IBrush RetryBrush = new SolidColorBrush(Color.Parse("#3b1828"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is bool isComplete && isComplete) ? PassBrush : RetryBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class PassFailForegroundConverter : IValueConverter
{
    private static readonly IBrush PassBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush RetryBrush = new SolidColorBrush(Color.Parse("#f87171"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is bool isComplete && isComplete) ? PassBrush : RetryBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
