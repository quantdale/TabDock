using System;
using System.Collections.Generic;
using System.IO;
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
/// WinEvent-driven guest lifecycle is delegated to
/// <see cref="GuestLifecycleService"/>; App only wires it in and gates the
/// hooks.
/// </summary>
public partial class App : Application
{
    private LoggingService _log = null!;
    private IconService _icons = null!;
    private WindowShepherdService _shepherd = null!;
    private PersistenceService _persistence = null!;
    private GroupManager _groups = null!;
    private WinEventMonitor _events = null!;
    private GuestLifecycleService _guestLifecycle = null!;
    private HotkeyService _hotkey = null!;
    private ProductMutationLease? _singleInstanceLease;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private readonly Dictionary<Guid, ContainerWindow> _containers = new();

    // True after Application_Exit disposes the WinEvent monitor. Guards the
    // deferred Stop() posted by SyncWinEventMonitor from running after disposal.
    private bool _winEventMonitorDisposed;
    private System.Windows.Threading.DispatcherTimer? _winEventRetryTimer;
    private int _winEventRetryAttempts;
    private bool _sessionEndingTeardownStarted;
    private bool _winEventFailureHandled;

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
            // Diagnostic commands must not touch product state, including the
            // normal rotating log. WPF still constructs the Application object,
            // but no windows, hooks, mutex, persistence, or services are started.
            if (!DiagnosticCommandLine.IsDiagnosticCommand(Environment.GetCommandLineArgs().Skip(1)))
            {
                _log = new LoggingService();
                _log.Log(BuildIdentity.ToLogLine(BuildIdentity.Current));
                _log.Log("TabDock starting.");
            }
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
            if (DiagnosticCommandLine.TryParse(e.Args, out DiagnosticCommandRequest command, out string? commandError))
            {
                int exitCode = commandError == null
                    ? DiagnosticCommandLine.Run(command)
                    : DiagnosticCommandLine.Run(e.Args);
                Shutdown(exitCode);
                return;
            }

            // The logger and AppDomain handler are initialized in the constructor. If
            // that failed, create a best-effort logger now so the rest of startup can
            // still be diagnosed.
            if (_log == null)
            {
                _log = new LoggingService();
                _log.Log(BuildIdentity.ToLogLine(BuildIdentity.Current));
            }

            // Deterministic split-geometry self-test mode (goal §27/§28): runs the
            // partition matrix + seeded fuzz with no windows, no input, no single
            // instance — usable on ANY machine (including a friend's) and by the
            // ValidationDriver as a standalone hermetic check. Exit 0 = all checks
            // pass; exit 1 = any failure. Must run before the mutex/UI setup.
            if (e.Args.Any(a => a.Equals("--selftest-geometry", StringComparison.OrdinalIgnoreCase)))
            {
                var (checks, failures) = SplitGeometry.RunSelfTest(_log.Log);
                _log.Log($"SELFTEST[geometry] checks={checks} failures={failures} result={(failures == 0 ? "PASS" : "FAIL")}");
                // Application_Exit disposes the logger and releases the (never
                // acquired) mutex; no explicit cleanup needed here.
                Shutdown(failures == 0 ? 0 : 1);
                return;
            }

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
            _events = new WinEventMonitor(_groups.IsCapturedWindow, _groups.GetCapturedWindow, _log);
            _hotkey = new HotkeyService(_log);
            DiagnosticRuntime.LogicalSnapshotProvider = CaptureLogicalSnapshots;

            // All WinEvent-driven guest lifecycle policy (destroy/hide teardown,
            // minimize restore, move/size re-glue, foreground pairing, title
            // refresh) lives behind GuestLifecycleService.Attach; App only wires
            // the module in and gates the hooks.
            _guestLifecycle = new GuestLifecycleService(_groups, _containers, _log);
            _guestLifecycle.Attach(_events);
            // The hooks observe the whole desktop, so they only earn their cost
            // while something is actually captured (PERF25-03).
            _groups.MonitoringNeededChanged += (_, _) => SyncWinEventMonitor();
            WindowShepherdService.RescueOrphanedWindows(_log);
            _groups.RestoreState();

            // Bounded environment fingerprint (goal §16): one startup block —
            // OS/.NET/bitness + full monitor layout — so customer/friend-machine
            // reports are diagnosable even when the machine is not reachable.
            _log.Log($"ENV[startup] {EnvironmentFingerprint.Platform} | {EnvironmentFingerprint.DescribeMonitors()}");

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
            _hotkey.DiagnosticHotkeyPressed += (_, _) => ExportDiagnosticsFromHotkey();
            _mainWindow.Show();

            ShowStorageCapabilityWarningIfNeeded();

            // Startup DPI (goal §16): the startup fingerprint runs before the
            // launcher exists, so the system DPI for the session is captured
            // once the launcher is on screen. Bounded — one line at startup.
            try
            {
                IntPtr launcherHwnd = new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle;
                if (launcherHwnd != IntPtr.Zero)
                    _log.Log($"ENV[launcher] {EnvironmentFingerprint.DescribeWindowMonitor(launcherHwnd)}");
            }
            catch (Exception ex)
            {
                _log.LogException("ENV[launcher]", ex);
            }

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

            ReconcileRestoredContainerZOrder();
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

    private IReadOnlyList<LogicalPresentationSnapshot> CaptureLogicalSnapshots()
    {
        var snapshots = new List<LogicalPresentationSnapshot>();
        foreach (ContainerWindow container in _containers.Values.ToList())
        {
            try
            {
                snapshots.Add(container.CreateDiagnosticSnapshot());
            }
            catch (Exception ex)
            {
                DiagnosticRuntime.Record("logical.snapshot", action: "observe", result: "failed",
                    data: new Dictionary<string, string> { ["error"] = ex.GetType().Name });
            }
        }
        return snapshots;
    }

    private void ExportDiagnosticsFromHotkey()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
                desktop = Environment.CurrentDirectory;
            string path = Path.Combine(desktop, $"TabDock-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            string output = DiagnosticReportService.ExportBundle(path);
            DiagnosticRuntime.Record("support.export", action: "zip", result: "success",
                data: new Dictionary<string, string>
                {
                    ["path"] = DiagnosticEnvironmentService.RedactPath(output),
                    ["trigger"] = "Ctrl+Alt+Shift+D",
                });
            _log.Log($"DIAGNOSTICS[export] path={DiagnosticEnvironmentService.RedactPath(output)}");
        }
        catch (Exception ex)
        {
            DiagnosticRuntime.Record("support.export", action: "zip", result: "failed",
                data: new Dictionary<string, string> { ["error"] = ex.GetType().Name });
            _log.LogException("Diagnostic bundle export", ex);
        }
    }

    private void ShowStorageCapabilityWarningIfNeeded()
    {
        var warnings = new List<string>();
        if (!_log.IsFileBacked)
            warnings.Add("diagnostic logs are memory-only");
        if (!_persistence.IsStorageAvailable)
            warnings.Add("layout persistence is disabled");
        if (!_shepherd.RecoveryJournalStorageAvailable)
        {
            _groups.SetCaptureAllowed(false, "durable crash-recovery journal storage is unavailable.");
            warnings.Add("guest capture is disabled because crash-recovery journaling is unavailable");
        }

        if (warnings.Count == 0)
            return;

        string message = "TabDock started with limited storage capabilities:\n\n- "
            + string.Join("\n- ", warnings)
            + "\n\nResolve the AppData/storage problem and restart TabDock before capturing windows.";
        _log.Log("STARTUP[storage-warning] " + string.Join("; ", warnings));
        try
        {
            MessageBox.Show(_mainWindow, message, "TabDock storage warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _log.LogException("STARTUP[storage-warning-dialog]", ex);
        }
    }

    /// <summary>
    /// One-shot startup z-order reconciliation for restored groups.
    ///
    /// On the startup-restore path each restored container is shown with
    /// Window.Show() followed by a launcher Hide() and is never given an explicit
    /// activation or z-order claim (OpenContainer). Whether the container lands
    /// above or below an overlapping pre-existing desktop window then depends
    /// entirely on the OS foreground grant at the moment of Show(): when that
    /// grant is missing, the container is silently buried behind the existing
    /// window. Nothing repairs it — the WinEvent pairing pipeline is not installed
    /// at this point (restored groups are empty, so IsMonitoringNeeded is false),
    /// and every container z-order memory (LayoutShepherdActiveWindow, the
    /// WM_ACTIVATE reassert, PairZOrderBehindGuest) requires a live guest, which an
    /// empty restored container has none of. The burial would persist for the whole
    /// session.
    ///
    /// This raises each restored container to the top of the normal z-order band
    /// exactly once, through the existing z-order authority primitive
    /// (WindowShepherdService.RaiseContainerForChrome, HWND_TOP + SWP_NOACTIVATE).
    /// It is a z-order-only repair: SWP_NOACTIVATE means it issues no foreground
    /// call, so it cannot steal focus and a later user activation of another app
    /// is never fought (nothing persists, no loop). Note that this DOES change
    /// visible z-order (the restored surface is raised in the normal band): TabDock
    /// has no supported background/silent/auto-start launch mode, so no such mode
    /// exists whose non-intrusiveness this needs to preserve. It is bounded — one
    /// write per restored container, once at startup.
    ///
    /// Containers are raised in restore order so the last-restored group (the one
    /// natural Show() ordering leaves on top) stays topmost among TabDock's own
    /// containers, matching pre-existing behavior. No guest HWND, style, owner, or
    /// placement is touched, and the container-behind-guest pairing invariant is
    /// unaffected (it is vacuous at startup and re-established by PositionAndShow
    /// when the first guest is captured).
    /// </summary>
    private void ReconcileRestoredContainerZOrder()
    {
        int raised = 0;
        foreach (var group in _groups.Groups.ToList())
        {
            if (!_containers.TryGetValue(group.Id, out ContainerWindow? container))
                continue;
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(container).Handle;
            if (hwnd != IntPtr.Zero)
            {
                _shepherd.RaiseContainerForChrome(hwnd);
                raised++;
            }
        }
        _log.Log($"STARTUP[reconcile] raised {raised} restored container(s) to the top of the normal z-order band (no activation)");
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
            StopWinEventMonitorRetry();
            _events?.Dispose();
            _winEventMonitorDisposed = true;
            _hotkey?.Dispose();
            _singleInstanceLease?.Dispose();
            DiagnosticRuntime.LogicalSnapshotProvider = null;
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
        // WPF raises SessionEnding before Windows has committed the logoff or
        // shutdown. TabDock deliberately chooses a one-way policy: once this
        // handler starts irreversible guest release and hook teardown, the app
        // exits completely. It never attempts to resume a half-normalized live
        // session if another application later causes Windows to cancel the
        // original session request.
        if (!SessionEndingPolicy.TryBeginTeardown(ref _sessionEndingTeardownStarted))
            return;

        // Logoff/shutdown can kill the process before Application_Exit runs.
        ContainerWindow.IsAppShuttingDown = true;
        _log?.Log($"Session ending ({e.ReasonSessionEnding}); committing one-way teardown and exiting after guest release.");
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

        // Stop dispatch before clearing the captured index. If the logoff is
        // cancelled by another application, no callback may act on a member
        // while its native window is already standalone.
        try
        {
            StopWinEventMonitorRetry();
            _events?.Stop();
        }
        catch (Exception ex)
        {
            _log?.LogException("Stopping WinEvent monitor during session ending", ex);
        }

        // Leave both the model and every open container in a coherent
        // post-release state. Preserve the just-released members as layout
        // intent before removing them from the captured index; otherwise a
        // later save after a cancelled logoff would erase that metadata.
        try
        {
            foreach (ContainerWindow container in _containers.Values.ToList())
            {
                try { container.ClearReleasedTabsAfterSessionEnding(); }
                catch (Exception containerEx) { _log?.LogException("Normalizing container during session ending", containerEx); }
            }

            _groups?.ClearCapturedMembersAfterSessionEnding();
            SaveStateGuarded("session-ending post-release normalization");
        }
        catch (Exception ex)
        {
            _log?.LogException("Clearing group members during session ending", ex);
        }

        // Explicitly finish the chosen policy. If Windows cancels the original
        // logoff/shutdown request, this local exit remains intentional and
        // prevents a half-running TabDock instance with monitoring disabled and
        // captured members already normalized away.
        _log?.Log("Session-ending teardown complete; shutting down TabDock by policy.");
        Shutdown(0);
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
    /// FlushJournal that can never throw (AUDIT25-01): finalizes the
    /// synchronous hidden-window crash-recovery journal on every exit/crash
    /// path. The call is retained as an idempotent safety boundary even though
    /// dangerous guest mutations already commit journal state synchronously.
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
            if (_events.IsRunning)
            {
                _winEventFailureHandled = false;
                _groups.SetCaptureAllowed(true, "WinEvent monitor is healthy.");
                StopWinEventMonitorRetry();
            }
            else
            {
                _groups.SetCaptureAllowed(false, "WinEvent monitor installation is pending retry.");
                ScheduleWinEventMonitorRetry();
            }
            return;
        }

        StopWinEventMonitorRetry();

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

    /// <summary>
    /// A capture-count edge can occur only once while a partial native hook
    /// install is failing. Retry a bounded number of times on the UI dispatcher
    /// so a transient SetWinEventHook failure cannot leave an otherwise healthy
    /// captured guest permanently unmonitored.
    /// </summary>
    private void ScheduleWinEventMonitorRetry()
    {
        if (_winEventRetryTimer != null || _winEventMonitorDisposed)
            return;

        _winEventRetryAttempts = 0;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_winEventRetryTimer, timer))
            {
                timer.Stop();
                return;
            }

            if (_winEventMonitorDisposed || _events == null || !_groups.IsMonitoringNeeded)
            {
                StopWinEventMonitorRetry();
                return;
            }

            _events.Start();
            if (_events.IsRunning)
            {
                _log.Log("WinEventMonitor retry succeeded while captured windows remained active.");
                _winEventFailureHandled = false;
                _groups.SetCaptureAllowed(true, "WinEvent monitor retry succeeded.");
                StopWinEventMonitorRetry();
                return;
            }

            _winEventRetryAttempts++;
            if (_winEventRetryAttempts >= 3)
            {
                HandleWinEventMonitoringFailure();
                StopWinEventMonitorRetry();
            }
        };
        _winEventRetryTimer = timer;
        timer.Start();
    }

    private void StopWinEventMonitorRetry()
    {
        _winEventRetryTimer?.Stop();
        _winEventRetryTimer = null;
        _winEventRetryAttempts = 0;
    }

    /// <summary>
    /// A captured guest without destroy/hide/minimize/move monitoring is not a
    /// supported steady state. After the bounded retry budget, release every
    /// guest, normalize the view/model, persist the resulting layout intent,
    /// and keep capture admission closed until a fresh process startup. This
    /// fails closed instead of silently running an unsupported lifecycle mode.
    /// </summary>
    private void HandleWinEventMonitoringFailure()
    {
        if (_winEventFailureHandled || _groups == null)
            return;
        _winEventFailureHandled = true;
        _log.Log($"WinEventMonitor permanently unavailable ({_events?.LastStartFailure ?? "unknown"}); releasing captured guests and disabling capture admission.");
        _groups.SetCaptureAllowed(false, "WinEvent monitor failed its bounded retry budget.");
        try
        {
            _groups.EmergencyReleaseAll();
            foreach (ContainerWindow container in _containers.Values.ToList())
            {
                try { container.ClearReleasedTabsAfterSessionEnding(); }
                catch (Exception ex) { _log.LogException("Normalizing container after monitor failure", ex); }
            }
            _groups.ClearCapturedMembersAfterSessionEnding();
            SaveStateGuarded("WinEvent monitor failure normalization");
        }
        catch (Exception ex)
        {
            _log.LogException("WinEvent monitor failure normalization", ex);
        }

        try
        {
            MessageBox.Show(
                _mainWindow,
                "TabDock could not maintain its native guest-lifecycle monitor. Captured windows were released safely and capture is disabled for this session. Restart TabDock to try again.",
                "TabDock monitoring unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _log.LogException("WinEvent monitor failure warning", ex);
        }
    }

    private void OnNewGroupRequested(object? sender, EventArgs e)
    {
        CreateAndOpenGroup(sender as Window);
    }

    private void OnContainerNewGroupRequested(object? sender, EventArgs e)
    {
        CreateAndOpenGroup(sender as Window);
    }

    private void CreateAndOpenGroup(Window? owner)
    {
        var group = _groups.CreateGroup();
        ContainerWindow? window = null;
        try
        {
            window = OpenContainer(group);
            window.Activate();
        }
        catch (Exception ex)
        {
            // The group must not outlive a container that failed to open: it would
            // be saved on exit and re-opened at startup, turning a one-time failure
            // into a crash on every subsequent launch.
            _groups.RemoveGroup(group);
            if (window != null)
            {
                try
                {
                    window.Close();
                }
                catch (Exception closeEx)
                {
                    _log.LogException($"CreateAndOpenGroup cleanup failed for group {group.Id}", closeEx);
                }
            }
            if (_containers.TryGetValue(group.Id, out ContainerWindow? registered)
                && ReferenceEquals(registered, window))
            {
                registered.GroupSelectedRequested -= OnContainerGroupSelectedRequested;
                registered.NewGroupRequested -= OnContainerNewGroupRequested;
                _containers.Remove(group.Id);
            }
            _log.LogException("OpenContainer for new group", ex);
            MessageBox.Show(owner ?? _mainWindow, "Could not open the container for the new group.", "TabDock", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnContainerGroupSelectedRequested(object? sender, ContainerWindow.GroupSelectionEventArgs e)
    {
        if (_containers.TryGetValue(e.Group.Id, out ContainerWindow? window))
        {
            window.Activate();
            return;
        }

        try
        {
            OpenContainer(e.Group).Activate();
        }
        catch (Exception ex)
        {
            _log.LogException($"OpenContainer for selected group {e.Group.Id}", ex);
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
        var pickerVm = new CapturePickerViewModel(_groups, _icons, _log);
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
        if (result != true || picker.Result == null || picker.Result.SelectedTargets.Count == 0)
            return;

        Group? group;
        bool createdGroupForCapture = false;
        if (picker.Result.TargetGroupId == Guid.Empty)
        {
            group = _groups.CreateGroup();
            createdGroupForCapture = true;
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
                createdGroupForCapture = true;
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

        if (!_containers.TryGetValue(group.Id, out ContainerWindow? container))
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

        try
        {
            foreach (WindowCaptureTarget target in picker.Result.SelectedTargets)
            {
                string? error = container.CaptureWindow(target);
                if (error != null)
                {
                    _log.Log($"Capture failed for 0x{target.Hwnd.ToInt64():X}: {error}");
                    // Explicit owner: an owner-less MessageBox falls back to WPF's
                    // own default modal-parent resolution, which can disable more
                    // than just this container if it resolves unexpectedly.
                    MessageBox.Show(container, error, "Could not capture window", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        finally
        {
            if (createdGroupForCapture && !group.HasMaterializedTabs)
                DiscardFailedCaptureGroup(group, container);
        }
    }

    /// <summary>
    /// A picker-created group is provisional until at least one selected window
    /// is admitted. If every selected capture fails, close its shell and remove
    /// it from the model so a failed or stale picker action cannot manufacture
    /// another zero-tab group.
    /// </summary>
    private void DiscardFailedCaptureGroup(Group group, ContainerWindow? container)
    {
        if (group.HasMaterializedTabs)
            return;

        _log.Log($"Discarding picker-created empty group {group.Id} after all captures failed.");
        try
        {
            if (container != null
                && _containers.TryGetValue(group.Id, out ContainerWindow? registered)
                && ReferenceEquals(registered, container))
            {
                container.Close();
            }
        }
        catch (Exception ex)
        {
            _log.LogException($"Closing failed-capture group {group.Id}", ex);
        }

        // OnContainerClosed normally removes the group synchronously. If the
        // window was already gone, keep the model equally tidy; if Close()
        // failed while the window remains registered, retain the live shell so
        // it cannot become an orphaned container bound to a detached Group.
        if (!_containers.ContainsKey(group.Id))
            _groups.RemoveGroup(group);
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
        ContainerWindow? window = null;
        try
        {
            window = new ContainerWindow(vm, _groups, _shepherd, _log, _icons);
            vm.AddWindowsRequested += (_, _) => window.OpenCapturePanel();
            window.GroupSelectedRequested += OnContainerGroupSelectedRequested;
            window.NewGroupRequested += OnContainerNewGroupRequested;
            window.Closed += (_, _) => OnContainerClosed(group.Id, window);
            // Register before Show: WPF can synchronously raise lifecycle
            // events while a window is being shown. A Closed callback must see
            // the same instance in the registry, or a just-closed window can
            // be inserted into _containers after its callback has already run.
            _containers[group.Id] = window;
            window.Show();
            _mainWindow?.Hide();
        }
        catch (Exception ex)
        {
            _log?.LogException($"OpenContainer failed for group {group.Id}", ex);
            if (window != null)
            {
                try
                {
                    window.Close();
                }
                catch (Exception closeEx)
                {
                    _log?.LogException($"OpenContainer cleanup failed for group {group.Id}", closeEx);
                }
            }
            if (_containers.TryGetValue(group.Id, out ContainerWindow? registered)
                && ReferenceEquals(registered, window))
            {
                registered.GroupSelectedRequested -= OnContainerGroupSelectedRequested;
                registered.NewGroupRequested -= OnContainerNewGroupRequested;
                _containers.Remove(group.Id);
            }
            vm.Detach();
            throw;
        }

        _log.Log($"Opened container for group {group.Id}.");
        return window!;
    }

    private void OnContainerClosed(Guid groupId, ContainerWindow closedWindow)
    {
        if (_containers.TryGetValue(groupId, out ContainerWindow? registered)
            && ReferenceEquals(registered, closedWindow))
        {
            registered.GroupSelectedRequested -= OnContainerGroupSelectedRequested;
            registered.NewGroupRequested -= OnContainerNewGroupRequested;
            _containers.Remove(groupId);
        }
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

        if (_containers.Count == 0 && _mainWindow != null)
            _mainWindow.Show();
    }

    /// <summary>
    /// Acquires the global single-instance mutex. Returns false if another
    /// TabDock process already owns it, in which case this instance must exit
    /// without touching shared state files.
    /// </summary>
    private bool AcquireSingleInstanceMutex()
    {
        if (ProductMutationLease.TryAcquire(out ProductMutationLease? lease))
        {
            _singleInstanceLease = lease;
            return true;
        }
        _log?.Log("AcquireSingleInstanceMutex: another product-mutating TabDock owner is active.");
        return false;
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
