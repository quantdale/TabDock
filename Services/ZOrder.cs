using System;

namespace TabDock.Services;

/// <summary>
/// Pure z-order predicates over the desktop window list. Kept free of WPF and
/// native state so the split compositor's relative-order invariant (a guest
/// need only sit somewhere above another, not be its strict neighbor) is
/// unit-testable without real top-level HWNDs.
/// </summary>
internal static class ZOrder
{
    // USER32's list is live, not a stable enumeration: concurrent reorder or
    // destruction can revisit handles. Bound probes without allocating a set
    // on every hot layout pass; an unprovable order is simply not satisfied.
    internal const int MaximumTraversalSteps = 4096;
    /// <summary>
    /// Returns true when <paramref name="upper"/> occurs anywhere above
    /// <paramref name="lower"/> in the top-level z-order. The split compositor
    /// cares about relative order, not strict adjacency: IME, accessibility,
    /// overlay and shell helper HWNDs can legally sit between two TabDock guests,
    /// and fighting them for exact adjacency would cause an endless deferred
    /// positioning storm.
    /// </summary>
    public static bool IsOrderedAbove(IntPtr upper, IntPtr lower, Func<IntPtr, IntPtr> getNext)
    {
        if (upper == IntPtr.Zero || lower == IntPtr.Zero || upper == lower)
            return false;

        IntPtr hwnd = upper;
        for (int step = 0; step < MaximumTraversalSteps && hwnd != IntPtr.Zero; step++)
        {
            if (hwnd == lower)
                return true;
            hwnd = getNext(hwnd);
        }
        return false;
    }
}
