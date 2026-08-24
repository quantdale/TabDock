using System;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

public sealed class WinEventRoutingPolicyTests
{
    private static readonly IntPtr Desktop = new(0xD);
    private static readonly IntPtr Window = new(0x99);

    [Fact]
    public void DirectCapturedEvent_IsAdmitted()
    {
        var input = new WinEventRoutingInput(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            Window,
            0,
            0,
            Desktop,
            Captured: true);

        Assert.Equal(WinEventRoutingDecision.DirectCaptured, WinEventRoutingPolicy.Decide(input));
    }

    [Fact]
    public void ChildObjectEvent_IsIgnoredBeforeMembershipAdmission()
    {
        var input = new WinEventRoutingInput(
            NativeMethods.EVENT_OBJECT_NAMECHANGE,
            Window,
            NativeMethods.OBJID_CLIENT,
            NativeMethods.CHILDID_SELF,
            Desktop,
            Captured: true);

        Assert.Equal(WinEventRoutingDecision.Ignore, WinEventRoutingPolicy.Decide(input));
    }

    [Fact]
    public void DesktopReorder_RequiresCapturedForeground()
    {
        var captured = new WinEventRoutingInput(
            NativeMethods.EVENT_OBJECT_REORDER,
            Desktop,
            NativeMethods.OBJID_CLIENT,
            NativeMethods.CHILDID_SELF,
            Desktop,
            Captured: true);
        var foreign = captured with { Captured = false };

        Assert.Equal(WinEventRoutingDecision.DesktopReorderCaptured, WinEventRoutingPolicy.Decide(captured));
        Assert.Equal(WinEventRoutingDecision.Ignore, WinEventRoutingPolicy.Decide(foreign));
    }

    [Fact]
    public void ForeignAndNullEvents_AreIgnored()
    {
        var foreign = new WinEventRoutingInput(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            Window,
            0,
            0,
            Desktop,
            Captured: false);
        var nullWindow = foreign with { Hwnd = IntPtr.Zero, Captured = true };

        Assert.Equal(WinEventRoutingDecision.Ignore, WinEventRoutingPolicy.Decide(foreign));
        Assert.Equal(WinEventRoutingDecision.Ignore, WinEventRoutingPolicy.Decide(nullWindow));
    }
}
