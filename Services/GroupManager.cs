using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Owns all groups and coordinates capture/release, tab switching/reordering,
/// group close, and emergency release. Enforces the flat no-nesting rule.
/// </summary>
public sealed class GroupManager
{
    private readonly WindowShepherdService _shepherd;
    private readonly PersistenceService _persistence;
    private readonly LoggingService _log;
    private readonly HashSet<IntPtr> _ownContainerHwnds = new();
    private readonly object _lock = new();

    /// <summary>O(1) index enabling WinEventMonitor's firehose of desktop-wide events to rapidly filter captured windows (PERF25-02).</summary>
    // O(1) HWND -> owning group + member index over every group's Members
    // collection (PERF25-02). WinEventMonitor's filter consults this for EVERY
    // system-wide destroy, hide, name-change, minimize, foreground and
    // move/size event — a firehose that includes every menu and tooltip in
    // every running process — and each of App's WinEvent handlers then repeated
    // the same search with Groups.ToList() + FirstOrDefault. Both were an
    // O(groups x members) scan plus per-event allocations on the single hottest
    // path in the app; they are now one dictionary probe.
    //
    // The index is maintained from CollectionChanged rather than from the
    // mutating call sites: members are added through GroupViewModel and removed
    // through several distinct release paths, so hooking the collections is the
    // only way to make it structurally impossible for the index to drift from
    // Group.Members. UI thread only, exactly like the collections it mirrors.
    private readonly Dictionary<IntPtr, CapturedMember> _capturedIndex = new();
    private readonly Dictionary<Group, NotifyCollectionChangedEventHandler> _memberHandlers = new();
    private bool _monitoringNeeded;
    private bool _captureAllowed = true;

    /// <summary>An indexed captured window together with the group that owns it.</summary>
    public readonly struct CapturedMember
    {
        public CapturedMember(Group group, CapturedWindow window)
        {
            Group = group;
            Window = window;
        }

        public Group Group { get; }
        public CapturedWindow Window { get; }
    }

    /// <summary>
    /// Raised when the number of captured windows crosses between zero and
    /// non-zero. App uses it to run the out-of-process WinEvent hooks only while
    /// there is actually something to observe (PERF25-03).
    /// </summary>
    public event EventHandler? MonitoringNeededChanged;

    /// <summary>
    /// True while at least one window is captured. With no captured windows
    /// every WinEvent hook callback is guaranteed to be filtered out, so leaving
    /// the hooks installed only pays the cost of marshalling desktop-wide UI
    /// events into this process for nothing.
    /// </summary>
    public bool IsMonitoringNeeded => _capturedIndex.Count > 0;

    /// <summary>
    /// Capture admission is disabled when the lifecycle monitor has failed
    /// permanently. Existing guests are released before this becomes the
    /// steady state, so the UI cannot silently create unsupported captures.
    /// </summary>
    public bool CaptureAllowed => _captureAllowed;

    public ObservableCollection<Group> Groups { get; } = new();

    public GroupManager(WindowShepherdService shepherd, PersistenceService persistence, LoggingService log)
    {
        _shepherd = shepherd;
        _persistence = persistence;
        _log = log;
        Groups.CollectionChanged += OnGroupsChanged;
    }

    public void RestoreState()
    {
        foreach (var group in _persistence.Load())
        {
            Groups.Add(group);
        }
    }

    public void SaveState()
    {
        _saveDebounce?.Stop();
        _persistence.Save(Groups);
    }

    public void SetCaptureAllowed(bool allowed, string reason)
    {
        if (_captureAllowed == allowed)
            return;
        _captureAllowed = allowed;
        _log.Log($"Capture admission {(allowed ? "enabled" : "disabled")}: {reason}");
    }

    private DispatcherTimer? _saveDebounce;

    /// <summary>
    /// Debounced SaveState: persists high-frequency layout churn ~1s after the
    /// most recent event without a disk write per intermediate drag/reorder
    /// mutation. Discrete semantic mutations use <see cref="RequestDurableSave"/>
    /// instead. UI thread only (DispatcherTimer).
    /// </summary>
    public void RequestSave()
    {
        if (_saveDebounce == null)
        {
            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _saveDebounce.Tick += (_, _) =>
            {
                _saveDebounce!.Stop();
                _persistence.Save(Groups);
            };
        }
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    /// <summary>
    /// Persists a completed semantic mutation immediately. High-frequency
    /// geometry/reorder activity continues to use <see cref="RequestSave"/>;
    /// capture, release, group metadata, active-tab selection, and completed
    /// reorder operations are durable boundaries that must not sit inside the
    /// one-second force-kill window.
    /// </summary>
    public void RequestDurableSave(string reason)
    {
        _saveDebounce?.Stop();
        _persistence.Save(Groups);
        _log.Log($"Persisted semantic state change: {reason}");
    }

    public void RegisterContainerHwnd(IntPtr hwnd)
    {
        lock (_lock) { _ownContainerHwnds.Add(hwnd); }
    }

    public void UnregisterContainerHwnd(IntPtr hwnd)
    {
        lock (_lock) { _ownContainerHwnds.Remove(hwnd); }
    }

    public bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        lock (_lock)
        {
            if (_ownContainerHwnds.Contains(hwnd))
                return true;

            // Own-process check before the ancestor walk: it is the broader of
            // the two (every container HWND is in this process anyway), and the
            // picker runs this for every top-level window on the desktop.
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == NativeMethods.CurrentProcessId)
                return true;

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            return root != IntPtr.Zero && _ownContainerHwnds.Contains(root);
        }
    }

    /// <summary>
    /// True if the HWND is a live captured member of any group. Works by value
    /// comparison only, so it is also valid for HWNDs that were just destroyed.
    /// Must be called on the UI thread (Groups/Members are mutated there).
    /// </summary>
    public bool IsCapturedWindow(IntPtr hwnd)
    {
        return hwnd != IntPtr.Zero && _capturedIndex.ContainsKey(hwnd);
    }

    /// <summary>
    /// Verifies a live member before a caller performs a native operation that
    /// is not owned by WindowShepherdService itself (for example WM_CLOSE).
    /// </summary>
    public bool IsCurrentCapturedWindow(CapturedWindow member)
        => _shepherd.IsCurrentCapturedWindow(member);

    /// <summary>
    /// Resolves the live member object for WinEvent correlation. Unlike an
    /// HWND-only lookup, retaining this reference lets the monitor reject a
    /// queued event if the handle was released and recycled before dispatch.
    /// </summary>
    public CapturedWindow? GetCapturedWindow(IntPtr hwnd)
        => _capturedIndex.TryGetValue(hwnd, out CapturedMember entry) ? entry.Window : null;

    /// <summary>
    /// Resolves an HWND to its captured member and owning group in one probe.
    /// An HWND can only ever be in one group (enforced by the picker's filter
    /// and by ContainerWindow.CaptureWindow), so this returns the same result
    /// the old scan-every-group loops did, without the per-event snapshot list
    /// and predicate closures those needed. UI thread only.
    /// </summary>
    public bool TryGetCapturedMember(IntPtr hwnd, [MaybeNullWhen(false)] out Group group, [MaybeNullWhen(false)] out CapturedWindow member)
    {
        if (hwnd != IntPtr.Zero && _capturedIndex.TryGetValue(hwnd, out CapturedMember entry))
        {
            group = entry.Group;
            member = entry.Window;
            return true;
        }

        group = null;
        member = null;
        return false;
    }

    #region Captured-window index (PERF25-02)

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RebuildIndex();
            return;
        }

        if (e.OldItems != null)
        {
            foreach (Group group in e.OldItems)
                DetachGroup(group);
        }
        if (e.NewItems != null)
        {
            foreach (Group group in e.NewItems)
                AttachGroup(group);
        }

        NotifyMonitoringNeeded();
    }

    private void AttachGroup(Group group)
    {
        if (_memberHandlers.ContainsKey(group))
            return;

        // One handler instance per group so the group identity is captured for
        // the index without needing a collection-to-group reverse map, and so
        // it can be unsubscribed again on removal.
        NotifyCollectionChangedEventHandler handler = (_, args) => OnMembersChanged(group, args);
        _memberHandlers[group] = handler;
        group.Members.CollectionChanged += handler;

        foreach (CapturedWindow member in group.Members)
            _capturedIndex[member.Hwnd] = new CapturedMember(group, member);
    }

    private void DetachGroup(Group group)
    {
        if (_memberHandlers.TryGetValue(group, out NotifyCollectionChangedEventHandler? handler))
        {
            group.Members.CollectionChanged -= handler;
            _memberHandlers.Remove(group);
        }

        foreach (CapturedWindow member in group.Members)
            RemoveFromIndex(member);
    }

    private void OnMembersChanged(Group group, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RebuildIndex();
            return;
        }

        // Order matters for Move/Replace, where the same instance appears in
        // both lists: remove first, then re-add.
        if (e.OldItems != null)
        {
            foreach (CapturedWindow member in e.OldItems)
                RemoveFromIndex(member);
        }
        if (e.NewItems != null)
        {
            foreach (CapturedWindow member in e.NewItems)
                _capturedIndex[member.Hwnd] = new CapturedMember(group, member);
        }

        NotifyMonitoringNeeded();
    }

    private void RemoveFromIndex(CapturedWindow member)
    {
        // Only drop the entry if it still points at THIS member. A recycled
        // HWND value re-captured into another group before the old member's
        // removal is processed must not have its live entry deleted.
        if (_capturedIndex.TryGetValue(member.Hwnd, out CapturedMember entry) && ReferenceEquals(entry.Window, member))
            _capturedIndex.Remove(member.Hwnd);
    }

    /// <summary>
    /// Full rebuild, used for the Reset notification a Clear() raises (it
    /// carries no item lists, so incremental maintenance is impossible).
    /// </summary>
    private void RebuildIndex()
    {
        foreach (KeyValuePair<Group, NotifyCollectionChangedEventHandler> pair in _memberHandlers)
            pair.Key.Members.CollectionChanged -= pair.Value;
        _memberHandlers.Clear();
        _capturedIndex.Clear();

        foreach (Group group in Groups)
            AttachGroup(group);

        NotifyMonitoringNeeded();
    }

    private void NotifyMonitoringNeeded()
    {
        bool needed = _capturedIndex.Count > 0;
        if (needed == _monitoringNeeded)
            return;
        _monitoringNeeded = needed;
        MonitoringNeededChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    public Group CreateGroup(string name = "Group", string accentColor = "#2196F3")
    {
        var group = new Group { Name = name, AccentColor = accentColor };
        Groups.Add(group);
        _log.Log($"Created group {group.Id} '{name}'");
        DiagnosticRuntime.Record("group.create", group: group.Id.ToString("N"), action: "create", result: "success");
        RequestDurableSave("group-created");
        return group;
    }

    public void SwitchActiveTab(Group group, int index)
    {
        if (index < 0 || index >= group.Members.Count)
            return;
        group.ActiveIndex = index;
        _log.Log($"Switched group {group.Id} to tab {index}");
        DiagnosticRuntime.Record("group.active-tab", group: group.Id.ToString("N"), action: "switch", result: "success",
            data: new Dictionary<string, string> { ["index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        RequestDurableSave("active-tab-selected");
    }

    public void MoveTab(Group group, int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= group.Members.Count)
            return;
        if (newIndex < 0 || newIndex >= group.Members.Count)
            return;
        if (oldIndex == newIndex)
            return;

        var item = group.Members[oldIndex];
        group.Members.RemoveAt(oldIndex);
        group.Members.Insert(newIndex, item);
        group.ActiveIndex = newIndex;
        _log.Log($"Reordered tab {oldIndex}->{newIndex} in group {group.Id}");
        DiagnosticRuntime.Record("group.reorder", group: group.Id.ToString("N"), action: "reorder", result: "success",
            data: new Dictionary<string, string>
            {
                ["oldIndex"] = oldIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["newIndex"] = newIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        RequestSave();
    }

    public void CommitReorder(string reason = "reorder-completed")
    {
        RequestDurableSave(reason);
    }

    public WindowReleaseOutcome ReleaseTab(Group group, int index, bool show = true)
    {
        if (index < 0 || index >= group.Members.Count)
            return WindowReleaseOutcome.TargetGoneOrRecycled;

        var cw = group.Members[index];
        int activeIndex = group.ActiveIndex;
        WindowReleaseOutcome outcome = _shepherd.Release(cw, show);
        if (outcome == WindowReleaseOutcome.RecoveryPending)
        {
            _log.Log($"Release retained tab {index} in group {group.Id}: native recovery is pending and journal evidence was preserved.");
            DiagnosticRuntime.Record("guest.release", guest: cw.Hwnd, group: group.Id.ToString("N"), action: "release", result: "recovery-pending");
            return outcome;
        }

        // WindowShepherdService verifies and completes the native transaction
        // before this collection mutation. An unverifiable identity therefore
        // cannot produce a detached logical member with no recovery evidence.
        group.Members.RemoveAt(index);

        // ActiveIndex is positional, and removing a member does not re-run its
        // setter, so releasing a tab AHEAD of the active one shifts the active
        // member down a slot while the index stays put — silently renaming the
        // active tab to its neighbour. Follow the member instead. Only when the
        // active member itself was the one released does the index have to move,
        // clamped for the released-the-last-tab case.
        if (index < activeIndex)
            group.ActiveIndex = activeIndex - 1;
        else if (group.ActiveIndex >= group.Members.Count)
            group.ActiveIndex = group.Members.Count - 1;

        _log.Log($"Released tab {index} from group {group.Id}");
        DiagnosticRuntime.Record("guest.release", guest: cw.Hwnd, group: group.Id.ToString("N"), action: "release", result: "success");
        RequestDurableSave("tab-released");
        return outcome;
    }

    /// <summary>
    /// Releases a specific captured member back to standalone by reference.
    /// Callers that hold a <see cref="CapturedWindow"/> (e.g. WinEvent-driven
    /// teardown) would otherwise have to re-derive the positional index from
    /// <see cref="Group.Members"/> themselves — a lookup that lived in two
    /// different collections across two call paths before this method existed.
    /// </summary>
    public WindowReleaseOutcome ReleaseMember(Group group, CapturedWindow member, bool show = true)
    {
        int index = group.Members.IndexOf(member);
        if (index >= 0)
            return ReleaseTab(group, index, show);
        return WindowReleaseOutcome.TargetGoneOrRecycled;
    }

    public bool CloseGroup(Group group)
    {
        // Release in reverse so indices stay stable.
        bool allReleased = true;
        for (int index = group.Members.Count - 1; index >= 0; index--)
        {
            WindowReleaseOutcome outcome = ReleaseTab(group, index, show: true);
            if (outcome == WindowReleaseOutcome.RecoveryPending)
            {
                allReleased = false;
                // Keep the pending member in the collection and move on to
                // older members. One uncertain guest must not prevent safe
                // cleanup of the others or cause an infinite retry loop.
            }
        }

        if (allReleased && Groups.Contains(group))
            Groups.Remove(group);

        _log.Log(allReleased
            ? $"Closed group {group.Id}"
            : $"Close group {group.Id} retained one or more members pending native recovery.");
        DiagnosticRuntime.Record("group.close", group: group.Id.ToString("N"), action: "close",
            result: allReleased ? "success" : "recovery-pending");
        RequestDurableSave(allReleased ? "group-closed" : "group-close-recovery-pending");
        return allReleased;
    }

    public void RemoveGroup(Group group)
    {
        if (Groups.Contains(group))
        {
            Groups.Remove(group);
            RequestDurableSave("group-deleted");
        }
    }

    public void EmergencyReleaseAll()
    {
        _log.Log("EMERGENCY RELEASE: releasing all captured windows.");
        try
        {
            foreach (var group in Groups.ToList())
            {
                for (int i = group.Members.Count - 1; i >= 0; i--)
                {
                    CapturedWindow cw = group.Members[i];
                    try
                    {
                        WindowReleaseOutcome outcome = ReleaseTab(group, i, show: true);
                        if (outcome == WindowReleaseOutcome.RecoveryPending)
                            _log.Log($"EmergencyReleaseAll retained 0x{cw.Hwnd.ToInt64():X} in group {group.Id}: recovery pending.");
                    }
                    catch (Exception ex)
                    {
                        _log.LogException("EmergencyReleaseAll", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogException("EmergencyReleaseAll enumeration", ex);
        }
    }

    /// <summary>
    /// Converts the currently live members into persisted layout intent and
    /// removes them from the captured index. Session-ending can be cancelled
    /// after the emergency release; leaving the members in the model would
    /// make the UI claim that released windows were still captured, while
    /// clearing them without copying their metadata would make a later save
    /// erase the state that was written just before the release.
    /// </summary>
    public void ClearCapturedMembersAfterSessionEnding()
    {
        foreach (Group group in Groups.ToList())
        {
            if (group.Members.Count == 0)
                continue;

            group.PersistedTabs.Clear();
            foreach (CapturedWindow member in group.Members)
            {
                group.PersistedTabs.Add(new PersistedTabMetadata
                {
                    ExePath = member.ExePath,
                    OriginalTitle = member.OriginalTitle,
                    CustomLabel = member.CustomLabel,
                    Left = member.OriginalBounds.left,
                    Top = member.OriginalBounds.top,
                    Right = member.OriginalBounds.right,
                    Bottom = member.OriginalBounds.bottom,
                    WasMaximized = member.WasMaximized,
                });
            }
            group.PersistedActiveIndex = group.ActiveIndex;
            group.Members.Clear();
        }
    }
}
