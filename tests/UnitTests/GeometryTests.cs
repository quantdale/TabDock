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
    [InlineData(0, 96u, 0)]
    [InlineData(0, 0u, 0)]
    [InlineData(-5, 120u, -5)]
    [InlineData(500, 0u, 500)]
    [InlineData(10, 120u, 13)]
    [InlineData(1, 240u, 3)]
    [InlineData(100, 120u, 125)]
    [InlineData(99, 120u, 124)]
    [InlineData(643, 120u, 804)]
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

    /// <summary>
    /// The authoritative partition qualification (Wave 4: migrated from the
    /// former SplitGeometry.RunSelfTest and its --selftest-geometry executable
    /// mode). An exhaustive matrix — every width 1..4096, representative
    /// heights, positive/zero/negative origins — plus targeted odd widths, a
    /// seeded fuzz sweep (100,000 rects, fixed seed 20260810), and the
    /// size-constraint minimality math. Asserts exact coverage, zero overlap,
    /// zero gap, and no overflow for every rect.
    /// </summary>
    [Fact]
    public void Partition_ExhaustiveMatrixSeededFuzzAndConstraintMath_AllInvariantsHold()
    {
        void VerifyRect(NativeMethods.RECT content)
        {
            var (left, right) = SplitGeometry.Partition(content);

            Assert.Equal(content.left, left.left);
            Assert.Equal(content.top, left.top);
            Assert.Equal(content.top, right.top);
            Assert.Equal(content.bottom, left.bottom);
            Assert.Equal(content.bottom, right.bottom);
            Assert.Equal(left.right, right.left);
            Assert.Equal(content.right, right.right);
            Assert.True(left.Width >= 0);
            Assert.True(right.Width >= 0);
            Assert.Equal(content.Width, left.Width + right.Width);
            // No overflow / wrap: all edges of both panes stay in range when
            // the content rect itself is.
            Assert.True(left.left <= left.right);
            Assert.True(right.left <= right.right);
            Assert.True(left.top <= left.bottom);
        }

        // --- Matrix: every width 1..4096, representative heights, origins
        // covering positive, zero, and negative. ---
        int[] heights = { 1, 2, 3, 10, 100, 1000, 4096 };
        int[] originsX = { -1920, -1, 0, 1, 1920, 5000 };
        int[] originsY = { -1080, -1, 0, 1, 1080, 3000 };
        for (int w = 1; w <= 4096; w++)
        {
            foreach (int h in heights)
            {
                foreach (int ox in originsX)
                {
                    foreach (int oy in originsY)
                    {
                        try { VerifyRect(new NativeMethods.RECT { left = ox, top = oy, right = ox + w, bottom = oy + h }); }
                        catch (Exception ex) { throw new Xunit.Sdk.XunitException($"matrix w={w} h={h} origin=({ox},{oy}): {ex.Message}"); }
                    }
                }
            }
        }

        // --- Targeted odd widths at positive and negative origins. ---
        foreach (int w in new[] { 799, 800, 801, 1023, 1024, 1025, 1919, 1920, 1921 })
        {
            VerifyRect(new NativeMethods.RECT { left = 0, top = 0, right = w, bottom = 1080 });
            VerifyRect(new NativeMethods.RECT { left = -1920, top = -1080, right = -1920 + w, bottom = 0 });
        }

        // --- Seeded fuzz: deterministic across machines. ---
        var rng = new Random(20260810);
        for (int i = 0; i < 100_000; i++)
        {
            int ox = rng.Next(-10000, 10001);
            int oy = rng.Next(-10000, 10001);
            int w = rng.Next(1, 10001);
            int h = rng.Next(1, 10001);
            try { VerifyRect(new NativeMethods.RECT { left = ox, top = oy, right = ox + w, bottom = oy + h }); }
            catch (Exception ex) { throw new Xunit.Sdk.XunitException($"fuzz#{i} x={ox} y={oy} w={w} h={h}: {ex.Message}"); }
        }

        // --- Size-constraint math: MinContentWidth must be the smallest width
        // at which the exact partition still fits both guests, and no width
        // below it may fit. ---
        int[] minWidths = { 0, 1, 100, 200, 500, 643, 1024 };
        foreach (int lm in minWidths)
        {
            foreach (int rm in minWidths)
            {
                int minW = SplitGeometry.MinContentWidth(split: true, lm, rm);
                Assert.True(minW / 2 >= lm, $"split lm={lm} rm={rm} minW={minW}: LEFT below minimum");
                Assert.True(minW - (minW / 2) >= rm, $"split lm={lm} rm={rm} minW={minW}: RIGHT below minimum");
                if (minW > 0)
                {
                    int leftAtMinus1 = (minW - 1) / 2;
                    int rightAtMinus1 = (minW - 1) - leftAtMinus1;
                    Assert.False(
                        leftAtMinus1 >= lm && rightAtMinus1 >= rm,
                        $"split lm={lm} rm={rm}: width minW-1 still fits both panes");
                }

                Assert.Equal(lm, SplitGeometry.MinContentWidth(split: false, lm, rm));
                Assert.Equal(Math.Max(lm, rm), SplitGeometry.MinContentHeight(split: true, lm, rm));
                Assert.Equal(lm, SplitGeometry.MinContentHeight(split: false, lm, rm));
            }
        }
    }
}
