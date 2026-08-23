using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabDock.Models;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Direct deterministic coverage for the GroupViewModel mutation paths —
/// ReorderTabs, CommitReorder, and ReleaseTab — executing the actual public
/// methods over the REAL production stack (GroupManager + WindowShepherdService
/// on the shared fake native APIs + PersistenceService on a temp state.json).
/// No live Win32 windows are involved; release outcomes are scripted through
/// the same fake identity APIs the shepherd transaction tests use.
///
/// Pinned here: the historical ReorderTabs clamp crash boundary (an unclamped
/// destination once made MoveTab reject the move while the VM's insert threw
/// ArgumentOutOfRangeException and killed the app), reference-identity
/// active-tab preservation across releases, RecoveryPending fail-closed
/// retention versus non-pending removal contracts, EmptiedByPopOut
/// exactly-once, and durable CommitReorder semantics.
/// </summary>
public sealed class GroupViewModelMutationTests : IDisposable
{
    private readonly string _root;
    private readonly LoggingService _log;
    private readonly Dictionary<int, ShepherdFakeIdentityApi> _identityBySlot;
    private readonly PersistenceService _persistence;
    private readonly GroupViewModel _vm;
    private int _popOutCount;

    public GroupViewModelMutationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TabDock-gvm-mutation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new LoggingService(Path.Combine(_root, "logs"));
        string journalPath = Path.Combine(_root, "hidden-windows.json");
        var entries = new List<HiddenWindowEntry>();
        _identityBySlot = new Dictionary<int, ShepherdFakeIdentityApi>();
        var members = new List<CapturedWindow>();
        ShepherdFakeIdentityApi? rootIdentity = null;
        var releaseApi = new ShepherdFakeReleaseApi();

        for (int i = 0; i < 4; i++)
        {
            long hwnd = i + 1;
            uint pid = (uint)(11 * (i + 1));
            long start = 101L * (i + 1);
            long token = 1001L * (i + 1);
            members.Add(ReleaseTestFixture.CapturedWindowFor(hwnd, pid, token, start));
            entries.Add(ReleaseTestFixture.JournalEntryFor(members[i]));
            ShepherdFakeIdentityApi identity = ShepherdFakeIdentityApi.For(hwnd, pid, start, token);
            _identityBySlot[i] = identity;
            if (rootIdentity == null) rootIdentity = identity; else rootIdentity.Add(identity);
        }

        var shepherd = new WindowShepherdService(
            _log, journalPath, rootIdentity!, new FakeMonitorDpiProbe(), releaseApi);
        var journalFile = new HiddenWindowJournalFile { Version = 3, Entries = entries };
        File.WriteAllText(journalPath, JsonSerializer.Serialize(journalFile, TabDockJsonContext.Default.HiddenWindowJournalFile));
        foreach (CapturedWindow member in members)
            shepherd.BindCapturedWindowForTesting(member);

        _persistence = new PersistenceService(_log, Path.Combine(_root, "state.json"));
        var manager = new GroupManager(shepherd, _persistence, _log);
        var group = new Group { Name = "mutation", ActiveIndex = 0 };
        foreach (CapturedWindow member in members)
            group.Members.Add(member);
        manager.Groups.Add(group);
        _vm = new GroupViewModel(group, manager, new IconService(_log), _log);
        _vm.EmptiedByPopOut += (_, _) => _popOutCount++;
    }

    public void Dispose()
    {
        _log.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }

    /// <summary>ExePath minted by ReleaseTestFixture.CapturedWindowFor for slot i.</summary>
    private static string Name(int slot) => $"guest-{11 * (slot + 1)}.exe";

    private static IReadOnlyList<string> MemberNames(GroupViewModel vm)
        => vm.Model.Members.Select(m => m.ExePath).ToList();

    private static IReadOnlyList<string> TabNames(GroupViewModel vm)
        => vm.Tabs.Select(t => t.Model.ExePath).ToList();

    private void AssertOrder(params int[] slotsInOrder)
    {
        string[] expected = slotsInOrder.Select(Name).ToArray();
        Assert.Equal(expected, MemberNames(_vm));
        Assert.Equal(expected, TabNames(_vm));
    }

    private void AssertActiveIndexAgreesWithActiveTab()
        => Assert.Equal(_vm.Tabs.IndexOf(_vm.ActiveTab!), _vm.Model.ActiveIndex);

    // ------------------------------------------------------------------
    // ReorderTabs
    // ------------------------------------------------------------------

    [Fact]
    public void ReorderTabs_InvalidOldIndex_IsStrictNoOp()
    {
        _vm.SetActiveTab(_vm.Tabs[2]);

        foreach (int badOldIndex in new[] { -1, _vm.Tabs.Count, _vm.Tabs.Count + 5 })
            _vm.ReorderTabs(badOldIndex, 0);

        AssertOrder(0, 1, 2, 3);
        Assert.Same(_vm.Tabs[2], _vm.ActiveTab);
        Assert.Equal(2, _vm.Model.ActiveIndex);
        Assert.False(File.Exists(_persistence.StatePath), "a rejected reorder must not durably save");
    }

    [Fact]
    public void ReorderTabs_NegativeDestination_IsStrictNoOp()
    {
        _vm.SetActiveTab(_vm.Tabs[1]);

        _vm.ReorderTabs(0, -1);

        AssertOrder(0, 1, 2, 3);
        Assert.Same(_vm.Tabs[1], _vm.ActiveTab);
        Assert.Equal(1, _vm.Model.ActiveIndex);
        Assert.False(File.Exists(_persistence.StatePath));
    }

    [Fact]
    public void ReorderTabs_SameIndex_IsStrictNoOp()
    {
        _vm.SetActiveTab(_vm.Tabs[1]);

        _vm.ReorderTabs(1, 1);

        AssertOrder(0, 1, 2, 3);
        Assert.Same(_vm.Tabs[1], _vm.ActiveTab);
        Assert.False(File.Exists(_persistence.StatePath));
    }

    [Fact]
    public void ReorderTabs_DestinationPastEnd_ClampsToEndInBothModelAndVm()
    {
        // The historical crash boundary: an unclamped destination once made
        // MoveTab reject the move while the VM collection applied it anyway.
        List<TabViewModel> originalInstances = _vm.Tabs.ToList();
        TabViewModel moved = _vm.Tabs[0];

        _vm.ReorderTabs(0, 999);

        AssertOrder(1, 2, 3, 0);
        Assert.Same(moved, _vm.Tabs[^1]);
        // Same four instances, none recreated: any remove/recreate identity
        // churn would break these containment checks.
        Assert.Equal(originalInstances.Count, _vm.Tabs.Count);
        foreach (TabViewModel instance in originalInstances)
            Assert.Contains(instance, _vm.Tabs);
        Assert.Same(moved, _vm.ActiveTab);
        Assert.All(_vm.Tabs, t => Assert.Equal(ReferenceEquals(t, moved), t.IsActive));
        AssertActiveIndexAgreesWithActiveTab();
    }

    [Theory]
    [InlineData(new[] { 3, 0 }, new[] { 3, 0, 1, 2 })] // D -> 0
    [InlineData(new[] { 1, 3 }, new[] { 0, 2, 3, 1 })] // A -> 3
    [InlineData(new[] { 1, 2 }, new[] { 0, 2, 1, 3 })] // B -> 2
    public void ReorderTabs_BackwardAndForward_MaintainsExactModelVmParity(int[] move, int[] expectedOrder)
    {
        _vm.ReorderTabs(move[0], move[1]);

        AssertOrder(expectedOrder);
        Assert.Equal(MemberNames(_vm), TabNames(_vm));
        AssertActiveIndexAgreesWithActiveTab();
    }

    [Fact]
    public void ReorderTabs_WithoutSplit_DisplayTabsStaysOrderEquivalent()
    {
        _vm.ReorderTabs(3, 0);
        _vm.ReorderTabs(1, 2);

        Assert.Equal(_vm.Tabs.Cast<object>().ToList(), _vm.DisplayTabs.ToList());
    }

    // ------------------------------------------------------------------
    // CommitReorder: the durable semantic completion boundary.
    // ------------------------------------------------------------------

    [Fact]
    public void CommitReorder_PersistsFinalOrder_NotIntermediatePositions()
    {
        _vm.ReorderTabs(3, 0);   // [D,A,B,C] — an intermediate drag position
        _vm.ReorderTabs(0, 2);   // final intended order [A,B,D,C]

        _vm.CommitReorder();

        Assert.True(File.Exists(_persistence.StatePath));
        Assert.Equal(new[] { Name(0), Name(1), Name(3), Name(2) }, ReadPersistedOrder());
        Assert.Equal(MemberNames(_vm), ReadPersistedOrder());
    }

    [Fact]
    public void CommitReorder_WithoutPrecedingMove_IsHarmless()
    {
        _vm.CommitReorder();

        Assert.True(File.Exists(_persistence.StatePath));
        Assert.Equal(MemberNames(_vm), ReadPersistedOrder());
    }

    [Fact]
    public void CommitReorder_RepeatedCommit_RemainsCoherent()
    {
        _vm.ReorderTabs(2, 0);
        _vm.CommitReorder();
        IReadOnlyList<string> first = ReadPersistedOrder();

        _vm.CommitReorder();

        Assert.Equal(first, ReadPersistedOrder());
        Assert.Equal(MemberNames(_vm), ReadPersistedOrder());
    }

    private IReadOnlyList<string> ReadPersistedOrder()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_persistence.StatePath));
        return document.RootElement.GetProperty("Groups")[0].GetProperty("Tabs")
            .EnumerateArray()
            .Select(t => t.GetProperty("ExePath").GetString()!)
            .ToList();
    }

    // ------------------------------------------------------------------
    // ReleaseTab: reference-identity pins, not just index pins.
    // ------------------------------------------------------------------

    [Fact]
    public void ReleaseTab_InactiveBeforeActive_KeepsActiveByReference()
    {
        _vm.SetActiveTab(_vm.Tabs[2]); // active = C (index 2)
        TabViewModel active = _vm.ActiveTab!;

        _vm.ReleaseTab(_vm.Tabs[0]); // release A, ahead of C

        AssertOrder(1, 2, 3);
        Assert.Same(active, _vm.ActiveTab); // same C instance, not a re-derived one
        Assert.True(active.IsActive);
        Assert.False(_vm.Tabs[0].IsActive);
        Assert.False(_vm.Tabs[^1].IsActive);
        AssertActiveIndexAgreesWithActiveTab();
        Assert.Equal(0, _popOutCount);
    }

    [Fact]
    public void ReleaseTab_InactiveAfterActive_KeepsActiveByReference()
    {
        _vm.SetActiveTab(_vm.Tabs[1]); // active = B
        TabViewModel active = _vm.ActiveTab!;

        _vm.ReleaseTab(_vm.Tabs[3]); // release D, behind B

        AssertOrder(0, 1, 2);
        Assert.Same(active, _vm.ActiveTab);
        AssertActiveIndexAgreesWithActiveTab();
        Assert.Equal(0, _popOutCount);
    }

    [Theory]
    [InlineData(0)] // first-slot active
    [InlineData(1)] // middle active
    [InlineData(3)] // last-slot active: falls back to the new last tab
    public void ReleaseTab_ActiveOrdinaryTab_SelectsTheNeighbourThatSlidIntoItsSlot(int slot)
    {
        _vm.SetActiveTab(_vm.Tabs[slot]);
        TabViewModel released = _vm.Tabs[slot];
        string releasedExe = released.Model.ExePath;
        int expectedNeighbourSlot = Math.Min(slot, 2); // post-release Tabs.Count == 3

        _vm.ReleaseTab(released);

        Assert.DoesNotContain(releasedExe, MemberNames(_vm));
        Assert.DoesNotContain(released, _vm.Tabs);
        Assert.NotNull(_vm.ActiveTab);
        Assert.Same(_vm.Tabs[expectedNeighbourSlot], _vm.ActiveTab);
        Assert.All(_vm.Tabs, t => Assert.Equal(ReferenceEquals(t, _vm.ActiveTab), t.IsActive));
        AssertActiveIndexAgreesWithActiveTab();
        Assert.Equal(0, _popOutCount);
    }

    [Fact]
    public void ReleaseTab_FinalRemainingTab_RaisesEmptiedByPopOutExactlyOnce()
    {
        _vm.ReleaseTab(_vm.Tabs[3]);
        _vm.ReleaseTab(_vm.Tabs[2]);
        _vm.ReleaseTab(_vm.Tabs[1]);
        TabViewModel last = _vm.Tabs.Single();
        Assert.Single(_vm.Tabs);

        _vm.ReleaseTab(last);

        Assert.Empty(_vm.Tabs);
        Assert.Empty(_vm.Model.Members);
        Assert.Null(_vm.ActiveTab);
        Assert.Equal(1, _popOutCount);
    }

    [Fact]
    public void ReleaseTab_UnknownTabViewModel_IsStrictNoOp()
    {
        _vm.SetActiveTab(_vm.Tabs[1]);
        var stranger = new TabViewModel(ReleaseTestFixture.CapturedWindowFor(99, 999, 9999, 909));

        _vm.ReleaseTab(stranger);

        AssertOrder(0, 1, 2, 3);
        Assert.Same(_vm.Tabs[1], _vm.ActiveTab);
        Assert.DoesNotContain(stranger, _vm.Tabs);
        Assert.Equal(4, _vm.Model.Members.Count);
        Assert.Equal(0, _popOutCount);
    }

    [Fact]
    public void ReleaseTab_RecoveryPending_RetainsEverythingFailClosed()
    {
        _vm.SetActiveTab(_vm.Tabs[2]); // active = C
        TabViewModel retained = _vm.Tabs[0];
        TabViewModel active = _vm.ActiveTab!;
        List<TabViewModel> tabsBefore = _vm.Tabs.ToList();
        _identityBySlot[0].ProcessStartTicks = 0; // strong probe unverifiable -> RecoveryPending

        _vm.ReleaseTab(retained);

        // Non-negotiable: nothing is half-mutated while native recovery pends.
        Assert.Same(retained, _vm.Tabs[0]);
        Assert.Equal(tabsBefore, _vm.Tabs);
        Assert.Contains(retained.Model, _vm.Model.Members);
        Assert.Same(active, _vm.ActiveTab);
        Assert.True(active.IsActive);
        Assert.Equal(2, _vm.Model.ActiveIndex);
        Assert.Equal(4, _vm.Model.Members.Count);
        Assert.Equal(0, _popOutCount);
    }

    [Fact]
    public void ReleaseTab_TargetGoneOrRecycled_RemovesMemberUnlikePending()
    {
        // Distinct deliberate contract: TargetGoneOrRecycled proves the target
        // is verifiably gone/recycled, so the logical member is removed. Only
        // RecoveryPending retains.
        _vm.SetActiveTab(_vm.Tabs[2]);
        TabViewModel gone = _vm.Tabs[0];
        TabViewModel activeBefore = _vm.ActiveTab!;
        _identityBySlot[0].ReplaceGeneration(); // capture token mismatch -> gone/recycled

        _vm.ReleaseTab(gone);

        Assert.Equal(3, _vm.Model.Members.Count);
        Assert.DoesNotContain(gone, _vm.Tabs);
        Assert.Equal(new[] { Name(1), Name(2), Name(3) }, MemberNames(_vm));
        Assert.Same(activeBefore, _vm.ActiveTab);
        AssertActiveIndexAgreesWithActiveTab();
        Assert.Equal(0, _popOutCount);
    }
}
