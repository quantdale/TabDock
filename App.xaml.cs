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
    private DpiService _dpi = null!;
    private RenderHealthService _renderHealth = null!;
    private IconService _icons = null!;
    private WindowCaptureService _capture = null!;
    private PersistenceService _persistence = null!;
    private GroupManager _groups = null!;
    private WinEventMonitor _events = null!;
    private HotkeyService _hotkey = null!;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private readonly Dictionary<Guid, ContainerWindow> _containers = new();

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

            _dpi = new DpiService();
            _renderHealth = new RenderHealthService(_dpi);
            _icons = new IconService();
            _capture = new WindowCaptureService(_log, _icons, _dpi);
            _persistence = new PersistenceService(_log);
            _groups = new GroupManager(_capture, _persistence, _log);
            _events = new WinEventMonitor(_groups.IsCapturedWindow, _log);
            _hotkey = new HotkeyService(_log);

            WireWinEvents();
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
        }
    }

    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
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

        _events.WindowNameChanged += (_, args) =>
        {
            foreach (var group in _groups.Groups)
            {
                var match = group.Members.FirstOrDefault(m => m.Hwnd == args.Hwnd);
                if (match == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(match.CustomLabel))
                    continue; // User label wins.

                string? newTitle = NativeMethods.GetWindowTextString(args.Hwnd);
                if (newTitle == null)
                    continue;

                match.OriginalTitle = newTitle;
                _log.Log($"WinEvent: title changed for 0x{args.Hwnd.ToInt64():X} -> '{newTitle}'.");

                if (_containers.TryGetValue(group.Id, out var container))
                {
                    Dispatcher.Invoke(() => container.RefreshTabTitles());
                }
            }
        };
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
        Shutdown();
    }

    private ContainerWindow OpenContainer(Group group)
    {
        if (_containers.TryGetValue(group.Id, out var existing))
        {
            existing.Activate();
            return existing;
        }

        var vm = new GroupViewModel(group, _groups, _capture, _icons);
        // The container's "+" button funnels through this event; without the
        // subscription it is a dead control.
        vm.AddWindowsRequested += (_, _) => ShowCapturePicker(group);
        var window = new ContainerWindow(vm, _groups, _capture, _renderHealth, _log);
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
    }

}
