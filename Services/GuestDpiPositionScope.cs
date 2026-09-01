using System;
using System.Diagnostics;

namespace TabDock.Services;

/// <summary>
/// Temporarily uses the DPI-unaware thread context when User32 positions a
/// known DPI-unaware top-level guest. User32 otherwise scales a size supplied by
/// a PMv2 caller after a cross-monitor transition, even though the guest's
/// position contract is the physical screen-pixel rectangle used by TabDock.
/// The scope keeps that conversion local to the native position call and always
/// restores the caller's context before returning to WPF.
/// </summary>
internal readonly struct GuestDpiPositionScope : IDisposable
{
    // Test seam: replaceable native delegates for deterministic unit tests.
    // Production defaults to NativeMethods; tests may substitute fakes and must restore.
    internal static Func<IntPtr, IntPtr> GetWindowDpiAwarenessContextImpl { get; set; } = NativeMethods.GetWindowDpiAwarenessContext;
    internal static Func<IntPtr, int> GetAwarenessFromDpiAwarenessContextImpl { get; set; } = NativeMethods.GetAwarenessFromDpiAwarenessContext;
    internal static Func<IntPtr, IntPtr> SetThreadDpiAwarenessContextImpl { get; set; } = NativeMethods.SetThreadDpiAwarenessContext;

    private readonly IntPtr _previousContext;
    private readonly bool _restore;

    private GuestDpiPositionScope(IntPtr previousContext, bool restore, bool available)
    {
        _previousContext = previousContext;
        _restore = restore;
        IsAvailable = available;
    }

    /// <summary>Whether the caller may safely issue the position operation.</summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Enters the target guest's coordinate context. A known aware guest needs
    /// no switch. A failed target-context probe or failed context switch refuses
    /// the mutation rather than silently applying a potentially virtualized
    /// physical rectangle.
    /// </summary>
    public static GuestDpiPositionScope EnterForWindow(IntPtr hwnd)
        => TryGetAwareness(hwnd, out int awareness)
            ? EnterForAwareness(awareness)
            : Unavailable();

    /// <summary>
    /// Enters one shared coordinate context for an atomic pair. A single
    /// USER32 deferred transaction cannot supply different caller contexts to
    /// two guests, so differing awareness is handled by the caller's
    /// generation-gated per-guest fallback.
    /// </summary>
    public static GuestDpiPositionScope EnterForWindows(IntPtr first, IntPtr second)
    {
        if (!TryGetAwareness(first, out int firstAwareness)
            || !TryGetAwareness(second, out int secondAwareness)
            || firstAwareness != secondAwareness)
        {
            return Unavailable();
        }

        return EnterForAwareness(firstAwareness);
    }

    internal static GuestDpiPositionScope EnterForAwarenessForTest(int awareness)
        => EnterForAwareness(awareness);

    private static GuestDpiPositionScope EnterForAwareness(int awareness)
    {
        if (!DpiCapturePolicy.IsKnownAwareness(awareness))
            return Unavailable();
        if (awareness != DpiCapturePolicy.DpiAwarenessUnaware)
        {
            // An aware guest needs no switch. Keeping this branch non-switching
            // also avoids changing WPF's context for an aware target.
            return new GuestDpiPositionScope(IntPtr.Zero, restore: false, available: true);
        }

        IntPtr previousContext = SetThreadDpiAwarenessContextImpl(
            NativeMethods.DpiAwarenessContextUnaware);
        return previousContext == IntPtr.Zero
            ? Unavailable()
            : new GuestDpiPositionScope(previousContext, restore: true, available: true);
    }

    private static bool TryGetAwareness(IntPtr hwnd, out int awareness)
    {
        awareness = 0;
        if (hwnd == IntPtr.Zero)
            return false;

        IntPtr guestContext = GetWindowDpiAwarenessContextImpl(hwnd);
        if (guestContext == IntPtr.Zero)
            return false;

        awareness = GetAwarenessFromDpiAwarenessContextImpl(guestContext);
        return DpiCapturePolicy.IsKnownAwareness(awareness);
    }

    private static GuestDpiPositionScope Unavailable()
        => new(IntPtr.Zero, restore: false, available: false);

    public void Dispose()
    {
        if (!_restore)
            return;

        if (SetThreadDpiAwarenessContextImpl(_previousContext) == IntPtr.Zero)
        {
            Debug.WriteLine("TabDock could not restore the caller's DPI awareness context after guest positioning.");
        }
    }
}
