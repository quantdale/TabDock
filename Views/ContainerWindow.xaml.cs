using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TabDock.Infrastructure;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;

namespace TabDock.Views;

/// <summary>
/// A container window that hosts shepherded application windows in a tabbed UI.
/// A shepherded guest is never reparented or restyled — it stays an unmodified
/// top-level window; this container just positions, shows, hides, and z-orders
/// it (see Services/WindowShepherdService.cs and
/// docs/internal/deep-audit-2026-07-17.md section 6).
/// </summary>
public partial class ContainerWindow : Window
{
    private readonly GroupViewModel _viewModel;
    private readonly GroupManager _manager;
    private readonly WindowShepherdService _shepherd;
    private readonly LoggingService _log;

    // The shepherded active-tab guest. Never bound through a WPF dependency
    // property (a shepherd guest is a sibling top-level window, not content
    // hosted inside anything) — this field and the methods around it are this
    // container's entire sync loop for the active tab.
    private CapturedWindow? _shepherdActiveWindow;

    // Drag state (tab-strip reorder / drag-out)
    private TabViewModel? _draggedTab;
    private Point _dragStart;
    private ListBoxItem? _draggedItem;
    private bool _isDragging;
    private const double DragThreshold = 4;

    // Tab-strip slot midpoints snapshotted at drag start. Drop targeting must
    // not read live container geometry mid-drag: a reorder mutates the layout
    // under a stationary pointer, and the next MouseMove would compute the
    // opposite index and reorder straight back (finding H2's oscillation).
    private System.Collections.Generic.List<double>? _dragMidpoints;
    private int _dragMidpointsCount;
    private bool _dragMidpointsValid;

    // A guest dragged more than this many pixels off its docked position by its
    // own (real, visible) title bar is treated as an intentional pop-out rather
    // than jitter to snap back. See NoteGuestMoveSize.
    private const int DragOutThresholdPx = 40;

    /// <summary>
    /// Set by App before any exit/crash path calls Application.Shutdown so every
    /// open container's Closing handler skips the Yes/No/Cancel prompt instead of
    /// showing one modal per container with nobody left to answer it (finding M6).
    /// GroupManager.EmergencyReleaseAll (called by the same exit/crash paths) is
    /// what actually returns captured windows to standalone; this flag only stops
    /// Closing from blocking on user input during teardown.
    /// </summary>
    public static bool IsAppShuttingDown { get; set; }

    /// <summary>
    /// The underlying group model.
    /// </summary>
    public Group Group => _viewModel.Model;

    /// <summary>
    /// The native marker HWND that defines exactly where the content area sits
    /// on screen. See Infrastructure/NativeHwndHost.cs.
    /// </summary>
    public IntPtr ContentHostHwnd => ContentHost.HostWindowHandle;

    public ContainerWindow(GroupViewModel viewModel, GroupManager manager, WindowShepherdService shepherd, LoggingService log)
    {
        _viewModel = viewModel;
        _manager = manager;
        _shepherd = shepherd;
        _log = log;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += ContainerWindow_Loaded;
        Closing += ContainerWindow_Closing;
        Closed += ContainerWindow_Closed;
        StateChanged += ContainerWindow_StateChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        // Popping out the last tab (drag-out or context menu) leaves the group
        // empty; close the now-pointless container instead of leaving it open
        // (finding L11). IsAppShuttingDown is irrelevant here — this always runs
        // on the interactive pop-out path, never during app teardown.
        _viewModel.EmptiedByPopOut += (_, _) => Close();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupViewModel.ActiveTab))
        {
            SyncShepherdActiveWindow();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Clamp maximize to the monitor work area. A WindowStyle="None" +
        // WindowChrome window otherwise maximizes to the full monitor plus the
        // invisible resize border, covering the taskbar and spilling a few pixels
        // past every edge. See WndProc/WM_GETMINMAXINFO.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        HwndSource? source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == NativeMethods.WM_ACTIVATE)
        {
            uint activateKind = (uint)(wParam.ToInt64() & 0xFFFF);
            // On activation (alt-tab back, click on caption), re-assert the
            // guest's overlay position/z-order (it may have drifted while
            // inactive) and give it real foreground activation. On
            // deactivation there is nothing to do — Windows naturally raises
            // whatever the user just activated above both the container and
            // its docked guest, and the guest's own input handling is
            // completely untouched (no attach/detach state to manage: the
            // guest is a real top-level window the whole time).
            if ((activateKind == NativeMethods.WA_ACTIVE || activateKind == NativeMethods.WA_CLICKACTIVE) &&
                _shepherdActiveWindow != null)
            {
                // Because the container is kept z-order-paired immediately
                // behind its active guest, the guest hiding ITSELF (e.g. a
                // tray-style close) naturally hands this container the very
                // next WM_ACTIVATE — delivered synchronously as part of the
                // same OS activation transaction, which can race ahead of the
                // guest's own Hide() call fully settling. Forcing it back to
                // visible here (PositionAndShow uses SWP_SHOWWINDOW) would
                // fight that intentional hide. Defer briefly and re-check
                // visibility right before acting, so an in-flight self-hide
                // has settled either way by the time this decides.
                CapturedWindow activeWindow = _shepherdActiveWindow;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (_shepherdActiveWindow == activeWindow && NativeMethods.IsWindowVisible(activeWindow.Hwnd))
                        _shepherd.BringToFront(activeWindow, hwnd, GetContentAreaScreenRect());
                };
                timer.Start();
            }
        }
        else if ((uint)msg == NativeMethods.WM_GETMINMAXINFO)
        {
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                if (NativeMethods.GetMonitorInfo(monitor, ref mi))
                {
                    var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
                    // Position/size the maximized window to the work area, expressed
                    // relative to the monitor origin (ptMaxPosition is monitor-relative).
                    mmi.ptMaxPosition.x = mi.rcWork.left - mi.rcMonitor.left;
                    mmi.ptMaxPosition.y = mi.rcWork.top - mi.rcMonitor.top;
                    mmi.ptMaxSize.x = mi.rcWork.Width;
                    mmi.ptMaxSize.y = mi.rcWork.Height;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                }
            }
        }
        return IntPtr.Zero;
    }

    private void ContainerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        _manager.RegisterContainerHwnd(hwnd);

        if (ContentHost.HostWindowHandle != IntPtr.Zero)
            _manager.RegisterContainerHwnd(ContentHost.HostWindowHandle);

        TabsListBox.PreviewMouseLeftButtonDown += TabsListBox_PreviewMouseLeftButtonDown;
        TabsListBox.MouseMove += TabsListBox_MouseMove;
        TabsListBox.PreviewMouseLeftButtonUp += TabsListBox_PreviewMouseLeftButtonUp;

        PreviewKeyDown += ContainerWindow_PreviewKeyDown;

        // Keep the shepherded active guest glued to the content area as the
        // container itself moves or resizes (a shepherded guest is a sibling
        // top-level window, so this container must reposition it explicitly —
        // there is no WS_CHILD/HwndHost relationship to get this for free).
        LocationChanged += (_, _) => LayoutShepherdActiveWindow();
        SizeChanged += (_, _) => LayoutShepherdActiveWindow();
    }

    private void ContainerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            return;
        if (e.Key != Key.Tab)
            return;

        int count = _viewModel.Tabs.Count;
        if (count <= 1)
            return;

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        int current = _viewModel.ActiveTab != null
            ? _viewModel.Tabs.IndexOf(_viewModel.ActiveTab)
            : 0;
        if (current < 0)
            current = 0;

        int next = shift
            ? (current - 1 + count) % count
            : (current + 1) % count;

        _viewModel.SetActiveTab(_viewModel.Tabs[next]);
        TabsListBox.SelectedIndex = next;
        e.Handled = true;
    }

    private void ContainerWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (IsAppShuttingDown)
            return;
        if (_viewModel.Tabs.Count == 0)
            return;

        var result = MessageBox.Show(
            "Do you want to close the grouped applications?\n\nYes = close all apps\nNo = release windows back to standalone",
            "Close group",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                // Snapshot HWNDs before CloseGroup clears the view model, then release
                // windows back to standalone and ask each one to close normally.
                var hwndsToClose = _viewModel.Tabs
                    .Select(t => t.Model.Hwnd)
                    .Where(h => h != IntPtr.Zero)
                    .ToList();
                _viewModel.CloseGroup();
                foreach (var hwnd in hwndsToClose)
                {
                    if (NativeMethods.IsWindow(hwnd))
                        NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                break;

            case MessageBoxResult.No:
                _viewModel.CloseGroup();
                break;

            default:
                e.Cancel = true;
                break;
        }
    }

    private void ContainerWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        _manager.UnregisterContainerHwnd(hwnd);
        if (ContentHost.HostWindowHandle != IntPtr.Zero)
            _manager.UnregisterContainerHwnd(ContentHost.HostWindowHandle);
    }

    private void ContainerWindow_StateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";

        if (_shepherdActiveWindow != null)
        {
            // A minimized container has no visible content area to overlay;
            // hide the docked guest along with it. Restoring re-positions and
            // re-shows it (LayoutShepherdActiveWindow uses SWP_SHOWWINDOW).
            if (WindowState == WindowState.Minimized)
                _shepherd.Hide(_shepherdActiveWindow);
            else
                LayoutShepherdActiveWindow();
        }

        // Lightweight state snapshot after the transition settles. Retained (low
        // volume: once per maximize/restore) as a field-diagnosis aid.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            LogStateSnapshot("settled");
        };
        timer.Start();
    }

    private void LogStateSnapshot(string phase)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT winRect);
            uint dpi = NativeMethods.GetDpiForWindow(hwnd);

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            NativeMethods.GetMonitorInfo(monitor, ref mi);

            string hostDesc = "none";
            if (ContentHostHwnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowRect(ContentHostHwnd, out NativeMethods.RECT hostRect);
                hostDesc = $"rect={hostRect.left},{hostRect.top},{hostRect.Width}x{hostRect.Height}";
            }

            string guestDesc = "none";
            var active = _viewModel.ActiveTab?.Model;
            if (active != null)
            {
                guestDesc = NativeMethods.DescribeWindow(active.Hwnd);
                if (NativeMethods.IsWindow(active.Hwnd))
                {
                    NativeMethods.GetWindowRect(active.Hwnd, out NativeMethods.RECT guestRect);
                    NativeMethods.RECT docked = GetContentAreaScreenRect();
                    bool isDocked = guestRect.left == docked.left && guestRect.top == docked.top
                        && guestRect.right == docked.right && guestRect.bottom == docked.bottom;
                    guestDesc += $" docked={isDocked}";
                }
            }

            _log.Log($"STATE[{phase}] winState={WindowState} container={winRect.left},{winRect.top},{winRect.Width}x{winRect.Height} dpi={dpi} " +
                     $"monitor={mi.rcMonitor.left},{mi.rcMonitor.top},{mi.rcMonitor.Width}x{mi.rcMonitor.Height} work={mi.rcWork.left},{mi.rcWork.top},{mi.rcWork.Width}x{mi.rcWork.Height} " +
                     $"host=({hostDesc}) guest={guestDesc}");
        }
        catch (Exception ex)
        {
            _log.LogException("LogStateSnapshot", ex);
        }
    }

    private void TabsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabsListBox.SelectedItem is TabViewModel tab)
        {
            _viewModel.SetActiveTab(tab);
        }
    }

    private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BeginRename();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Enters rename mode and focuses the editor. The RenameBox is Collapsed until
    /// the IsRenaming DataTrigger applies after a layout pass, and Focus() on a
    /// not-yet-visible element is a no-op (keystrokes would go nowhere), so focus is
    /// applied when the box actually becomes visible.
    /// </summary>
    private void BeginRename()
    {
        _viewModel.IsRenaming = true;

        if (RenameBox.IsVisible)
        {
            FocusRenameBox();
            return;
        }

        DependencyPropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (RenameBox.IsVisible)
            {
                RenameBox.IsVisibleChanged -= handler;
                FocusRenameBox();
            }
        };
        RenameBox.IsVisibleChanged += handler;
    }

    private void FocusRenameBox()
    {
        RenameBox.Focus();
        Keyboard.Focus(RenameBox);
        RenameBox.SelectAll();
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.IsRenaming = false;
        // Persist the (possibly) new name now; state is otherwise only saved on
        // clean app exit, so a rename would not survive a crash. Fires on every
        // completion path (Enter, Escape, click-away) because collapsing the
        // focused TextBox always forces a focus loss.
        _manager.SaveState();
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Commit explicitly rather than relying on collapse-induced
            // LostFocus ordering (the binding is UpdateSourceTrigger=LostFocus),
            // and persist immediately so the new name is durable at the moment of
            // commit rather than whenever focus happens to drop.
            RenameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            _viewModel.IsRenaming = false;
            _manager.SaveState();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Revert the box to the current name so the forced focus loss
            // below does not commit the abandoned edit.
            RenameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            _viewModel.IsRenaming = false;
            e.Handled = true;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ColorChip_Click(object sender, RoutedEventArgs e)
    {
        ColorContextMenu.PlacementTarget = (UIElement)sender;
        ColorContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ColorContextMenu.IsOpen = true;
    }

    private void ColorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string color)
        {
            _viewModel.AccentColor = color;
            _manager.SaveState();
        }
    }

    private void AddWindow_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RequestAddWindows();
    }

    /// <summary>
    /// Captures a top-level window and adds it as a new tab in this container.
    /// Returns an error message if capture fails (e.g. UIPI, elevated window, own window).
    /// </summary>
    public string? CaptureWindow(IntPtr hwnd)
    {
        if (_manager.IsOwnWindow(hwnd))
            return "Cannot capture a TabDock window (no nesting).";

        CapturedWindow? cw = _shepherd.Capture(hwnd, out string? error);
        if (cw == null)
            return error ?? "Capture failed.";

        AddCapturedWindow(cw);
        return null;
    }

    /// <summary>
    /// Adds an already-captured window to this container's view model.
    /// </summary>
    public void AddCapturedWindow(CapturedWindow window)
    {
        _viewModel.AddCapturedWindow(window);
    }

    /// <summary>
    /// Releases a specific captured window from this container back to standalone.
    /// When <paramref name="show"/> is false the window stays hidden after release
    /// (used when the guest hid itself, e.g. a tray-style close).
    /// </summary>
    public void ReleaseCapturedWindow(CapturedWindow window, bool show = true)
    {
        var tab = _viewModel.Tabs.FirstOrDefault(t => t.Model == window);
        if (tab != null)
            _viewModel.ReleaseTab(tab, show);
    }

    #region Shepherd active-tab sync

    /// <summary>
    /// Reacts to an ActiveTab change: hides the previously active guest (only
    /// if it is still a member of this group — if it was just released,
    /// WindowShepherdService.Release already decided its final visible state
    /// and must not be second-guessed here), then positions and shows the
    /// newly active one.
    /// </summary>
    private void SyncShepherdActiveWindow()
    {
        CapturedWindow? newWindow = _viewModel.ActiveTab?.Model;
        CapturedWindow? oldWindow = _shepherdActiveWindow;
        if (ReferenceEquals(oldWindow, newWindow))
            return;

        if (oldWindow != null && _viewModel.Tabs.Any(t => t.Model == oldWindow))
        {
            _shepherd.Hide(oldWindow);
        }

        _shepherdActiveWindow = newWindow;

        if (newWindow != null && NativeMethods.IsWindow(newWindow.Hwnd))
        {
            LayoutShepherdActiveWindow();
        }
    }

    /// <summary>
    /// Positions and shows the active guest to exactly cover the content area,
    /// layered directly above this container. Called on tab switch, container
    /// move/resize/restore, and drag-out-threshold snap-back.
    /// </summary>
    private void LayoutShepherdActiveWindow()
    {
        if (_shepherdActiveWindow == null)
            return;
        if (WindowState == WindowState.Minimized)
            return;

        IntPtr containerHwnd = new WindowInteropHelper(this).Handle;
        if (containerHwnd == IntPtr.Zero)
            return;

        // StateChanged (maximize/restore) can fire before WPF's own layout pass
        // has run the content marker's ArrangeOverride, which is what actually
        // resizes its native HWND — reading its screen rect too early silently
        // repositions the guest to the stale, pre-transition size. Force the
        // pending layout to flush first so the marker's HWND is already correct.
        UpdateLayout();
        _shepherd.PositionAndShow(_shepherdActiveWindow, containerHwnd, GetContentAreaScreenRect());
    }

    /// <summary>
    /// The content area in screen coordinates, in physical pixels — read
    /// directly off the native marker window (Infrastructure/NativeHwndHost.cs)
    /// rather than computed from the WPF element, so production code and the
    /// real-input test harness (a separate process) agree on exactly the same
    /// rect via the same native calls.
    /// </summary>
    private NativeMethods.RECT GetContentAreaScreenRect()
    {
        IntPtr hostHwnd = ContentHost.HostWindowHandle;
        NativeMethods.GetClientRect(hostHwnd, out NativeMethods.RECT rc);
        var topLeft = new NativeMethods.POINT { x = 0, y = 0 };
        NativeMethods.ClientToScreen(hostHwnd, ref topLeft);
        return new NativeMethods.RECT
        {
            left = topLeft.x,
            top = topLeft.y,
            right = topLeft.x + rc.Width,
            bottom = topLeft.y + rc.Height,
        };
    }

    /// <summary>
    /// Keeps the container paired with its shepherded active guest when the
    /// guest becomes the system foreground window by some means other than
    /// this container's own BringToFront (e.g. the user alt-tabs via Windows'
    /// own switcher, or clicks the guest directly instead of the tab strip).
    /// Purely a z-order nicety: mouse and keyboard input already route
    /// correctly to the guest natively regardless; this just keeps the
    /// container visually paired immediately behind it so the docked look
    /// holds together through real-world alt-tab patterns.
    /// </summary>
    public void PairZOrderBehindGuest(IntPtr foregroundHwnd)
    {
        if (_shepherdActiveWindow == null || _shepherdActiveWindow.Hwnd != foregroundHwnd)
            return;

        IntPtr containerHwnd = new WindowInteropHelper(this).Handle;
        if (containerHwnd == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(containerHwnd, foregroundHwnd, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    #endregion

    /// <summary>
    /// Restores a captured window that minimized itself (e.g. via the guest app's
    /// own custom-drawn minimize button or an in-app shortcut). A captured child
    /// has no taskbar presence, so leaving it iconic shows a black content area
    /// with no way to bring it back. Only the active tab is restored eagerly;
    /// inactive tabs are restored when they are next activated.
    /// </summary>
    public void RestoreMinimizedWindow(CapturedWindow window)
    {
        if (_viewModel.ActiveTab?.Model != window)
            return;
        if (!NativeMethods.IsWindow(window.Hwnd) || !NativeMethods.IsIconic(window.Hwnd))
            return;
        // A window that minimizes AND drops WS_VISIBLE is minimizing to the
        // tray (X-button close on tray apps) — restoring it here would fight
        // the guest and defeat the guest-initiated-hide teardown. But a guest
        // that does both (e.g. WindowState = Minimized; Hide();) fires this
        // MINIMIZESTART-triggered check before its own very next line has
        // taken effect — checking IsWindowVisible synchronously here can
        // observe "iconic AND still visible" in that narrow gap and wrongly
        // decide to restore. Defer briefly so an immediately-following Hide()
        // has a chance to land first; re-check both flags at that point.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!NativeMethods.IsWindow(window.Hwnd) || !NativeMethods.IsIconic(window.Hwnd)
                || !NativeMethods.IsWindowVisible(window.Hwnd))
                return;

            NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_RESTORE);
            LayoutShepherdActiveWindow();
        };
        timer.Start();
    }

    /// <summary>
    /// Tracks a captured guest's interactive move/size modal loop. A shepherded
    /// guest keeps its own real, visible title bar (see the audit's honest
    /// tradeoffs), so a user can genuinely drag it by that title bar. On
    /// MOVESIZEEND: if the guest ended up more than DragOutThresholdPx from its
    /// docked position, treat it as an intentional pop-out (release the tab,
    /// same as dragging it out via the tab strip — restores to the original
    /// pre-capture placement, consistent with every other release path);
    /// otherwise snap it back exactly (absorbs OS-level jitter). This replaces
    /// the old Reparent-only drift-watchdog/reclamp-retry-timer trio with a
    /// single event-driven check.
    /// </summary>
    public void NoteGuestMoveSize(CapturedWindow window, bool started)
    {
        if (started)
            return;
        if (_viewModel.ActiveTab?.Model != window)
            return;
        if (!NativeMethods.IsWindow(window.Hwnd) || !NativeMethods.IsWindowVisible(window.Hwnd)
            || NativeMethods.IsIconic(window.Hwnd) || NativeMethods.IsZoomed(window.Hwnd))
            return;

        NativeMethods.RECT docked = GetContentAreaScreenRect();
        NativeMethods.GetWindowRect(window.Hwnd, out NativeMethods.RECT guest);
        bool movedFar = Math.Abs(guest.left - docked.left) > DragOutThresholdPx
            || Math.Abs(guest.top - docked.top) > DragOutThresholdPx;

        if (movedFar)
        {
            _log.Log($"SHEPHERD[dragout] guest 0x{window.Hwnd.ToInt64():X} dragged to {guest.left},{guest.top} (docked was {docked.left},{docked.top}); releasing as pop-out.");
            ReleaseCapturedWindow(window);
        }
        else
        {
            LayoutShepherdActiveWindow();
        }
    }

    /// <summary>
    /// Refreshes tab titles after an external window name change.
    /// </summary>
    public void RefreshTabTitles()
    {
        foreach (var tab in _viewModel.Tabs)
            tab.RefreshTitle();
    }

    #region Drag reorder / drag-out release

    private void TabsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggedItem = FindListBoxItem(e.OriginalSource);
        if (_draggedItem == null)
            return;

        _draggedTab = _draggedItem.DataContext as TabViewModel;
        _dragStart = e.GetPosition(TabsListBox);
        _isDragging = false;
        // Do NOT take mouse capture here. Capturing during the tunneling event
        // makes WPF route the subsequent bubbling MouseLeftButtonDown to the
        // ListBox (the capture holder) instead of the ListBoxItem, so the item's
        // click-to-select logic never runs and tab clicks silently do nothing.
        // Capture starts in MouseMove once the drag threshold is exceeded.
        e.Handled = false;
    }

    private void TabsListBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedTab == null || _draggedItem == null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        Point pos = e.GetPosition(TabsListBox);
        Vector delta = pos - _dragStart;
        if (!_isDragging && (Math.Abs(delta.X) > DragThreshold || Math.Abs(delta.Y) > DragThreshold))
        {
            _isDragging = true;
            Mouse.Capture(TabsListBox);
            SnapshotDragMidpoints();
        }

        if (!_isDragging)
            return;

        // Dragged outside the container window => release (pop out).
        // Compare in device-independent units relative to this window;
        // PointToScreen yields device pixels, which diverge from the DIP-based
        // window rect on any DPI scale other than 100%.
        Point posInWindow = e.GetPosition(this);
        if (posInWindow.X < 0 || posInWindow.Y < 0 ||
            posInWindow.X > ActualWidth || posInWindow.Y > ActualHeight)
        {
            var tab = _draggedTab;
            EndDrag();
            _viewModel.ReleaseTab(tab);
            return;
        }

        // Reorder within the strip.
        int? targetIndex = GetDropIndex(pos);
        if (targetIndex.HasValue)
        {
            int currentIndex = _viewModel.Tabs.IndexOf(_draggedTab);
            if (currentIndex >= 0 && targetIndex.Value != currentIndex)
            {
                _viewModel.ReorderTabs(currentIndex, targetIndex.Value);
            }
        }
    }

    private void TabsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndDrag();
    }

    private void EndDrag()
    {
        Mouse.Capture(null);
        _draggedTab = null;
        _draggedItem = null;
        _isDragging = false;
        _dragMidpoints = null;
        _dragMidpointsCount = 0;
        _dragMidpointsValid = false;
    }

    private ListBoxItem? FindListBoxItem(object source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current != null && !(current is ListBoxItem))
        {
            current = VisualTreeHelper.GetParent(current);
        }
        return current as ListBoxItem;
    }

    /// <summary>
    /// Caches each tab slot's horizontal midpoint at drag start. Geometry is
    /// settled at that moment; mid-drag it is not — a reorder moves the slots
    /// under a stationary pointer, and recomputing the drop index from live
    /// containers made the next MouseMove reorder straight back (the H2
    /// oscillation: hundreds of A-&gt;B / B-&gt;A flips per second).
    /// </summary>
    private void SnapshotDragMidpoints()
    {
        var midpoints = new System.Collections.Generic.List<double>(_viewModel.Tabs.Count);
        for (int i = 0; i < _viewModel.Tabs.Count; i++)
        {
            if (TabsListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                Point itemPos = item.TranslatePoint(new Point(0, 0), TabsListBox);
                midpoints.Add(itemPos.X + item.ActualWidth / 2);
            }
            else
            {
                // A container is missing (virtualized/not yet generated); the
                // cache would be misaligned. Disable reorder for this drag.
                _dragMidpoints = null;
                _dragMidpointsCount = 0;
                _dragMidpointsValid = false;
                return;
            }
        }
        _dragMidpoints = midpoints;
        _dragMidpointsCount = midpoints.Count;
        _dragMidpointsValid = true;
    }

    private int? GetDropIndex(Point mousePos)
    {
        if (!_dragMidpointsValid)
            return null;

        // A count change mid-drag (a tab destroyed or hidden by a WinEvent
        // handler between mouse moves) invalidates the cache. Re-snapshot:
        // reorders never change the count, so this cannot reintroduce the
        // oscillation feedback loop.
        if (_dragMidpoints != null && _viewModel.Tabs.Count != _dragMidpointsCount)
        {
            SnapshotDragMidpoints();
            if (!_dragMidpointsValid)
                return null;
        }

        if (_dragMidpoints != null)
        {
            for (int i = 0; i < _dragMidpoints.Count; i++)
            {
                if (mousePos.X < _dragMidpoints[i])
                    return i;
            }
            return _dragMidpoints.Count > 0 ? _dragMidpoints.Count : null;
        }

        return null;
    }

    #endregion
}
