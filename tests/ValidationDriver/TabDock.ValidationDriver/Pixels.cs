using System;
using System.Runtime.InteropServices;

namespace TabDock.ValidationDriver;

/// <summary>
/// Screen-capture verification, ported from the CaptureReleaseTest harness
/// (tests/CaptureReleaseTest/TabDock.CaptureReleaseTest/Program.cs): BitBlt of the
/// DWM-composited screen region covering a window's client area, plus brightness /
/// inter-frame-variance / dominant-channel analysis.
/// </summary>
internal static class Pixels
{
    /// <summary>
    /// Captures the window's client area from the screen via BitBlt.
    /// This captures the DWM-composited result, which is more reliable for
    /// GPU-rendered children than PrintWindow. Pixels are 32-bit 0x00RRGGBB ints.
    /// </summary>
    public static int[]? CaptureHostScreenArea(IntPtr hostHwnd)
    {
        if (!NativeMethods.IsWindow(hostHwnd))
            return null;

        NativeMethods.GetClientRect(hostHwnd, out NativeMethods.RECT rc);
        int width = rc.Width;
        int height = rc.Height;
        if (width <= 0 || height <= 0)
            return null;

        var pt = new NativeMethods.POINT { x = rc.left, y = rc.top };
        if (!NativeMethods.ClientToScreen(hostHwnd, ref pt))
            return null;

        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
            return null;

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
                biSizeImage = (uint)(width * height * 4),
            }
        };

        IntPtr bits = IntPtr.Zero;
        IntPtr hbm = NativeMethods.CreateDIBSection(hdcScreen, ref bmi, NativeMethods.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
        if (hbm == IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            return null;
        }

        IntPtr hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
        if (hdcMem == IntPtr.Zero)
        {
            NativeMethods.DeleteObject(hbm);
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            return null;
        }

        NativeMethods.SelectObject(hdcMem, hbm);
        bool copied = NativeMethods.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, pt.x, pt.y, NativeMethods.SRCCOPY);

        int[] pixels = new int[width * height];
        if (copied && bits != IntPtr.Zero)
        {
            Marshal.Copy(bits, pixels, 0, pixels.Length);
        }

        NativeMethods.DeleteDC(hdcMem);
        NativeMethods.DeleteObject(hbm);
        NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);

        if (!copied)
            return null;
        return pixels;
    }

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
