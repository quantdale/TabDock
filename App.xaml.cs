using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using TabDock.Views;

namespace TabDock;

/// <summary>
/// WPF application entry point and orchestrator.
/// Owns service lifetime, persisted-state load/save, global hotkey,
/// container-window management, and the guaranteed emergency-release path.
/// </summary>
public partial class App : Application
{
    private LoggingService _log = null!;
    private IconService _icons = null!;
    private WindowShepherdService _shepherd = null!;
    private PersistenceService _persistence = null!;
    private GroupManager _groups = null!;
    private WinEventMonitor _events = null!;
    private HotkeyService _hotkey = null!;
    private Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private readonly Dictionary<Guid, ContainerWindow> _containers = new();

    // True after Application_Exit disposes the WinEvent monitor. Guards the
    // deferred Stop() posted by SyncWinEventMonitor from running after disposal.
    private bool _winEventMonitorDisposed;

    // Debounces EVENT_OBJECT_NAMECHANGE storms (see DebounceNameChanged).
    private readonly Dictionary<IntPtr, System.Windows.Threading.DispatcherTimer> _nameChangeDebounce = new();

    // Re-entrancy guard for ShowCapturePicker. ShowDialog runs a nested
    // dispatcher loop, which keeps pumping WM_HOTKEY to the HotkeyService sink
    // and clicks to every container's "+" button — so a second picker can open
    // on top of the first while it is still up, each with its own modal loop and
    // its own capture pass over the same window list.
    private bool _pickerOpen;

    public App()
    {
        // Create the logger and attach the AppDomain fatal handler before anything
        // else runs (including Application.InitializeComponent / XAML wiring) so an
        // exception during the very earliest startup is still recorded.
        try
        {
            _log = new LoggingService();
            _log.Log("TabDock starting.");
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fatal: failed to initialize logging or AppDomain handler: {ex}");
        }

        InitializeComponent();
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            // The logger and AppDomain handler are initialized in the constructor. If
            // that failed, create a best-effort logger now so the rest of startup can
            // still be diagnosed.
            _log ??= new LoggingService();

            // Only one instance may run at a time: sharing state.json and the hidden-
            // window journal between two processes leads to lost updates and double
            // rescue attempts. Exit cleanly if another instance already holds the mutex.
            if (!AcquireSingleInstanceMutex())
            {
                _log.Log("Another TabDock instance is already running. Exiting.");
                Shutdown(0);
                return;
            }

            // Remove orphaned atomic-write temp files left behind by a prior run that
            // died before File.Move completed. This is purely disk-litter cleanup;
            // the real state.json / hidden-windows.json are never torn.
            CleanupStaleTempFiles();

            _icons = new IconService(_log);
            _shepherd = new WindowShepherdService(_log);
            _persistence = new PersistenceService(_log);
            _groups = new GroupManager(_shepherd, _persistence, _log);
            _events = new WinEventMonitor(_groups.IsCapturedWindow, _log);
            _hotkey = new HotkeyService(_log);

            WireWinEvents();
            // The hooks observe the whole desktop, so they only earn their cost
            // while something is actually captured (PERF25-03).
            _groups.MonitoringNeededChanged += (_, _) => SyncWinEventMonitor();
            WindowShepherdService.RescueOrphanedWindows(_log);
            _groups.RestoreState();

            _mainViewModel = new MainViewModel(_groups);
            _mainViewModel.NewGroupRequested += OnNewGroupRequested;
            _mainViewModel.CaptureRequested += OnCaptureRequested;
            _mainViewModel.ExitRequested += OnExitRequested;

            _mainWindow = new MainWindow(_mainViewModel);
            // Null the reference on close: the app can outlive the launcher
            // (ShutdownMode=OnLastWindowClose with containers open), and using a
            // closed Window as a picker Owner throws InvalidOperationException.
            _mainWindow.Closed += (_, _) =>
            {
                _log.Log("MainWindow closed.");
                _mainWindow = null;
            };
            _hotkey.Register();
            _hotkey.HotkeyPressed += (_, _) => OnCaptureRequested(this, EventArgs.Empty);
            _mainWindow.Show();

            // Open containers for groups restored from persistence. Live HWNDs are not
            // restored across reboots, so these groups start empty; the container is kept
            // open so the user can re-populate it.
            foreach (var group in _groups.Groups.ToList())
            {
                try
                {
                    OpenContainer(group);
                }
                catch (Exception ex)
                {
                    // A container that fails to open must not abort startup: the group
                    // is re-saved on exit, so an unguarded throw here locks the app into
                    // crashing at every launch until state.json is deleted by hand.
                    _log.LogException($"OpenContainer for restored group {group.Id}", ex);
                }
            }

            SyncWinEventMonitor();
            _log.Log("TabDock startup complete.");
        }
        catch (Exception ex)
        {
            ContainerWindow.IsAppShuttingDown = true;
            _log?.LogException("FATAL Application_Startup", ex);
            FlushJournalGuarded("startup failure");
            try
            {
                _groups?.EmergencyReleaseAll();
            }
            catch (Exception releaseEx)
            {
                _log?.LogException("Emergency release during startup failure", releaseEx);
            }
            Shutdown(1);
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        ContainerWindow.IsAppShuttingDown = true;
        _log?.Log("Application exiting; releasing all captured windows and saving state.");
        try
        {
            _groups?.EmergencyReleaseAll();
            _groups?.SaveState();
        }
        catch (Exception ex)
        {
            _log?.LogException("Application_Exit", ex);
        }
        finally
        {
            FlushJournalGuarded("application exit");
            _events?.Dispose();
            _winEventMonitorDisposed = true;
            _hotkey?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _log?.Dispose();
        }
    }

    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        ContainerWindow.IsAppShuttingDown = true;
        _log?.LogException("DispatcherUnhandledException", e.Exception);
        SaveStateGuarded("dispatcher exception");
        FlushJournalGuarded("dispatcher exception");
        try
        {
            _groups?.EmergencyReleaseAll();
        }
        catch (Exception ex)
        {
            _log?.LogException("EmergencyReleaseAll during dispatcher exception", ex);
        }
        e.Handled = true;
        Shutdown(1);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        ContainerWindow.IsAppShuttingDown = true;
        _log?.Log($"AppDomain unhandled exception. IsTerminating={e.IsTerminating}: {e.ExceptionObject}");

        if (e.IsTerminating)
        {
            // Terminating exceptions can arrive on any thread and the runtime is
            // about to tear the process down. Restrict the handler to thread-safe
            // logging so it never races the UI thread's collection/journal mutation.
            return;
        }

        // Non-terminating exceptions still have a live dispatcher. Marshal the
        // UI-thread-affined work onto it with a short deadline so a deadlocked UI
        // thread does not leave this arbitrary thread hung.
        try
        {
            if (Dispatcher == null)
            {
                _log?.Log("AppDomain exception: no UI dispatcher available; skipping crash-time save/release.");
                return;
            }

            Dispatcher.Invoke(() =>
            {
                SaveStateGuarded("AppDomain exception");
                FlushJournalGuarded("AppDomain exception");
                try
                {
                    _groups?.EmergencyReleaseAll();
                }
                catch (Exception ex)
                {
                    _log?.LogException("EmergencyReleaseAll during AppDomain exception", ex);
                }
            }, TimeSpan.FromSeconds(1));
        }
        catch (Exception dispatchEx)
        {
            _log?.LogException("AppDomain exception: dispatcher invocation timed out or failed; falling back to log-only", dispatchEx);
        }
    }

    private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // Logoff/shutdown can kill the process before Application_Exit runs.
        ContainerWindow.IsAppShuttingDown = true;
        _log?.Log($"Session ending ({e.ReasonSessionEnding}); saving state and releasing captured windows.");
        SaveStateGuarded("session ending");
        FlushJournalGuarded("session ending");
        try
        {
            _groups?.EmergencyReleaseAll();
        }
        catch (Exception ex)
        {
            _log?.LogException("EmergencyReleaseAll during session ending", ex);
        }

        // Leave GroupManager in a coherent post-release state. If the logoff is
        // cancelled by another application, there must be no captured HWNDs left in
        // the index and no live hooks repositioning now-unmanaged windows.
        try
        {
            if (_groups != null)
            {
                foreach (var group in _groups.Groups.ToList())
                {
                    group.Members.Clear();
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogException("Clearing group members during session ending", ex);
        }

        try
        {
            _events?.Stop();
        }
        catch (Exception ex)
        {
            _log?.LogException("Stopping WinEvent monitor during session ending", ex);
        }
    }

    /// <summary>
    /// SaveState that can never throw: used in crash paths, where a save failure
    /// must not mask the original exception or abort the emergency release.
    /// </summary>
    private void SaveStateGuarded(string context)
    {
        try
        {
            _groups?.SaveState();
        }
        catch (Exception ex)
        {
            _log?.LogException($"SaveState during {context}", ex);
        }
    }

    /// <summary>
    /// FlushJournal that can never throw (AUDIT25-01): forces the debounced
    /// hidden-window crash-recovery journal to write immediately, called from
    /// every exit/crash path so a pending debounced write is never lost to a
    /// timer that never got the chance to fire.
    /// </summary>
    private void FlushJournalGuarded(string context)
    {
        try
        {
            _shepherd?.FlushJournal();
        }
        catch (Exception ex)
        {
            _log?.LogException($"FlushJournal during {context}", ex);
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
            int index = group.Members.IndexOf(match);
            if (index >= 0)
                _groups.ReleaseTab(group, index, show);
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

            // Same rule as OnContainerClosed, and load-bearing for the same reason
            // (findings L12 / M5): PersistedTabs is populated only for a group
            // restored from a previous session's state.json, so a non-empty one
            // means real saved layout intent. Removing the group here regardless —
            // which this path used to do — wiped that intent whenever the last
            // live member of a restored, re-populated group was closed or
            // tray-hidden, overriding the guard the close path applies.
            if (group.PersistedTabs.Count == 0)
                _groups.RemoveGroup(group);
        }
    }

    /// <summary>
    /// Installs the desktop-wide WinEvent hooks only while at least one window
    /// is captured, and removes them again when the last one is released
    /// (PERF25-03). Every hooked event — destroy, hide, name change, minimize,
    /// foreground, move/size — is unconditionally discarded by the monitor's
    /// captured-member filter when nothing is captured, so with no groups
    /// populated (the state TabDock sits in whenever the user is just running
    /// the launcher, and the state every restored-but-empty group starts in)
    /// the hooks did nothing but marshal every menu, tooltip and title change on
    /// the desktop into this process's message loop to be thrown away.
    ///
    /// Runs on the UI thread from startup and from
    /// GroupManager.MonitoringNeededChanged, which is raised off the
    /// UI-thread-only group collections — UnhookWinEvent requires the thread
    /// that installed the hooks, and this is it.
    /// </summary>
    private void SyncWinEventMonitor()
    {
        if (_events == null)
            return;

        if (_groups.IsMonitoringNeeded)
        {
            // Install immediately: the notification that brings us here is
            // raised as the member is added to its group, before the guest is
            // positioned and shown, so the hooks are live before there is
            // anything for them to miss.
            _events.Start();
            return;
        }

        // Removing hooks is deferred by one dispatcher turn. The zero-captured
        // transition is usually reached from inside a WinEvent handler (a guest
        // was destroyed or hid itself), and unhooking is best done once that
        // dispatch has fully unwound rather than underneath it. Re-check on
        // arrival: a capture in the same turn (releasing one window and grabbing
        // another) must not have its hooks torn back down.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_winEventMonitorDisposed || _events == null)
                return;
            if (!_groups.IsMonitoringNeeded)
                _events.Stop();
        }));
    }

    private void WireWinEvents()
    {
        // Every handler below resolves the event's HWND through
        // GroupManager.TryGetCapturedMember: one dictionary probe instead of the
        // Groups.ToList() snapshot plus per-group FirstOrDefault scan these used
        // to run per event (PERF25-02). The snapshot existed to survive a
        // handler mutating Groups mid-iteration; a single lookup never iterates
        // Groups at all, so that hazard is gone rather than merely guarded — and
        // the result is identical, because an HWND can only be in one group.
        _events.WindowDestroyed += (_, args) =>
        {
            if (!_groups.TryGetCapturedMember(args.Hwnd, out Group? group, out CapturedWindow? match))
                return;

            _log.Log($"WinEvent: captured window 0x{args.Hwnd.ToInt64():X} destroyed; removing its tab.");
            RemoveDeadMember(group, match, show: true);
        };

        _events.WindowHidden += (_, args) =>
        {
            if (!_groups.TryGetCapturedMember(args.Hwnd, out Group? group, out CapturedWindow? match))
                return;

            // Inactive tabs are hidden by TabDock's own tab switching, so
            // only a hide of the ACTIVE tab can be guest-initiated. By the
            // time this queued event is dispatched, any TabDock-initiated
            // switch has already completed and moved the active tab, so
            // the just-hidden old tab is rejected here. Release-path hides
            // never reach this handler at all: the member leaves
            // Group.Members before Release() runs, so the monitor's
            // captured-window filter drops the event.
            if (group.ActiveIndex < 0 || group.ActiveIndex >= group.Members.Count
                || group.Members[group.ActiveIndex] != match)
                return;
            if (!NativeMethods.IsWindow(args.Hwnd))
                return; // EVENT_OBJECT_DESTROY owns this case.
            if (NativeMethods.IsWindowVisible(args.Hwnd))
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
            if (_containers.TryGetValue(group.Id, out var hidContainer)
                && hidContainer.WindowState == WindowState.Minimized)
                return;

            _log.Log($"WinEvent: captured window 0x{args.Hwnd.ToInt64():X} hid itself (tray-style close); releasing its tab hidden.");
            RemoveDeadMember(group, match, show: false);
        };

        _events.WindowMinimized += (_, args) =>
        {
            if (!_groups.TryGetCapturedMember(args.Hwnd, out Group? group, out CapturedWindow? match))
                return;

            _log.Log($"WinEvent: captured window 0x{args.Hwnd.ToInt64():X} minimized; restoring it inside its tab.");
            if (_containers.TryGetValue(group.Id, out var container))
            {
                container.RestoreMinimizedWindow(match);
            }
        };

        // A guest entered/left its interactive move/size modal loop (e.g. the
        // user dragged it by its own real title bar — a shepherded guest keeps
        // one). The container decides on MOVESIZEEND whether that was jitter
        // (snap back) or an intentional drag-out (release the tab).
        _events.WindowMoveSizeStarted += (_, args) => OnGuestMoveSize(args.Hwnd, started: true);
        _events.WindowMoveSizeEnded += (_, args) => OnGuestMoveSize(args.Hwnd, started: false);

        // The guest became the system foreground window by some means other
        // than the container's own BringToFront — the user alt-tabbed to it
        // via Windows' own switcher, or clicked it directly instead of the
        // tab strip. Keep the container paired immediately behind it in
        // z-order (purely cosmetic: input already routes to the guest
        // correctly regardless, since it is a real, untouched top-level
        // window either way).
        _events.WindowForegroundChanged += (sender, args) =>
        {
            // "sender" must be named, not "_": with a "_" parameter in scope,
            // "out _" below would bind to that object instead of being a
            // discard, and fail to compile against out CapturedWindow.
            if (!_groups.TryGetCapturedMember(args.Hwnd, out Group? group, out _))
                return;
            if (_containers.TryGetValue(group.Id, out var container))
                container.PairZOrderBehindGuest(args.Hwnd);
        };

        _events.WindowNameChanged += (_, args) => DebounceNameChanged(args.Hwnd);
    }

    // Some guests (e.g. Windows 11 Notepad) mirror document content into the
    // window title, firing EVENT_OBJECT_NAMECHANGE on every keystroke. Handling
    // each one synchronously (log line + tab-title refresh) turned ordinary
    // typing into a UI-thread event storm. Coalesce rapid repeats per HWND and
    // act once, 250ms after the last one, reading the title fresh at that point
    // rather than trusting whichever event happened to trigger the timer.
    private void DebounceNameChanged(IntPtr hwnd)
    {
        if (_nameChangeDebounce.TryGetValue(hwnd, out var timer))
        {
            timer.Stop();
            timer.Start();
            return;
        }

        timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer!.Stop();
            _nameChangeDebounce.Remove(hwnd);
            HandleNameChanged(hwnd);
        };
        _nameChangeDebounce[hwnd] = timer;
        timer.Start();
    }

    private void HandleNameChanged(IntPtr hwnd)
    {
        if (!_groups.TryGetCapturedMember(hwnd, out Group? group, out CapturedWindow? match))
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

    private void OnGuestMoveSize(IntPtr hwnd, bool started)
    {
        // A drag-out (MOVESIZEEND past the pop-out threshold) of the LAST tab
        // releases it, which empties the group, closes its container, and
        // removes the group from _groups.Groups — all synchronously, inside the
        // NoteGuestMoveSize call below. The old form of this method iterated
        // _groups.Groups and needed a ToList() snapshot to survive that
        // ("Collection was modified" otherwise, escalating to the dispatcher
        // crash handler); resolving the member by index instead means there is
        // no live enumeration in progress to invalidate.
        if (!_groups.TryGetCapturedMember(hwnd, out Group? group, out CapturedWindow? match))
            return;

        if (_containers.TryGetValue(group.Id, out var container))
        {
            container.NoteGuestMoveSize(match, started);
        }
    }

    private void OnNewGroupRequested(object? sender, EventArgs e)
    {
        var group = _groups.CreateGroup();
        try
        {
            OpenContainer(group);
        }
        catch (Exception ex)
        {
            // The group must not outlive a container that failed to open: it would
            // be saved on exit and re-opened at startup, turning a one-time failure
            // into a crash on every subsequent launch.
            _groups.RemoveGroup(group);
            _log.LogException("OpenContainer for new group", ex);
            MessageBox.Show(_mainWindow, "Could not open the container for the new group.", "TabDock", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCaptureRequested(object? sender, EventArgs e)
    {
        ShowCapturePicker(preselectedGroup: null);
    }

    private void ShowCapturePicker(Group? preselectedGroup)
    {
        // Holding Ctrl+Alt+G repeats WM_HOTKEY, and the nested modal loops run by
        // ShowCapturePickerCore (the picker's own ShowDialog, plus the
        // capture-failed MessageBox) keep dispatching those repeats — which
        // re-entered here and stacked one picker per repeat.
        if (_pickerOpen)
        {
            _log.Log("Capture picker is already open; ignoring the duplicate request.");
            return;
        }

        // Defer picker requests while a container close-confirm prompt is open.
        // The prompt runs its own nested dispatcher loop, so a picker opened on
        // top of it would stack modals and could be answered out of order.
        if (_containers.Values.Any(c => c.IsClosePromptOpen))
        {
            _log.Log("Capture picker requested while a container close prompt is open; ignoring.");
            return;
        }

        _pickerOpen = true;
        try
        {
            ShowCapturePickerCore(preselectedGroup);
        }
        finally
        {
            _pickerOpen = false;
        }
    }

    private void ShowCapturePickerCore(Group? preselectedGroup)
    {
        var pickerVm = new CapturePickerViewModel(_groups, _icons);
        if (preselectedGroup != null)
        {
            pickerVm.SelectedGroupOption = pickerVm.Groups.FirstOrDefault(o => o.Id == preselectedGroup.Id)
                ?? pickerVm.SelectedGroupOption;
        }
        var picker = new CapturePickerWindow(pickerVm);
        // Own the picker to whichever window actually requested it — a
        // specific container's own "+" button, when that's the trigger —
        // rather than always the main launcher. An owned window is kept
        // properly paired in z-order with its owner by Windows itself (it
        // can never fall behind it, and reactivating the owner correctly
        // resurfaces it too); defaulting every picker to _mainWindow as
        // owner regardless of trigger meant a container-triggered picker had
        // NO enforced z-order relationship to the container that opened it,
        // letting the two stack unpredictably. The picker must still keep
        // working after the launcher closes (hotkey or a container's "+"
        // button with only containers open), so fall back through main
        // window, then no owner at all.
        Window? requestingWindow = preselectedGroup != null && _containers.TryGetValue(preselectedGroup.Id, out var requestingContainer)
            ? requestingContainer
            : _mainWindow;
        if (requestingWindow is { IsLoaded: true })
        {
            picker.Owner = requestingWindow;
        }
        else
        {
            picker.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        bool? result = picker.ShowDialog();
        if (result != true || picker.Result == null || picker.Result.SelectedHwnds.Count == 0)
            return;

        Group? group;
        if (picker.Result.TargetGroupId == Guid.Empty)
        {
            group = _groups.CreateGroup();
            try
            {
                OpenContainer(group);
            }
            catch (Exception ex)
            {
                _groups.RemoveGroup(group);
                _log.LogException("OpenContainer for new group from capture picker", ex);
                MessageBox.Show(picker, "Could not open the container for the new group.", "TabDock", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else
        {
            group = _groups.Groups.FirstOrDefault(g => g.Id == picker.Result.TargetGroupId);
            if (group == null)
            {
                _log.Log($"Capture picker referenced unknown group {picker.Result.TargetGroupId}; creating new group.");
                group = _groups.CreateGroup();
                try
                {
                    OpenContainer(group);
                }
                catch (Exception ex)
                {
                    _groups.RemoveGroup(group);
                    _log.LogException("OpenContainer for replacement group from capture picker", ex);
                    MessageBox.Show(picker, "Could not open the container for the new group.", "TabDock", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
        }

        if (group == null)
            return;

        if (!_containers.TryGetValue(group.Id, out var container))
        {
            _log.Log($"No container open for group {group.Id}; opening one.");
            try
            {
                container = OpenContainer(group);
            }
            catch (Exception ex)
            {
                _log.LogException($"OpenContainer for existing group {group.Id} from capture picker", ex);
                MessageBox.Show(picker, "Could not open the container for this group.", "TabDock", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        foreach (var hwnd in picker.Result.SelectedHwnds)
        {
            string? error = container.CaptureWindow(hwnd);
            if (error != null)
            {
                _log.Log($"Capture failed for 0x{hwnd.ToInt64():X}: {error}");
                // Explicit owner: an owner-less MessageBox falls back to WPF's
                // own default modal-parent resolution, which can disable more
                // than just this container if it resolves unexpectedly.
                MessageBox.Show(container, error, "Could not capture window", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        // Set before Shutdown() so every open container's Closing handler (which
        // runs as part of Shutdown closing each window) skips its confirmation
        // prompt instead of showing one modal per populated group (finding M6).
        ContainerWindow.IsAppShuttingDown = true;
        Shutdown();
    }

    private ContainerWindow OpenContainer(Group group)
    {
        if (_containers.TryGetValue(group.Id, out var existing))
        {
            existing.Activate();
            return existing;
        }

        var vm = new GroupViewModel(group, _groups, _icons, _log);
        // The container's "+" button funnels through this event; without the
        // subscription it is a dead control.
        vm.AddWindowsRequested += (_, _) => ShowCapturePicker(group);
        ContainerWindow window;
        try
        {
            window = new ContainerWindow(vm, _groups, _shepherd, _log);
            window.Closed += (_, _) => OnContainerClosed(group.Id);
            window.Show();
        }
        catch (Exception ex)
        {
            _log?.LogException($"OpenContainer failed for group {group.Id}", ex);
            vm.Detach();
            throw;
        }

        _containers[group.Id] = window;
        _log.Log($"Opened container for group {group.Id}.");
        return window;
    }

    private void OnContainerClosed(Guid groupId)
    {
        _containers.Remove(groupId);
        _log.Log($"Container closed for group {groupId}.");

        // A closed container that never represented any real layout intent must
        // not persist as a residual group re-opening at every future launch
        // (finding L12: one affected machine had 18 stale empty groups
        // accumulate this way, each reopening an empty container on every
        // startup). A populated group closed via the Yes/No prompt already
        // removes itself through GroupManager.CloseGroup; this only catches the
        // empty-container case that path never reaches (ContainerWindow_Closing
        // returns early when Tabs.Count == 0, skipping the prompt and any group
        // removal).
        //
        // Group.PersistedTabs is populated ONLY by PersistenceService.Load, i.e.
        // only for a group restored from a PREVIOUS session's state.json — never
        // for one created fresh in the running session (GroupManager.CreateGroup
        // starts it empty). Requiring PersistedTabs.Count == 0 too is load-bearing,
        // not a nicety: without it, this would also delete a just-relaunched
        // restored-but-not-yet-repopulated group the moment its auto-opened empty
        // shell closes during ordinary app exit, wiping exactly the persisted
        // "layout intent" PersistenceService/M5 exist to preserve (regression
        // caught live by the persist-kill scenario's step 5).
        var group = _groups.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null && group.Members.Count == 0 && group.PersistedTabs.Count == 0)
        {
            _groups.RemoveGroup(group);
        }
    }

    /// <summary>
    /// Acquires the global single-instance mutex. Returns false if another
    /// TabDock process already owns it, in which case this instance must exit
    /// without touching shared state files.
    /// </summary>
    private bool AcquireSingleInstanceMutex()
    {
        try
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: @"Global\TabDock", createdNew: out createdNew);
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogException("AcquireSingleInstanceMutex", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes orphaned atomic-write temp files that a prior run may have left
    /// behind if it died after <see cref="File.WriteAllText"/> but before
    /// <see cref="File.Move"/>. Only files matching TabDock's own known temp
    /// names in its own app-data directory are touched.
    /// </summary>
    private void CleanupStaleTempFiles()
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TabDock");
            if (!Directory.Exists(dir))
                return;

            string[] knownTempFiles = { "state.json.tmp", "hidden-windows.json.tmp" };
            foreach (string name in knownTempFiles)
            {
                string path = Path.Combine(dir, name);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        _log.Log($"STARTUP[cleanup] removed stale temp file: {path}");
                    }
                }
                catch (Exception ex)
                {
                    _log.LogException($"STARTUP[cleanup] failed to remove {path}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogException("STARTUP[cleanup] failed to enumerate temp files", ex);
        }
    }

}
