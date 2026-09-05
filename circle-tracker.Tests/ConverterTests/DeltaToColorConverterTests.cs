using Avalonia.Media;
using Circle_Tracker.Converters;
using FluentAssertions;
using System.Globalization;
using Xunit;

namespace CircleTracker.Tests.ConverterTests;

public class DeltaToColorConverterTests
{
    private readonly DeltaToColorConverter _converter = new();

    [Fact]
    public void Should_ReturnGreenColor_When_ValueIsPositiveDecimal_And_TargetIsColor()
    {
        var result = _converter.Convert(1.25m, typeof(Color), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<Color>();
        var color = (Color)result!;
        color.Should().Be(Color.Parse("#4ade80"));
    }

    [Fact]
    public void Should_ReturnRedBrush_When_ValueIsNegativeDecimal_And_TargetIsIBrush()
    {
        var result = _converter.Convert(-0.85m, typeof(IBrush), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result!;
        brush.Color.Should().Be(Color.Parse("#f87171"));
    }

    [Fact]
    public void Should_ReturnMutedGray_When_ValueIsZero()
    {
        var result = _converter.Convert(0.0, typeof(IBrush), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result!;
        brush.Color.Should().Be(Color.Parse("#8f87a3"));
    }

    [Fact]
    public void Should_ReturnMutedGray_When_ValueIsNull()
    {
        var result = _converter.Convert(null, typeof(IBrush), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result!;
        brush.Color.Should().Be(Color.Parse("#8f87a3"));
    }

    [Fact]
    public void Should_ReturnMutedGray_When_ValueIsInvalidString()
    {
        var result = _converter.Convert("invalid", typeof(IBrush), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result!;
        brush.Color.Should().Be(Color.Parse("#8f87a3"));
    }

    [Fact]
    public void Should_ReturnGreenBrush_When_ValueIsPositiveDouble()
    {
        var result = _converter.Convert(2.5, typeof(IBrush), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result!;
        brush.Color.Should().Be(Color.Parse("#4ade80"));
    }

    [Fact]
    public void Should_ReturnRedColor_When_ValueIsNegativeFloat_And_TargetIsColor()
    {
        var result = _converter.Convert(-1.5f, typeof(Color), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<Color>();
        var color = (Color)result!;
        color.Should().Be(Color.Parse("#f87171"));
    }

    [Fact]
    public void Should_ReturnGreenBrush_When_ValueIsPositiveInt()
    {
        var result = _converter.Convert(5, typeof(object), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result!;
        brush.Color.Should().Be(Color.Parse("#4ade80"));
    }

    [Fact]
    public void Should_ReturnRedBrush_When_ValueIsNegativeLong()
    {
        var result = _converter.Convert(-10L, typeof(IBrush), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result!;
        brush.Color.Should().Be(Color.Parse("#f87171"));
    }

    [Fact]
    public void Should_HandleNullableColor_TargetType()
    {
        var result = _converter.Convert(3.14m, typeof(Color?), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<Color>();
        var color = (Color)result!;
        color.Should().Be(Color.Parse("#4ade80"));
    }
}
