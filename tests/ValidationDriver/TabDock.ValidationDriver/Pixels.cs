using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace TabDock.ValidationDriver;

/// <summary>
/// Screen-capture verification, ported from the CaptureReleaseTest harness
/// (tests/CaptureReleaseTest/TabDock.CaptureReleaseTest/Program.cs): BitBlt of the
/// DWM-composited screen region covering a window's client area, plus brightness /
/// inter-frame-variance / dominant-channel analysis.
/// </summary>
internal readonly record struct PixelCaptureResult(
    int Width,
    int Height,
    VisualRect ScreenRect,
    int[] Pixels,
    VisualCaptureMethod Method,
    long DurationMilliseconds);

internal static class Pixels
{
    /// <summary>
    /// Captures the window's client area from the screen via BitBlt.
    /// This captures the DWM-composited result, which is more reliable for
    /// GPU-rendered children than PrintWindow. Pixels are 32-bit 0x00RRGGBB ints.
    /// </summary>
    public static int[]? CaptureHostScreenArea(IntPtr hostHwnd)
        => CaptureHostScreenAreaDetailed(hostHwnd)?.Pixels;

    /// <summary>Captures the DWM-composited client area and its physical screen rectangle.</summary>
    public static PixelCaptureResult? CaptureHostScreenAreaDetailed(IntPtr hostHwnd)
    {
        if (!NativeMethods.IsWindow(hostHwnd)
            || !NativeMethods.GetClientRect(hostHwnd, out NativeMethods.RECT client))
        {
            return null;
        }

        int width = client.Width;
        int height = client.Height;
        if (!IsCaptureSizeValid(width, height))
            return null;

        var origin = new NativeMethods.POINT { x = client.left, y = client.top };
        if (!NativeMethods.ClientToScreen(hostHwnd, ref origin))
            return null;

        return CaptureBitmap(
            width,
            height,
            new VisualRect(origin.x, origin.y, origin.x + width, origin.y + height),
            VisualCaptureMethod.SCREEN_COMPOSITION,
            printWindowHwnd: IntPtr.Zero);
    }

    /// <summary>Captures an arbitrary approved physical screen rectangle via DWM composition.</summary>
    public static PixelCaptureResult? CaptureScreenRectDetailed(VisualRect screenRect)
    {
        if (!screenRect.IsPositive || !IsCaptureSizeValid(screenRect.Width, screenRect.Height))
            return null;
        return CaptureBitmap(
            screenRect.Width,
            screenRect.Height,
            screenRect,
            VisualCaptureMethod.SCREEN_COMPOSITION,
            printWindowHwnd: IntPtr.Zero);
    }

    /// <summary>
    /// Captures a window's own rendered content directly via PrintWindow
    /// (PW_RENDERFULLCONTENT), reading the window's own back-buffer instead of
    /// the DWM-composited screen region. Pixels are 32-bit 0x00RRGGBB ints,
    /// same layout as <see cref="CaptureHostScreenArea"/>.
    /// </summary>
    public static int[]? CaptureWindowViaPrintWindow(IntPtr hwnd)
        => CaptureWindowViaPrintWindowDetailed(hwnd)?.Pixels;

    /// <summary>Captures a window's direct rendering and its physical window rectangle.</summary>
    public static PixelCaptureResult? CaptureWindowViaPrintWindowDetailed(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)
            || !NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
        {
            return null;
        }

        int width = rect.Width;
        int height = rect.Height;
        if (!IsCaptureSizeValid(width, height))
            return null;

        return CaptureBitmap(
            width,
            height,
            new VisualRect(rect.left, rect.top, rect.right, rect.bottom),
            VisualCaptureMethod.PRINT_WINDOW,
            hwnd);
    }

    private static PixelCaptureResult? CaptureBitmap(
        int width,
        int height,
        VisualRect screenRect,
        VisualCaptureMethod method,
        IntPtr printWindowHwnd)
    {
        var stopwatch = Stopwatch.StartNew();
        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
            return null;

        IntPtr hbm = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            var bmi = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                    biSizeImage = checked((uint)(width * height * 4)),
                }
            };

            hbm = NativeMethods.CreateDIBSection(
                hdcScreen,
                ref bmi,
                NativeMethods.DIB_RGB_COLORS,
                out IntPtr bits,
                IntPtr.Zero,
                0);
            if (hbm == IntPtr.Zero || bits == IntPtr.Zero)
                return null;

            hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero)
                return null;

            previous = NativeMethods.SelectObject(hdcMem, hbm);
            if (previous == IntPtr.Zero)
                return null;

            bool captured = printWindowHwnd == IntPtr.Zero
                ? NativeMethods.BitBlt(
                    hdcMem,
                    0,
                    0,
                    width,
                    height,
                    hdcScreen,
                    screenRect.Left,
                    screenRect.Top,
                    NativeMethods.SRCCOPY)
                : NativeMethods.PrintWindow(
                    printWindowHwnd,
                    hdcMem,
                    NativeMethods.PW_RENDERFULLCONTENT);
            if (!captured)
                return null;

            var pixels = new int[checked(width * height)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            stopwatch.Stop();
            return new PixelCaptureResult(
                width,
                height,
                screenRect,
                pixels,
                method,
                stopwatch.ElapsedMilliseconds);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero && hdcMem != IntPtr.Zero)
                NativeMethods.SelectObject(hdcMem, previous);
            if (hdcMem != IntPtr.Zero)
                NativeMethods.DeleteDC(hdcMem);
            if (hbm != IntPtr.Zero)
                NativeMethods.DeleteObject(hbm);
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    private static bool IsCaptureSizeValid(int width, int height)
        => width > 0 && height > 0 && width <= 16_384 && height <= 16_384;

    /// <summary>Average per-pixel-channel brightness (0..255). Below ~1.0 means black/blank.</summary>
    public static double ComputeAvgBrightness(int[] pixels)
    {
        if (pixels.Length == 0)
            return 0;

        long total = 0;
        foreach (int p in pixels)
        {
            total += (p & 0xFF) + ((p >> 8) & 0xFF) + ((p >> 16) & 0xFF);
        }
        return total / (double)(pixels.Length * 3);
    }

    /// <summary>
    /// Average per-pixel-channel absolute difference between two frames.
    /// Above ~0.005 means visible change between frames (a blinking cursor is enough).
    /// </summary>
    public static double ComputeAvgFrameDiff(int[] frame0, int[] frame1)
    {
        if (frame0.Length != frame1.Length || frame0.Length == 0)
            return -1;

        long diff = 0;
        int len = frame0.Length;
        for (int i = 0; i < len; i++)
        {
            int a = frame0[i];
            int b = frame1[i];
            diff += Math.Abs((a & 0xFF) - (b & 0xFF))
                  + Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF))
                  + Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF));
        }
        return diff / (double)(len * 3);
    }

    /// <summary>
    /// Which color channel dominates the frame: 'r', 'g', or 'b'.
    /// A 32bpp GDI DIB section is BGRA in memory, so as a little-endian int:
    /// blue = bits 0-7, green = bits 8-15, red = bits 16-23.
    /// </summary>
    public static char DominantChannel(int[] pixels)
    {
        long r = 0, g = 0, b = 0;
        foreach (int p in pixels)
        {
            b += p & 0xFF;
            g += (p >> 8) & 0xFF;
            r += (p >> 16) & 0xFF;
        }
        if (r >= g && r >= b)
            return 'r';
        return g >= b ? 'g' : 'b';
    }
}
