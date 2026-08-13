using System;

namespace TabDock.Services;

/// <summary>
/// Deterministic coverage for the monitor-DPI injection seam. The native
/// helper remains a real-display qualification; conversion policy is tested
/// here without depending on a particular monitor matrix.
/// </summary>
internal static class MonitorDpiSelfTest
{
    public static bool CoversProbeAndConversionSeam()
    {
        var probe = new FakeProbe { Dpi = 144 };
        bool targetDpiIsConsumed = probe.GetEffectiveDpi(new IntPtr(7)) == 144;
        bool conversionUsesProbeDpi = SplitGeometry.ScaleUnawareLogicalToPhysical(500, probe.GetEffectiveDpi(new IntPtr(7))) == 750;

        probe.Dpi = 0;
        bool failedProbeIsUnavailable = probe.GetEffectiveDpi(new IntPtr(7)) == 0
            && SplitGeometry.ScaleUnawareLogicalToPhysical(500, probe.GetEffectiveDpi(new IntPtr(7))) == 500;

        var negativeMonitor = new NativeMethods.RECT
        {
            left = -3840,
            top = -120,
            right = 0,
            bottom = 2040,
        };
        bool negativeCoordinatesArePreserved = MonitorDpiGeometry.TryGetCenter(
            in negativeMonitor, out int negativeX, out int negativeY)
            && negativeX == -1920
            && negativeY == 960;

        var extremeMonitor = new NativeMethods.RECT
        {
            left = int.MinValue,
            top = int.MinValue,
            right = int.MaxValue,
            bottom = int.MaxValue,
        };
        bool extremeCoordinatesDoNotOverflow = MonitorDpiGeometry.TryGetCenter(
            in extremeMonitor, out int extremeX, out int extremeY)
            && extremeX == -1
            && extremeY == -1;

        var invalidMonitor = new NativeMethods.RECT { left = 10, top = 10, right = 10, bottom = 9 };
        bool invalidRectangleFailsClosed = !MonitorDpiGeometry.TryGetCenter(
            in invalidMonitor, out _, out _);

        bool nativeLifecycleIsBounded = CoversNativeLifecycle();

        return targetDpiIsConsumed && conversionUsesProbeDpi && failedProbeIsUnavailable
            && negativeCoordinatesArePreserved
            && extremeCoordinatesDoNotOverflow
            && invalidRectangleFailsClosed
            && nativeLifecycleIsBounded;
    }

    private static bool CoversNativeLifecycle()
    {
        var api = new FakeNativeDpiApi();
        var probe = new NativeMonitorDpiProbe(api);
        bool success = probe.GetEffectiveDpi(api.TargetMonitor) == 144
            && api.DestroyCount == 1
            && api.RestoreContextCount == 1
            && api.LastCreateX == -960
            && api.LastCreateY == 540;

        api.Reset();
        api.FailMonitorInfo = true;
        bool monitorFailureRestoresContext = probe.GetEffectiveDpi(api.TargetMonitor) == 0
            && api.DestroyCount == 0
            && api.RestoreContextCount == 1;

        api.Reset();
        api.HelperContextMatches = false;
        bool helperAwarenessFailureDestroysAndRestores = probe.GetEffectiveDpi(api.TargetMonitor) == 0
            && api.DestroyCount == 1
            && api.RestoreContextCount == 1;

        api.Reset();
        api.CreateHelperFails = true;
        bool helperCreationFailureRestoresContext = probe.GetEffectiveDpi(api.TargetMonitor) == 0
            && api.DestroyCount == 0
            && api.RestoreContextCount == 1;

        api.Reset();
        api.MonitorAssociationMatches = false;
        bool monitorDisappearanceFailsClosed = probe.GetEffectiveDpi(api.TargetMonitor) == 0
            && api.DestroyCount == 1
            && api.RestoreContextCount == 1;

        api.Reset();
        api.Dpi = 0;
        bool zeroDpiStillCleansUp = probe.GetEffectiveDpi(api.TargetMonitor) == 0
            && api.DestroyCount == 1
            && api.RestoreContextCount == 1;

        api.Reset();
        api.DestroyThrows = true;
        bool destructionExceptionStillRestoresContext = probe.GetEffectiveDpi(api.TargetMonitor) == 144
            && api.DestroyCount == 1
            && api.RestoreContextCount == 1;

        api.Reset();
        api.InitialContextUnavailable = true;
        bool contextFailureDoesNotAttemptRestore = probe.GetEffectiveDpi(api.TargetMonitor) == 0
            && api.DestroyCount == 0
            && api.RestoreContextCount == 0;

        return success
            && monitorFailureRestoresContext
            && helperAwarenessFailureDestroysAndRestores
            && helperCreationFailureRestoresContext
            && monitorDisappearanceFailsClosed
            && zeroDpiStillCleansUp
            && destructionExceptionStillRestoresContext
            && contextFailureDoesNotAttemptRestore;
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

        public void Reset()
        {
            FailMonitorInfo = false;
            HelperContextMatches = true;
            CreateHelperFails = false;
            MonitorAssociationMatches = true;
            InitialContextUnavailable = false;
            DestroyThrows = false;
            Dpi = 144;
            DestroyCount = 0;
            RestoreContextCount = 0;
            LastCreateX = 0;
            LastCreateY = 0;
        }
    }
}
