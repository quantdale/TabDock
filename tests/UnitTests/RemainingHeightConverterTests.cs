using System;
using System.Globalization;
using TabDock.Converters;
using Xunit;

namespace TabDock.UnitTests;

public sealed class RemainingHeightConverterTests
{
    [Theory]
    [InlineData(300, 38, 46, 216)]
    [InlineData(640, 38, 46, 360)]
    [InlineData(300, 38, 62, 200)]
    [InlineData(60, 38, 46, 0)]
    public void CapturePanelFitsBelowChrome(double window, double caption, double tabs, double expected)
    {
        var converter = new RemainingHeightConverter();
        Assert.Equal(expected, converter.Convert(new object[] { window, caption, tabs }, typeof(double), "360", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void UnmeasuredWindowDoesNotProduceInvalidDimensions()
    {
        var converter = new RemainingHeightConverter();
        Assert.Equal(0d, converter.Convert(new object[] { double.NaN }, typeof(double), "360", CultureInfo.InvariantCulture));
    }
}
