using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Uses PrintWindow to detect windows that have stopped rendering inside the host.
/// A window is flagged unhealthy only if PrintWindow fails or the captured bitmap is blank.
/// The captured window itself is never destroyed.
/// </summary>
public sealed class RenderHealthService
{
    private readonly DpiService _dpi;

    public RenderHealthService(DpiService dpi)
    {
        _dpi = dpi;
    }

    public async Task<bool> CheckHealthAsync(CapturedWindow window, IntPtr hostHwnd)
    {
        await Task.Delay(800).ConfigureAwait(false);

        if (!NativeMethods.IsWindow(window.Hwnd))
            return false;

        // A hidden window (e.g. an inactive tab hidden by tab switching) cannot
        // be judged: PrintWindow output for it is meaningless and would produce
        // a false "unhealthy" verdict and a spurious auto-release.
        if (!NativeMethods.IsWindowVisible(window.Hwnd))
            return true;

        NativeMethods.GetClientRect(hostHwnd, out NativeMethods.RECT rc);
        int width = rc.Width;
        int height = rc.Height;
        if (width <= 0 || height <= 0)
            return true; // Nothing to measure.

        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
            return false;

        var bmi = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
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
            return false;
        }

        IntPtr hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
        if (hdcMem == IntPtr.Zero)
        {
            NativeMethods.DeleteObject(hbm);
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            return false;
        }

        NativeMethods.SelectObject(hdcMem, hbm);
        bool printed = NativeMethods.PrintWindow(window.Hwnd, hdcMem, NativeMethods.PW_RENDERFULLCONTENT);
        bool hasContent = !IsUniformNearBlack(bits, width, height);

        NativeMethods.DeleteDC(hdcMem);
        NativeMethods.DeleteObject(hbm);
        NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);

        return printed && hasContent;
    }

    /// <summary>
    /// True when every pixel is near-black in RGB. The alpha byte is masked off:
    /// PrintWindow with PW_RENDERFULLCONTENT produces opaque output, so a dead
    /// black surface reads as 0xFF000000 per pixel, not 0x00000000 — comparing
    /// the full 32-bit value against zero can never flag it.
    /// </summary>
    private static bool IsUniformNearBlack(IntPtr bits, int width, int height)
    {
        int pixels = width * height;
        if (pixels <= 0)
            return true;

        const int channelThreshold = 10; // per-channel headroom for near-black noise
        for (int i = 0; i < pixels; i++)
        {
            int px = Marshal.ReadInt32(bits, i * 4);
            if (((px >> 16) & 0xFF) > channelThreshold ||
                ((px >> 8) & 0xFF) > channelThreshold ||
                (px & 0xFF) > channelThreshold)
            {
                return false;
            }
        }
        return true;
    }
}
