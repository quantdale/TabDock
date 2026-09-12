using System;
using System.Windows;
using System.Windows.Interop;

namespace TabDock.Infrastructure;

/// <summary>
/// Applies TabDock's dark window chrome (caption, caption text, and window
/// border colors) to a standard WPF window through DWM. Every attribute is
/// best-effort: unsupported attributes are rejected by the OS and the window
/// keeps its system default rather than failing creation. The container uses a
/// custom <see cref="System.Windows.Shell.WindowChrome"/> instead and does not
/// need this.
/// </summary>
public static class WindowChromeTheme
{
    public static void ApplyDarkChrome(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int dark = 1;
        _ = NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        int caption = ToColorRef(0x0C, 0x0F, 0x14); // TdChromeBrush #0C0F14
        int text = ToColorRef(0xF7, 0xF8, 0xFA);    // TdTextBrush #F7F8FA
        int border = ToColorRef(0x27, 0x2D, 0x38);  // TdBorderBrush #272D38
        _ = NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        _ = NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref text, sizeof(int));
        _ = NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    /// <summary>Packs an RGB triple into the COLORREF layout (0x00BBGGRR) DWM expects.</summary>
    private static int ToColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
}
