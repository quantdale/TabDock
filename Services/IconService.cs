using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TabDock.Services;

/// <summary>
/// Extracts the executable path for a process and converts its icon into a WPF ImageSource.
/// </summary>
public sealed class IconService
{
    public string? GetProcessImagePath(uint pid)
    {
        return NativeMethods.GetProcessImagePath(pid);
    }

    public ImageSource? GetWindowIcon(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return null;

        string? exe = GetProcessImagePath(pid);
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return null;

        return GetFileIcon(exe);
    }

    public ImageSource? GetFileIcon(string exePath)
    {
        try
        {
            // Try the small icon first; fall back to large.
            IntPtr hSmall = IntPtr.Zero;
            IntPtr hLarge = IntPtr.Zero;
            uint count = NativeMethods.ExtractIconEx(exePath, 0, out hLarge, out hSmall, 1);
            IntPtr hIcon = hSmall != IntPtr.Zero ? hSmall : hLarge;
            if (hIcon == IntPtr.Zero)
                return null;

            var image = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();

            // Only delete the handle we actually used.
            if (hSmall != IntPtr.Zero && hIcon == hSmall && hLarge != IntPtr.Zero)
                NativeMethods.DestroyIcon(hLarge);
            else if (hLarge != IntPtr.Zero && hIcon == hLarge && hSmall != IntPtr.Zero)
                NativeMethods.DestroyIcon(hSmall);

            NativeMethods.DestroyIcon(hIcon);
            return image;
        }
        catch
        {
            return null;
        }
    }
}
