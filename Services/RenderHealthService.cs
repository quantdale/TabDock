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
        bool hasContent = !IsUniformZero(bits, width, height);

        NativeMethods.DeleteDC(hdcMem);
        NativeMethods.DeleteObject(hbm);
        NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);

        return printed && hasContent;
    }

    private static bool IsUniformZero(IntPtr bits, int width, int height)
    {
        int stride = width * 4;
        int total = stride * height;
        if (total <= 0)
            return true;

        // Fast scan of 32-bit pixels.
        int pixels = width * height;
        for (int i = 0; i < pixels; i++)
        {
            if (Marshal.ReadInt32(bits, i * 4) != 0)
                return false;
        }
        return true;
    }
}
