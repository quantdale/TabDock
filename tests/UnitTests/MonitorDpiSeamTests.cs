using System;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former MonitorDpiSelfTest (Wave 4): deterministic
/// coverage for the monitor-DPI injection seam. The native helper remains a
/// real-display qualification; acceptance/refusal policy, center math, and the
/// PMv2 helper lifecycle are tested here without a monitor matrix.
/// </summary>
public class MonitorDpiSeamTests
{
    [Fact]
    public void ConversionConsumesTheInjectedProbeDpi()
    {
        var probe = new FakeProbe { Dpi = 144 };
        Assert.Equal(144u, probe.GetEffectiveDpi(new IntPtr(7)));
        Assert.Equal(750, SplitGeometry.ScaleUnawareLogicalToPhysical(500, probe.GetEffectiveDpi(new IntPtr(7))));
    }

    [Fact]
    public void FailedProbe_IsTreatedAsUnavailableAndNeverScales()
    {
        var probe = new FakeProbe { Dpi = 0 };
        Assert.Equal(0u, probe.GetEffectiveDpi(new IntPtr(7)));
        Assert.Equal(500, SplitGeometry.ScaleUnawareLogicalToPhysical(500, probe.GetEffectiveDpi(new IntPtr(7))));
    }

    [Theory]
    [InlineData(DpiCapturePolicy.DpiAwarenessUnaware, 144, true)]
    [InlineData(DpiCapturePolicy.DpiAwarenessPerMonitor, 144, true)]
    [InlineData(-1, 144, false)]
    [InlineData(DpiCapturePolicy.DpiAwarenessUnaware, 0, false)]
    public void HasKnownAwarenessAndMonitorDpi_FailsClosedOnUnknownEvidence(int awareness, uint dpi, bool expected)
    {
        Assert.Equal(expected, DpiCapturePolicy.HasKnownAwarenessAndMonitorDpi(awareness, dpi));
    }

    [Fact]
    public void AwareGuestMinimum_IsNeverScaled()
    {
        Assert.False(DpiCapturePolicy.ShouldScaleUnawareMinimum(DpiCapturePolicy.DpiAwarenessPerMonitor, 144));
        Assert.Equal(500, SplitGeometry.ScaleUnawareLogicalToPhysical(500, 96));
    }

    [Fact]
    public void TryGetCenter_PreservesNegativeMultiMonitorCoordinates()
    {
        var negativeMonitor = new NativeMethods.RECT { left = -3840, top = -120, right = 0, bottom = 2040 };
        Assert.True(MonitorDpiGeometry.TryGetCenter(in negativeMonitor, out int negativeX, out int negativeY));
        Assert.Equal(-1920, negativeX);
        Assert.Equal(960, negativeY);
    }

    [Fact]
    public void TryGetCenter_ExtremeCoordinatesDoNotOverflow()
    {
        var extremeMonitor = new NativeMethods.RECT
        {
            left = int.MinValue,
            top = int.MinValue,
            right = int.MaxValue,
            bottom = int.MaxValue,
        };
        Assert.True(MonitorDpiGeometry.TryGetCenter(in extremeMonitor, out int extremeX, out int extremeY));
        Assert.Equal(-1, extremeX);
        Assert.Equal(-1, extremeY);
    }

    [Fact]
    public void TryGetCenter_InvalidRectangleFailsClosed()
    {
        var invalidMonitor = new NativeMethods.RECT { left = 10, top = 10, right = 10, bottom = 9 };
        Assert.False(MonitorDpiGeometry.TryGetCenter(in invalidMonitor, out _, out _));
    }

    // ---- PMv2 helper lifecycle over the injectable native API -----------------

    [Fact]
    public void NativeLifecycle_SuccessPathCreatesHelperAtTargetCenterAndDestroysIt()
    {
        var api = new FakeNativeDpiApi();
        var probe = new NativeMonitorDpiProbe(api);

        Assert.Equal(144u, probe.GetEffectiveDpi(api.TargetMonitor));
        Assert.Equal(1, api.DestroyCount);
        Assert.Equal(1, api.RestoreContextCount);
        Assert.Equal(-960, api.LastCreateX);
        Assert.Equal(540, api.LastCreateY);
    }

    [Fact]
    public void NativeLifecycle_MonitorInfoFailure_RestoresContextWithoutDestroyingAnything()
    {
        var api = new FakeNativeDpiApi { FailMonitorInfo = true };
        var probe = new NativeMonitorDpiProbe(api);

        Assert.Equal(0u, probe.GetEffectiveDpi(api.TargetMonitor));
        Assert.Equal(0, api.DestroyCount);
        Assert.Equal(1, api.RestoreContextCount);
    }

    [Fact]
    public void NativeLifecycle_HelperContextMismatch_DestroysHelperAndRestoresContext()
    {
        var api = new FakeNativeDpiApi { HelperContextMatches = false };
        var probe = new NativeMonitorDpiProbe(api);

        Assert.Equal(0u, probe.GetEffectiveDpi(api.TargetMonitor));
        Assert.Equal(1, api.DestroyCount);
        Assert.Equal(1, api.RestoreContextCount);
    }

    [Fact]
    public void NativeLifecycle_HelperCreationFailure_RestoresContext()
    {
        var api = new FakeNativeDpiApi { CreateHelperFails = true };
        var probe = new NativeMonitorDpiProbe(api);

        Assert.Equal(0u, probe.GetEffectiveDpi(api.TargetMonitor));
        Assert.Equal(0, api.DestroyCount);
        Assert.Equal(1, api.RestoreContextCount);
    }

    [Fact]
    public void NativeLifecycle_MonitorDisappearance_FailsClosedWithCleanup()
    {
        var api = new FakeNativeDpiApi { MonitorAssociationMatches = false };
        var probe = new NativeMonitorDpiProbe(api);

        Assert.Equal(0u, probe.GetEffectiveDpi(api.TargetMonitor));
        Assert.Equal(1, api.DestroyCount);
        Assert.Equal(1, api.RestoreContextCount);
    }

    [Fact]
    public void NativeLifecycle_ZeroDpiStillCleansUp()
    {
        var api = new FakeNativeDpiApi { Dpi = 0 };
        var probe = new NativeMonitorDpiProbe(api);

        Assert.Equal(0u, probe.GetEffectiveDpi(api.TargetMonitor));
        Assert.Equal(1, api.DestroyCount);
        Assert.Equal(1, api.RestoreContextCount);
    }

    [Fact]
    public void NativeLifecycle_DestroyException_StillRestoresContextAndReturnsResult()
    {
        var api = new FakeNativeDpiApi { DestroyThrows = true };
        var probe = new NativeMonitorDpiProbe(api);

        Assert.Equal(144u, probe.GetEffectiveDpi(api.TargetMonitor));
        Assert.Equal(1, api.DestroyCount);
        Assert.Equal(1, api.RestoreContextCount);
    }

    [Fact]
    public void NativeLifecycle_UnavailableInitialContext_NeverAttemptsRestoreOrDestroy()
    {
        var api = new FakeNativeDpiApi { InitialContextUnavailable = true };
        var probe = new NativeMonitorDpiProbe(api);

        Assert.Equal(0u, probe.GetEffectiveDpi(api.TargetMonitor));
        Assert.Equal(0, api.DestroyCount);
        Assert.Equal(0, api.RestoreContextCount);
    }

    private sealed class FakeProbe : IMonitorDpiProbe
    {
        public uint Dpi { get; set; }

        public uint GetEffectiveDpi(IntPtr monitor) => monitor == IntPtr.Zero ? 0 : Dpi;
    }

    private sealed class FakeNativeDpiApi : IMonitorDpiNativeApi
    {
        public IntPtr TargetMonitor { get; } = new(7);
        private IntPtr Helper { get; } = new(9);
        private IntPtr PreviousContext { get; } = new(17);
        public bool FailMonitorInfo { get; set; }
        public bool HelperContextMatches { get; set; } = true;
        public bool CreateHelperFails { get; set; }
        public bool MonitorAssociationMatches { get; set; } = true;
        public bool InitialContextUnavailable { get; set; }
        public bool DestroyThrows { get; set; }
        public uint Dpi { get; set; } = 144;
        public int DestroyCount { get; private set; }
        public int RestoreContextCount { get; private set; }
        public int LastCreateX { get; private set; }
        public int LastCreateY { get; private set; }

        public IntPtr SetThreadDpiAwarenessContext(IntPtr context)
        {
            if (context == NativeMethods.DpiAwarenessContextPerMonitorV2)
                return InitialContextUnavailable ? IntPtr.Zero : PreviousContext;

            RestoreContextCount++;
            return PreviousContext;
        }

        public IntPtr GetThreadDpiAwarenessContext()
            => NativeMethods.DpiAwarenessContextPerMonitorV2;

        public bool AreDpiAwarenessContextsEqual(IntPtr left, IntPtr right)
            => HelperContextMatches || left == NativeMethods.DpiAwarenessContextPerMonitorV2;

        public bool GetMonitorInfo(IntPtr monitor, ref NativeMethods.MONITORINFO info)
        {
            if (FailMonitorInfo)
                return false;
            info.rcMonitor = new NativeMethods.RECT
            {
                left = -1920,
                top = 0,
                right = 0,
                bottom = 1080,
            };
            return monitor == TargetMonitor;
        }

        public IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu,
            IntPtr instance, IntPtr parameter)
        {
            LastCreateX = x;
            LastCreateY = y;
            return CreateHelperFails ? IntPtr.Zero : Helper;
        }

        public IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd)
            => HelperContextMatches ? NativeMethods.DpiAwarenessContextPerMonitorV2 : IntPtr.Zero;

        public IntPtr MonitorFromWindow(IntPtr hwnd, uint flags)
            => MonitorAssociationMatches ? TargetMonitor : new IntPtr(8);

        public uint GetDpiForWindow(IntPtr hwnd) => Dpi;

        public bool DestroyWindow(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
                DestroyCount++;
            if (DestroyThrows)
                throw new InvalidOperationException("synthetic DestroyWindow failure");
            return true;
        }

        public string FormatLastError() => "fake error";
    }
}
