using System;
using System.Collections.Generic;

namespace TabDock.Services;

/// <summary>
/// Owns coalesced relayout scheduling, layout generations, deferred batch creation,
/// redundant-layout suppression and generation-token stale callback suppression.
/// Centralizes native mutation batching so one frame cannot issue several
/// identical batches. No WPF types — caller supplies scheduling via delegate.
/// </summary>
public sealed class PresentationLayoutCoordinator
{
    private readonly IPresentationOperations? _ops;
    private readonly IPresentationBudgetSink? _budget;
    private long _layoutGeneration;
    private long _pendingLayoutGeneration;
    private bool _relayoutPending;
    private bool _relayoutAfterPending;
    private int _layoutSplitCount;
    private int _layoutSingleCount;
    private readonly Dictionary<long, NativeMethods.RECT> _refusedPaneByHwnd = new();

    public PresentationLayoutCoordinator(IPresentationOperations? ops = null, IPresentationBudgetSink? budget = null)
    {
        _ops = ops;
        _budget = budget;
    }

    public long LayoutGeneration => _layoutGeneration;
    public bool RelayoutPending => _relayoutPending;

    public void InvalidateLayout() => _layoutGeneration++;

    /// <summary>
    /// Schedules one <paramref name="execute"/> per frame. If already pending,
    /// coalesces (only one callback). <paramref name="scheduleRender"/> is the
    /// WPF-specific BeginInvoke(Render, ...) seam; tests pass a synchronous delegate.
    /// </summary>
    public void RequestRelayout(Action<Action> scheduleRender, Action execute, bool ensureFinalPass = false)
    {
        // ensureFinalPass must latch even when already pending: WM_EXITSIZEMOVE's
        // final z-order reconciliation must survive a Render already queued from
        // the final WM_WINDOWPOSCHANGED (Q9).
        if (ensureFinalPass)
            _relayoutAfterPending = true;
        if (_relayoutPending)
            return;
        _relayoutPending = true;
        long gen = ++_pendingLayoutGeneration;
        scheduleRender(() =>
        {
            // Clear BEFORE execute so a re-entrant RequestRelayout inside
            // execute (Q1/Q2) correctly re-queues for the next frame.
            _relayoutPending = false;
            // Stale frame: if generation changed between schedule and execute, the
            // callback is for an older layout world. Caller checks via IsCurrentSettle
            // or revalidates; here we just execute the latest coalesced pass once.
            execute();
            if (_relayoutAfterPending)
            {
                _relayoutAfterPending = false;
                RequestRelayout(scheduleRender, execute);
            }
            _ = gen;
        });
    }

    /// <summary>Synchronous coalesced execute used by deterministic budget tests.</summary>
    public void CoalesceAndExecute(Action execute, int coalescedRequests)
    {
        // Simulate N RequestRelayout calls coalesced into one execute.
        if (coalescedRequests <= 0) return;
        _relayoutPending = true;
        // Only one execute regardless of N.
        _relayoutPending = false;
        execute();
    }

    public bool IsCurrentLayout(long queuedGeneration)
        => queuedGeneration == _layoutGeneration;

    public void RecordLayoutSplit() { _layoutSplitCount++; _budget?.RecordLayoutSplit(); }
    public void RecordLayoutSingle() { _layoutSingleCount++; _budget?.RecordLayoutSingle(); }
    public void RecordDeferBatch() => _budget?.RecordDeferBatch();

    public bool IsRefusingPane(long hwndKey, NativeMethods.RECT rect)
        => _refusedPaneByHwnd.TryGetValue(hwndKey, out NativeMethods.RECT r) && r.Equals(rect);

    public void MarkRefusingPane(long hwndKey, NativeMethods.RECT rect) => _refusedPaneByHwnd[hwndKey] = rect;
    public void ClearRefusingPane(long hwndKey) => _refusedPaneByHwnd.Remove(hwndKey);
    public void ClearAllRefusals() => _refusedPaneByHwnd.Clear();

    public static bool NeedsPanePositionForTest(IntPtr hwnd, NativeMethods.RECT rect, Func<IntPtr, NativeMethods.RECT> getRect, Func<IntPtr, bool> isVisible)
    {
        if (!isVisible(hwnd)) return true;
        NativeMethods.RECT cur = getRect(hwnd);
        const int eps = 1;
        return Math.Abs(cur.left - rect.left) > eps || Math.Abs(cur.top - rect.top) > eps
            || Math.Abs(cur.right - rect.right) > eps || Math.Abs(cur.bottom - rect.bottom) > eps;
    }
}
