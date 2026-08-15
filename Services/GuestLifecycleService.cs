using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using TabDock.Models;
using TabDock.Views;

namespace TabDock.Services;

/// <summary>
/// Owns every reaction to <see cref="WinEventMonitor"/>'s captured-guest events
/// (destroy, hide, minimize, move/size, foreground, name change) and the
/// teardown they trigger. This is the single place where a WinEvent is
/// resolved to a captured member and turned into a decision; App wires the
/// module in with one <see cref="Attach"/> call and otherwise stays out of
/// the event path.
///
/// The container registry is injected as the live dictionary App maintains
/// (there is exactly one container implementation — a WPF window — so no
/// interface seam earns its keep; the coupling is the same one App had, just
/// concentrated here with the policy that uses it).
/// </summary>
public sealed class GuestLifecycleService
{
    private readonly GroupManager _groups;
    private readonly Dictionary<Guid, ContainerWindow> _containers;
    private readonly LoggingService _log;

    // Debounces EVENT_OBJECT_NAMECHANGE storms (see DebounceNameChanged).
    private readonly Dictionary<IntPtr, (DispatcherTimer Timer, CapturedWindow Member)> _nameChangeDebounce = new();
    private readonly Dictionary<IntPtr, (DispatcherTimer Timer, CapturedWindow Member)> _minimizeHideDebounce = new();

    public GuestLifecycleService(GroupManager groups, Dictionary<Guid, ContainerWindow> containers, LoggingService log)
    {
        _groups = groups;
        _containers = containers;
        _log = log;
    }

    /// <summary>
    /// Subscribes every WinEventMonitor event to its handler. Call once, on
    /// the UI thread, before the monitor is started.
    ///
    /// Every handler resolves the event's HWND through
    /// GroupManager.TryGetCapturedMember: one dictionary probe instead of the
    /// Groups.ToList() snapshot plus per-group FirstOrDefault scan these used
    /// to run per event (PERF25-02). The snapshot existed to survive a
    /// handler mutating Groups mid-iteration; a single lookup never iterates
    /// Groups at all, so that hazard is gone rather than merely guarded — and
    /// the result is identical, because an HWND can only be in one group.
    /// </summary>
    public void Attach(WinEventMonitor monitor)
    {
        monitor.WindowDestroyed += (_, args) => OnWindowDestroyed(args.Hwnd);
        monitor.WindowHidden += (_, args) => OnWindowHidden(args.Hwnd, args.VisibleAtCallback, args.EventTime);
        monitor.WindowMinimized += (_, args) => OnWindowMinimized(args.Hwnd);
        monitor.WindowMoveSizeStarted += (_, args) => OnGuestMoveSize(args.Hwnd, started: true);
        monitor.WindowMoveSizeEnded += (_, args) => OnGuestMoveSize(args.Hwnd, started: false);
        monitor.WindowForegroundChanged += OnForegroundChanged;
        monitor.WindowZOrderChanged += OnZOrderChanged;
        monitor.WindowNameChanged += (_, args) => DebounceNameChanged(args.Hwnd);
    }

    private void OnWindowDestroyed(IntPtr hwnd)
    {
        StopMinimizeHideProbe(hwnd);
        if (!_groups.TryGetCapturedMember(hwnd, out Group? group, out CapturedWindow? match))
            return;

        _log.Log($"WinEvent: captured window 0x{hwnd.ToInt64():X} destroyed; removing its tab.");
        RemoveDeadMember(group, match, show: true);
    }

    private void OnWindowHidden(IntPtr hwnd, bool? visibleAtCallback, uint eventTime)
    {
        StopMinimizeHideProbe(hwnd);
        if (!_groups.TryGetCapturedMember(hwnd, out Group? group, out CapturedWindow? match))
            return;

        // Inactive tabs are hidden by TabDock's own tab switching, so
        // only a hide of the ACTIVE tab can be guest-initiated. By the
        // time this queued event is dispatched, any TabDock-initiated
        // switch has already completed and moved the active tab, so
        // the just-hidden old tab is rejected here. Release-path hides
        // never reach this handler at all: the member leaves
        // Group.Members before Release() runs, so the monitor's
        // captured-window filter drops the event.
        //
        // In SPLIT mode both members are visible, so a hide of EITHER split
        // member is guest-initiated (a self-hide), not a tab-switch hide —
        // neither is ever hidden by TabDock's ordinary tab logic. During a
        // pair -> single-guest suspension, TabDock hides both members while
        // IsInSplit is still true, so those queued hides are recognized as
        // intentional presentation work. Split exit/replacement and
        // dormant-member removal clear the relationship before any later hide
        // event is dispatched; IsInSplit is then false and the active-tab check
        // below rejects the stale intentional hide.
        bool inSplit = false;
        ContainerWindow? container = null;
        if (_containers.TryGetValue(group.Id, out var c))
        {
            container = c;
            inSplit = container.IsInSplit(match);
        }
        if (!inSplit)
        {
            if (group.ActiveIndex < 0 || group.ActiveIndex >= group.Members.Count
                || group.Members[group.ActiveIndex] != match)
                return;
        }
        if (!NativeMethods.IsWindow(hwnd))
            return; // EVENT_OBJECT_DESTROY owns this case.
        // A hide event is posted to the UI thread because the WinEvent callback
        // cannot mutate WPF state directly. A guest can be re-shown by a
        // synchronous activation/reassert path before this queued event runs;
        // in that case the current visibility is no longer evidence that the
        // original hide was transient. Preserve the native observation from
        // the callback and only apply the current-visibility filter when the
        // callback did not observe a hidden state.
        bool wasHiddenAtCallback = visibleAtCallback == false;
        if (!wasHiddenAtCallback && NativeMethods.IsWindowVisible(hwnd))
            return; // Transient hide; the window is visible again.

        // TabDock itself hides the ACTIVE guest when its container is
        // minimized (ContainerWindow.StateChanged -> _shepherd.Hide),
        // which fires the very same EVENT_OBJECT_HIDE as a guest-initiated
        // tray-close and — unlike a tab-switch hide — leaves the active
        // tab unchanged, so it passes every check above. Distinguish the
        // two by container state: a genuine tray-close happens while the
        // container is open; a minimize-hide happens because the container
        // is minimized (the guest is re-shown on restore). Without this
        // guard, minimizing a group would release its active tab as a
        // hidden, orphaned window (and close a single-tab group outright).
        // This also covers split mode: minimizing the container hides both
        // split members, and neither may be torn down as a self-hide.
        // A container minimize hides its guests through the same USER32 path as
        // a guest tray-close. The hide event is posted asynchronously and can be
        // delivered after WPF has already reported Normal/Maximized, so current
        // WindowState alone is not a sufficient source proof. ContainerWindow
        // records the exact expected hide and its restore boundary; consume only
        // an event proven to belong to that transition. A hide generated after
        // restore remains a genuine guest lifecycle event and is not suppressed.
        if (container?.IsContainerDrivenGuestHide(match, eventTime) == true)
        {
            _log.Log($"WinEvent: captured window 0x{hwnd.ToInt64():X} hide matched the container minimize transition; retaining its captured tab.");
            return;
        }

        if (container?.WindowState == WindowState.Minimized)
            return;

        _log.Log($"WinEvent: captured window 0x{hwnd.ToInt64():X} hid itself (tray-style close); releasing its tab hidden.");
        RemoveDeadMember(group, match, show: false);
    }

    private void OnWindowMinimized(IntPtr hwnd)
    {
        if (!_groups.TryGetCapturedMember(hwnd, out Group? group, out CapturedWindow? match))
            return;

        _log.Log($"WinEvent: captured window 0x{hwnd.ToInt64():X} minimized; restoring it inside its tab.");
        if (_containers.TryGetValue(group.Id, out var container))
        {
            container.RestoreMinimizedWindow(match);
            ArmMinimizeHideProbe(hwnd, match, container);
        }
    }

    private void ArmMinimizeHideProbe(IntPtr hwnd, CapturedWindow member, ContainerWindow container)
    {
        StopMinimizeHideProbe(hwnd);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            if (!_minimizeHideDebounce.TryGetValue(hwnd, out var pending)
                || !ReferenceEquals(pending.Timer, timer))
            {
                timer.Stop();
                return;
            }

            timer.Stop();
            _minimizeHideDebounce.Remove(hwnd);

            // Some guests implement close as WindowState=Minimized followed
            // immediately by Hide(). The minimize event can reach this UI
            // thread before the hide has settled, while an activation reassert
            // can otherwise make the HWND visible again before EVENT_OBJECT_HIDE
            // is dispatched. Treat a still-captured, non-minimized-container
            // guest that is now hidden as the same guest-initiated hide path.
            // Normal self-minimize leaves WS_VISIBLE set and therefore does not
            // enter this branch.
            if (!ReferenceEquals(_resolveCurrentMember(member), member)
                || container.WindowState == WindowState.Minimized
                || !NativeMethods.IsWindow(hwnd)
                || NativeMethods.IsWindowVisible(hwnd))
            {
                return;
            }

            OnWindowHidden(hwnd, visibleAtCallback: false, eventTime: unchecked((uint)Environment.TickCount));
        };
        _minimizeHideDebounce[hwnd] = (timer, member);
        timer.Start();
    }

    private CapturedWindow? _resolveCurrentMember(CapturedWindow expected)
    {
        return _groups.TryGetCapturedMember(expected.Hwnd, out _, out CapturedWindow? current)
            ? current
            : null;
    }

    private void StopMinimizeHideProbe(IntPtr hwnd)
    {
        if (_minimizeHideDebounce.Remove(hwnd, out var pending))
            pending.Timer.Stop();
    }

    // The guest became the system foreground window by some means other
    // than the container's own BringToFront — the user alt-tabbed to it
    // via Windows' own switcher, or clicked it directly instead of the
    // tab strip. Keep the container paired immediately behind it in
    // z-order (purely cosmetic: input already routes to the guest
    // correctly regardless, since it is a real, untouched top-level
    // window either way).
    private void OnForegroundChanged(object? sender, WindowEventArgs args)
    {
        if (!_groups.TryGetCapturedMember(args.Hwnd, out Group? group, out _))
            return;
        if (_containers.TryGetValue(group.Id, out var container))
            container.PairZOrderBehindGuest(args.Hwnd);
    }

    // A top-level guest activation also reorders the desktop's window list.
    // That reorder event is the earliest reliable proof that the guest was
    // raised above the unrelated window; EVENT_SYSTEM_FOREGROUND can be
    // coalesced or delivered later for a direct click. The monitor snapshots
    // the foreground at native callback time; validate that snapshot when the
    // UI dispatch runs before feeding the same authoritative pairing policy.
    // No new z-order subsystem is created.
    private void OnZOrderChanged(object? sender, WindowEventArgs args)
    {
        IntPtr foregroundHwnd = args.RelatedHwnd;
        if (foregroundHwnd == IntPtr.Zero || NativeMethods.GetForegroundWindow() != foregroundHwnd)
            return;
        if (!_groups.TryGetCapturedMember(foregroundHwnd, out Group? group, out _))
            return;
        if (_containers.TryGetValue(group.Id, out var container))
            container.PairZOrderBehindGuest(foregroundHwnd);
    }

    // A guest entered/left its interactive move/size modal loop (e.g. the
    // user dragged it by its own real title bar — a shepherded guest keeps
    // one). The container re-glues it on MOVESIZEEND; explicit tab Pop out is
    // the only release gesture.
    private void OnGuestMoveSize(IntPtr hwnd, bool started)
    {
        // Resolve the member directly through GroupManager's HWND index. The
        // move/size callback only re-glues an existing member; it never mutates
        // the group collection, so there is no release-induced enumeration or
        // re-entrancy here.
        if (!_groups.TryGetCapturedMember(hwnd, out Group? group, out CapturedWindow? match))
            return;

        if (_containers.TryGetValue(group.Id, out var container))
        {
            container.NoteGuestMoveSize(match, started);
        }
    }

    // Some guests (e.g. Windows 11 Notepad) mirror document content into the
    // window title, firing EVENT_OBJECT_NAMECHANGE on every keystroke. Handling
    // each one synchronously (log line + tab-title refresh) turned ordinary
    // typing into a UI-thread event storm. Coalesce rapid repeats per HWND and
    // act once, 250ms after the last one, reading the title fresh at that point
    // rather than trusting whichever event happened to trigger the timer.
    private void DebounceNameChanged(IntPtr hwnd)
    {
        if (!_groups.TryGetCapturedMember(hwnd, out _, out CapturedWindow? member))
            return;

        if (_nameChangeDebounce.TryGetValue(hwnd, out var pending))
        {
            if (ReferenceEquals(pending.Member, member))
            {
                pending.Timer.Stop();
                pending.Timer.Start();
                return;
            }

            // The HWND was released and recycled while the old debounce was
            // pending. Drop the old timer before scheduling one for the new
            // captured object.
            pending.Timer.Stop();
            _nameChangeDebounce.Remove(hwnd);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_nameChangeDebounce.TryGetValue(hwnd, out var current)
                && ReferenceEquals(current.Timer, timer))
            {
                _nameChangeDebounce.Remove(hwnd);
            }
            HandleNameChanged(hwnd, member);
        };
        _nameChangeDebounce[hwnd] = (timer, member);
        timer.Start();
    }

    private void HandleNameChanged(IntPtr hwnd, CapturedWindow expectedMember)
    {
        if (!_groups.TryGetCapturedMember(hwnd, out Group? group, out CapturedWindow? match)
            || !ReferenceEquals(match, expectedMember))
            return;
        if (!string.IsNullOrWhiteSpace(match.CustomLabel))
            return; // User label wins.

        // GetWindowTextString never returns null — it reports an unreadable or
        // zero-length title as string.Empty — so the old null check could not
        // fire and an empty read (routine mid-teardown, or while a guest
        // rebuilds its title) overwrote the last known title, blanking the
        // tab's label permanently. Keep the previous title instead.
        string? newTitle = NativeMethods.GetWindowTextString(hwnd);
        if (string.IsNullOrEmpty(newTitle))
            return;

        // Nothing to repaint when the title is the one already on the tab: some
        // guests re-announce an unchanged title repeatedly, and every one of
        // those used to cost a log line and a binding invalidation.
        if (string.Equals(match.OriginalTitle, newTitle, StringComparison.Ordinal))
            return;

        match.OriginalTitle = newTitle;
        _log.Log($"WinEvent: title changed for 0x{hwnd.ToInt64():X} -> '{newTitle}'.");

        if (_containers.TryGetValue(group.Id, out var container))
        {
            container.RefreshTabTitle(match);
        }
    }

    /// <summary>
    /// Removes a member whose window is gone (destroyed) or has withdrawn itself
    /// (guest-initiated hide): tab removal through the container's view model so
    /// the tab strip, the active-tab selection, and Group.Members all stay in
    /// sync (going through GroupManager.ReleaseTab alone leaves a stale
    /// TabViewModel behind and desyncs Tabs indices from Members indices),
    /// followed by empty-group container close. When <paramref name="show"/> is
    /// false the release leaves the window hidden (tray-style close).
    /// </summary>
    private void RemoveDeadMember(Group group, CapturedWindow match, bool show)
    {
        if (_containers.TryGetValue(group.Id, out var container))
        {
            container.ReleaseCapturedWindow(match, show);
        }
        else
        {
            _groups.ReleaseMember(group, match, show);
        }

        if (group.Members.Count == 0)
        {
            // Close the container for the empty group.
            if (_containers.TryGetValue(group.Id, out var emptyContainer))
            {
                _containers.Remove(group.Id);
                try { emptyContainer.Close(); }
                catch (Exception ex) { _log.LogException("Close empty container", ex); }
            }

            // Same rule as App.OnContainerClosed, and load-bearing for the same
            // reason (findings L12 / M5): PersistedTabs is populated only for a
            // group restored from a previous session's state.json, so a
            // non-empty one means real saved layout intent. Removing the group
            // here regardless — which this path used to do — wiped that intent
            // whenever the last live member of a restored, re-populated group
            // was closed or tray-hidden, overriding the guard the close path
            // applies.
            if (group.PersistedTabs.Count == 0)
                _groups.RemoveGroup(group);
        }
    }
}
