using TabDock;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Headless regression coverage for the pure split-partition math. These
/// exercise the same deterministic contract the app's --selftest-geometry mode
/// uses, but as fast xUnit facts runnable without building/launching the
/// executable or sending any input.
/// </summary>
public class GeometryTests
{
    private static void AssertExactPartition(NativeMethods.RECT content)
    {
        var (left, right) = SplitGeometry.Partition(content);

        // Geometry: both panes share the content's vertical extent.
        Assert.Equal(content.left, left.left);
        Assert.Equal(content.top, left.top);
        Assert.Equal(content.top, right.top);
        Assert.Equal(content.bottom, left.bottom);
        Assert.Equal(content.bottom, right.bottom);

        // The panes abut exactly: no overlap, no gap.
        Assert.Equal(left.right, right.left);
        Assert.Equal(content.right, right.right);

        // No inverted/overflow rects.
        Assert.True(left.left <= left.right);
        Assert.True(right.left <= right.right);
        Assert.True(left.top <= left.bottom);

        // The two pane widths sum to the content width.
        Assert.Equal(content.Width, left.Width + right.Width);
    }

    [Fact]
    public void Partition_IsExactForPositiveRect()
    {
        AssertExactPartition(new NativeMethods.RECT { left = 0, top = 0, right = 1920, bottom = 1080 });
    }

    [Fact]
    public void Partition_AbutsOnOddWidth()
    {
        // 1921 is odd: LEFT = floor(1921/2) = 960, RIGHT = 961 (the extra pixel).
        var (left, right) = SplitGeometry.Partition(new NativeMethods.RECT { left = 0, top = 0, right = 1921, bottom = 1080 });
        Assert.Equal(960, left.Width);
        Assert.Equal(961, right.Width);
        Assert.Equal(left.right, right.left);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(799)]
    [InlineData(800)]
    [InlineData(801)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(1919)]
    [InlineData(1920)]
    [InlineData(1921)]
    public void Partition_HandlesEverySampledWidth(int width)
    {
        AssertExactPartition(new NativeMethods.RECT { left = 0, top = 0, right = width, bottom = 1080 });
    }

    [Fact]
    public void Partition_WorksForNegativeOrigins()
    {
        // Secondary-monitor / negative-coordinate content rects must still abut.
        AssertExactPartition(new NativeMethods.RECT { left = -1920, top = -1080, right = -1, bottom = -1 });
        AssertExactPartition(new NativeMethods.RECT { left = -500, top = 200, right = 640, bottom = 900 });
    }

    [Fact]
    public void MinContentWidth_SplitIsBindingConstraint()
    {
        // LEFT = floor(W/2) >= leftMin ; RIGHT = ceil(W/2) >= rightMin.
        Assert.Equal(2 * 200, SplitGeometry.MinContentWidth(split: true, leftMinW: 200, rightMinW: 200));
        // Asymmetric: the larger of (2*left, 2*right-1).
        Assert.Equal(2 * 500 - 1, SplitGeometry.MinContentWidth(split: true, leftMinW: 100, rightMinW: 500));
        Assert.Equal(2 * 643, SplitGeometry.MinContentWidth(split: true, leftMinW: 643, rightMinW: 1));
    }

    [Fact]
    public void MinContentWidth_NormalSpansFullWidth()
    {
        Assert.Equal(300, SplitGeometry.MinContentWidth(split: false, leftMinW: 300, rightMinW: 999));
        Assert.Equal(0, SplitGeometry.MinContentWidth(split: false, leftMinW: 0, rightMinW: 0));
    }

    [Fact]
    public void MinContentHeight_SplitNeedsTallerGuest()
    {
        Assert.Equal(500, SplitGeometry.MinContentHeight(split: true, leftMinH: 100, rightMinH: 500));
        Assert.Equal(100, SplitGeometry.MinContentHeight(split: false, leftMinH: 100, rightMinH: 500));
    }

    [Theory]
    [InlineData(500, 96u, 500)]
    [InlineData(500, 120u, 625)]
    [InlineData(500, 144u, 750)]
    [InlineData(500, 192u, 1000)]
    [InlineData(0, 120u, 0)]
    [InlineData(1, 96u, 1)]
    public void ScaleUnawareLogicalToPhysical_MatchesExpected(int value, uint dpi, int expected)
    {
        Assert.Equal(expected, SplitGeometry.ScaleUnawareLogicalToPhysical(value, dpi));
    }

    [Fact]
    public void ScaleUnawareLogicalToPhysical_NeverUnderestimates()
    {
        int[] values = { 1, 2, 7, 100, 499, 500, 643, 1024 };
        uint[] dpis = { 96, 120, 125, 144, 150, 192, 200, 240 };
        foreach (int v in values)
        {
            foreach (uint d in dpis)
            {
                int physical = SplitGeometry.ScaleUnawareLogicalToPhysical(v, d);
                double exact = v * d / (double)NativeMethods.USER_DEFAULT_SCREEN_DPI;
                Assert.True(physical >= exact, $"value={v} dpi={d}: physical {physical} < exact {exact}");
                if (d > 96)
                    Assert.True(physical > v, $"value={v} dpi={d}: {physical} not strictly greater than {v}");
            }
        }
    }

    [Fact]
    public void RunSelfTest_ReportsZeroFailures()
    {
        var (checks, failures) = SplitGeometry.RunSelfTest();
        Assert.True(checks > 0, "self-test should execute checks");
        Assert.Equal(0, failures);
    }
}
