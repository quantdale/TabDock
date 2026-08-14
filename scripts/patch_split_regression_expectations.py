from pathlib import Path

path = Path("tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Split.cs")
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}")
    text = text.replace(old, new, 1)


def replace_region(start_marker: str, end_marker: str, replacement: str, label: str) -> None:
    global text
    start_count = text.count(start_marker)
    end_count = text.count(end_marker)
    if start_count != 1 or end_count != 1:
        raise SystemExit(f"{label}: marker count start={start_count} end={end_count}")
    start = text.index(start_marker)
    end = text.index(end_marker, start)
    text = text[:start] + replacement + text[end:]


replace_once(
    """    //   * Clicking a split member keeps split; clicking a non-paired tab also
    //     keeps split (the pair is the persistent selected unit — split exits
    //     only via the explicit \"Exit split screen\" item, a Split Screen
    //     replacement, or a structural member removal).
""",
    """    //   * Clicking either split member keeps the pair active. Hover/right-click
    //     on an unrelated tab also leaves it untouched, but an ordinary LEFT
    //     click on a non-member is an explicit presentation switch: split exits
    //     and the clicked tab becomes the full-width active guest.
""",
    "overview split-click policy",
)

replace_once(
    """        AssertSplitPanes(ctx, host, pigA, pigB, \"split-two-auto\");
        ctx.Check(TabCount(container) == 1, \"pair renders as ONE composite tab item (both captured, none released)\");
""",
    """        AssertSplitPanes(ctx, host, pigA, pigB, \"split-two-auto\");
        ctx.Check(TabDockLog.WaitForLogLine(off, \"SPLIT[settled]\", 3000),
            \"post-popup split presentation settle logged\");
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pigA.Hwnd, 3000),
            \"split creation settles the initiating LEFT member as real foreground\");
        ctx.Check(TabCount(container) == 1, \"pair renders as ONE composite tab item (both captured, none released)\");
""",
    "split-two-auto settle assertion",
)

replace_once(
    """        AssertSplitPanes(ctx, host, pigA, pigC, \"split-select-partner\");
        ctx.Check(IsReleasedAndHidden(pigB.Hwnd), $\"'{pigB.Title}' hidden (non-member of the split pair)\");
""",
    """        AssertSplitPanes(ctx, host, pigA, pigC, \"split-select-partner\");
        ctx.Check(TabDockLog.WaitForLogLine(off, \"SPLIT[settled]\", 3000),
            \"submenu split receives the post-popup presentation settle\");
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetForegroundWindow() == pigA.Hwnd, 3000),
            \"submenu split settles the initiating LEFT member as real foreground\");
        ctx.Check(IsReleasedAndHidden(pigB.Hwnd), $\"'{pigB.Title}' hidden (non-member of the split pair)\");
""",
    "split-select-partner settle assertion",
)

split_click_start = """    // -------------------------------------------------------------------------
    // split-click-third: clicking a NON-paired tab (C) while A/B are split must
"""
split_hover_start = """    // -------------------------------------------------------------------------
    // split-third-tab-hover-persists (goal §10): with A+B split and a third tab
"""
split_click_replacement = """    // -------------------------------------------------------------------------
    // split-click-third: regression for the user-reported three-tab defect.
    // With A/B split, an ordinary LEFT click on C is an explicit presentation
    // switch: the split exits, A/B remain captured but hidden, C becomes the
    // full-width active guest, and the ordinary three-tab strip is restored.
    // -------------------------------------------------------------------------
    private static void SplitClickThird(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, \"SCTA\", \"--color\", \"red\");
        GuestInfo pigB = SpawnPig(ctx, \"SCTB\", \"--color\", \"blue\");
        GuestInfo pigC = SpawnPig(ctx, \"SCTC\", \"--color\", \"green\");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, \"3 tabs after capture\");

        long enterOff = TabDockLog.RecordLogLength();
        ClickTabSubmenuItem(ctx, container, pigA.Title, \"Split screen\", pigB.Title);
        ctx.Check(TabDockLog.WaitForLogLine(enterOff, \"SPLIT[enter]\", 3000), \"split entered (A/B, C present)\");
        AssertSplitPanes(ctx, host, pigA, pigB, \"split-click-third enter\");
        ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), \"non-member C hidden during split\");

        AutomationElement? tabC = FindTabText(container, pigC.Title, out int count);
        if (tabC == null || count != 1)
            throw new InvalidOperationException($\"Tab for '{pigC.Title}' not found uniquely (count={count}).\");
        (int tx, int ty) = Uia.Center(tabC);
        if (!EnsureClickable(container, tx, ty))
            throw new InvalidOperationException(\"Could not bring the third tab to a safe clickable state.\");

        long clickOff = TabDockLog.RecordLogLength();
        Input.ClickAt(tx, ty);

        ctx.Check(TabDockLog.WaitForLogLine(clickOff, \"SPLIT[exit]\", 3000),
            \"clicking the third tab exits split\");
        ctx.Check(Util.WaitUntil(() => IsDocked(pigC.Hwnd, host), 5000),
            \"clicked third tab C becomes the full-width docked guest\");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(pigA.Hwnd)
            && !NativeMethods.IsWindowVisible(pigB.Hwnd), 3000),
            \"former split members A and B are hidden after the switch\");
        ctx.Check(TabCount(container) == 3, \"ordinary three-tab strip restored after split exit\");
        ctx.Check(TabDockLog.CountNewLines(clickOff, \"SPLIT[member-gone]\") == 0,
            \"third-tab switch removes no split member\");
        ctx.Check(TabDockLog.CountNewLines(clickOff, \"Released tab\") == 0,
            \"third-tab switch releases no captured window\");

        AutomationElement? tabA = FindTabText(container, pigA.Title, out int aCount);
        if (tabA == null || aCount != 1)
            throw new InvalidOperationException($\"Tab for '{pigA.Title}' not found uniquely after split exit (count={aCount}).\");
        (int ax, int ay) = Uia.Center(tabA);
        if (!EnsureClickable(container, ax, ay))
            throw new InvalidOperationException(\"Could not bring the restored ordinary tab strip to a safe clickable state.\");
        Input.ClickAt(ax, ay);
        ctx.Check(Util.WaitUntil(() => IsDocked(pigA.Hwnd, host), 5000),
            \"ordinary tab switching works after leaving split\");
        ctx.Check(!NativeMethods.IsWindowVisible(pigB.Hwnd) && !NativeMethods.IsWindowVisible(pigC.Hwnd),
            \"normal single-visible-tab semantics restored after the switch\");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, \"EXCEPTION\") == 0, \"no EXCEPTION lines\");
    }

"""
replace_region(split_click_start, split_hover_start, split_click_replacement, "split-click-third region")

click_persist_start = """    // -------------------------------------------------------------------------
    // split-third-tab-click-persists (goal §11): clicking an unrelated third
"""
drag_start = """    // -------------------------------------------------------------------------
    // split-drag-release-render-stability (goal §20/§21): with A+B split,
"""
click_persist_replacement = """    // -------------------------------------------------------------------------
    // Historical CLI alias: this scenario originally asserted that a third-tab
    // click persisted the pair. The corrected contract is the opposite. Keep
    // the registered name to avoid unnecessary shard/CLI churn, but repeatedly
    // prove enter-split -> click-third -> ordinary-tab recovery instead.
    // -------------------------------------------------------------------------
    private static void SplitThirdTabClickPersists(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, \"S3C-A\", \"--color\", \"red\");
        GuestInfo pigB = SpawnPig(ctx, \"S3C-B\", \"--color\", \"blue\");
        GuestInfo pigC = SpawnPig(ctx, \"S3C-C\", \"--color\", \"green\");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, \"3 tabs after capture\");

        int cycles = Math.Max(5, opt.Cycles ?? 5);
        for (int i = 1; i <= cycles; i++)
        {
            long enterOff = TabDockLog.RecordLogLength();
            ClickTabSubmenuItem(ctx, container, pigA.Title, \"Split screen\", pigB.Title);
            ctx.Check(TabDockLog.WaitForLogLine(enterOff, \"SPLIT[enter]\", 3000),
                $\"cycle {i}: split entered\");
            AssertSplitPanes(ctx, host, pigA, pigB, $\"cycle {i}: split enter\");
            ctx.Check(!NativeMethods.IsWindowVisible(pigC.Hwnd), $\"cycle {i}: C hidden while pair is active\");

            AutomationElement? tabC = FindTabText(container, pigC.Title, out int cCount);
            if (tabC == null || cCount != 1)
                throw new InvalidOperationException($\"cycle {i}: tab C not found uniquely (count={cCount}).\");
            (int cx, int cy) = Uia.Center(tabC);
            if (!EnsureClickable(container, cx, cy))
                throw new InvalidOperationException($\"cycle {i}: third tab is obscured — refusing to click blind.\");

            long clickOff = TabDockLog.RecordLogLength();
            Input.ClickAt(cx, cy);
            ctx.Check(TabDockLog.WaitForLogLine(clickOff, \"SPLIT[exit]\", 3000),
                $\"cycle {i}: third-tab click exits split\");
            ctx.Check(Util.WaitUntil(() => IsDocked(pigC.Hwnd, host), 5000),
                $\"cycle {i}: C becomes the full-width active guest\");
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(pigA.Hwnd)
                && !NativeMethods.IsWindowVisible(pigB.Hwnd), 3000),
                $\"cycle {i}: A/B hidden after third-tab switch\");
            ctx.Check(TabCount(container) == 3, $\"cycle {i}: ordinary three-tab strip restored\");
            ctx.Check(TabDockLog.CountNewLines(clickOff, \"SPLIT[member-gone]\") == 0,
                $\"cycle {i}: no structural member removal\");
            ctx.Check(TabDockLog.CountNewLines(clickOff, \"Released tab\") == 0,
                $\"cycle {i}: no captured window released\");

            AutomationElement? tabA = FindTabText(container, pigA.Title, out int aCount);
            if (tabA == null || aCount != 1)
                throw new InvalidOperationException($\"cycle {i}: tab A not found uniquely after exit (count={aCount}).\");
            (int ax, int ay) = Uia.Center(tabA);
            if (!EnsureClickable(container, ax, ay))
                throw new InvalidOperationException($\"cycle {i}: ordinary tab A is obscured — refusing to click blind.\");
            Input.ClickAt(ax, ay);
            ctx.Check(Util.WaitUntil(() => IsDocked(pigA.Hwnd, host), 5000),
                $\"cycle {i}: normal tab A can be activated after split exit\");
            ctx.Check(!NativeMethods.IsWindowVisible(pigB.Hwnd) && !NativeMethods.IsWindowVisible(pigC.Hwnd),
                $\"cycle {i}: single-visible-tab semantics restored\");
            ctx.Check(TabDockLog.CountNewLines(clickOff, \"EXCEPTION\") == 0,
                $\"cycle {i}: no EXCEPTION\");
        }

        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited
            && pigC.Proc != null && !pigC.Proc.HasExited, \"all three pigs alive after the switching cycles\");
    }

"""
replace_region(click_persist_start, drag_start, click_persist_replacement, "split-third-tab-click historical alias region")

replace_once(
    """    // split-composite: the split pair renders as ONE composite tab item; clicking
    // either half focuses that member WITHOUT hiding the partner; clicking a
    // non-member tab keeps the pair active (persistence contract); per-half × and
    // middle-click pop the specific member out and end the split; \"Exit split
    // screen\" restores the ordinary per-tab strip.
""",
    """    // split-composite: the split pair renders as ONE composite tab item; clicking
    // either half focuses that member WITHOUT hiding the partner; clicking a
    // non-member tab exits the split and restores the ordinary per-tab strip;
    // per-half × and middle-click pop the specific member out and end the split.
""",
    "split-composite overview",
)

composite_start = """        // Clicking a NON-member tab leaves the split pair untouched: no exit,
"""
composite_end = """        // Re-enter split (B LEFT, C RIGHT) and pop the RIGHT member via its half ×.
"""
composite_replacement = """        // Clicking the non-member C is an explicit presentation switch. The
        // split exits, C becomes full-width, and all three ordinary tabs return.
        long switchOff = TabDockLog.RecordLogLength();
        AutomationElement? cText = FindTabText(container, pigC.Title, out int cCount);
        if (cText == null || cCount != 1)
            throw new InvalidOperationException($\"Tab '{pigC.Title}' not found uniquely (count={cCount}).\");
        (int cx, int cy) = Uia.Center(cText);
        Input.ClickAt(cx, cy);
        ctx.Check(TabDockLog.WaitForLogLine(switchOff, \"SPLIT[exit]\", 3000),
            \"clicking a non-member tab exits split\");
        ctx.Check(Util.WaitUntil(() => IsDocked(pigC.Hwnd, host), 5000),
            \"non-member C becomes the full-width active guest\");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(pigA.Hwnd)
            && !NativeMethods.IsWindowVisible(pigB.Hwnd), 3000),
            \"former pair members are hidden after the presentation switch\");
        ctx.Check(TabCount(container) == 3, \"ordinary three-tab strip restored after clicking C\");
        ctx.Check(TabDockLog.CountNewLines(switchOff, \"SPLIT[member-gone]\") == 0
            && TabDockLog.CountNewLines(switchOff, \"Released tab\") == 0,
            \"presentation switch does not remove or release a member\");

"""
replace_region(composite_start, composite_end, composite_replacement, "split-composite third-tab section")

path.write_text(text, encoding="utf-8", newline="\n")
print(f"updated {path}")
