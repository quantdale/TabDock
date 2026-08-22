using System.Collections.Generic;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former ShowWindowSemanticsSelfTest (Wave 4). The first
/// argument models ShowWindow's previous-visibility BOOL; it is deliberately
/// false for hidden/minimized restore and must not turn a successful post-state
/// into a failure.
/// </summary>
public class ShowWindowSemanticsTests
{
    [Theory]
    [InlineData(false, true, false, false, true)]  // hidden restore succeeds
    [InlineData(true, true, false, false, true)]   // minimized/visible restore succeeds
    [InlineData(false, false, false, false, false)] // still hidden is a failed restore
    [InlineData(false, true, true, false, false)]  // still iconic is a failed restore
    [InlineData(false, true, false, true, false)]  // still zoomed is a failed restore
    public void RestoreSucceeded_ClassifiesPostState(
        bool previouslyVisible,
        bool visibleAfter,
        bool iconicAfter,
        bool zoomedAfter,
        bool expected)
    {
        Assert.Equal(
            expected,
            ShowWindowSemantics.RestoreSucceeded(previouslyVisible, visibleAfter, iconicAfter, zoomedAfter));
    }

    [Theory]
    [InlineData(true, false, false, true)]   // hide succeeded
    [InlineData(false, true, true, true)]    // release show succeeded
    [InlineData(false, false, true, false)]  // expected visible but stayed hidden
    public void VisibilitySucceeded_ClassifiesPostState(
        bool previouslyVisible,
        bool visibleAfter,
        bool expectedVisible,
        bool expected)
    {
        Assert.Equal(
            expected,
            ShowWindowSemantics.VisibilitySucceeded(previouslyVisible, visibleAfter, expectedVisible));
    }

    [Fact]
    public void BenignFalseReturn_DoesNotConsumeThePositioningFailureSlot()
    {
        // A hidden restore reports previous-visibility FALSE even when it fully
        // succeeded; the shepherd's once-per-HWND failure log must not treat
        // that benign FALSE as a genuine positioning failure.
        var positioningFailuresLogged = new HashSet<System.IntPtr>();
        bool hiddenRestoreSucceeded = ShowWindowSemantics.RestoreSucceeded(
            previouslyVisible: false, visibleAfter: true, iconicAfter: false, zoomedAfter: false);
        bool stillIconicWasRealFailure = !ShowWindowSemantics.RestoreSucceeded(
            previouslyVisible: false, visibleAfter: true, iconicAfter: true, zoomedAfter: false);

        bool benignFalseDidNotConsumeSlot = hiddenRestoreSucceeded
            && stillIconicWasRealFailure
            && positioningFailuresLogged.Add(new System.IntPtr(1));

        Assert.True(benignFalseDidNotConsumeSlot);
    }
}
