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

        report?.Invoke($"SELFTEST[geometry] checks={checks} failures={failures} seed=20260810 matrixWidths=1..4096 fuzzRects=100000");
        return (checks, failures);
    }
}
