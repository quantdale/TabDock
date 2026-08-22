using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Regression coverage for the Ctrl+Tab navigation DECISION path (Wave 0A).
/// The view delegates to <see cref="TabNavigationPolicy"/> and applies the
/// returned authoritative target tab; no presentation-space index is ever part
/// of the result, so a reintroduction of a raw Tabs-space index write against
/// the DisplayTabs-bound ListBox (the pre-3591ee3 defect) cannot pass through
/// this seam. TabNavigationSourceContractTests additionally locks the wiring
/// itself in place.
/// </summary>
public class TabNavigationPolicyTests
{
    private static CapturedWindow W(int i) => new()
    {
        Hwnd = new IntPtr(0xA000 + i),
        ProcessId = 8000u + (uint)i,
        WindowThreadId = 9000u + (uint)i,
        WindowIdentityToken = 10000 + i,
        ExePath = $"guest{i}.exe",
        OriginalClassName = "Pig",
        OriginalTitle = "Guest " + i,
    };

    private static TabNavigationPolicy.Decision Resolve(
        IReadOnlyList<CapturedWindow> tabs,
        CapturedWindow? active,
        bool backward,
        bool presented = false,
        CapturedWindow? left = null,
        CapturedWindow? right = null,
        CapturedWindow? foreground = null)
        => TabNavigationPolicy.ResolveCtrlTab(tabs, active, backward, presented, left, right, foreground);

    // ---- ordinary cycling -------------------------------------------------

    [Fact]
    public void OrdinaryForward_ActivatesNextTabInOrder()
    {
        var a = W(0); var b = W(1); var c = W(2);
        var d = Resolve(new[] { a, b, c }, a, backward: false);
        Assert.Equal(TabNavigationPolicy.NavigationKind.ActivateTab, d.Kind);
        Assert.Same(b, d.Target);
    }

    [Fact]
    public void OrdinaryBackward_ActivatesPreviousTab()
    {
        var a = W(0); var b = W(1); var c = W(2);
        var d = Resolve(new[] { a, b, c }, b, backward: true);
        Assert.Equal(TabNavigationPolicy.NavigationKind.ActivateTab, d.Kind);
        Assert.Same(a, d.Target);
    }

    [Fact]
    public void ForwardFromLast_WrapsToFirst()
    {
        var a = W(0); var b = W(1); var c = W(2);
        var d = Resolve(new[] { a, b, c }, c, backward: false);
        Assert.Same(a, d.Target);
    }

    [Fact]
    public void BackwardFromFirst_WrapsToLast()
    {
        var a = W(0); var b = W(1); var c = W(2);
        var d = Resolve(new[] { a, b, c }, a, backward: true);
        Assert.Same(c, d.Target);
    }

    [Fact]
    public void NoActiveTab_AnchorsAtFirstTab_ForwardActivatesSecond()
    {
        var a = W(0); var b = W(1); var c = W(2);
        var d = Resolve(new[] { a, b, c }, null, backward: false);
        Assert.Same(b, d.Target);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void FewerThanTwoTabs_NotNavigable(int count)
    {
        var tabs = Enumerable.Range(0, count).Select(W).ToArray();
        var d = Resolve(tabs, tabs.Length > 0 ? tabs[0] : null, backward: false);
        Assert.Equal(TabNavigationPolicy.NavigationKind.NotNavigable, d.Kind);
        Assert.Null(d.Target);
    }

    // ---- presented split pair ---------------------------------------------

    [Fact]
    public void PresentedPair_ForegroundLeft_FocusesRight_IgnoringForeignTabsBeforeAndAfter()
    {
        var x = W(5); var a = W(0); var y = W(6); var b = W(1); var z = W(7);
        var tabs = new[] { x, a, y, b, z };
        var d = Resolve(tabs, x, backward: false, presented: true, left: a, right: b, foreground: a);
        Assert.Equal(TabNavigationPolicy.NavigationKind.FocusSplitMember, d.Kind);
        Assert.Same(b, d.Target);
    }

    [Fact]
    public void PresentedPair_ForegroundRight_FocusesLeft()
    {
        var a = W(0); var b = W(1);
        var d = Resolve(new[] { a, b }, a, backward: true, presented: true, left: a, right: b, foreground: b);
        Assert.Equal(TabNavigationPolicy.NavigationKind.FocusSplitMember, d.Kind);
        Assert.Same(a, d.Target);
    }

    [Fact]
    public void PresentedPair_NoFocusedMember_AnchorsOnLeftPartner()
    {
        var a = W(0); var b = W(1);
        var d = Resolve(new[] { a, b }, null, backward: false, presented: true, left: a, right: b, foreground: null);
        Assert.Same(a, d.Target);
    }

    [Fact]
    public void PresentedPair_RepeatedResolve_AlternatesBetweenMembers()
    {
        var a = W(0); var b = W(1); var c = W(2);
        var tabs = new[] { c, a, b };
        var first = Resolve(tabs, a, backward: false, presented: true, left: a, right: b, foreground: a);
        Assert.Same(b, first.Target);
        var second = Resolve(tabs, b, backward: false, presented: true, left: a, right: b, foreground: first.Target);
        Assert.Same(a, second.Target);
    }

    // ---- dormant split relationship ----------------------------------------

    [Fact]
    public void DormantPair_NonMemberActive_OrdinaryCyclingReachesMembers()
    {
        var x = W(5); var a = W(0); var b = W(1); var y = W(6);
        var tabs = new[] { x, a, b, y };
        var d = Resolve(tabs, x, backward: false, presented: false, left: a, right: b);
        Assert.Equal(TabNavigationPolicy.NavigationKind.ActivateTab, d.Kind);
        Assert.Same(a, d.Target);
    }

    [Fact]
    public void DormantPair_MemberActive_ContinuesPastPartnerToAdjacentNonMember()
    {
        var a = W(0); var b = W(1); var y = W(6);
        var tabs = new[] { a, b, y };
        // Active member A while dormant: navigation proceeds ordinarily onto
        // B (the partner); resuming the pair is owned by the active-tab change
        // path, not by keyboard navigation.
        var d = Resolve(tabs, a, backward: false, presented: false, left: a, right: b);
        Assert.Equal(TabNavigationPolicy.NavigationKind.ActivateTab, d.Kind);
        Assert.Same(b, d.Target);

        var d2 = Resolve(tabs, b, backward: false, presented: false, left: a, right: b);
        Assert.Same(y, d2.Target);
    }

    [Fact]
    public void DormantPair_BackwardFromMember_WrapsToLastNonMember()
    {
        var y = W(6); var a = W(0); var b = W(1);
        var tabs = new[] { y, a, b };
        var d = Resolve(tabs, a, backward: true, presented: false, left: a, right: b);
        Assert.Same(y, d.Target);
    }
}

/// <summary>
/// Locks the Wave-0A wiring itself: ContainerWindow_PreviewKeyDown must route
/// its Ctrl+Tab decision through <see cref="TabNavigationPolicy"/> and must
/// never write a presentation-space SelectedIndex against the tab strip (the
/// exact mechanism of the fixed Ctrl+Tab misdirection bug).
/// </summary>
public class TabNavigationSourceContractTests
{
    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TabDock.sln not found above test output directory.");
    }

    [Fact]
    public void PreviewKeyDown_RoutesThroughTabNavigationPolicy()
    {
        string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "ContainerWindow.xaml.cs"));
        Assert.Contains("TabNavigationPolicy.ResolveCtrlTab", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TabStrip_NeverReceivesManualSelectedIndexWrites()
    {
        string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "ContainerWindow.xaml.cs"));
        Assert.DoesNotContain("TabsListBox.SelectedIndex", source, StringComparison.Ordinal);
    }
}

/// <summary>
/// Integration-shape check: the policy operates on the real group view-model's
/// authoritative <see cref="GroupViewModel.Tabs"/> order and keeps targeting
/// the correct MODEL even while the split composite makes DisplayTabs indices
/// diverge from Tabs indices (the collection shape locked by
/// GroupViewModelDisplayTabsTests).
/// </summary>
public class TabNavigationIntegrationTests
{
    private static CapturedWindow MakeWindow(int i) => new()
    {
        Hwnd = new IntPtr(0x7000 + i),
        ProcessId = 8000u + (uint)i,
        WindowThreadId = 9000u + (uint)i,
        WindowIdentityToken = 10000 + i,
        ExePath = $"guest{i}.exe",
        OriginalClassName = "Pig",
        OriginalTitle = "Guest " + i,
    };

    private static (string dir, GroupViewModel vm) MakeViewModel()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tabdock-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var log = new LoggingService(Path.Combine(dir, "logs"));
        var shepherd = new WindowShepherdService(log, Path.Combine(dir, "hidden-windows.json"));
        var persistence = new PersistenceService(log, Path.Combine(dir, "state.json"));
        var manager = new GroupManager(shepherd, persistence, log);
        var icons = new IconService(log);
        var group = new Group { Name = "test" };
        var vm = new GroupViewModel(group, manager, icons, log);
        return (dir, vm);
    }

    [Fact]
    public void WithCompositePresent_PolicyTargetsCorrectModel_FromRealTabsOrder()
    {
        var (dir, vm) = MakeViewModel();
        try
        {
            var ta = new TabViewModel(MakeWindow(0));
            var tb = new TabViewModel(MakeWindow(1));
            var tc = new TabViewModel(MakeWindow(2));
            var td = new TabViewModel(MakeWindow(3));
            vm.Tabs.Add(ta);
            vm.Tabs.Add(tb);
            vm.Tabs.Add(tc);
            vm.Tabs.Add(td);

            vm.SetSplitComposite(ta, tb);

            // DisplayTabs is now one shorter than Tabs; any index computed in
            // Tabs space and applied to DisplayTabs lands on the wrong item
            // for every position after both members.
            Assert.NotEqual(vm.Tabs.IndexOf(td), vm.DisplayTabs.IndexOf(td));

            // From the last tab, forward Ctrl+Tab must wrap to the FIRST tab's
            // model — resolved through the policy over the real Tabs order.
            var decision = TabNavigationPolicy.ResolveCtrlTab(
                vm.Tabs.Select(t => t.Model).ToArray(),
                vm.ActiveTab?.Model ?? td.Model,
                backward: false,
                splitPresented: false,
                splitLeft: null,
                splitRight: null,
                splitForeground: null);

            Assert.Equal(TabNavigationPolicy.NavigationKind.ActivateTab, decision.Kind);
            Assert.Same(ta.Model, decision.Target);

            // And activating it goes through SetActiveTab (binding-chain sync),
            // never through a ListBox index write.
            vm.SetActiveTab(vm.Tabs.First(t => ReferenceEquals(t.Model, decision.Target)));
            Assert.Same(ta, vm.ActiveTab);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
