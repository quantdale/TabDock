using System;
using System.Collections.Generic;
using System.Linq;
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
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private readonly Dictionary<Guid, ContainerWindow> _containers = new();

    // Debounces EVENT_OBJECT_NAMECHANGE storms (see DebounceNameChanged).
    private readonly Dictionary<IntPtr, System.Windows.Threading.DispatcherTimer> _nameChangeDebounce = new();

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

            _icons = new IconService();
            _shepherd = new WindowShepherdService(_log);
            _persistence = new PersistenceService(_log);
            _groups = new GroupManager(_shepherd, _persistence, _log);
            _events = new WinEventMonitor(_groups.IsCapturedWindow, _log);
            _hotkey = new HotkeyService(_log);

            WireWinEvents();
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

            _events.Start();
            _log.Log("TabDock startup complete.");
        }
        catch (Exception ex)
        {
            _log?.LogException("FATAL Application_Startup", ex);
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
            _events?.Dispose();
            _hotkey?.Dispose();
            _log?.Dispose();
        }
    }

    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        ContainerWindow.IsAppShuttingDown = true;
        _log?.LogException("DispatcherUnhandledException", e.Exception);
        SaveStateGuarded("dispatcher exception");
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
        SaveStateGuarded("AppDomain exception");
        try
        {
            _groups?.EmergencyReleaseAll();
        }
        catch (Exception ex)
        {
            _log?.LogException("EmergencyReleaseAll during AppDomain exception", ex);
        }
    }

    private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // Logoff/shutdown can kill the process before Application_Exit runs.
        ContainerWindow.IsAppShuttingDown = true;
        _log?.Log($"Session ending ({e.ReasonSessionEnding}); saving state and releasing captured windows.");
        SaveStateGuarded("session ending");
        try
        {
            _groups?.EmergencyReleaseAll();
        }
        catch (Exception ex)
        {
            _log?.LogException("EmergencyReleaseAll during session ending", ex);
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
            _groups.RemoveGroup(group);
        }
    }

    private void WireWinEvents()
    {
        _events.WindowDestroyed += (_, args) =>
        {
            foreach (var group in _groups.Groups.ToList())
            {
                var match = group.Members.FirstOrDefault(m => m.Hwnd == args.Hwnd);
                if (match == null)
                    continue;

                _log.Log($"WinEvent: captured window 0x{args.Hwnd.ToInt64():X} destroyed; removing its tab.");
                RemoveDeadMember(group, match, show: true);
            }
        };

        _events.WindowHidden += (_, args) =>
        {
            foreach (var group in _groups.Groups.ToList())
            {
                var match = group.Members.FirstOrDefault(m => m.Hwnd == args.Hwnd);
                if (match == null)
                    continue;

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
                    continue;
                if (!NativeMethods.IsWindow(args.Hwnd))
                    continue; // EVENT_OBJECT_DESTROY owns this case.
                if (NativeMethods.IsWindowVisible(args.Hwnd))
                    continue; // Transient hide; the window is visible again.

                _log.Log($"WinEvent: captured window 0x{args.Hwnd.ToInt64():X} hid itself (tray-style close); releasing its tab hidden.");
                RemoveDeadMember(group, match, show: false);
            }
        };

        _events.WindowMinimized += (_, args) =>
        {
            foreach (var group in _groups.Groups)
            {
                var match = group.Members.FirstOrDefault(m => m.Hwnd == args.Hwnd);
                if (match == null)
                    continue;

                _log.Log($"WinEvent: captured window 0x{args.Hwnd.ToInt64():X} minimized; restoring it inside its tab.");
                if (_containers.TryGetValue(group.Id, out var container))
                {
                    container.RestoreMinimizedWindow(match);
                }
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
        _events.WindowForegroundChanged += (_, args) =>
        {
            foreach (var group in _groups.Groups)
            {
                if (!group.Members.Any(m => m.Hwnd == args.Hwnd))
                    continue;
                if (_containers.TryGetValue(group.Id, out var container))
                    container.PairZOrderBehindGuest(args.Hwnd);
            }
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
        foreach (var group in _groups.Groups.ToList())
        {
            var match = group.Members.FirstOrDefault(m => m.Hwnd == hwnd);
            if (match == null)
                continue;
            if (!string.IsNullOrWhiteSpace(match.CustomLabel))
                continue; // User label wins.

            string? newTitle = NativeMethods.GetWindowTextString(hwnd);
            if (newTitle == null)
                continue;

            match.OriginalTitle = newTitle;
            _log.Log($"WinEvent: title changed for 0x{hwnd.ToInt64():X} -> '{newTitle}'.");

            if (_containers.TryGetValue(group.Id, out var container))
            {
                container.RefreshTabTitles();
            }
        }
    }

    private void OnGuestMoveSize(IntPtr hwnd, bool started)
    {
        foreach (var group in _groups.Groups)
        {
            var match = group.Members.FirstOrDefault(m => m.Hwnd == hwnd);
            if (match == null)
                continue;

            if (_containers.TryGetValue(group.Id, out var container))
            {
                container.NoteGuestMoveSize(match, started);
            }
        }
    }

    private void OnNewGroupRequested(object? sender, EventArgs e)
    {
        var group = _groups.CreateGroup();
        try
        {
            OpenContainer(group);
        }
        catch
        {
            // The group must not outlive a container that failed to open: it would
            // be saved on exit and re-opened at startup, turning a one-time failure
            // into a crash on every subsequent launch.
            _groups.RemoveGroup(group);
            throw;
        }
    }

    private void OnCaptureRequested(object? sender, EventArgs e)
    {
        ShowCapturePicker(preselectedGroup: null);
    }

    private void ShowCapturePicker(Group? preselectedGroup)
    {
        var pickerVm = new CapturePickerViewModel(_groups, _icons);
        if (preselectedGroup != null)
        {
            pickerVm.SelectedGroupOption = pickerVm.Groups.FirstOrDefault(o => o.Id == preselectedGroup.Id)
                ?? pickerVm.SelectedGroupOption;
        }
        var picker = new CapturePickerWindow(pickerVm);
        // The picker must keep working after the launcher closes (hotkey or a
        // container's "+" button with only containers open).
        if (_mainWindow is { IsLoaded: true })
        {
            picker.Owner = _mainWindow;
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
            OpenContainer(group);
        }
        else
        {
            group = _groups.Groups.FirstOrDefault(g => g.Id == picker.Result.TargetGroupId);
            if (group == null)
            {
                _log.Log($"Capture picker referenced unknown group {picker.Result.TargetGroupId}; creating new group.");
                group = _groups.CreateGroup();
                OpenContainer(group);
            }
        }

        if (group == null)
            return;

        if (!_containers.TryGetValue(group.Id, out var container))
        {
            _log.Log($"No container open for group {group.Id}; opening one.");
            container = OpenContainer(group);
        }

        foreach (var hwnd in picker.Result.SelectedHwnds)
        {
            string? error = container.CaptureWindow(hwnd);
            if (error != null)
            {
                _log.Log($"Capture failed for 0x{hwnd.ToInt64():X}: {error}");
                MessageBox.Show(error, "Could not capture window", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        var vm = new GroupViewModel(group, _groups, _icons);
        // The container's "+" button funnels through this event; without the
        // subscription it is a dead control.
        vm.AddWindowsRequested += (_, _) => ShowCapturePicker(group);
        var window = new ContainerWindow(vm, _groups, _shepherd, _log);
        window.Closed += (_, _) => OnContainerClosed(group.Id);
        _containers[group.Id] = window;
        window.Show();
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

}
