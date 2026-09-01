using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualOutcomeAggregationTests
{
    [Fact]
    public void VisualOk_DoesNotOverrideNativeFailure()
    {
        string nativeCode = "FAIL_PRODUCT";
        var visualVerdict = VisualReviewVerdict.VISUAL_OK;
        Assert.Equal("FAIL_PRODUCT", nativeCode);
        Assert.NotEqual("PASS", visualVerdict.ToString());
    }

    [Fact]
    public void VisualDefect_DoesNotCreateNativePass()
    {
        string nativeCode = "PASS";
        var visual = VisualReviewVerdict.VISUAL_DEFECT;
        Assert.Equal("PASS", nativeCode);
        Assert.Equal(VisualReviewVerdict.VISUAL_DEFECT, visual);
    }

    [Fact]
    public void ReviewUnavailable_IsNeutralWhenNotRequired()
    {
        var visual = VisualReviewVerdict.REVIEW_UNAVAILABLE;
        Assert.Equal(VisualReviewVerdict.REVIEW_UNAVAILABLE, visual);
    }
}
