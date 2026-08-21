using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
    private readonly LoggingService _log;
    private readonly object _cacheGate = new();
    private readonly Func<string, ImageSource?>? _testExtractor;

    public IconService(LoggingService log)
        : this(log, testExtractor: null)
    {
    }

    internal IconService(LoggingService log, Func<string, ImageSource?>? testExtractor)
    {
        _log = log;
        _testExtractor = testExtractor;
    }

    // Keyed by exe path so repeat windows of the same executable (the common
    // case: multiple browser/terminal/IDE windows) don't re-run ExtractIconEx.
    // A cached null means extraction failed for that path; it is not retried
    // for the lifetime of this instance (AUDIT25-02). The comparer is
    // case-insensitive because Windows paths are: QueryFullProcessImageName can
    // report the same executable with different casing for different processes,
    // which used to miss the cache and re-extract the icon (PERF25-05).
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCompletionSource<ImageSource?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public string? GetProcessImagePath(uint pid)
    {
        return NativeMethods.GetProcessImagePath(pid);
    }


    /// <summary>Retrieves and caches the icon for an executable path, avoiding repeated ExtractIconEx calls for frequently-captured apps.</summary>
    public ImageSource? GetFileIcon(string exePath)
    {
        // A member captured from a process whose image path could not be read
        // carries an empty ExePath; there is nothing to extract, so skip the
        // interop call rather than caching a failure under an empty key.
        if (string.IsNullOrEmpty(exePath))
            return null;

        TaskCompletionSource<ImageSource?>? waitFor = null;
        TaskCompletionSource<ImageSource?>? producer = null;
        lock (_cacheGate)
        {
            if (_iconCache.TryGetValue(exePath, out ImageSource? cached))
                return cached;

            if (_inFlight.TryGetValue(exePath, out waitFor))
            {
                // Another picker worker is already resolving this executable.
                // Wait outside the lock so concurrent callers share one native
                // extraction rather than racing into ExtractIconEx.
            }
            else
            {
                producer = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight[exePath] = producer;
            }
        }

        if (producer == null)
        {
            // The producer always completes (extraction failures are converted
            // to a cached null), but the wait stays bounded so an anomalous
            // producer can never pin this worker indefinitely. A timeout
            // returns an uncached null; the next query retries.
            if (!waitFor!.Task.Wait(TimeSpan.FromSeconds(5)))
                return null;
            return waitFor.Task.Result;
        }

        ImageSource? result;
        try
        {
            result = _testExtractor != null ? _testExtractor(exePath) : ExtractFileIcon(exePath);
        }
        catch
        {
            // Preserve the existing cosmetic-failure behavior and cache the
            // null result for this process lifetime.
            result = null;
        }

        lock (_cacheGate)
        {
            _iconCache[exePath] = result;
            _inFlight.Remove(exePath);
        }
        producer.TrySetResult(result);
        return result;
    }

    /// <summary>
    /// Returns only a completed cache entry. A miss never performs native icon
    /// extraction, which lets the capture picker display candidate rows before
    /// uncached icons are resolved by its bounded worker.
    /// </summary>
    internal bool TryGetCachedFileIcon(string exePath, out ImageSource? icon)
    {
        if (string.IsNullOrEmpty(exePath))
        {
            icon = null;
            return true;
        }

        lock (_cacheGate)
            return _iconCache.TryGetValue(exePath, out icon);
    }

    private ImageSource? ExtractFileIcon(string exePath)
    {
        // Try the small icon first; fall back to large.
        IntPtr hSmall = IntPtr.Zero;
        IntPtr hLarge = IntPtr.Zero;
        try
        {
            uint count = NativeMethods.ExtractIconEx(exePath, 0, out hLarge, out hSmall, 1);
            // 0xFFFFFFFF means the call itself failed (file unreadable), as
            // opposed to 0 (no icons in file) — distinguishable only because
            // the import now declares SetLastError.
            if (count == 0xFFFFFFFF)
                _log.Log($"ExtractIconEx failed for '{exePath}': {NativeMethods.FormatLastError()}");
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
