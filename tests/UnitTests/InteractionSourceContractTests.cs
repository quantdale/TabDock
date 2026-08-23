using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Narrow source-contract guards for interaction regressions whose failure
/// mechanism lives at the WPF/Win32 wiring boundary rather than in a cheap
/// headless unit seam. These deliberately assert the production call-site
/// invariants so a future refactor cannot silently restore the exact defects
/// fixed by 3591ee3 while lower-level model tests remain green.
/// </summary>
public sealed class InteractionSourceContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void CtrlTab_UsesAuthoritativeActiveTabBinding_NotDisplayTabsIndexWrites()
    {
        string code = Read("Views/ContainerWindow.xaml.cs");
        string handler = Slice(
            code,
            "private void ContainerWindow_PreviewKeyDown",
            "private void ContainerWindow_Closing");

        // Regression 3591ee3: Tabs is the identity/order collection, while the
        // ListBox is bound to DisplayTabs. Once A+B become one split-composite
        // slot, a Tabs-space integer is not a valid DisplayTabs-space integer.
        // The navigation DECISION is owned by TabNavigationPolicy (Wave-0 seam)
        // and returns the authoritative target tab itself; the view resolves it
        // back to its TabViewModel and applies it through the canonical
        // activation paths (SetActiveTab / FocusSplitMember), letting bindings
        // select the visual item. Neither a SelectedIndex write nor any other
        // presentation-space index math may return to this handler.
        Assert.Contains("TabNavigationPolicy.ResolveCtrlTab", handler);
        Assert.DoesNotMatch(
            new Regex(@"TabsListBox\s*\.\s*SelectedIndex\s*="),
            handler);
        Assert.DoesNotMatch(
            new Regex(@"_viewModel\s*\.\s*Tabs\s*\["),
            handler);

        string xaml = Read("Views/ContainerWindow.xaml");
        Assert.Contains("ItemsSource=\"{Binding DisplayTabs}\"", xaml);
        Assert.Contains("SelectedItem=\"{Binding ActiveTab, Mode=OneWay}\"", xaml);
        Assert.Contains("IsSelected\" Value=\"{Binding IsActive, Mode=TwoWay}\"", xaml);
    }

    [Fact]
    public void RefusedPaneCache_OnlyShortCircuitsGuestsThatAreStillVisible()
    {
        // Wave 3C: refusal STORAGE lives behind PaneContainmentCoordinator
        // (keyed by CapturedWindow reference); the DECISION remains in the pure
        // PaneContainmentPolicy. The view must not hold a second copy of either.
        string coordinator = Read("Services/PaneContainmentCoordinator.cs");
        Assert.Matches(
            new Regex(
                @"PaneContainmentPolicy\s*\.\s*ShouldSuppressRepositioning\s*\(\s*guestCurrentlyVisible\s*,\s*refused\s*,\s*requestedRect\s*\)"),
            coordinator);
        Assert.Contains("Dictionary<CapturedWindow, NativeMethods.RECT>", coordinator);
        Assert.DoesNotContain("long", Regex.Match(coordinator, @"private readonly Dictionary<[^>]+>").Value);

        string code = Read("Views/ContainerWindow.xaml.cs");
        Assert.DoesNotContain("_refusedPaneByHwnd", code);
        int invalidations = Regex.Matches(code, @"_paneContainment\s*\.\s*InvalidateAll\s*\(\s*\)").Count;
        Assert.True(invalidations >= 12, $"expected all boundary invalidation sites routed through the owner, found {invalidations}");

        string single = Slice(
            code,
            "private void LayoutShepherdActiveWindow",
            "private void LayoutSplitPanes");
        // Suppression is decided by PaneContainmentPolicy (Wave-0 seam): visible
        // guest + same refused rect => suppress; hidden guest => never. Each
        // call site must feed CURRENT visibility (sampled at decision time) so a
        // guest hidden by container minimize always receives a fresh position
        // attempt on restore instead of being pinned invisible (3591ee3).
        Assert.Matches(
            new Regex(
                @"_paneContainment\s*\.\s*ShouldSuppressRepositioning\s*\(\s*ShepherdActiveWindow\s*,\s*guestCurrentlyVisible\s*:\s*NativeMethods\s*\.\s*IsWindowVisible\s*\(\s*ShepherdActiveWindow\s*\.\s*Hwnd\s*\)"),
            single);
        Assert.Matches(
            new Regex(@"_shepherd\s*\.\s*PositionAndShow\s*\(\s*ShepherdActiveWindow\s*,\s*containerHwnd\s*,\s*rect\s*\)\s*;"),
            single);

        string split = Slice(
            code,
            "private void LayoutSplitPanes",
            "private void EnterSplit");
        Assert.Matches(
            new Regex(
                @"_paneContainment\s*\.\s*ShouldSuppressRepositioning\s*\(\s*top\s*,\s*guestCurrentlyVisible\s*:\s*NativeMethods\s*\.\s*IsWindowVisible\s*\(\s*top\s*\.\s*Hwnd\s*\)"),
            split);
        Assert.Matches(
            new Regex(
                @"_paneContainment\s*\.\s*ShouldSuppressRepositioning\s*\(\s*bottom\s*,\s*guestCurrentlyVisible\s*:\s*NativeMethods\s*\.\s*IsWindowVisible\s*\(\s*bottom\s*\.\s*Hwnd\s*\)"),
            split);
        Assert.Matches(
            new Regex(@"_shepherd\s*\.\s*PositionGuestsDeferred\s*\(\s*top\s*,\s*topRect\s*,\s*bottom\s*,\s*bottomRect\s*,\s*containerHwnd\s*\)\s*;"),
            split);
    }

    [Fact]
    public void ForegroundGrantSequence_ExistsExactlyOnce_InShepherd()
    {
        string code = Read("Services/WindowShepherdService.cs");

        // Wave 2C consolidation: BringToFront and SetForeground previously
        // hand-duplicated SetForegroundWindow -> benign key nudge -> generation
        // revalidation -> retry. The subtle sequence now lives only in
        // TryGrantForeground; callers keep positioning/z-order/telemetry.
        // A second handwritten copy anywhere in the file is a drift regression:
        // exactly one nudge call site (the helper) and no direct
        // SetForegroundWindow outside the helper may exist.
        int nudgeCallSites = Regex.Matches(code, @"(?<!static void )SendBenignKeyNudge\s*\(").Count;
        Assert.Equal(1, nudgeCallSites);

        // Direct native foreground calls live ONLY in the helper (initial
        // attempt + single retry) plus the pre-existing single-line
        // presentation-operations forwarder near the top of the file. A fourth
        // occurrence would mean someone hand-rolled the grant sequence again.
        int setFgCallsWholeFile = Regex.Matches(code, @"NativeMethods\s*\.\s*SetForegroundWindow\s*\(").Count;
        Assert.Equal(3, setFgCallsWholeFile);

        string helper = Slice(
            code,
            "private ForegroundGrantOutcome TryGrantForeground",
            "private static void SendBenignKeyNudge");
        Assert.Equal(2, Regex.Matches(helper, @"SetForegroundWindow\s*\(").Count);

        Assert.Contains("TryGrantForeground(", code);
        Assert.Contains("foreground-before-set", code);
        Assert.Contains("bring-to-front-before-foreground", code);
    }

    [Fact]
    public void ContainerWindow_HasNoHandwrittenOnePixelRectComparisons()
    {
        string code = Read("Views/ContainerWindow.xaml.cs");

        // Wave 2D consolidation: every requested-vs-observed ±1px pane/content
        // comparison routes through PaneContainmentPolicy.MatchesWithinEpsilon
        // (the Wave-0 authority). A second handwritten per-edge epsilon compare
        // in the view risks drifting to a different tolerance/order.
        Assert.DoesNotMatch(
            new Regex(@"Math\s*\.\s*Abs\s*\(\s*\w+\s*\.\s*(left|top|right|bottom)\s*-"),
            code);
        Assert.DoesNotContain("const int epsilon = 1;", code);
        // The authority is genuinely used by the consolidated call sites: three
        // direct MatchesWithinEpsilon pane/content comparisons plus the
        // LayoutUpdated content-rect dirty-check decision, which Wave-DT moved
        // behind PaneContainmentPolicy.ShouldRequestRelayoutForContentRect so
        // the whole per-notification boundary stays headless-testable.
        Assert.Equal(3, Regex.Matches(code, @"PaneContainmentPolicy\s*\.\s*MatchesWithinEpsilon\s*").Count);
        Assert.Single(Regex.Matches(code, @"PaneContainmentPolicy\s*\.\s*ShouldRequestRelayoutForContentRect\s*"));
    }

    [Fact]
    public void ContainerWindow_HasNoHandwrittenReplaceableTimerIdioms()
    {
        string code = Read("Views/ContainerWindow.xaml.cs");

        // Wave 2E consolidation: all five replaceable/coalesced container timers
        // arm through ReplaceableDispatcherTimer, which makes the AUDIT25-05/Q5/
        // Q8 stale-callback guard unavoidable. A handwritten DispatcherTimer with
        // a ReferenceEquals ownership guard must not return to this view.
        Assert.DoesNotContain("new System.Windows.Threading.DispatcherTimer", code);
        Assert.DoesNotContain("new DispatcherTimer", code);
        Assert.DoesNotMatch(
            new Regex(@"ReferenceEquals\s*\(\s*_\w*Timer\s*,"),
            code);
        // The helper slots are the only timers wired here.
        Assert.Equal(5, Regex.Matches(code, @"private readonly ReplaceableDispatcherTimer\s+").Count);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot, relativePath));

    private static string Slice(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker not found: {startMarker}");
        int end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker not found after {startMarker}: {endMarker}");
        return text.Substring(start, end - start);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TabDock.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate TabDock.csproj above test base directory '{AppContext.BaseDirectory}'.");
    }
}
