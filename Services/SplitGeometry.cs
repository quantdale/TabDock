using System;
using System.Collections.Generic;

namespace TabDock.Services;

/// <summary>
/// The single definition of the vertical split partition (goal §13/§14/§27/§28).
/// For any content rectangle in physical pixels, LEFT gets floor(Width/2) and
/// RIGHT gets the remainder (the extra pixel on odd widths), so the panes abut
/// exactly: LEFT.Right == RIGHT.Left, zero overlap, zero gap, and the right
/// pane ends exactly at content.Right. Works for any signed origin (negative
/// coordinates, secondary monitors) because the math is additive on the
/// caller's rect, never absolute. Its invariants are qualified by the headless
/// xUnit suite (tests/UnitTests/GeometryTests.cs), which owns the exhaustive
/// matrix, seeded fuzz, and constraint-minimality sweeps.
/// </summary>
public static class SplitGeometry
{
    public static (NativeMethods.RECT Left, NativeMethods.RECT Right) Partition(NativeMethods.RECT content)
    {
        int leftW = content.Width / 2;
        var left = new NativeMethods.RECT
        {
            left = content.left,
            top = content.top,
            right = content.left + leftW,
            bottom = content.bottom,
        };
        var right = new NativeMethods.RECT
        {
            left = content.left + leftW,
            top = content.top,
            right = content.right,
            bottom = content.bottom,
        };
        return (left, right);
    }

    /// <summary>
    /// The minimum CONTENT WIDTH that lets the currently relevant visible
    /// guest(s) physically fit their pane(s), given each guest's effective
    /// native minimum track width (physical pixels). This is the deterministic
    /// half of the size-constraint policy: the container refuses to shrink below
    /// it, so a guest can never be asked to occupy a pane narrower than its own
    /// native minimum.
    ///
    /// Normal mode: the single visible guest spans the full content width, so
    /// the content must be at least <paramref name="leftMinW"/> wide.
    ///
    /// Split mode: LEFT = floor(W/2), RIGHT = W - floor(W/2). LEFT fits iff
    /// floor(W/2) &gt;= leftMinW, i.e. W &gt;= 2*leftMinW; RIGHT fits iff
    /// ceil(W/2) &gt;= rightMinW, i.e. W &gt;= 2*rightMinW - 1. The binding
    /// constraint is the max of the two. Right pane width is whatever the exact
    /// partition leaves; the divider width is zero (the panes abut).
    /// </summary>
    public static int MinContentWidth(bool split, int leftMinW, int rightMinW)
    {
        leftMinW = Math.Max(0, leftMinW);
        rightMinW = Math.Max(0, rightMinW);
        return split ? Math.Max(2 * leftMinW, 2 * rightMinW - 1) : leftMinW;
    }

    /// <summary>
    /// The minimum CONTENT HEIGHT. Normal mode: the active guest spans the full
    /// content height. Split mode: both panes span the full content height, so
    /// the content must be at least the taller guest's minimum.
    /// </summary>
    public static int MinContentHeight(bool split, int leftMinH, int rightMinH)
    {
        leftMinH = Math.Max(0, leftMinH);
        rightMinH = Math.Max(0, rightMinH);
        return split ? Math.Max(leftMinH, rightMinH) : leftMinH;
    }

    /// <summary>
    /// The single authoritative logical→physical scale for a DPI-UNAWARE guest's
    /// geometry. WM_GETMINMAXINFO is answered by the target's own window proc, so
    /// an unaware guest reports its min-track in ITS logical 96-DPI space; Windows
    /// DWM-scales that by the monitor's effective DPI to the real physical minimum
    /// the guest enforces. This converts a logical dimension into the physical-pixel
    /// contract every other geometry in TabDock lives in. Pure and deterministic:
    /// no-op at 100% (monitorDpi == 96, or 0 on a failed probe) and for non-positive
    /// values; rounds UP so the physical minimum is never UNDER-estimated (an
    /// under-estimate would re-open the pane-overflow this contract exists to
    /// prevent). Awareness-aware guests never reach this (they report in the
    /// physical contract directly).
    /// </summary>
    public static int ScaleUnawareLogicalToPhysical(int value, uint monitorDpi)
    {
        if (value <= 0 || monitorDpi == 0 || monitorDpi == NativeMethods.USER_DEFAULT_SCREEN_DPI)
            return value;
        return (int)Math.Ceiling(value * monitorDpi / (double)NativeMethods.USER_DEFAULT_SCREEN_DPI);
    }
}
