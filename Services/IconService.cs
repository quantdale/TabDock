using System;
using System.Collections.Generic;
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
    // Keyed by exe path so repeat windows of the same executable (the common
    // case: multiple browser/terminal/IDE windows) don't re-run ExtractIconEx.
    // A cached null means extraction failed for that path; it is not retried
    // for the lifetime of this instance (AUDIT25-02). The comparer is
    // case-insensitive because Windows paths are: QueryFullProcessImageName can
    // report the same executable with different casing for different processes,
    // which used to miss the cache and re-extract the icon (PERF25-05).
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

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
        // A member captured from a process whose image path could not be read
        // carries an empty ExePath; there is nothing to extract, so skip the
        // interop call rather than caching a failure under an empty key.
        if (string.IsNullOrEmpty(exePath))
            return null;

        if (_iconCache.TryGetValue(exePath, out ImageSource? cached))
            return cached;

        ImageSource? result = ExtractFileIcon(exePath);
        _iconCache[exePath] = result;
        return result;
    }

    private static ImageSource? ExtractFileIcon(string exePath)
    {
        // Try the small icon first; fall back to large.
        IntPtr hSmall = IntPtr.Zero;
        IntPtr hLarge = IntPtr.Zero;
        try
        {
            uint count = NativeMethods.ExtractIconEx(exePath, 0, out hLarge, out hSmall, 1);
            IntPtr hIcon = hSmall != IntPtr.Zero ? hSmall : hLarge;
            if (hIcon == IntPtr.Zero)
                return null;

            var image = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            // Both handles must be destroyed on every path, including an
            // exception from CreateBitmapSourceFromHIcon — the previous
            // catch-and-return skipped this and leaked both GDI icon handles
            // on every failure (finding M7).
            if (hSmall != IntPtr.Zero)
                NativeMethods.DestroyIcon(hSmall);
            if (hLarge != IntPtr.Zero)
                NativeMethods.DestroyIcon(hLarge);
        }
    }
}
