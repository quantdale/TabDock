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
/// Regression coverage for the Tabs-space/DisplayTabs-space index mismatch
/// that caused Ctrl+Tab to land on the wrong tab while a split pair exists
/// (Views/ContainerWindow.xaml.cs ContainerWindow_PreviewKeyDown used to reuse
/// a Tabs-space index directly against the shorter DisplayTabs collection).
/// This locks in the root-cause invariant so no future caller reintroduces a
/// raw Tabs-index write against the DisplayTabs-bound ListBox.
/// </summary>
public class GroupViewModelDisplayTabsTests
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
        string dir = Path.Combine(Path.GetTempPath(), "tabdock-dt-" + Guid.NewGuid().ToString("N"));
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

    private static void TryCleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    [Fact]
    public void SetSplitComposite_MisalignsDisplayTabsIndexFromTabsIndex_ForTabsAfterBothMembers()
    {
        var (dir, vm) = MakeViewModel();
        try
        {
            var a = new TabViewModel(MakeWindow(0));
            var b = new TabViewModel(MakeWindow(1));
            var c = new TabViewModel(MakeWindow(2));
            var d = new TabViewModel(MakeWindow(3));
            vm.Tabs.Add(a);
            vm.Tabs.Add(b);
            vm.Tabs.Add(c);
            vm.Tabs.Add(d);

            // Before the composite exists, both collections are index-aligned.
            Assert.Equal(vm.Tabs.IndexOf(c), vm.DisplayTabs.IndexOf(c));

            vm.SetSplitComposite(a, b);

            // The composite collapses A+B into ONE DisplayTabs slot and
            // suppresses B's own entry, so DisplayTabs is permanently one
            // shorter than Tabs for as long as the composite exists (whether
            // the pair is presented or merely dormant) — any code that reuses
            // a Tabs-space index against DisplayTabs resolves to the wrong
            // item for every tab positioned after both members.
            Assert.Equal(4, vm.Tabs.Count);
            Assert.Equal(3, vm.DisplayTabs.Count);
            Assert.Equal(2, vm.Tabs.IndexOf(c));
            Assert.Equal(1, vm.DisplayTabs.IndexOf(c));
            Assert.Equal(3, vm.Tabs.IndexOf(d));
            Assert.Equal(2, vm.DisplayTabs.IndexOf(d));
            Assert.NotEqual(vm.Tabs.IndexOf(c), vm.DisplayTabs.IndexOf(c));
            Assert.NotEqual(vm.Tabs.IndexOf(d), vm.DisplayTabs.IndexOf(d));

            // Dormant persistence: the composite is not cleared by anything
            // short of an explicit exit/member-removal, so the misalignment
            // outlives "presented" state — ClearSplitComposite is the only
            // path back to index alignment.
            vm.ClearSplitComposite();
            Assert.Equal(vm.Tabs.IndexOf(d), vm.DisplayTabs.IndexOf(d));
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    [Fact]
    public void DormantPair_NonMemberReorderThroughVisibleProjection_PreservesPairIdentity()
    {
        var (dir, vm) = MakeViewModel();
        try
        {
            var a = new TabViewModel(MakeWindow(0));
            var b = new TabViewModel(MakeWindow(1));
            var c = new TabViewModel(MakeWindow(2));
            var d = new TabViewModel(MakeWindow(3));
            // Mirror the production population contract: authoritative members
            // and their view models are added together.
            vm.Model.Members.Add(a.Model);
            vm.Model.Members.Add(b.Model);
            vm.Model.Members.Add(c.Model);
            vm.Model.Members.Add(d.Model);
            vm.Tabs.Add(a);
            vm.Tabs.Add(b);
            vm.Tabs.Add(c);
            vm.Tabs.Add(d);
            vm.SetActiveTab(c);
            vm.SetSplitComposite(a, b); // pair defined but DORMANT

            // Exactly what the drag path snapshots once at drag start: one slot
            // per VISIBLE DisplayTabs item, identity stored by reference.
            SplitCompositeViewModel composite = Assert.IsType<SplitCompositeViewModel>(vm.DisplayTabs[0]);
            var slots = new List<TabStripDragProjection.DragSlot>
            {
                new(50, composite),
                new(150, c),
                new(250, d),
            };

            int? AnchorOf(object item) => item switch
            {
                TabViewModel t => vm.Tabs.IndexOf(t) >= 0 ? vm.Tabs.IndexOf(t) : null,
                SplitCompositeViewModel comp => comp.Left != null && vm.Tabs.IndexOf(comp.Left) >= 0
                    ? vm.Tabs.IndexOf(comp.Left)
                    : null,
                _ => null,
            };

            // Drag C rightward past D's midpoint: boundary resolves past the
            // last slot -> Tabs.Count -> ReorderTabs clamps to the end.
            int? targetPastEnd = TabStripDragProjection.ResolveDropTargetIndex(slots, 400, AnchorOf, vm.Tabs.Count);
            Assert.Equal(vm.Tabs.Count, targetPastEnd);
            int currentIndex = vm.Tabs.IndexOf(c);
            vm.ReorderTabs(currentIndex, targetPastEnd!.Value);

            // No exception; C/D reordered; authoritative and VM agree.
            Assert.Equal(new[] { "guest0.exe", "guest1.exe", "guest3.exe", "guest2.exe" },
                vm.Model.Members.Select(m => m.ExePath).ToList());
            Assert.Same(c, vm.ActiveTab);

            // The pair survived untouched: same composite instance, same member
            // references, still one-shorter projection.
            SplitCompositeViewModel after = Assert.IsType<SplitCompositeViewModel>(vm.DisplayTabs[0]);
            Assert.Same(composite, after);
            Assert.Same(a, after.Left);
            Assert.Same(b, after.Right);
            Assert.Equal(4, vm.Tabs.Count);
            Assert.Equal(3, vm.DisplayTabs.Count);
            Assert.Equal(new object[] { composite, vm.Tabs[2], vm.Tabs[3] }, vm.DisplayTabs.ToList());

            // A structural count change (the other half of the H2 rule) would
            // invalidate the snapshot; a reorder changed none of them.
            Assert.Equal(slots.Count, vm.DisplayTabs.Count);

            // Clearing the pair restores ordinary alignment; the member
            // instances are unchanged, so a later split resume still targets
            // the same A/B identities.
            vm.ClearSplitComposite();
            Assert.Equal(vm.Tabs.Count, vm.DisplayTabs.Count);
            Assert.Same(a, vm.Tabs[0]);
            Assert.Same(b, vm.Tabs[1]);
            Assert.Contains(c, vm.Tabs);
            Assert.Contains(d, vm.Tabs);
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    [Fact]
    public void SplitComposite_AccessibleNameTracksMemberTitleChanges()
    {
        var (dir, vm) = MakeViewModel();
        try
        {
            var left = new TabViewModel(MakeWindow(0));
            var right = new TabViewModel(MakeWindow(1));
            vm.Tabs.Add(left);
            vm.Tabs.Add(right);
            vm.SetSplitComposite(left, right);

            var composite = Assert.IsType<SplitCompositeViewModel>(vm.DisplayTabs[0]);
            Assert.Contains("Guest 0", composite.AutomationName, StringComparison.Ordinal);

            left.Model.OriginalTitle = "Renamed guest";
            left.RefreshTitle();

            Assert.Contains("Renamed guest", composite.AutomationName, StringComparison.Ordinal);
        }
        finally
        {
            TryCleanup(dir);
        }
    }
}
