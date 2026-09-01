using System;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualCaptureScopeTests
{
    private static VisualTargetIdentity Target(
        string hwnd = "0x10",
        uint processId = 10,
        long processStartTicks = 100)
        => new(hwnd, processId, 20, "TabDock.Guest", processStartTicks, "GuestWindow", "OwnedWindow");

    [Fact]
    public void HostClient_StaysInsideClientBoundary()
    {
        VisualCaptureScope scope = VisualCaptureScope.ForWindow(
            VisualCaptureScopeKind.HOST_CLIENT,
            Target(),
            VisualPrivacyClass.PRODUCT_OWNED,
            requestedRect: new VisualRect(0, 0, 1000, 1000));

        bool resolved = VisualScopeResolver.TryResolveWindow(
            scope,
            new VisualRect(100, 100, 900, 700),
            new VisualRect(120, 140, 880, 660),
            new VisualRect(0, 0, 1920, 1080),
            out VisualScopeResolution result,
            out string reason);

        Assert.True(resolved, reason);
        Assert.Equal(new VisualRect(0, 0, 1000, 1000), result.RequestedRect);
        Assert.Equal(new VisualRect(120, 140, 880, 660), result.ActualRect);
        Assert.True(result.WasClipped);
    }

    [Fact]
    public void TargetWithContext_ClipsInflatedRegionToMonitorWorkArea()
    {
        VisualCaptureScope scope = VisualCaptureScope.ForWindow(
            VisualCaptureScopeKind.TARGET_WITH_CONTEXT,
            Target(),
            VisualPrivacyClass.TEST_OWNED,
            contextMargin: 32);

        bool resolved = VisualScopeResolver.TryResolveWindow(
            scope,
            new VisualRect(100, 100, 500, 500),
            new VisualRect(110, 130, 490, 490),
            new VisualRect(0, 0, 400, 300),
            out VisualScopeResolution result,
            out string reason);

        Assert.True(resolved, reason);
        Assert.Equal(new VisualRect(68, 68, 532, 532), result.RequestedRect);
        Assert.Equal(new VisualRect(68, 68, 400, 300), result.ActualRect);
        Assert.True(result.WasClipped);
    }

    [Fact]
    public void Resolver_RejectsEmptyIntersectionAndUnexpectedContextMargin()
    {
        VisualCaptureScope outside = VisualCaptureScope.ForWindow(
            VisualCaptureScopeKind.GUEST_WINDOW,
            Target(),
            VisualPrivacyClass.TEST_OWNED,
            requestedRect: new VisualRect(900, 900, 1000, 1000));
        Assert.False(VisualScopeResolver.TryResolveWindow(
            outside,
            new VisualRect(100, 100, 500, 500),
            new VisualRect(110, 130, 490, 490),
            new VisualRect(0, 0, 1920, 1080),
            out _,
            out string outsideReason));
        Assert.Contains("intersect", outsideReason, StringComparison.Ordinal);

        VisualCaptureScope invalidMargin = VisualCaptureScope.ForWindow(
            VisualCaptureScopeKind.GUEST_WINDOW,
            Target(),
            VisualPrivacyClass.TEST_OWNED,
            contextMargin: 1);
        Assert.False(VisualScopeResolver.TryResolveWindow(
            invalidMargin,
            new VisualRect(100, 100, 500, 500),
            new VisualRect(110, 130, 490, 490),
            new VisualRect(0, 0, 1920, 1080),
            out _,
            out string marginReason));
        Assert.Contains("context-margin", marginReason, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityComparison_RejectsRecycledOrReclassifiedTargets()
    {
        VisualTargetIdentity expected = Target();
        Assert.True(VisualScopeResolver.SameStableIdentity(expected, Target()));
        Assert.False(VisualScopeResolver.SameStableIdentity(expected, Target(processStartTicks: 101)));
        Assert.False(VisualScopeResolver.SameStableIdentity(expected, Target(processId: 11)));
        Assert.False(VisualScopeResolver.SameStableIdentity(
            expected,
            expected with { Ownership = "Foreign" }));
    }

    [Fact]
    public void VirtualDesktopScope_RequiresExplicitAuthorization()
    {
        Assert.Throws<ArgumentException>(() => VisualCaptureScope.ForVirtualDesktop(authorized: false));
        VisualCaptureScope authorized = VisualCaptureScope.ForVirtualDesktop(authorized: true);
        Assert.False(VisualScopeResolver.TryResolveWindow(
            authorized,
            new VisualRect(0, 0, 10, 10),
            new VisualRect(0, 0, 10, 10),
            new VisualRect(0, 0, 10, 10),
            out _,
            out string reason));
        Assert.Contains("virtual-desktop", reason, StringComparison.Ordinal);
    }
}
