using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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


    /// <summary>Background-only compatibility wrapper; UI callers use the completed cache or async lookup.</summary>
    public ImageSource? GetFileIcon(string exePath)
    {
        Task<ImageSource?> pending = GetFileIconAsync(exePath);
        return pending.Wait(TimeSpan.FromSeconds(5)) ? pending.GetAwaiter().GetResult() : null;
    }

    internal const int MaximumActiveProducers = 2;
    internal const int MaximumQueuedPaths = 256;
    private readonly Queue<(string Path, TaskCompletionSource<ImageSource?> Completion)> _pending = new();
    private int _workers;

    /// <summary>
    /// Bounded case-insensitive single-flight extraction. Cancellation abandons
    /// only this consumer, never the shared native producer. Queue refusal is
    /// not cached, whereas an actual extraction failure is cached as null.
    /// </summary>
    public Task<ImageSource?> GetFileIconAsync(string exePath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<ImageSource?>(cancellationToken);
        if (string.IsNullOrEmpty(exePath))
            return Task.FromResult<ImageSource?>(null);

        Task<ImageSource?> task;
        lock (_cacheGate)
        {
            if (_iconCache.TryGetValue(exePath, out ImageSource? cached))
                return Task.FromResult(cached);
            if (!_inFlight.TryGetValue(exePath, out TaskCompletionSource<ImageSource?>? completion))
            {
                if (_workers >= MaximumActiveProducers && _pending.Count >= MaximumQueuedPaths)
                    return Task.FromResult<ImageSource?>(null);
                completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight.Add(exePath, completion);
                _pending.Enqueue((exePath, completion));
                if (_workers < MaximumActiveProducers)
                {
                    _workers++;
                    _ = Task.Run(DrainQueue);
                }
            }
            task = completion.Task;
        }
        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    private void DrainQueue()
    {
        while (true)
        {
            (string Path, TaskCompletionSource<ImageSource?> Completion) work;
            lock (_cacheGate)
            {
                if (_pending.Count == 0)
                {
                    _workers--;
                    return;
                }
                work = _pending.Dequeue();
            }
            ImageSource? result;
            try
            {
                result = _testExtractor != null ? _testExtractor(work.Path) : ExtractFileIcon(work.Path);
                if (result is { IsFrozen: false })
                {
                    if (result.CanFreeze)
                        result.Freeze();
                    else
                        result = null;
                }
            }
            catch
            {
                result = null;
            }
            lock (_cacheGate)
            {
                _iconCache[work.Path] = result;
                _inFlight.Remove(work.Path);
            }
            work.Completion.TrySetResult(result);
        }
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
