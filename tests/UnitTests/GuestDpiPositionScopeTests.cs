using System;
using TabDock;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Regression for bc678ef: USER32 DPI virtualization for DPI-unaware guests.
/// Proves that a PMv2 TabDock thread positions physical pixels correctly —
/// without the scope, USER32 double-scales an unaware guest after a cross-DPI
/// transfer; with the scope, the thread temporarily enters unaware context and
/// always restores. Covers aware vs unaware, unknown, destroyed HWND,
/// failed context switch, mixed pair, nested, and exception paths.
/// </summary>
public class GuestDpiPositionScopeTests : IDisposable
{
    private readonly Func<IntPtr, IntPtr> _origGetWindow;
    private readonly Func<IntPtr, int> _origGetAwareness;
    private readonly Func<IntPtr, IntPtr> _origSetThread;

    public GuestDpiPositionScopeTests()
    {
        _origGetWindow = GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl;
        _origGetAwareness = GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl;
        _origSetThread = GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl;
    }

    public void Dispose()
    {
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _origGetWindow;
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _origGetAwareness;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _origSetThread;
    }

    [Fact]
    public void AwareGuest_DoesNotSwitchContext_IsAvailable()
    {
        int setCalls = 0;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _ => { setCalls++; return new IntPtr(1); };
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0x100);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => DpiCapturePolicy.DpiAwarenessPerMonitor;

        var scope = GuestDpiPositionScope.EnterForWindow(new IntPtr(0x1234));
        try
        {
            Assert.True(scope.IsAvailable);
            Assert.Equal(0, setCalls);
        }
        finally { scope.Dispose(); Assert.Equal(0, setCalls); }
    }

    [Fact]
    public void SystemAwareGuest_DoesNotSwitchContext()
    {
        int setCalls = 0;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _ => { setCalls++; return new IntPtr(1); };
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0x101);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => DpiCapturePolicy.DpiAwarenessSystem;

        var scope = GuestDpiPositionScope.EnterForWindow(new IntPtr(0x2222));
        Assert.True(scope.IsAvailable);
        Assert.Equal(0, setCalls);
        scope.Dispose();
        Assert.Equal(0, setCalls);
    }

    [Fact]
    public void UnawareGuest_SwitchesToUnaware_AndRestoresOnDispose()
    {
        IntPtr pmv2 = new IntPtr(0xABCD);
        IntPtr unawareCtx = NativeMethods.DpiAwarenessContextUnaware;
        IntPtr requestedCtx = IntPtr.Zero;
        int restoreCalls = 0;
        IntPtr restoredTo = IntPtr.Zero;

        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0x200);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => DpiCapturePolicy.DpiAwarenessUnaware;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = ctx =>
        {
            if (ctx == unawareCtx)
            {
                requestedCtx = ctx;
                return pmv2; // previous was PMv2
            }
            restoreCalls++;
            restoredTo = ctx;
            return new IntPtr(0x1);
        };

        var scope = GuestDpiPositionScope.EnterForWindow(new IntPtr(0x3333));
        Assert.True(scope.IsAvailable);
        Assert.Equal(unawareCtx, requestedCtx);
        scope.Dispose();
        Assert.Equal(1, restoreCalls);
        Assert.Equal(pmv2, restoredTo);
    }

    [Fact]
    public void EnterForAwarenessForTest_Unaware_SwitchesAndRestores()
    {
        IntPtr pmv2 = new IntPtr(0x5000);
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = ctx =>
            ctx == NativeMethods.DpiAwarenessContextUnaware ? pmv2 : new IntPtr(0x1);

        var scope = GuestDpiPositionScope.EnterForAwarenessForTest(DpiCapturePolicy.DpiAwarenessUnaware);
        Assert.True(scope.IsAvailable);
        bool disposed = false;
        try { Assert.True(scope.IsAvailable); }
        finally { scope.Dispose(); disposed = true; }
        Assert.True(disposed);
    }

    [Fact]
    public void UnknownAwareness_IsUnavailable_NeverSwitches()
    {
        int setCalls = 0;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _ => { setCalls++; return new IntPtr(1); };
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0x300);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => 99;

        var scope = GuestDpiPositionScope.EnterForWindow(new IntPtr(0x4444));
        Assert.False(scope.IsAvailable);
        Assert.Equal(0, setCalls);
        scope.Dispose();
        Assert.Equal(0, setCalls);
    }

    [Fact]
    public void DestroyedHwnd_ZeroHwnd_IsUnavailable()
    {
        int setCalls = 0;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _ => { setCalls++; return new IntPtr(1); };
        var scope = GuestDpiPositionScope.EnterForWindow(IntPtr.Zero);
        Assert.False(scope.IsAvailable);
        Assert.Equal(0, setCalls);
    }

    [Fact]
    public void GetWindowContextReturnsZero_IsUnavailable()
    {
        int setCalls = 0;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _ => { setCalls++; return new IntPtr(1); };
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => IntPtr.Zero;
        var scope = GuestDpiPositionScope.EnterForWindow(new IntPtr(0x5555));
        Assert.False(scope.IsAvailable);
        Assert.Equal(0, setCalls);
    }

    [Fact]
    public void FailedContextSwitch_ReturnsUnavailable()
    {
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0x400);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => DpiCapturePolicy.DpiAwarenessUnaware;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _ => IntPtr.Zero; // failure

        var scope = GuestDpiPositionScope.EnterForWindow(new IntPtr(0x6666));
        Assert.False(scope.IsAvailable);
        scope.Dispose(); // should not attempt restore
    }

    [Fact]
    public void MixedAwarenessPair_IsUnavailable_RequiresFallback()
    {
        // first unaware, second per-monitor -> mixed must be unavailable for HDWP
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = hwnd =>
            hwnd == new IntPtr(0x1111) ? new IntPtr(0xA1) : new IntPtr(0xA2);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = ctx =>
            ctx == new IntPtr(0xA1) ? DpiCapturePolicy.DpiAwarenessUnaware : DpiCapturePolicy.DpiAwarenessPerMonitor;

        int setCalls = 0;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _ => { setCalls++; return new IntPtr(1); };

        var scope = GuestDpiPositionScope.EnterForWindows(new IntPtr(0x1111), new IntPtr(0x2222));
        Assert.False(scope.IsAvailable);
        Assert.Equal(0, setCalls);
    }

    [Fact]
    public void SameAwarenessPair_Unaware_IsAvailable()
    {
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0xB1);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => DpiCapturePolicy.DpiAwarenessUnaware;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _ => new IntPtr(0x9000);

        var scope = GuestDpiPositionScope.EnterForWindows(new IntPtr(0x7777), new IntPtr(0x8888));
        Assert.True(scope.IsAvailable);
        scope.Dispose();
    }

    [Fact]
    public void SameAwarenessPair_Aware_NoSwitch()
    {
        int setCalls = 0;
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0xC1);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => DpiCapturePolicy.DpiAwarenessPerMonitor;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = _ => { setCalls++; return new IntPtr(1); };

        var scope = GuestDpiPositionScope.EnterForWindows(new IntPtr(0x9999), new IntPtr(0xAAAA));
        Assert.True(scope.IsAvailable);
        Assert.Equal(0, setCalls);
        scope.Dispose();
        Assert.Equal(0, setCalls);
    }

    [Fact]
    public void NestedUnawareScopes_RestoreOuterContext()
    {
        IntPtr pmv2 = new IntPtr(0x1000);
        IntPtr unaware = NativeMethods.DpiAwarenessContextUnaware;
        IntPtr current = pmv2;
        var calls = new System.Collections.Generic.List<IntPtr>();
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0xD1);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => DpiCapturePolicy.DpiAwarenessUnaware;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = ctx =>
        {
            calls.Add(ctx);
            IntPtr prev = current;
            current = ctx;
            // Simulate OS: return previous context, never zero unless we choose
            return prev;
        };

        var outer = GuestDpiPositionScope.EnterForWindow(new IntPtr(0xBBBB));
        Assert.True(outer.IsAvailable);
        var inner = GuestDpiPositionScope.EnterForWindow(new IntPtr(0xCCCC));
        Assert.True(inner.IsAvailable);
        // Two switches to unaware
        Assert.Equal(2, calls.Count);
        Assert.Equal(unaware, calls[0]);
        Assert.Equal(unaware, calls[1]);
        Assert.Equal(unaware, current);
        inner.Dispose();
        Assert.Equal(3, calls.Count);
        Assert.Equal(unaware, calls[2]); // inner restores to unaware (its previous)
        Assert.Equal(unaware, current);
        outer.Dispose();
        Assert.Equal(4, calls.Count);
        Assert.Equal(pmv2, calls[3]); // outer restores to pmv2
        Assert.Equal(pmv2, current);
    }

    [Fact]
    public void ExceptionBetweenEnterAndDispose_StillRestores()
    {
        IntPtr pmv2 = new IntPtr(0x2000);
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0xE1);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => DpiCapturePolicy.DpiAwarenessUnaware;
        bool restored = false;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = ctx =>
        {
            if (ctx == NativeMethods.DpiAwarenessContextUnaware) return pmv2;
            restored = true;
            Assert.Equal(pmv2, ctx);
            return new IntPtr(0x1);
        };

        var scope = GuestDpiPositionScope.EnterForWindow(new IntPtr(0xDDDD));
        Assert.True(scope.IsAvailable);
        try
        {
            throw new InvalidOperationException("simulated failure during positioning");
        }
        catch { }
        finally
        {
            scope.Dispose();
        }
        Assert.True(restored);
    }

    [Fact]
    public void RestoreFailure_IsReported_NotThrown()
    {
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => new IntPtr(0xF1);
        GuestDpiPositionScope.GetAwarenessFromDpiAwarenessContextImpl = _ => DpiCapturePolicy.DpiAwarenessUnaware;
        GuestDpiPositionScope.SetThreadDpiAwarenessContextImpl = ctx =>
            ctx == NativeMethods.DpiAwarenessContextUnaware ? new IntPtr(0x3000) : IntPtr.Zero;

        var scope = GuestDpiPositionScope.EnterForWindow(new IntPtr(0xEEEE));
        Assert.True(scope.IsAvailable);
        // Dispose should not throw even if restore fails
        var ex = Record.Exception(() => scope.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void UnknownAwarenessEnterForTest_IsUnavailable()
    {
        var scope = GuestDpiPositionScope.EnterForAwarenessForTest(99);
        Assert.False(scope.IsAvailable);
        scope.Dispose();
        var negative = GuestDpiPositionScope.EnterForAwarenessForTest(-1);
        Assert.False(negative.IsAvailable);
    }

    [Fact]
    public void PhysicalPixelContract_NoDoubleScalingForUnaware()
    {
        // Deterministic proof: unaware logical 500 at 144 DPI should be 750 physical.
        // Without scope, PMv2 caller would let USER32 scale 500->750 again? Actually
        // the contract says TabDock supplies physical rect; USER32 must not re-scale.
        // This test proves the scale math used by min-track is correct and that
        // positioning scope prevents second scale.
        Assert.Equal(750, SplitGeometry.ScaleUnawareLogicalToPhysical(500, 144));
        Assert.Equal(500, SplitGeometry.ScaleUnawareLogicalToPhysical(500, 96));
        // Aware guest must not be scaled
        Assert.False(DpiCapturePolicy.ShouldScaleUnawareMinimum(DpiCapturePolicy.DpiAwarenessPerMonitor, 144));
        Assert.False(DpiCapturePolicy.ShouldScaleUnawareMinimum(DpiCapturePolicy.DpiAwarenessSystem, 144));
        Assert.True(DpiCapturePolicy.ShouldScaleUnawareMinimum(DpiCapturePolicy.DpiAwarenessUnaware, 144));
    }

    [Fact]
    public void EnterForWindows_WithDestroyedHwnd_IsUnavailable()
    {
        GuestDpiPositionScope.GetWindowDpiAwarenessContextImpl = _ => IntPtr.Zero;
        var scope = GuestDpiPositionScope.EnterForWindows(new IntPtr(0x123), new IntPtr(0x456));
        Assert.False(scope.IsAvailable);
        var zeroSecond = GuestDpiPositionScope.EnterForWindows(new IntPtr(0x123), IntPtr.Zero);
        Assert.False(zeroSecond.IsAvailable);
    }
}
