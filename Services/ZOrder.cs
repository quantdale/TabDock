using System;
using TabDock.Infrastructure;

namespace TabDock.Services;

/// <summary>
/// Pure z-order predicates over the desktop window list. Kept free of WPF and
/// native state so the split compositor's relative-order invariant (a guest
/// need only sit somewhere above another, not be its strict neighbor) is
/// unit-testable without real top-level HWNDs.
/// </summary>
internal static class ZOrder
{
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

        for (IntPtr hwnd = upper; hwnd != IntPtr.Zero; hwnd = getNext(hwnd))
        {
            if (hwnd == lower)
                return true;
        }
        return false;
    }
}
