using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

public class NativePresentationRestorePolicyTests
{
    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom) => new()
    {
        left = left,
        top = top,
        right = right,
        bottom = bottom,
    };

    [Fact]
    public void BorderlessGeometryMismatch_RequiresRestore()
    {
        Assert.True(NativePresentationRestorePolicy.NeedsRestore(
            Rect(0, 0, 1920, 1200),
            Rect(96, 196, 1321, 896),
            style: 0,
            isIconic: false,
            isZoomed: false));
    }

    [Fact]
    public void CaptionedGeometryMismatch_DoesNotRequireFullscreenRestore()
    {
        Assert.False(NativePresentationRestorePolicy.NeedsRestore(
            Rect(0, 0, 1920, 1200),
            Rect(96, 196, 1321, 896),
            style: NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME,
            isIconic: false,
            isZoomed: false));
    }

    [Fact]
    public void MatchingBorderlessGeometry_DoesNotRequireRestore()
    {
        Assert.False(NativePresentationRestorePolicy.NeedsRestore(
            Rect(96, 196, 1321, 896),
            Rect(96, 196, 1321, 896),
            style: 0,
            isIconic: false,
            isZoomed: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IconicOrZoomedState_RequiresRestoreRegardlessOfStyle(bool isIconic, bool isZoomed)
    {
        Assert.True(NativePresentationRestorePolicy.NeedsRestore(
            Rect(96, 196, 1321, 896),
            Rect(96, 196, 1321, 896),
            style: NativeMethods.WS_CAPTION,
            isIconic,
            isZoomed));
    }
}
