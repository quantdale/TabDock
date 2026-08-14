using System;
using System.Collections.Generic;

namespace TabDock.Services;

/// <summary>
/// One entry in a deferred window-position transaction.
/// </summary>
internal readonly struct DeferredWindowPositionEntry
{
    public DeferredWindowPositionEntry(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags)
    {
        Window = window;
        InsertAfter = insertAfter;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Flags = flags;
    }

    public IntPtr Window { get; }
    public IntPtr InsertAfter { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public uint Flags { get; }
}

/// <summary>
/// Native operations needed by a deferred window-position transaction. The
/// seam keeps the HDWP lifetime rules deterministic-testable without moving
/// P/Invoke declarations out of <see cref="NativeMethods"/>.
/// </summary>
internal interface IDeferredWindowPositionApi
{
    IntPtr Begin(int windowCount);

    IntPtr Defer(
        IntPtr hdwp,
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    bool End(IntPtr hdwp);
}

/// <summary>Production adapter for the user32 deferred-position APIs.</summary>
internal sealed class NativeDeferredWindowPositionApi : IDeferredWindowPositionApi
{
    public static NativeDeferredWindowPositionApi Instance { get; } = new();

    private NativeDeferredWindowPositionApi()
    {
    }

    public IntPtr Begin(int windowCount) => NativeMethods.BeginDeferWindowPos(windowCount);

    public IntPtr Defer(
        IntPtr hdwp,
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags)
    {
        return NativeMethods.DeferWindowPos(hdwp, window, insertAfter, x, y, width, height, flags);
    }

    public bool End(IntPtr hdwp) => NativeMethods.EndDeferWindowPos(hdwp);
}

internal enum DeferredWindowPositionResult
{
    Applied,
    BeginFailed,
    DeferFailed,
    ValidationFailed,
    EndFailed,
}

/// <summary>
/// Applies an HDWP transaction while preserving the handle returned by each
/// <c>DeferWindowPos</c> call. The validator runs immediately before every
/// guest is queued. A failed Defer abandons the transaction and deliberately
/// does not call <c>EndDeferWindowPos</c>, as required by Win32. There is no
/// documented cancellation API for a valid HDWP, so a generation failure
/// after Begin/after earlier queues closes the valid batch with End and never
/// falls back to touching the stale guest.
/// </summary>
internal static class DeferredWindowPositionBatch
{
    public static DeferredWindowPositionResult Apply(
        IDeferredWindowPositionApi api,
        IReadOnlyList<DeferredWindowPositionEntry> entries,
        Func<int, bool>? beforeDefer = null)
    {
        if (entries.Count == 0)
            return DeferredWindowPositionResult.BeginFailed;

        IntPtr hdwp = api.Begin(entries.Count);
        if (hdwp == IntPtr.Zero)
            return DeferredWindowPositionResult.BeginFailed;

        for (int i = 0; i < entries.Count; i++)
        {
            if (beforeDefer != null && !beforeDefer(i))
            {
                // BeginDeferWindowPos succeeded, so End is the documented
                // lifecycle close for this valid HDWP. Any earlier entries
                // were independently generation-validated before queuing.
                // The caller must not run a stale-guest fallback afterwards.
                api.End(hdwp);
                return DeferredWindowPositionResult.ValidationFailed;
            }
            DeferredWindowPositionEntry entry = entries[i];
            IntPtr updated = api.Defer(
                hdwp,
                entry.Window,
                entry.InsertAfter,
                entry.X,
                entry.Y,
                entry.Width,
                entry.Height,
                entry.Flags);
            if (updated == IntPtr.Zero)
                return DeferredWindowPositionResult.DeferFailed;

            hdwp = updated;
        }

        return api.End(hdwp)
            ? DeferredWindowPositionResult.Applied
            : DeferredWindowPositionResult.EndFailed;
    }
}
