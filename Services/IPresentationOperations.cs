using System;

namespace TabDock.Services;

/// <summary>
/// Minimal presentation seam so split-transition logic can drive
/// hide/show/position/foreground without real windows. Production delegates to
/// <see cref="WindowShepherdService"/> (via the view's ShepherdPresentationOps
/// shim); unit tests substitute deterministic fakes.
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
