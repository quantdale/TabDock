using System;
using System.Runtime.InteropServices;

namespace TabDock.ValidationDriver;

internal static class VisualTargetIdentityFactory
{
    public static VisualTargetIdentity From(WindowIdentity identity)
        => new(
            $"0x{identity.Hwnd.ToInt64():X}",
            identity.ProcessId,
            identity.WindowThreadId,
            identity.ClassName,
            identity.ProcessStartTimeUtcTicks,
            TestRunProvenance.WindowRole(identity.Hwnd),
            TestRunProvenance.GetOwnership(identity).ToString());
}

/// <summary>
/// Native implementation of the approved visual scopes. Every target is
/// re-identified immediately before and after acquisition; a recycled or
/// reclassified HWND is never emitted as evidence.
/// </summary>
internal sealed class VisualCaptureService : IVisualCaptureProvider
{
    private const int DefaultMaximumDimension = 16_384;
    private readonly int _maximumWidth;
    private readonly int _maximumHeight;
    private readonly bool _allowVirtualDesktop;

    public VisualCaptureService(
        int maximumWidth = DefaultMaximumDimension,
        int maximumHeight = DefaultMaximumDimension,
        bool allowVirtualDesktop = false)
    {
        if (maximumWidth <= 0 || maximumWidth > DefaultMaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        if (maximumHeight <= 0 || maximumHeight > DefaultMaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(maximumHeight));
        _maximumWidth = maximumWidth;
        _maximumHeight = maximumHeight;
        _allowVirtualDesktop = allowVirtualDesktop;
    }

    public bool TryCapture(
        VisualCaptureScope scope,
        out VisualFrame? frame,
        out string reason)
    {
        frame = null;
        reason = string.Empty;
        try
        {
            scope.Validate();
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }

        if (scope.Kind == VisualCaptureScopeKind.VIRTUAL_DESKTOP)
            return TryCaptureVirtualDesktop(scope, out frame, out reason);
        if (scope.Method == VisualCaptureMethod.SYNTHETIC)
        {
            reason = "synthetic-capture-method-requires-a-synthetic-backend";
            return false;
        }
        if (!TryParseHwnd(scope.Target!.Hwnd, out IntPtr hwnd))
        {
            reason = "target HWND is not a valid pointer value";
            return false;
        }
        if (!NativeMethods.IsWindow(hwnd))
        {
            reason = "target window no longer exists";
            return false;
        }
        if (!Discover.TryCaptureIdentity(hwnd, out WindowIdentity identity))
        {
            reason = "target identity could not be captured";
            return false;
        }
        if (!MatchesDeclaredTarget(scope.Target, identity, out reason))
            return false;
        if (!TestRunProvenance.TryValidateWindow(identity, out reason))
            return false;

        if (!TryGetWindowGeometry(hwnd, out VisualRect windowRect, out VisualRect clientRect, out reason))
            return false;
        if (!TryGetMonitorGeometry(hwnd, out VisualRect monitorWorkArea, out int dpi, out string monitorId, out reason))
            return false;
        if (!VisualScopeResolver.TryResolveWindow(
                scope,
                windowRect,
                clientRect,
                monitorWorkArea,
                out VisualScopeResolution resolution,
                out reason))
        {
            return false;
        }
        if (resolution.Width > _maximumWidth || resolution.Height > _maximumHeight)
        {
            reason = "resolved capture dimensions exceed the visual policy budget";
            return false;
        }
        if (scope.Method == VisualCaptureMethod.PRINT_WINDOW
            && !Contains(windowRect, resolution.ActualRect))
        {
            reason = "direct-window capture cannot represent context outside the target window";
            return false;
        }

        PixelCaptureResult? acquired = scope.Method switch
        {
            VisualCaptureMethod.SCREEN_COMPOSITION
                => Pixels.CaptureScreenRectDetailed(resolution.ActualRect),
            VisualCaptureMethod.PRINT_WINDOW
                => CaptureDirectWindow(hwnd, windowRect, resolution.ActualRect),
            _ => null,
        };
        if (acquired is not PixelCaptureResult capture)
        {
            reason = "native pixel acquisition failed";
            return false;
        }
        if (!VisualScopeResolver.SameStableIdentity(scope.Target, VisualTargetIdentityFactory.From(identity)))
        {
            reason = "target identity changed during capture setup";
            return false;
        }
        if (!Discover.TryCaptureIdentity(hwnd, out WindowIdentity afterCapture)
            || !VisualScopeResolver.SameStableIdentity(
                VisualTargetIdentityFactory.From(identity),
                VisualTargetIdentityFactory.From(afterCapture))
            || !TestRunProvenance.TryValidateWindow(afterCapture, out reason))
        {
            if (string.IsNullOrWhiteSpace(reason))
                reason = "target identity changed during capture";
            return false;
        }
        if (capture.Width != resolution.Width || capture.Height != resolution.Height
            || capture.ScreenRect != resolution.ActualRect)
        {
            reason = "native capture metadata does not match resolved scope";
            return false;
        }

        try
        {
            frame = new VisualFrame(
                capture.Width,
                capture.Height,
                capture.Pixels,
                DateTimeOffset.UtcNow,
                resolution.RequestedRect,
                resolution.ActualRect,
                scope.Method,
                scope.Kind,
                scope.Target,
                scope.Privacy,
                dpi,
                monitorId,
                sequence: 0,
                relativeMilliseconds: 0,
                captureDurationMilliseconds: capture.DurationMilliseconds);
            return true;
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private bool TryCaptureVirtualDesktop(
        VisualCaptureScope scope,
        out VisualFrame? frame,
        out string reason)
    {
        frame = null;
        reason = string.Empty;
        if (!_allowVirtualDesktop || !scope.VirtualDesktopAuthorization)
        {
            reason = "virtual-desktop-capture-is-disabled-by-policy";
            return false;
        }
        if (scope.Method != VisualCaptureMethod.SCREEN_COMPOSITION)
        {
            reason = "virtual-desktop-capture supports screen composition only";
            return false;
        }

        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0 || width > _maximumWidth || height > _maximumHeight)
        {
            reason = "virtual-desktop dimensions exceed the visual policy budget";
            return false;
        }

        var requested = new VisualRect(left, top, checked(left + width), checked(top + height));
        PixelCaptureResult? acquired = Pixels.CaptureScreenRectDetailed(requested);
        if (acquired is not PixelCaptureResult capture)
        {
            reason = "virtual-desktop pixel acquisition failed";
            return false;
        }
        try
        {
            frame = new VisualFrame(
                capture.Width,
                capture.Height,
                capture.Pixels,
                DateTimeOffset.UtcNow,
                requested,
                requested,
                scope.Method,
                scope.Kind,
                null,
                scope.Privacy,
                checked((int)Math.Max(NativeMethods.GetDpiForSystem(), NativeMethods.USER_DEFAULT_SCREEN_DPI)),
                "virtual-desktop",
                sequence: 0,
                relativeMilliseconds: 0,
                captureDurationMilliseconds: capture.DurationMilliseconds);
            return true;
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static bool TryGetWindowGeometry(
        IntPtr hwnd,
        out VisualRect windowRect,
        out VisualRect clientRect,
        out string reason)
    {
        windowRect = default;
        clientRect = default;
        reason = string.Empty;
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT nativeWindow)
            || !TryRect(nativeWindow, out windowRect))
        {
            reason = "target window rectangle could not be read";
            return false;
        }
        if (!NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT nativeClient))
        {
            reason = "target client rectangle could not be read";
            return false;
        }
        var origin = new NativeMethods.POINT { x = nativeClient.left, y = nativeClient.top };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
        {
            reason = "target client origin could not be mapped to screen coordinates";
            return false;
        }
        try
        {
            clientRect = new VisualRect(
                origin.x,
                origin.y,
                checked(origin.x + nativeClient.Width),
                checked(origin.y + nativeClient.Height));
            clientRect.Validate(nameof(clientRect));
            return true;
        }
        catch (ArgumentException)
        {
            reason = "target client rectangle is empty or invalid";
            return false;
        }
        catch (OverflowException)
        {
            reason = "target client rectangle exceeds screen coordinate bounds";
            return false;
        }
    }

    private static bool TryGetMonitorGeometry(
        IntPtr hwnd,
        out VisualRect workArea,
        out int dpi,
        out string monitorId,
        out string reason)
    {
        workArea = default;
        dpi = 0;
        monitorId = string.Empty;
        reason = string.Empty;
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            reason = "target monitor could not be determined";
            return false;
        }
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)
            || !TryRect(info.rcWork, out workArea))
        {
            reason = "target monitor work area could not be read";
            return false;
        }
        uint nativeDpi = NativeMethods.GetDpiForWindow(hwnd);
        dpi = nativeDpi == 0
            ? checked((int)Math.Max(NativeMethods.GetDpiForSystem(), NativeMethods.USER_DEFAULT_SCREEN_DPI))
            : checked((int)nativeDpi);
        monitorId = $"0x{monitor.ToInt64():X}";
        return true;
    }

    private static PixelCaptureResult? CaptureDirectWindow(
        IntPtr hwnd,
        VisualRect windowRect,
        VisualRect actualRect)
    {
        PixelCaptureResult? direct = Pixels.CaptureWindowViaPrintWindowDetailed(hwnd);
        if (direct is not PixelCaptureResult capture)
            return null;
        if (capture.ScreenRect == actualRect)
            return capture;
        if (!Contains(windowRect, actualRect) || capture.ScreenRect != windowRect)
            return null;

        int offsetX = actualRect.Left - capture.ScreenRect.Left;
        int offsetY = actualRect.Top - capture.ScreenRect.Top;
        int[] pixels = new int[checked(actualRect.Width * actualRect.Height)];
        for (int y = 0; y < actualRect.Height; y++)
        {
            Array.Copy(
                capture.Pixels,
                checked((offsetY + y) * capture.Width + offsetX),
                pixels,
                y * actualRect.Width,
                actualRect.Width);
        }
        return new PixelCaptureResult(
            actualRect.Width,
            actualRect.Height,
            actualRect,
            pixels,
            VisualCaptureMethod.PRINT_WINDOW,
            capture.DurationMilliseconds);
    }

    private static bool MatchesDeclaredTarget(
        VisualTargetIdentity declared,
        WindowIdentity current,
        out string reason)
    {
        VisualTargetIdentity observed = VisualTargetIdentityFactory.From(current);
        if (VisualScopeResolver.SameStableIdentity(declared, observed))
        {
            reason = string.Empty;
            return true;
        }
        reason = "declared target identity does not match the live window";
        return false;
    }

    private static bool TryParseHwnd(string value, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || !ulong.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out ulong raw)
            || raw > long.MaxValue)
        {
            return false;
        }
        hwnd = new IntPtr(unchecked((long)raw));
        return hwnd != IntPtr.Zero;
    }

    private static bool TryRect(NativeMethods.RECT native, out VisualRect rect)
    {
        rect = default;
        try
        {
            rect = new VisualRect(native.left, native.top, native.right, native.bottom);
            rect.Validate(nameof(rect));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool Contains(VisualRect outer, VisualRect inner)
        => inner.Left >= outer.Left && inner.Top >= outer.Top
            && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}
