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
/// A container window that hosts reparented application windows in a tabbed UI.
/// </summary>
public partial class ContainerWindow : Window
{
    private readonly GroupViewModel _viewModel;
    private readonly GroupManager _manager;
    private readonly WindowCaptureService _capture;
    private readonly RenderHealthService _renderHealth;
    private readonly LoggingService _log;

    // Drag state
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

    // Guest clamp enforcement. The capture-time fill-clamp is not final: a
    // guest can move/resize itself within the host (Chrome hit-tests its
    // client-drawn tab strip as HTCAPTION, so it can be dragged even as a
    // frame-stripped WS_CHILD; apps also self-position programmatically).
    private IntPtr _guestInMoveSize;
    private System.Windows.Threading.DispatcherTimer? _driftTimer;
    private IntPtr _driftHwnd;
    private int _driftCorrections;
    private const int MaxConsecutiveDriftCorrections = 10;

    /// <summary>
    /// The underlying group model.
    /// </summary>
    public Group Group => _viewModel.Model;

    /// <summary>
    /// The native HWND into which captured windows are reparented.
    /// </summary>
    public IntPtr ContentHostHwnd => ContentHost.HostWindowHandle;

    public ContainerWindow(GroupViewModel viewModel, GroupManager manager, WindowCaptureService capture, RenderHealthService renderHealth, LoggingService log)
    {
        _viewModel = viewModel;
        _manager = manager;
        _capture = capture;
        _renderHealth = renderHealth;
        _log = log;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += ContainerWindow_Loaded;
        Closing += ContainerWindow_Closing;
        Closed += ContainerWindow_Closed;
        StateChanged += ContainerWindow_StateChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Clamp maximize to the monitor work area. A WindowStyle="None" +
        // WindowChrome window otherwise maximizes to the full monitor plus the
        // invisible resize border, covering the taskbar and spilling a few pixels
        // past every edge — which sizes the reparented guest LARGER than the
        // visible monitor at a negative origin. See WndProc/WM_GETMINMAXINFO.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        HwndSource? source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == NativeMethods.WM_GETMINMAXINFO)
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

        ContentHost.Service = _capture;
        if (ContentHost.HostWindowHandle != IntPtr.Zero)
            _manager.RegisterContainerHwnd(ContentHost.HostWindowHandle);

        TabsListBox.PreviewMouseLeftButtonDown += TabsListBox_PreviewMouseLeftButtonDown;
        TabsListBox.MouseMove += TabsListBox_MouseMove;
        TabsListBox.PreviewMouseLeftButtonUp += TabsListBox_PreviewMouseLeftButtonUp;

        PreviewKeyDown += ContainerWindow_PreviewKeyDown;

        // Watchdog for guests that re-position themselves programmatically
        // (DPI-suggested rects, Electron setBounds, ...) — those never enter a
        // move/size modal loop, so the MOVESIZEEND hook cannot see them.
        _driftTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _driftTimer.Tick += DriftTimer_Tick;
        _driftTimer.Start();
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
        if (_driftTimer != null)
        {
            _driftTimer.Stop();
            _driftTimer.Tick -= DriftTimer_Tick;
            _driftTimer = null;
        }

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        _manager.UnregisterContainerHwnd(hwnd);
        if (ContentHost.HostWindowHandle != IntPtr.Zero)
            _manager.UnregisterContainerHwnd(ContentHost.HostWindowHandle);
    }

    private void ContainerWindow_StateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE739";

        // Lightweight state snapshot after the transition settles. Retained (low
        // volume: once per maximize/restore) as a field-diagnosis aid for the
        // known GPU/Electron black-content-on-reparent limitation.
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
                NativeMethods.GetClientRect(ContentHostHwnd, out NativeMethods.RECT hostClient);
                hostDesc = $"rect={hostRect.left},{hostRect.top},{hostRect.Width}x{hostRect.Height} client={hostClient.Width}x{hostClient.Height}";
            }

            string guestDesc = "none";
            var active = _viewModel.ActiveTab?.Model;
            if (active != null)
            {
                guestDesc = WindowCaptureService.DescribeWindow(active.Hwnd);
                if (NativeMethods.IsWindow(active.Hwnd))
                    guestDesc += $" parentIsHost={NativeMethods.GetParent(active.Hwnd) == ContentHostHwnd}";
                guestDesc += $" renderHealth={active.RenderHealth}";
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
        _log.Log($"MAXCLICK from={WindowState}");
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
        if (ContentHostHwnd == IntPtr.Zero)
            return "Container content host is not ready yet.";

        if (_manager.IsOwnWindow(hwnd))
            return "Cannot capture a TabDock window (no nesting).";

        var cw = _capture.Capture(hwnd, ContentHostHwnd, out string? error);
        if (cw == null)
            return error ?? "Capture failed.";

        // Route through AddCapturedWindow so the render-health check (and its
        // auto-release of black/frozen tabs) runs on every real capture.
        AddCapturedWindow(cw);
        return null;
    }

    /// <summary>
    /// Adds an already-captured window to this container's view model and schedules
    /// a render-health check so black/frozen tabs are released automatically.
    /// </summary>
    public void AddCapturedWindow(CapturedWindow window)
    {
        _viewModel.AddCapturedWindow(window);
        _ = CheckRenderHealthAsync(window);
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
        // A window that minimized AND dropped WS_VISIBLE is minimizing to the
        // tray (X-button close on tray apps). Restoring it here fights the
        // guest in a loop and would defeat the guest-initiated-hide teardown.
        if (!NativeMethods.IsWindowVisible(window.Hwnd))
            return;

        NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_RESTORE);
        if (ContentHostHwnd != IntPtr.Zero)
            _capture.Layout(window, ContentHostHwnd, "restore");
    }

    /// <summary>
    /// Tracks a captured guest's interactive move/size modal loop. Chrome (and
    /// other custom-frame apps) hit-tests its client-drawn tab strip as
    /// HTCAPTION, and DefWindowProc's SC_MOVE loop moves WS_CHILD windows too,
    /// so a frame-stripped guest can still be dragged around inside the host.
    /// While the loop runs the drift watchdog stands down (re-clamping a window
    /// under the user's cursor fights the drag); when it ends the guest is
    /// snapped back to fill the content host.
    /// </summary>
    public void NoteGuestMoveSize(CapturedWindow window, bool started)
    {
        if (started)
        {
            _guestInMoveSize = window.Hwnd;
            return;
        }

        if (_guestInMoveSize == window.Hwnd)
            _guestInMoveSize = IntPtr.Zero;
        ReclampGuest(window, "movesize");
    }

    /// <summary>
    /// Re-applies the fill-clamp to a captured guest that moved or resized
    /// itself within the content host. The visibility/iconic guards are
    /// load-bearing, not hygiene: Layout uses SWP_SHOWWINDOW and force-restores
    /// iconic windows, so an unguarded re-clamp against a guest that just hid
    /// itself (tray-style close) would re-show it, fight the guest in a loop,
    /// and defeat the guest-initiated-hide teardown.
    /// </summary>
    private void ReclampGuest(CapturedWindow window, string reason)
    {
        if (_viewModel.ActiveTab?.Model != window)
            return;
        if (ContentHostHwnd == IntPtr.Zero || !NativeMethods.IsWindow(window.Hwnd))
            return;
        if (!NativeMethods.IsWindowVisible(window.Hwnd) || NativeMethods.IsIconic(window.Hwnd))
            return;
        if (NativeMethods.GetParent(window.Hwnd) != ContentHostHwnd)
            return;

        _capture.Layout(window, ContentHostHwnd, reason);
    }

    private void DriftTimer_Tick(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            return;

        var window = _viewModel.ActiveTab?.Model;
        if (window == null || ContentHostHwnd == IntPtr.Zero)
            return;
        if (window.Hwnd == _guestInMoveSize)
            return; // Interactive drag in progress; MOVESIZEEND re-clamps.
        if (!NativeMethods.IsWindow(window.Hwnd) ||
            !NativeMethods.IsWindowVisible(window.Hwnd) ||
            NativeMethods.IsIconic(window.Hwnd))
            return;
        if (NativeMethods.GetParent(window.Hwnd) != ContentHostHwnd)
            return;

        // The host is a borderless WS_CHILD, so its window rect IS its client
        // area in screen coordinates; exact equality means the guest fills it.
        NativeMethods.GetWindowRect(window.Hwnd, out NativeMethods.RECT guest);
        NativeMethods.GetWindowRect(ContentHostHwnd, out NativeMethods.RECT host);
        if (guest.left == host.left && guest.top == host.top &&
            guest.right == host.right && guest.bottom == host.bottom)
        {
            _driftHwnd = IntPtr.Zero;
            _driftCorrections = 0;
            return;
        }

        // A guest that keeps re-asserting its own geometry would otherwise turn
        // this into a 1 Hz tug-of-war; degrade to a single diagnostic line.
        if (window.Hwnd == _driftHwnd)
        {
            _driftCorrections++;
            if (_driftCorrections > MaxConsecutiveDriftCorrections)
            {
                if (_driftCorrections == MaxConsecutiveDriftCorrections + 1)
                    _log.Log($"LAYOUT[drift] giving up on 0x{window.Hwnd.ToInt64():X}: still diverged after {MaxConsecutiveDriftCorrections} consecutive corrections.");
                return;
            }
        }
        else
        {
            _driftHwnd = window.Hwnd;
            _driftCorrections = 1;
        }

        _log.Log($"LAYOUT[drift] guest={WindowCaptureService.DescribeWindow(window.Hwnd)} host={host.left},{host.top},{host.Width}x{host.Height}");
        ReclampGuest(window, "drift");
    }

    /// <summary>
    /// Refreshes tab titles after an external window name change.
    /// </summary>
    public void RefreshTabTitles()
    {
        foreach (var tab in _viewModel.Tabs)
            tab.RefreshTitle();
    }

    private async System.Threading.Tasks.Task CheckRenderHealthAsync(CapturedWindow window)
    {
        try
        {
            bool healthy = await _renderHealth.CheckHealthAsync(window, ContentHostHwnd).ConfigureAwait(false);
            window.RenderHealth = healthy;
            _log.Log($"Render health for 0x{window.Hwnd.ToInt64():X} ('{window.DisplayLabel}'): {(healthy ? "healthy" : "unhealthy; releasing back to standalone")}");
            if (!healthy)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_viewModel.Tabs.Any(t => t.Model == window))
                    {
                        ReleaseCapturedWindow(window);
                        MessageBox.Show(
                            $"'{window.DisplayLabel}' could not be embedded reliably and has been released back to a standalone window.",
                            "Window could not be tabbed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            // Render-health failure must not crash the container.
            System.Diagnostics.Debug.WriteLine($"Render health check failed: {ex}");
        }
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
                // cache would be misaligned, so fall back to live geometry.
                _dragMidpoints = null;
                _dragMidpointsCount = 0;
                return;
            }
        }
        _dragMidpoints = midpoints;
        _dragMidpointsCount = midpoints.Count;
    }

    private int? GetDropIndex(Point mousePos)
    {
        // A count change mid-drag (a tab destroyed or hidden by a WinEvent
        // handler between mouse moves) invalidates the cache. Re-snapshot:
        // reorders never change the count, so this cannot reintroduce the
        // oscillation feedback loop.
        if (_dragMidpoints != null && _viewModel.Tabs.Count != _dragMidpointsCount)
            SnapshotDragMidpoints();

        if (_dragMidpoints != null)
        {
            for (int i = 0; i < _dragMidpoints.Count; i++)
            {
                if (mousePos.X < _dragMidpoints[i])
                    return i;
            }
            return _dragMidpoints.Count > 0 ? _dragMidpoints.Count : null;
        }

        for (int i = 0; i < _viewModel.Tabs.Count; i++)
        {
            if (TabsListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                Point itemPos = item.TranslatePoint(new Point(0, 0), TabsListBox);
                if (mousePos.X < itemPos.X + item.ActualWidth / 2)
                    return i;
            }
        }
        return _viewModel.Tabs.Count > 0 ? _viewModel.Tabs.Count : null;
    }

    #endregion
}
