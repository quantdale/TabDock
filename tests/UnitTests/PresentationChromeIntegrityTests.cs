using System;
using Xunit;
using TabDock.Services;

namespace TabDock.UnitTests;

public sealed class PresentationChromeIntegrityTests
{
    [Fact]
    public void LocationChangeRoutedAsDirectCapturedWhenCaptured()
    {
        var input = new WinEventRoutingInput(
            EventType: NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            Hwnd: new IntPtr(0x123),
            IdObject: 0,
            IdChild: NativeMethods.CHILDID_SELF,
            DesktopWindow: new IntPtr(0x999),
            Captured: true);
        Assert.Equal(WinEventRoutingDecision.DirectCaptured, WinEventRoutingPolicy.Decide(input));
    }

    [Fact]
    public void LocationChangeIgnoredWhenNotCaptured()
    {
        var input = new WinEventRoutingInput(
            EventType: NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            Hwnd: new IntPtr(0x123),
            IdObject: 0,
            IdChild: 0,
            DesktopWindow: new IntPtr(0x999),
            Captured: false);
        Assert.Equal(WinEventRoutingDecision.Ignore, WinEventRoutingPolicy.Decide(input));
    }

    [Fact]
    public void LocationChangeWithNonZeroIdObjectIgnoredEvenIfCaptured()
    {
        var input = new WinEventRoutingInput(
            EventType: NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            Hwnd: new IntPtr(0x123),
            IdObject: NativeMethods.OBJID_CLIENT,
            IdChild: 0,
            DesktopWindow: new IntPtr(0x999),
            Captured: true);
        // Policy requires IdObject==0 and IdChild==0 for DirectCaptured unless desktopReorder.
        Assert.Equal(WinEventRoutingDecision.Ignore, WinEventRoutingPolicy.Decide(input));
    }

    [Fact]
    public void DesktopReorderStillRequiresCaptured()
    {
        var input = new WinEventRoutingInput(
            EventType: NativeMethods.EVENT_OBJECT_REORDER,
            Hwnd: new IntPtr(0x999),
            IdObject: NativeMethods.OBJID_CLIENT,
            IdChild: NativeMethods.CHILDID_SELF,
            DesktopWindow: new IntPtr(0x999),
            Captured: true);
        Assert.Equal(WinEventRoutingDecision.DesktopReorderCaptured, WinEventRoutingPolicy.Decide(input));
    }

    [Fact]
    public void DriftPolicyIsPureAndDeterministic()
    {
        var assigned = new NativeMethods.RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var observed = new NativeMethods.RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var a = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, false, false, true, true);
        var b = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, false, false, true, true);
        Assert.Equal(a, b);
        Assert.False(a.NeedsReconciliation);
    }

    [Fact]
    public void NoDriftWhenContainerMinimizedShouldNotBeVisible()
    {
        var assigned = new NativeMethods.RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var observed = new NativeMethods.RECT { left = 0, top = 0, right = 800, bottom = 600 };
        // shouldBeVisible false simulates container minimized (drift reconciler guards this, but policy also returns false)
        var eval = GuestPresentationDriftPolicy.EvaluateSingle(assigned, observed, isZoomed: true, isIconic: false, isVisible: true, shouldBeVisible: false);
        Assert.False(eval.NeedsReconciliation);
    }
}
