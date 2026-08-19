using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TabDock.Converters;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Headless regression coverage for the XAML value converters. These run on the
/// UI thread in the app but their Convert logic is pure and needs no Dispatcher,
/// so they can be exercised deterministically. The BoolToVisibility contract is
/// the exact trap that produced the dead "No groups yet" launcher hint
/// (L-series): a non-bool truthy value (e.g. an int Groups.Count) is NOT
/// `is true`, so it must collapse rather than appear visible.
/// </summary>
public class ConverterTests
{
    private static readonly BoolToVisibilityConverter BoolConv = new();
    private static readonly ColorToBrushConverter ColorConv = new();
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(true, Visibility.Visible)]
    [InlineData(false, Visibility.Collapsed)]
    public void BoolToVisibility_Convert_MapsBool(object value, Visibility expected)
    {
        Assert.Equal(expected, BoolConv.Convert(value, typeof(Visibility), null, Invariant));
    }

    [Fact]
    public void BoolToVisibility_Convert_NonBoolIsFalse()
    {
        // Lock the L-series contract: an int (Groups.Count), zero, and null are
        // all "not true", so each collapses. A future change must not start
        // treating a non-zero int as visible.
        Assert.Equal(Visibility.Collapsed, BoolConv.Convert(5, typeof(Visibility), null, Invariant));
        Assert.Equal(Visibility.Collapsed, BoolConv.Convert(0, typeof(Visibility), null, Invariant));
        Assert.Equal(Visibility.Collapsed, BoolConv.Convert(null, typeof(Visibility), null, Invariant));
    }

    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void BoolToVisibility_Convert_InvertParameterFlips(object value, Visibility expected)
    {
        Assert.Equal(expected, BoolConv.Convert(value, typeof(Visibility), "true", Invariant));
    }

    [Fact]
    public void BoolToVisibility_ConvertBack_NotSupported()
    {
        Assert.Throws<NotSupportedException>(() => BoolConv.ConvertBack(Visibility.Visible, typeof(bool), null, Invariant));
    }

    [Fact]
    public void ColorToBrush_Convert_ParsesNamedColor()
    {
        var brush = (SolidColorBrush)ColorConv.Convert("Red", typeof(Brush), null, Invariant);
        Assert.IsType<SolidColorBrush>(brush);
        Assert.True(brush.IsFrozen);
        Assert.Equal(Colors.Red, brush.Color);
    }

    [Fact]
    public void ColorToBrush_Convert_ParsesHexColor()
    {
        var brush = (SolidColorBrush)ColorConv.Convert("#2196F3", typeof(Brush), null, Invariant);
        Assert.IsType<SolidColorBrush>(brush);
        Assert.True(brush.IsFrozen);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#2196F3"), brush.Color);
    }

    [Fact]
    public void ColorToBrush_Convert_InvalidColorFallsBackToTransparent()
    {
        var result = ColorConv.Convert("not-a-color", typeof(Brush), null, Invariant);
        Assert.Same(Brushes.Transparent, result);
    }

    [Fact]
    public void ColorToBrush_Convert_NullUsesDefaultAccent()
    {
        // null does not throw and does not fall back to Transparent; it resolves to
        // the default accent (#2196F3) via the null-coalescing default. Only a
        // genuinely unparseable string hits the catch and returns Transparent.
        var brush = (SolidColorBrush)ColorConv.Convert(null, typeof(Brush), null, Invariant);
        Assert.IsType<SolidColorBrush>(brush);
        Assert.True(brush.IsFrozen);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#2196F3"), brush.Color);
    }
}
