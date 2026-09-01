using System;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Locks the ValidationDriver pixel representation before the visual evidence
/// layer adds metadata and artifact projections around it.
/// </summary>
public sealed class VisualPixelsRegressionTests
{
    [Fact]
    public void Metrics_UseExistingBgraIntegerChannelOrder()
    {
        int[] red = { unchecked((int)0x00FF0000), unchecked((int)0x00FF0000) };
        int[] green = { 0x0000FF00, 0x0000FF00 };
        int[] blue = { 0x000000FF, 0x000000FF };

        Assert.Equal('r', Pixels.DominantChannel(red));
        Assert.Equal('g', Pixels.DominantChannel(green));
        Assert.Equal('b', Pixels.DominantChannel(blue));
        Assert.Equal(85d, Pixels.ComputeAvgBrightness(red), precision: 10);
    }

    [Fact]
    public void FrameDiff_ReportsNormalizedChannelDifferenceAndRejectsMismatchedFrames()
    {
        int[] first = { 0x00000000, 0x00000000 };
        int[] second = { 0x00FFFFFF, 0x00000000 };

        Assert.Equal(255d / 2d, Pixels.ComputeAvgFrameDiff(first, second), precision: 10);
        Assert.Equal(-1d, Pixels.ComputeAvgFrameDiff(first, new[] { 0x00000000 }));
        Assert.Equal(-1d, Pixels.ComputeAvgFrameDiff(Array.Empty<int>(), Array.Empty<int>()));
    }

    [Fact]
    public void EmptyMetrics_PreserveExistingNeutralResults()
    {
        Assert.Equal(0d, Pixels.ComputeAvgBrightness(Array.Empty<int>()));
        Assert.Equal('r', Pixels.DominantChannel(Array.Empty<int>()));
    }

    [Fact]
    public void NativeCapture_RejectsNullAndInvalidWindowHandles()
    {
        Assert.Null(Pixels.CaptureHostScreenArea(IntPtr.Zero));
        Assert.Null(Pixels.CaptureWindowViaPrintWindow(IntPtr.Zero));
    }
}
