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
/// caller's rect, never absolute.
///
/// Extracted from ContainerWindow's private SplitRect so the partition can be
/// exercised deterministically by the app's own --selftest-geometry mode (and
/// by the ValidationDriver on any machine) without any UI or real input.
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

    /// <summary>
    /// Deterministic partition self-test (goal §27/§28): an exhaustive matrix of
    /// boundary/sample widths and heights over positive/zero/negative origins,
    /// plus a seeded randomized fuzz sweep, asserting exact coverage, zero
    /// overlap, zero unintended gap, and no overflow for every rect. Pure
    /// integer math — no windows, no input. Returns check/failure counts;
    /// <paramref name="report"/> receives every failure line (bounded) and is
    /// called with the summary as well.
    /// </summary>
    public static (int Checks, int Failures) RunSelfTest(Action<string>? report = null)
    {
        int checks = 0;
        int failures = 0;
        void Check(bool ok, string what)
        {
            checks++;
            if (!ok)
            {
                failures++;
                report?.Invoke($"FAIL: {what}");
            }
        }

        void VerifyRect(NativeMethods.RECT content, string tag)
        {
            var (left, right) = Partition(content);
            Check(left.left == content.left, $"{tag}: left.Left != content.Left");
            Check(left.top == content.top, $"{tag}: left.Top != content.Top");
            Check(right.top == content.top, $"{tag}: right.Top != content.Top");
            Check(left.bottom == content.bottom, $"{tag}: left.Bottom != content.Bottom");
            Check(right.bottom == content.bottom, $"{tag}: right.Bottom != content.Bottom");
            Check(left.right == right.left, $"{tag}: LEFT.Right != RIGHT.Left");
            Check(right.right == content.right, $"{tag}: right.Right != content.Right (right pane does not end at content.Right)");
            Check(left.Width >= 0, $"{tag}: left.Width < 0");
            Check(right.Width >= 0, $"{tag}: right.Width < 0");
            Check(left.Width + right.Width == content.Width, $"{tag}: left.Width + right.Width != content.Width");
            // No overflow / wrap: all four edges of both panes must stay within
            // int range when the content rect itself is.
            Check(left.left <= left.right, $"{tag}: left rect inverted (overflow?)");
            Check(right.left <= right.right, $"{tag}: right rect inverted (overflow?)");
            Check(left.top <= left.bottom, $"{tag}: left vertical inverted (overflow?)");
        }

        // --- Matrix (goal §14/§27): every width 1..4096, representative
        // heights, and origins covering positive, zero, and negative. ---
        int[] heights = { 1, 2, 3, 10, 100, 1000, 4096 };
        int[] originsX = { -1920, -1, 0, 1, 1920, 5000 };
        int[] originsY = { -1080, -1, 0, 1, 1080, 3000 };
        for (int w = 1; w <= 4096; w++)
        {
            foreach (int h in heights)
            {
                foreach (int ox in originsX)
                {
                    foreach (int oy in originsY)
                    {
                        VerifyRect(new NativeMethods.RECT { left = ox, top = oy, right = ox + w, bottom = oy + h },
                            $"matrix w={w} h={h} origin=({ox},{oy})");
                    }
                }
            }
        }

        // --- Targeted odd widths (goal §14). ---
        foreach (int w in new[] { 799, 800, 801, 1023, 1024, 1025, 1919, 1920, 1921 })
        {
            VerifyRect(new NativeMethods.RECT { left = 0, top = 0, right = w, bottom = 1080 }, $"odd-width w={w}");
            VerifyRect(new NativeMethods.RECT { left = -1920, top = -1080, right = -1920 + w, bottom = 0 }, $"odd-width-negative w={w}");
        }

        // --- Fuzz (goal §28): seeded, deterministic, thousands of rects. ---
        var rng = new Random(20260810); // fixed seed: reproducible across machines
        for (int i = 0; i < 100_000; i++)
        {
            int ox = rng.Next(-10000, 10001);
            int oy = rng.Next(-10000, 10001);
            int w = rng.Next(1, 10001);
            int h = rng.Next(1, 10001);
            VerifyRect(new NativeMethods.RECT { left = ox, top = oy, right = ox + w, bottom = oy + h },
                $"fuzz#{i} x={ox} y={oy} w={w} h={h}");
        }

        // --- Size-constraint math (post-audit containment finding). For every
        // pair of guest minimum widths, the computed MinContentWidth must be the
        // smallest width at which the exact partition still fits both guests
        // (LEFT pane = floor(W/2) &gt;= leftMin, RIGHT pane = ceil(W/2) &gt;= rightMin),
        // and no width below it may fit. ---
        int[] minWidths = { 0, 1, 100, 200, 500, 643, 1024 };
        foreach (int lm in minWidths)
        {
            foreach (int rm in minWidths)
            {
                int minW = MinContentWidth(split: true, lm, rm);
                // At exactly minW both panes must fit.
                int left = minW / 2;
                int right = minW - left;
                Check(left >= lm, $"constraint: split lm={lm} rm={rm} minW={minW}: LEFT {left} < leftMin {lm}");
                Check(right >= rm, $"constraint: split lm={lm} rm={rm} minW={minW}: RIGHT {right} < rightMin {rm}");
                // At minW-1 at least one pane must NOT fit (minimality).
                if (minW > 0)
                {
                    int lm1 = (minW - 1) / 2;
                    int rm1 = (minW - 1) - lm1;
                    bool fits = lm1 >= lm && rm1 >= rm;
                    Check(!fits, $"constraint: split lm={lm} rm={rm} minW={minW}: width minW-1 still fits");
                }
                // Normal mode: a single guest spans the full width.
                Check(MinContentWidth(false, lm, rm) == lm, $"constraint: normal lm={lm} => {MinContentWidth(false, lm, rm)}");
                // Height: split requires the taller guest's min; normal the active guest's.
                Check(MinContentHeight(true, lm, rm) == Math.Max(lm, rm), $"constraint: split height lm={lm} rm={rm}");
                Check(MinContentHeight(false, lm, rm) == lm, $"constraint: normal height lm={lm}");
            }
        }

        // --- DPI-unaware min-track scaling math (DPI-acceptance goal). The
        // conversion is pure and deterministic, so it is fully exercised even on
        // a 100%-scaling machine. Verify: 96 (100%) and <=0 are no-ops; 120/125%,
        // 144/150%, 192/200% scale logical->physical; and rounding is UP (never
        // under-estimates the physical minimum). ---
        (int Value, uint Dpi, int Expected)[] scaleCases =
        {
            (0, 120, 0), (0, 96, 0), (0, 0, 0), (-5, 120, -5),
            (500, 96, 500), (500, 0, 500), (500, 120, 625), (500, 144, 750),
            (500, 192, 1000), (10, 120, 13), (1, 240, 3), (100, 120, 125),
            (99, 120, 124), (643, 120, 804), (1, 96, 1),
        };
        foreach (var (v, dpi, exp) in scaleCases)
        {
            int got = ScaleUnawareLogicalToPhysical(v, dpi);
            Check(got == exp, $"dpi-scale value={v} dpi={dpi}: expected {exp}, got {got}");
        }
        // Rounding-up guarantee: for any positive value at dpi>96, the physical
        // min must be >= the exact logical*dpi/96 (never under-estimate).
        int[] probeValues = { 1, 2, 7, 100, 499, 500, 643, 1024 };
        uint[] probeDpis = { 96, 120, 125, 144, 150, 192, 200, 240 };
        foreach (int v in probeValues)
        {
            foreach (uint d in probeDpis)
            {
                int phys = ScaleUnawareLogicalToPhysical(v, d);
                double exact = v * d / (double)NativeMethods.USER_DEFAULT_SCREEN_DPI;
                Check(phys >= exact, $"dpi-scale never-underestimate value={v} dpi={d}: physical {phys} < exact {exact}");
                if (d > 96)
                    Check(phys > v, $"dpi-scale strict-increase value={v} dpi={d}: {phys} not > {v}");
            }
        }

        report?.Invoke($"SELFTEST[geometry] checks={checks} failures={failures} seed=20260810 matrixWidths=1..4096 fuzzRects=100000");
        return (checks, failures);
    }
}
