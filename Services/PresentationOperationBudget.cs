using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TabDock.Services;

/// <summary>
/// Aggregated presentation operation counts for deterministic budget tests.
/// All counters are presentation mutations: hide/show/position/defer/foreground/layout.
/// The counters live in a test seam; production hot paths guard on a null sink
/// so there is zero overhead when no observer is attached.
/// </summary>
public sealed record PresentationOperationCounts(
    int HideCount,
    int PositionAndShowCount,
    int DeferBatchCount,
    int SetForegroundCount,
    int LayoutSplitPanesCount,
    int LayoutSingleCount,
    int PairZOrderBehindCount,
    int ContainerRaiseCount,
    IReadOnlyDictionary<long, int> HideByHwnd,
    IReadOnlyDictionary<long, int> PositionAndShowByHwnd,
    IReadOnlyDictionary<long, int> SetForegroundByHwnd)
{
    public static PresentationOperationCounts Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0,
        new ReadOnlyDictionary<long, int>(new Dictionary<long, int>()),
        new ReadOnlyDictionary<long, int>(new Dictionary<long, int>()),
        new ReadOnlyDictionary<long, int>(new Dictionary<long, int>()));

    public int HideForHwnd(IntPtr hwnd)
        => HideByHwnd.TryGetValue(hwnd.ToInt64(), out int c) ? c : 0;

    public int PositionAndShowForHwnd(IntPtr hwnd)
        => PositionAndShowByHwnd.TryGetValue(hwnd.ToInt64(), out int c) ? c : 0;

    public int SetForegroundForHwnd(IntPtr hwnd)
        => SetForegroundByHwnd.TryGetValue(hwnd.ToInt64(), out int c) ? c : 0;
}

/// <summary>
/// Test-seam sink for presentation operation budgets. Production code optionally
/// notifies it; test doubles count via this interface without touching real windows.
/// </summary>
public interface IPresentationBudgetSink
{
    void RecordHide(IntPtr hwnd);
    void RecordPositionAndShow(IntPtr hwnd);
    void RecordDeferBatch();
    void RecordSetForeground(IntPtr hwnd);
    void RecordLayoutSplit();
    void RecordLayoutSingle();
    void RecordPairZOrder();
    void RecordContainerRaise();
    PresentationOperationCounts Snapshot();
    void Reset();
}

/// <summary>
/// Thread-safe counting implementation of <see cref="IPresentationBudgetSink"/>
/// used by deterministic unit tests. No native calls.
/// </summary>
public sealed class PresentationOperationCounter : IPresentationBudgetSink
{
    private readonly object _gate = new();
    private int _hideCount;
    private int _positionAndShowCount;
    private int _deferBatchCount;
    private int _setForegroundCount;
    private int _layoutSplitCount;
    private int _layoutSingleCount;
    private int _pairZOrderCount;
    private int _containerRaiseCount;
    private readonly Dictionary<long, int> _hideByHwnd = new();
    private readonly Dictionary<long, int> _positionByHwnd = new();
    private readonly Dictionary<long, int> _foregroundByHwnd = new();

    public void RecordHide(IntPtr hwnd)
    {
        lock (_gate)
        {
            _hideCount++;
            long k = hwnd.ToInt64();
            _hideByHwnd[k] = _hideByHwnd.TryGetValue(k, out int c) ? c + 1 : 1;
        }
    }

    public void RecordPositionAndShow(IntPtr hwnd)
    {
        lock (_gate)
        {
            _positionAndShowCount++;
            long k = hwnd.ToInt64();
            _positionByHwnd[k] = _positionByHwnd.TryGetValue(k, out int c) ? c + 1 : 1;
        }
    }

    public void RecordDeferBatch()
    {
        lock (_gate) _deferBatchCount++;
    }

    public void RecordSetForeground(IntPtr hwnd)
    {
        lock (_gate)
        {
            _setForegroundCount++;
            long k = hwnd.ToInt64();
            _foregroundByHwnd[k] = _foregroundByHwnd.TryGetValue(k, out int c) ? c + 1 : 1;
        }
    }

    public void RecordLayoutSplit()
    {
        lock (_gate) _layoutSplitCount++;
    }

    public void RecordLayoutSingle()
    {
        lock (_gate) _layoutSingleCount++;
    }

    public void RecordPairZOrder()
    {
        lock (_gate) _pairZOrderCount++;
    }

    public void RecordContainerRaise()
    {
        lock (_gate) _containerRaiseCount++;
    }

    public PresentationOperationCounts Snapshot()
    {
        lock (_gate)
        {
            return new PresentationOperationCounts(
                _hideCount,
                _positionAndShowCount,
                _deferBatchCount,
                _setForegroundCount,
                _layoutSplitCount,
                _layoutSingleCount,
                _pairZOrderCount,
                _containerRaiseCount,
                new ReadOnlyDictionary<long, int>(new Dictionary<long, int>(_hideByHwnd)),
                new ReadOnlyDictionary<long, int>(new Dictionary<long, int>(_positionByHwnd)),
                new ReadOnlyDictionary<long, int>(new Dictionary<long, int>(_foregroundByHwnd)));
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _hideCount = 0;
            _positionAndShowCount = 0;
            _deferBatchCount = 0;
            _setForegroundCount = 0;
            _layoutSplitCount = 0;
            _layoutSingleCount = 0;
            _pairZOrderCount = 0;
            _containerRaiseCount = 0;
            _hideByHwnd.Clear();
            _positionByHwnd.Clear();
            _foregroundByHwnd.Clear();
        }
    }
}

/// <summary>
/// Minimal presentation seam so tests can count hide/show/position/foreground
/// without real windows. Production delegates to <see cref="WindowShepherdService"/>.
/// </summary>
public interface IPresentationOperations
{
    WindowHideOutcome Hide(Models.CapturedWindow window);
    void PositionAndShow(Models.CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect);
    void PositionGuestsDeferred(Models.CapturedWindow top, NativeMethods.RECT topRect, Models.CapturedWindow bottom, NativeMethods.RECT bottomRect, IntPtr containerHwnd);
    void SetForeground(Models.CapturedWindow window);
    void PairZOrderBehind(IntPtr containerHwnd, Models.CapturedWindow guest);
    bool IsCurrentCapturedWindow(Models.CapturedWindow window);
}
