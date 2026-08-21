using System;
using System.IO;
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
}
