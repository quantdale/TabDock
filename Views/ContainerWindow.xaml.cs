using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
/// docs/internal/deep-audit-2026-07-17.md section 6). Split presentation policy
/// lives in <see cref="SplitPresentationController"/> / <see cref="Models.SplitPresentationPolicy"/> /
/// <see cref="Services.SplitInteractionPolicy"/>; coalesced relayout and deferred
/// batch bookkeeping lives in <see cref="PresentationLayoutCoordinator"/> — this
/// window owns the split fields and timers but delegates the policy.
/// </summary>
public partial class ContainerWindow : Window
{
    public sealed class GroupSelectionEventArgs : EventArgs
    {
        public GroupSelectionEventArgs(Group group) => Group = group;
        public Group Group { get; }
    }

    private readonly GroupViewModel _viewModel;
    private readonly GroupManager _manager;
    private readonly WindowShepherdService _shepherd;
    private readonly LoggingService _log;
    private readonly IconService _icons;
    private CapturePickerViewModel? _capturePicker;
    internal SplitPresentationController SplitController => _splitController;

    // The shepherded active-tab guest. Never bound through a WPF dependency
    // property (a shepherd guest is a sibling top-level window, not content
    // hosted inside anything) — this field and the methods around it are this
    // container's entire sync loop for the active tab.
    private CapturedWindow? _shepherdActiveWindow;

    // Vertical split-screen state is owned by SplitPresentationController
    // (_splitController): left/right/presented/foreground/generation are its
    // get-only properties. A dormant relationship may coexist with a non-member
    // full-width _shepherdActiveWindow. Identity is by CapturedWindow reference
    // (never positional index), so LEFT/RIGHT survive tab reordering and
    // ordinary non-member selection. The container only owns the WPF-wiring
    // concerns below (_constraintDirty, _refusedPaneByHwnd)
    // and routes every member/present/foreground/generation transition through
    // the controller (DefinePair/SuspendForGuest/ResumeMember/ExplicitExit/
    // HandleMemberRemoved/FocusMember) so there is a single runtime authority
    // (no parallel state machine). The isCurrent lambda and the production
    // shim only touch _shepherd at invocation time, after construction.
    private readonly SplitPresentationController _splitController;

    // Size-constraint state (post-audit containment finding). The container
    // refuses to shrink below what the currently visible guest(s) can physically
    // fit, so a guest is never asked to occupy a pane narrower than its own
    // native minimum track width. The per-guest minimum is measured once via
    // WindowShepherdService.GetEffectiveMinTrackSize and cached; _constraintDirty
    // is set whenever the visible set or window state changes so the next
    // relayout recomputes the container's minimum. See RefreshSizeConstraint.
    private int _constraintMinLeftW;
    private int _constraintMinRightW;
    private int _constraintMinLeftH;
    private int _constraintMinRightH;
    private bool _constraintDirty = true;
    // Bounded non-compliance guard: a guest that refuses its assigned pane (its
    // native minimum grew larger than the pane — e.g. a browser sidebar opened)
    // must never be re-fought every frame (resize war). Records the pane rect
    // each guest last refused; the layout skips re-positioning that exact rect.
    private readonly Dictionary<long, NativeMethods.RECT> _refusedPaneByHwnd = new();

    // Debounced refresh of the guest minima while a guest is visible, so a
    // dynamic native minimum (browser UI state, sidebar, toolbar) is respected
    // without probing on every frame.
    private System.Windows.Threading.DispatcherTimer? _constraintRefreshTimer;

    // Coalesced timers for WM_ACTIVATE's guest re-assert, StateChanged's
    // settled-snapshot diagnostic, and the self-minimize restore check. Each
    // holds at most one pending instance — stopped and replaced, never left to
    // accumulate (AUDIT25-05) — and all three are stopped in
    // ContainerWindow_Closed so nothing fires against a released guest.
    private System.Windows.Threading.DispatcherTimer? _activateReassertTimer;
    private System.Windows.Threading.DispatcherTimer? _stateSettledTimer;
    private System.Windows.Threading.DispatcherTimer? _restoreMinimizedTimer;

    // A captured guest's native title-bar move/size loop is authoritative until
    // EVENT_SYSTEM_MOVESIZEEND. Do not let an intermediate relayout classify
    // the still-moving HWND as a native-minimum refusal; that transient record
    // would suppress the final re-glue after the gesture ends.
    private bool _guestMoveSizeActive;
    private long _guestMoveSizeGeneration;

    // The tab context menu most recently opened by TabsListBox_PreviewMouseRightButtonDown.
    // Tracked so the WM_ACTIVATE reassert can tell "the user is interacting with the
    // container's own chrome" (a right-clicked menu that must stay open) apart from a
    // genuine guest-foregrounding intent. Cleared on the menu's Closed event and on
    // container teardown; a destroyed menu reports IsOpen == false so a stale reference
    // is harmless.
    private ContextMenu? _openTabContextMenu;
    private readonly HashSet<ContextMenu> _trackedTabContextMenus = new();
    private bool _chromePopupActive;

    // Coalesces the several per-frame reposition triggers (native
    // WM_WINDOWPOSCHANGED, LocationChanged, SizeChanged, LayoutUpdated) into a
    // single guest re-glue per WPF frame. Without this, a container drag fires
    // multiple RelayoutGuests in one frame, each issuing native SetWindowPos calls
    // and producing the redundant reposition/redraw artifacts that made movement
    // look glitchy.
    // Production relayout now routes through the shared PresentationLayoutCoordinator
    // — the same coalescing scheduler the deterministic layout/budget tests exercise —
    // so there is a single relayout authority (no scheduler logic duplicated only in tests).
    private readonly PresentationLayoutCoordinator _layoutCoordinator = new();
    private bool _hasObservedContentRect;
    private NativeMethods.RECT _lastObservedContentRect;

    // True while Windows is running a native modal move/resize loop on this
    // container (WM_ENTERSIZEMOVE..WM_EXITSIZEMOVE). The 120ms WM_ACTIVATE
    // reassert must not fire SetForegroundWindow mid-gesture.
    private bool _inNativeMoveLoop;

    // Drag state (tab-strip reorder / explicit tab-strip pop-out)
    private TabViewModel? _draggedTab;
    private Point _dragStart;
    private ListBoxItem? _draggedItem;
    private bool _isDragging;
    /// <summary>Minimum pointer movement in pixels before drag gesture is recognized (avoids flaky tab activation on clicks).</summary>
    private const double DragThreshold = 4;

    // Tab-strip slot midpoints snapshotted at drag start. Drop targeting must
    // not read live container geometry mid-drag: a reorder mutates the layout
    // under a stationary pointer, and the next MouseMove would compute the
    // opposite index and reorder straight back (finding H2's oscillation).
    private System.Collections.Generic.List<double>? _dragMidpoints;
    private int _dragMidpointsCount;
    private bool _dragMidpointsValid;

    // Container and content-marker HWNDs cached at Loaded time (both are known
    // non-zero there). By Closed time WindowInteropHelper.Handle and
    // ContentHost.HostWindowHandle already read IntPtr.Zero, so unregistering
    // from live reads leaked stale values into GroupManager's no-nesting set —
    // and Windows aggressively recycles HWND values for unrelated windows.
    private IntPtr _containerHwnd;
    private IntPtr _contentHostHwnd;

    // Re-entrancy guard for the close-confirm modal. The MessageBox pumps a
    // nested dispatcher loop, so a guest destroying itself mid-prompt can fire
    // EmptiedByPopOut and re-enter Close() on a window already inside Closing.
    private bool _closePromptOpen;
    private bool _closePending;
    private DispatcherTimer? _closePromptRaiseTimer;

    /// <summary>
    /// Set by App before any exit/crash path calls Application.Shutdown so every
    /// open container's Closing handler skips the Yes/No/Cancel prompt instead of
    /// showing one modal per container with nobody left to answer it (finding M6).
    /// GroupManager.EmergencyReleaseAll (called by the same exit/crash paths) is
    /// what actually returns captured windows to standalone; this flag only stops
    /// Closing from blocking on user input during teardown.
    /// </summary>
    public static bool IsAppShuttingDown { get; set; }

    public event EventHandler<GroupSelectionEventArgs>? GroupSelectedRequested;
    public event EventHandler? NewGroupRequested;

    /// <summary>
    /// The underlying group model.
    /// </summary>
    public Group Group => _viewModel.Model;

    /// <summary>True while the inline capture surface is open.</summary>
    public bool IsCapturePanelOpen => _capturePicker != null;

    /// <summary>
    /// The native marker HWND that defines exactly where the content area sits
    /// on screen. See Infrastructure/NativeHwndHost.cs.
    /// </summary>
    public IntPtr ContentHostHwnd => ContentHost.HostWindowHandle;

    /// <summary>
    /// True while the close-confirm MessageBox is open. Used by App to defer
    /// capture-picker requests until the prompt returns.
    /// </summary>
    public bool IsClosePromptOpen => _closePromptOpen;

    /// <summary>
    /// Returns the current desired/logical presentation without invoking layout,
    /// activation, Shepherd positioning, or any other native mutation. Native
    /// observations are collected separately so a broken presentation remains
    /// visible in the diagnostic comparison.
    /// </summary>
    public LogicalPresentationSnapshot CreateDiagnosticSnapshot()
    {
        var snapshot = new LogicalPresentationSnapshot
        {
            GroupId = Group.Id,
            ContainerHwnd = _containerHwnd.ToInt64(),
            ContainerVisible = _containerHwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(_containerHwnd),
            WindowState = WindowState.ToString(),
            Minimized = WindowState == WindowState.Minimized,
            Maximized = WindowState == WindowState.Maximized,
            ActiveMemberKey = _shepherdActiveWindow == null ? null : DiagnosticMemberKey(_shepherdActiveWindow),
            ActiveGuestHwnd = _shepherdActiveWindow?.Hwnd.ToInt64() ?? 0,
            SplitActive = _splitController.IsRelationshipDefined,
            SplitPresented = _splitController.IsPresented,
            SplitLeftMemberKey = _splitController.Left == null ? null : DiagnosticMemberKey(_splitController.Left),
            SplitLeftHwnd = _splitController.Left?.Hwnd.ToInt64() ?? 0,
            SplitRightMemberKey = _splitController.Right == null ? null : DiagnosticMemberKey(_splitController.Right),
            SplitRightHwnd = _splitController.Right?.Hwnd.ToInt64() ?? 0,
            SplitForegroundMemberKey = _splitController.Foreground == null ? null : DiagnosticMemberKey(_splitController.Foreground),
            SplitForegroundHwnd = _splitController.Foreground?.Hwnd.ToInt64() ?? 0,
            ChromeInteractionActive = IsContainerChromeInteractionActive(),
            Monitor = _containerHwnd == IntPtr.Zero ? "unavailable" : EnvironmentFingerprint.DescribeWindowMonitor(_containerHwnd),
        };

        NativeMethods.RECT content = GetContentAreaScreenRect();
        NativeMethods.RECT left = default;
        NativeMethods.RECT right = default;
        if (content.Width > 0 && content.Height > 0)
        {
            (left, right) = SplitRect(content);
            if (IsSplitPresented)
            {
                snapshot.ExpectedPaneRects.Add(DiagnosticRect.From(left));
                snapshot.ExpectedPaneRects.Add(DiagnosticRect.From(right));
            }
        }

        foreach (CapturedWindow member in Group.Members)
        {
            var memberSnapshot = new DiagnosticMemberSnapshot
            {
                MemberKey = DiagnosticMemberKey(member),
                Hwnd = member.Hwnd.ToInt64(),
                ProcessId = member.ProcessId,
                ExecutableName = string.IsNullOrWhiteSpace(member.ExePath) ? "unavailable" : System.IO.Path.GetFileName(member.ExePath),
                WindowClass = string.IsNullOrWhiteSpace(member.OriginalClassName) ? "unavailable" : member.OriginalClassName,
                Visible = NativeMethods.IsWindow(member.Hwnd) && NativeMethods.IsWindowVisible(member.Hwnd),
                Iconic = NativeMethods.IsWindow(member.Hwnd) && NativeMethods.IsIconic(member.Hwnd),
                Zoomed = NativeMethods.IsWindow(member.Hwnd) && NativeMethods.IsZoomed(member.Hwnd),
            };
            if (IsSplitPresented && ReferenceEquals(member, _splitController.Left))
                memberSnapshot.ExpectedPaneRect = DiagnosticRect.From(left);
            else if (IsSplitPresented && ReferenceEquals(member, _splitController.Right))
                memberSnapshot.ExpectedPaneRect = DiagnosticRect.From(right);
            else if (!IsSplitPresented && ReferenceEquals(member, _shepherdActiveWindow) && content.Width > 0 && content.Height > 0)
                memberSnapshot.ExpectedPaneRect = DiagnosticRect.From(content);
            snapshot.Members.Add(memberSnapshot);
        }
        return snapshot;
    }

    private static string DiagnosticMemberKey(CapturedWindow member)
        => $"pid:{member.ProcessId}/hwnd:0x{member.Hwnd.ToInt64():X}";

    public ContainerWindow(GroupViewModel viewModel, GroupManager manager, WindowShepherdService shepherd, LoggingService log, IconService icons)
    {
        _viewModel = viewModel;
        _manager = manager;
        _shepherd = shepherd;
        _log = log;
        _icons = icons;
        // Field initializers cannot reference 'this', so the runtime-authority
        // controller (wired to the production WindowShepherdService via the
        // ShepherdPresentationOps shim) is constructed here, after _shepherd is
        // assigned. The shim and the isCurrent lambda only touch _shepherd at
        // invocation time, never during construction.
        _splitController = new SplitPresentationController(
            ops: new ShepherdPresentationOps(this),
            isCurrent: w => _shepherd.IsCurrentCapturedWindow(w));
        DataContext = viewModel;
        InitializeComponent();
        Loaded += ContainerWindow_Loaded;
        Closing += ContainerWindow_Closing;
        Closed += ContainerWindow_Closed;
        StateChanged += ContainerWindow_StateChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        // Split state must react to a split member leaving the group (pop-out,
        // drag-out, self-close, self-hide, group close): end the split and
        // promote the survivor to the single visible guest.
        _viewModel.Tabs.CollectionChanged += Tabs_CollectionChanged;
        // Popping out the last tab (drag-out or context menu) leaves the group
        // empty; close the now-pointless container instead of leaving it open
        // (finding L11). IsAppShuttingDown is irrelevant here — this always runs
        // on the interactive pop-out path, never during app teardown.
        _viewModel.EmptiedByPopOut += ViewModel_EmptiedByPopOut;
        _viewModel.DeleteGroupRequested += ViewModel_DeleteGroupRequested;
        ColorContextMenu.Closed += ColorContextMenu_Closed;
    }

    private void ViewModel_EmptiedByPopOut(object? sender, EventArgs e)
    {
        if (_closePromptOpen)
        {
            // The close-confirm prompt is already asking the user what to do with
            // the group. Defer the empty-container close until the prompt returns.
            _closePending = true;
            return;
        }
        Close();
    }

    private void ViewModel_DeleteGroupRequested(object? sender, EventArgs e)
    {
        if (_closePromptOpen)
            return;

        // Guard the modal the same way the close-confirm prompt does: while it
        // pumps its nested dispatcher loop, WinEvents keep firing and App's
        // picker-deferral guard (IsClosePromptOpen) must see this prompt so a
        // hotkey-triggered capture picker cannot stack on top of it.
        MessageBoxResult result = MessageBoxResult.Cancel;
        _closePromptOpen = true;
        try
        {
            result = MessageBox.Show(
                this,
                "Release all captured windows back to standalone and delete this group?\n\nThe windows will keep running.",
                "Delete group",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
        }
        finally
        {
            _closePromptOpen = false;
        }
        if (result != MessageBoxResult.OK)
            return;

        // Use the same transaction-first release contract as the ordinary
        // close path. Removing the group/index before release would detach an
        // uncertain member from lifecycle ownership and could make a pending
        // tab loop forever in ReleaseTab. CloseGroup retains the group and its
        // pending members when any identity/native recovery is uncertain; the
        // user can retry after the guest becomes verifiable.
        if (!_viewModel.CloseGroup())
        {
            _log.Log($"Delete group {Group.Id} retained because one or more guest releases are pending recovery.");
            MessageBox.Show(
                this,
                "One or more windows could not be safely released yet. The group remains open and recovery evidence was preserved; retry after the windows become verifiable.",
                "Release pending",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // CloseGroup removes the fully released group from GroupManager and
        // clears the view model. This explicit Close also covers an empty-group
        // delete, where no tab-removal event can close the container.
        Close();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupViewModel.ActiveTab))
        {
            // Defensive: every add/release/pop-out ends with a SetActiveTab
            // call, so this fires on every tab-set mutation. If a drag was
            // somehow left in progress across one of those mutations (e.g. a
            // tab removed out from under an in-flight drag), a stale
            // Mouse.Capture(TabsListBox) would swallow every future click
            // anywhere in the strip and its header — clicks stop reaching
            // ListBoxItems/buttons at all, exactly like the tunneling-capture
            // bug documented in TabsListBox_PreviewMouseLeftButtonDown, just
            // via a different trigger. Clearing it here costs nothing when
            // there was nothing to clear.
            //
            // Scope it to the documented case: an in-flight drag whose tab
            // actually LEFT the tab set. An unconditional EndDrag() here also
            // fires on the selection change caused by the drag's own mousedown
            // on an inactive tab (press -> select -> ActiveTab changed ->
            // EndDrag), disarming the drag before the first MouseMove and
            // swallowing every drag that starts on a non-active tab.
            if (_isDragging && (_draggedTab == null || !_viewModel.Tabs.Contains(_draggedTab)))
                EndDrag();
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
            DiagnosticRuntime.Record("container.wm-activate", hwnd, _shepherdActiveWindow?.Hwnd ?? IntPtr.Zero,
                group: Group.Id.ToString("N"), action: "observe", result: activateKind.ToString(),
                data: new Dictionary<string, string>
                {
                    ["activateKind"] = activateKind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["foreground"] = DiagnosticEnvironmentService.FormatHwnd(NativeMethods.GetForegroundWindow()),
                });
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
                _activateReassertTimer?.Stop();
                var activateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                activateTimer.Tick += (_, _) =>
                {
                    if (!ReferenceEquals(_activateReassertTimer, activateTimer))
                    {
                        activateTimer.Stop();
                        return;
                    }

                    activateTimer.Stop();
                    _activateReassertTimer = null;
                    if (_shepherdActiveWindow == activeWindow
                        && !NativeMethods.IsIconic(activeWindow.Hwnd)
                        && NativeMethods.IsWindowVisible(activeWindow.Hwnd)
                        && !_inNativeMoveLoop && !_isDragging && !IsContainerChromeInteractionActive())
                    {
                        if (IsSplitPresented && IsSplitMember(activeWindow))
                        {
                            // Re-assert both panes and foreground the active
                            // member without disturbing the pair's z-order.
                            // Guarded by IsSplitMember so a 120ms-stale timer
                            // (the user clicked a different tab within that
                            // window) cannot overwrite a newer split foreground member
                            // or re-glue a guest that has already left the pair.
                            var activeTab = _viewModel.Tabs.FirstOrDefault(t => t.Model == activeWindow);
                            if (activeTab != null)
                                FocusSplitMember(activeTab);
                            _shepherd.SetForeground(activeWindow);
                        }
                        else
                        {
                            if (TryGetContentAreaScreenRect(out NativeMethods.RECT contentRect))
                                _shepherd.BringToFront(activeWindow, hwnd, contentRect);
                            else
                                _log.Log("LAYOUT[skip] active-guest reassert: content marker bounds were unavailable.");
                        }
                    }
                };
                _activateReassertTimer = activateTimer;
                activateTimer.Start();
            }
        }
        else if ((uint)msg == NativeMethods.WM_ENTERSIZEMOVE)
        {
            // A native modal move/resize loop is running (caption drag, edge
            // resize). The 120ms WM_ACTIVATE reassert must not fire
            // SetForegroundWindow mid-gesture (it would steal foreground from
            // the gesture and add visual churn).
            _inNativeMoveLoop = true;
        }
        else if ((uint)msg == NativeMethods.WM_EXITSIZEMOVE)
        {
            DiagnosticRuntime.Record("container.wm-exitsizemove", hwnd, _shepherdActiveWindow?.Hwnd ?? IntPtr.Zero,
                group: Group.Id.ToString("N"), action: "reconcile-request", result: "queued");
            _inNativeMoveLoop = false;
            // The container's resize just ended: re-probe the visible guest(s)'
            // native minima (a size change can accompany a UI-state shift) and
            // schedule the coalesced post-layout reconciliation.
            _constraintDirty = true;
            _refusedPaneByHwnd.Clear();
            // The native move/size loop has fully unwound and the container's
            // final position is authoritative. Windows keeps a dragged window
            // at the top of the z-order for the whole modal loop, and its
            // final z-order finalization can land AFTER the last per-frame
            // re-glue — leaving the container above its guest while the
            // guest's rect still exactly matches the content area (the
            // redundant-glue guard in LayoutShepherdActiveWindow would then
            // skip every later repair, blanking the content area until a tab
            // switch re-glues it). RequestRelayout coalesces; ensureFinalPass
            // latches so a Render already pending from the final
            // WM_WINDOWPOSCHANGED is not lost and produces one authoritative
            // post-loop pass (Q9). WM_ACTIVATE reassert remains gated by
            // _inNativeMoveLoop above (Q4).
            RequestRelayout(ensureFinalPass: true);
        }
        else if ((uint)msg == NativeMethods.WM_WINDOWPOSCHANGED)
        {
            // The container's native move/resize is processed in the same message
            // loop as this handler, so re-glue the shepherded guest(s) here — in
            // the same compositor frame as the container — instead of waiting for
            // the later WPF LocationChanged/LayoutUpdated notifications. This is
            // the most immediate native movement signal and removes the
            // one-frame guest lag that made dragging feel glitchy.
            RequestRelayout();
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
                    ApplyMonitorMaximizeBounds(mi, ref mmi);
                    // Size-constraint policy: the container refuses to shrink
                    // below what the currently visible guest(s) can physically
                    // fit (see RefreshSizeConstraint). This clamps the native
                    // drag-resize so a guest is never asked to occupy a pane
                    // narrower than its own native minimum — the containment
                    // defect. The min track is expressed as the OUTER window
                    // size: content min + the chrome delta (outer minus content).
                    //
                    // The incoming ptMinTrackSize is the floor already composed
                    // by earlier handling (the XAML MinWidth/MinHeight and the
                    // system default). The effective minimum is the MAX of that
                    // floor and the guest-derived constraint — never a blind
                    // overwrite, which could drop the container below its XAML
                    // floor when no guest minimum exists or the guest minimum
                    // is smaller.
                    if (ComputeContainerMinTrack(out int minTrackW, out int minTrackH)
                        && minTrackW > 0 && minTrackH > 0)
                    {
                        mmi.ptMinTrackSize.x = Math.Max(mmi.ptMinTrackSize.x, minTrackW);
                        mmi.ptMinTrackSize.y = Math.Max(mmi.ptMinTrackSize.y, minTrackH);
                    }
                    System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                }
            }
        }
        else if ((uint)msg == NativeMethods.WM_DPICHANGED)
        {
            // The container moved to a monitor with a different DPI (or the
            // monitor's DPI changed). Cached per-monitor DPI answers and the
            // guest-minimum cache are keyed to the old scale; recompute.
            MonitorDpiService.InvalidateDpiCache();
            _constraintDirty = true;
            _refusedPaneByHwnd.Clear();
        }
        else if ((uint)msg == NativeMethods.WM_DISPLAYCHANGE)
        {
            // Display topology changed: monitor handles can be recycled, so
            // every cached DPI answer and geometry refusal is stale.
            MonitorDpiService.InvalidateDpiCache();
            _constraintDirty = true;
            _refusedPaneByHwnd.Clear();
        }
        return IntPtr.Zero;
    }

    internal static void ApplyMonitorMaximizeBounds(
        NativeMethods.MONITORINFO monitorInfo,
        ref NativeMethods.MINMAXINFO minMaxInfo)
    {
        minMaxInfo.ptMaxPosition.x = monitorInfo.rcWork.left - monitorInfo.rcMonitor.left;
        minMaxInfo.ptMaxPosition.y = monitorInfo.rcWork.top - monitorInfo.rcMonitor.top;
        minMaxInfo.ptMaxSize.x = monitorInfo.rcWork.Width;
        minMaxInfo.ptMaxSize.y = monitorInfo.rcWork.Height;
    }

    private void ContainerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        _containerHwnd = hwnd;
        _manager.RegisterContainerHwnd(hwnd);

        if (ContentHost.HostWindowHandle != IntPtr.Zero)
        {
            _contentHostHwnd = ContentHost.HostWindowHandle;
            _manager.RegisterContainerHwnd(_contentHostHwnd);
        }

        // Per-container environment fingerprint (goal §16): active monitor,
        // DPI, container rect, host rect, window state — once at open, so a
        // pasted customer log is self-describing without per-frame noise.
        try
        {
            NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT winRect);
            string hostDesc = "none";
            if (_contentHostHwnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowRect(_contentHostHwnd, out NativeMethods.RECT hostRect);
                hostDesc = $"{hostRect.left},{hostRect.top},{hostRect.Width}x{hostRect.Height}";
            }
            string guestDesc = "none";
            var activeGuest = _viewModel.ActiveTab?.Model;
            if (activeGuest != null)
                guestDesc = $"{activeGuest.ExePath} {NativeMethods.DescribeWindow(activeGuest.Hwnd)}";
            _log.Log($"ENV[container] hwnd=0x{hwnd.ToInt64():X} state={WindowState} container={winRect.left},{winRect.top},{winRect.Width}x{winRect.Height} host={hostDesc} guest=({guestDesc}) " +
                     $"{EnvironmentFingerprint.DescribeWindowMonitor(hwnd)}");
        }
        catch (Exception ex)
        {
            _log.LogException("ENV[container]", ex);
        }

        TabsListBox.PreviewMouseLeftButtonDown += TabsListBox_PreviewMouseLeftButtonDown;
        TabsListBox.PreviewMouseDown += TabsListBox_PreviewMouseDown;
        TabsListBox.MouseMove += TabsListBox_MouseMove;
        TabsListBox.PreviewMouseLeftButtonUp += TabsListBox_PreviewMouseLeftButtonUp;

        PreviewKeyDown += ContainerWindow_PreviewKeyDown;

        // Keep the shepherded active guest glued to the content area as the
        // container itself moves or resizes (a shepherded guest is a sibling
        // top-level window, so this container must reposition it explicitly —
        // there is no WS_CHILD/HwndHost relationship to get this for free).
        // Split-aware: in split mode this re-glues BOTH panes, never the active
        // guest to full width (LayoutShepherdActiveWindow alone would overwrite
        // a split member's half-pane position).
        LocationChanged += (_, _) => RequestRelayout();
        SizeChanged += (_, _) => RequestRelayout();
        // The content marker's native HWND is resized inside NativeHwndHost's
        // ArrangeOverride during WPF's layout pass, which runs asynchronously
        // AFTER this window's SizeChanged/StateChanged (the native resize
        // invalidation is queued for the dispatcher, so this Window's layout is
        // still flagged valid — and UpdateLayout() is a no-op — when those fire).
        // That leaves the guest glued to the stale pre-transition size on
        // maximize/restore: neither the marker's own WPF SizeChanged nor
        // UpdateLayout() help (HwndHost does not re-raise SizeChanged for a
        // layout-driven resize, and there is no pending WPF layout yet when the
        // synchronous handlers run). Re-glue instead at DispatcherPriority after
        // layout, by which time the marker's HWND really has its new size.
        LayoutUpdated += ContainerWindow_LayoutUpdated;

        // Debounced periodic re-probe of the visible guest(s)' native minima, so
        // a dynamic minimum (browser sidebar, toolbar, UI-state change) is picked
        // up without probing on every frame or a resize war. Bounded: one probe
        // batch every few seconds, and only re-measures when a guest is visible.
        _constraintRefreshTimer?.Stop();
        var refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        refreshTimer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_constraintRefreshTimer, refreshTimer))
            {
                refreshTimer.Stop();
                return;
            }
            bool hasVisible = IsSplitPresented
                || (_shepherdActiveWindow != null && NativeMethods.IsWindowVisible(_shepherdActiveWindow.Hwnd));
            if (hasVisible)
            {
                _constraintDirty = true;
                // A guest may have gained or lost the ability to fit its pane
                // (e.g. a browser sidebar toggled). Clear refusals so the next
                // relayout re-evaluates every visible guest against its current
                // native minimum — a bounded retry (once per interval), never a
                // per-frame resize war.
                _refusedPaneByHwnd.Clear();
            }
        };
        _constraintRefreshTimer = refreshTimer;
        refreshTimer.Start();
    }

    /// <summary>
    /// Re-glues the visible guest(s) to the content area. In split mode both
    /// panes are laid out; otherwise the single active guest is laid out.
    /// </summary>
    private void RelayoutGuests()
    {
        RuntimeTelemetry.Instance.RecordRelayoutGuests();
        if (IsSplitPresented)
            LayoutSplitPanes();
        else
            LayoutShepherdActiveWindow();
    }

    /// <summary>
    /// Re-requests relayout only when the container's physical content rect
    /// actually changed. Unchanged <see cref="LayoutUpdated"/> notifications
    /// (e.g. tab-strip reorders during a drag) no longer queue a native
    /// re-glue, eliminating a per-layout relayout amplification source.
    /// </summary>
    private void ContainerWindow_LayoutUpdated(object? sender, EventArgs e)
    {
        if (_containerHwnd == IntPtr.Zero || ContentHost.HostWindowHandle == IntPtr.Zero)
            return;

        NativeMethods.RECT rect = GetContentAreaScreenRect();
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        if (_hasObservedContentRect
            && Math.Abs(rect.left - _lastObservedContentRect.left) <= 1
            && Math.Abs(rect.top - _lastObservedContentRect.top) <= 1
            && Math.Abs(rect.right - _lastObservedContentRect.right) <= 1
            && Math.Abs(rect.bottom - _lastObservedContentRect.bottom) <= 1)
        {
            return;
        }

        _lastObservedContentRect = rect;
        _hasObservedContentRect = true;
        RequestRelayout();
    }

    /// <summary>
    /// Schedules a single <see cref="RelayoutGuests"/> per WPF frame through the
    /// shared <see cref="PresentationLayoutCoordinator"/> — the same coalescing
    /// scheduler the deterministic layout/budget tests exercise. All native
    /// movement signals (WM_WINDOWPOSCHANGED, LocationChanged, SizeChanged) and the
    /// post-layout dirty-check event call this; the coordinator ensures only the
    /// first in a frame schedules the work, and the Render-priority callback runs
    /// after WPF has arranged the content marker to its new geometry — so the
    /// guest is re-glued to the final rect exactly once instead of several times
    /// per frame.
    /// </summary>
    private void RequestRelayout(bool ensureFinalPass = false)
    {
        if (_containerHwnd == IntPtr.Zero)
            return;
        RuntimeTelemetry.Instance.RecordRequestRelayout();
        _layoutCoordinator.RequestRelayout(
            scheduleRender: cb => Dispatcher.BeginInvoke(DispatcherPriority.Render, cb),
            execute: () =>
            {
                // Recompute the container's minimum size from the currently visible
                // guest(s) before laying them out, so the min-track clamp (WM_GETMINMAXINFO)
                // and the pane rects agree on the same constraint.
                RefreshSizeConstraint();
                RelayoutGuests();
            },
            ensureFinalPass);
    }

    private void ContainerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsCapturePanelOpen && !_viewModel.IsRenaming)
        {
            CloseCapturePanel();
            e.Handled = true;
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            return;
        if (e.Key != Key.Tab)
            return;

        // The navigation DECISION is owned by TabNavigationPolicy: it returns
        // the authoritative target tab (never a presentation-space index), so
        // the split composite's Tabs/DisplayTabs divergence cannot misdirect
        // this shortcut. Selection sync happens via the ActiveTab/IsActive/
        // IsSelected binding chain (ContainerWindow.xaml), same as every other
        // active-tab switch.
        var decision = TabNavigationPolicy.ResolveCtrlTab(
            _viewModel.Tabs.Select(t => t.Model).ToArray(),
            _viewModel.ActiveTab?.Model,
            backward: (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift,
            splitPresented: IsSplitPresented,
            splitLeft: _splitController.Left,
            splitRight: _splitController.Right,
            splitForeground: _splitController.Foreground);
        if (decision.Kind == TabNavigationPolicy.NavigationKind.NotNavigable)
            return;

        if (decision.Kind == TabNavigationPolicy.NavigationKind.FocusSplitMember)
        {
            // The split pair is the selected tab-strip unit: Ctrl+Tab cycles
            // between the two members only (FocusSplitMember resumes a dormant
            // pair through its own path when needed).
            if (decision.Target != null)
            {
                TabViewModel? member = _viewModel.Tabs.FirstOrDefault(t => ReferenceEquals(t.Model, decision.Target));
                if (member != null)
                    FocusSplitMember(member);
            }
            e.Handled = true;
            return;
        }

        if (decision.Target != null)
        {
            TabViewModel? next = _viewModel.Tabs.FirstOrDefault(t => ReferenceEquals(t.Model, decision.Target));
            if (next != null)
                _viewModel.SetActiveTab(next);
        }
        e.Handled = true;
    }

    private void ContainerWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (IsAppShuttingDown)
            return;
        if (_viewModel.Tabs.Count == 0)
            return;

        MessageBoxResult result = MessageBoxResult.Cancel;
        _closePromptOpen = true;
        IntPtr containerHwnd = _containerHwnd;
        if (containerHwnd != IntPtr.Zero)
            _shepherd.RaiseContainerForChrome(containerHwnd, useTopmostBand: true);
        ArmClosePromptRaise();
        try
        {
            result = MessageBox.Show(
                this,
                "Do you want to close the grouped applications?\n\nYes = close all apps\nNo = release windows back to standalone",
                "Close group",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
        }
        finally
        {
            _closePromptOpen = false;
            _closePromptRaiseTimer?.Stop();
            _closePromptRaiseTimer = null;
            // MessageBox is an owned popup, but the guests are independent
            // top-level windows and can already sit above the owner. Reconcile
            // after the modal closes so Cancel leaves the guest stack healthy;
            // Yes/No clear the tabs before this queued pass runs.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_containerHwnd == IntPtr.Zero)
                    return;
                _shepherd.RestoreContainerFromChrome(_containerHwnd);
                if (_viewModel.Tabs.Count == 0)
                    return;
                if (IsSplitPresented)
                    LayoutSplitPanes();
                else
                    LayoutShepherdActiveWindow(forceZOrder: true);
            }));
            if (_closePending)
            {
                _closePending = false;
                if (result == MessageBoxResult.Cancel)
                    Dispatcher.BeginInvoke(new Action(() => Close()));
            }
        }

        switch (result)
        {
            case MessageBoxResult.Yes:
                // A guest may have destroyed itself while the prompt was open,
                // emptying the tab list. Re-validate before acting.
                if (_viewModel.Tabs.Count == 0)
                    return;

                // Snapshot independent native identities while the guests are
                // still captured. CloseGroup deliberately unregisters and
                // removes each capture token, so a later check against the
                // live Shepherd registry would be false by design.
                var windowsToClose = new List<ReleasedWindowCloseTarget>();
                foreach (CapturedWindow window in _viewModel.Tabs.Select(t => t.Model))
                {
                    if (_shepherd.TryCreateReleasedWindowCloseTarget(
                            window,
                            out ReleasedWindowCloseTarget target,
                            out WindowIdentityResult snapshotResult,
                            out string snapshotReason))
                    {
                        windowsToClose.Add(target);
                        continue;
                    }

                    if (snapshotResult == WindowIdentityResult.Mismatch)
                    {
                        _log.Log($"Close-group: skipped already-gone/recycled HWND 0x{window.Hwnd.ToInt64():X} before release ({snapshotReason}).");
                        continue;
                    }

                    _log.Log($"Close-group: released-target snapshot was unverifiable for 0x{window.Hwnd.ToInt64():X} ({snapshotReason}); Yes action cancelled fail-closed.");
                    e.Cancel = true;
                    break;
                }
                if (e.Cancel)
                    break;

                if (!_viewModel.CloseGroup())
                {
                    e.Cancel = true;
                    break;
                }
                foreach (ReleasedWindowCloseTarget target in windowsToClose)
                {
                    ReleasedWindowCloseTargetResult verification =
                        _shepherd.VerifyReleasedWindowCloseTarget(target, out string verificationReason);
                    if (verification != ReleasedWindowCloseTargetResult.Match)
                    {
                        _log.Log($"Close-group: skipped HWND 0x{target.Hwnd.ToInt64():X} after release ({verification}, {verificationReason}).");
                        continue;
                    }
                    if (!NativeMethods.PostMessage(target.Hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                        _log.Log($"Close-group: PostMessage(WM_CLOSE) to 0x{target.Hwnd.ToInt64():X} failed: {NativeMethods.FormatLastError()}");
                }
                break;

            case MessageBoxResult.No:
                if (!_viewModel.CloseGroup())
                    e.Cancel = true;
                break;

            default:
                e.Cancel = true;
                break;
        }
    }

    /// <summary>
    /// The native MessageBox is created inside <see cref="MessageBox.Show"/>'s
    /// nested modal loop, after the Closing handler has raised the owner. A
    /// single dispatcher tick (not a polling loop) finds that HWND once it
    /// exists and places the dialog itself in the topmost band, above any
    /// independent top-level guest window. The HWND is destroyed with the
    /// dialog; no guest style or parent is changed.
    /// </summary>
    private void ArmClosePromptRaise()
    {
        _closePromptRaiseTimer?.Stop();
        var promptTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        promptTimer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_closePromptRaiseTimer, promptTimer))
            {
                promptTimer.Stop();
                return;
            }

            promptTimer.Stop();
            _closePromptRaiseTimer = null;
            if (!_closePromptOpen)
                return;

            IntPtr dialogHwnd = FindOwnedClosePrompt();
            if (dialogHwnd != IntPtr.Zero)
                _shepherd.RaiseContainerForChrome(dialogHwnd, useTopmostBand: true);
        };
        _closePromptRaiseTimer = promptTimer;
        promptTimer.Start();
    }

    /// <summary>
    /// Finds this container's close confirmation dialog without trusting its
    /// caption alone. A same-titled foreign window must never receive a z-order
    /// mutation from the prompt reconciliation path.
    /// </summary>
    private IntPtr FindOwnedClosePrompt()
    {
        if (_containerHwnd == IntPtr.Zero)
            return IntPtr.Zero;

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((candidate, _) =>
        {
            if (!NativeMethods.IsWindowVisible(candidate)
                || !string.Equals(NativeMethods.GetClassNameString(candidate), "#32770", StringComparison.Ordinal)
                || !string.Equals(NativeMethods.GetWindowTextString(candidate), "Close group", StringComparison.Ordinal))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(candidate, out uint pid);
            if (pid != NativeMethods.CurrentProcessId)
                return true;

            IntPtr directOwner = NativeMethods.GetWindow(candidate, NativeMethods.GW_OWNER);
            IntPtr rootOwner = NativeMethods.GetAncestor(candidate, NativeMethods.GA_ROOTOWNER);
            if (directOwner != _containerHwnd && rootOwner != _containerHwnd)
                return true;

            found = candidate;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private void ContainerWindow_Closed(object? sender, EventArgs e)
    {
        // Tear down coalesced callbacks before dropping state. Disarm the split
        // settle so a queued Rendering handler does not fire against nulled
        // split members after close, and stop the activate reassert timer
        // before clearing the active guest it would reassert (Q5/Q8).
        DisarmSplitPresentationSettle();
        _activateReassertTimer?.Stop();
        _activateReassertTimer = null;
        CloseCapturePanel();
        _constraintRefreshTimer?.Stop();
        _constraintRefreshTimer = null;
        _refusedPaneByHwnd.Clear();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.EmptiedByPopOut -= ViewModel_EmptiedByPopOut;
        // Detach unsubscribes Group.PropertyChanged + Tabs.CollectionChanged
        // internally (hardening: Tabs is VM-owned and would otherwise keep the
        // VM/Window rooted via the strip projection after Close). Do not also
        // unsubscribe Tabs here — that would double-unsubscribe and hide future
        // wiring mistakes; rely on the single Detach path.
        _viewModel.Detach();

        // Drop the active guest reference so any pending WM_ACTIVATE or restore
        // timer that fires after the window has closed cannot act on a released
        // guest (finding #3). Disarm + timer stop already done at top of
        // handler; do not re-add them here.
        _shepherdActiveWindow = null;
        // Clear split logical state without re-hiding the guests that are being
        // released or closed with this window. HandleMemberRemoved nulls
        // left/right/foreground/presented and bumps generation without issuing
        // native hides; the settle is already disarmed above, so no async reader
        // can observe the cleared state.
        if (_splitController.IsRelationshipDefined)
            _splitController.HandleMemberRemoved(_splitController.Left!);
        _guestMoveSizeActive = false;
        _guestMoveSizeGeneration++;
        _stateSettledTimer?.Stop();
        _stateSettledTimer = null;
        _restoreMinimizedTimer?.Stop();
        _restoreMinimizedTimer = null;
        // _closePromptRaiseTimer is armed via ArmClosePromptRaise (50ms tick)
        // and may still be pending when the container closes. Stop it and null
        // the field so a pending tick cannot fire post-close against nulled
        // state, and so the timer does not keep the closed window rooted.
        _closePromptRaiseTimer?.Stop();
        _closePromptRaiseTimer = null;
        // WndProc was added in OnSourceInitialized; remove explicitly so a
        // hidden/reused HwndSource does not keep dispatching to a dead host.
        try
        {
            HwndSource? src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            src?.RemoveHook(WndProc);
        }
        catch { }
        // TabsListBox handler was added in code (+= PreviewMouseLeftButtonDown)
        // and is not covered by ViewModel detach — remove so teardown clicks
        // cannot re-enter drag/split logic with nulled state.
        try { TabsListBox.PreviewMouseLeftButtonDown -= TabsListBox_PreviewMouseLeftButtonDown; } catch { }
        try { LayoutUpdated -= ContainerWindow_LayoutUpdated; } catch { }
        _openTabContextMenu = null;
        _viewModel.DeleteGroupRequested -= ViewModel_DeleteGroupRequested;
        ColorContextMenu.Closed -= ColorContextMenu_Closed;
        foreach (ContextMenu menu in _trackedTabContextMenus)
            menu.Closed -= TabContextMenu_Closed;
        _trackedTabContextMenus.Clear();
        _chromePopupActive = false;

        // Unregister the HWNDs cached at Loaded time — live reads return
        // IntPtr.Zero by now, which would no-op and leak the stale values.
        _manager.UnregisterContainerHwnd(_containerHwnd);
        if (_contentHostHwnd != IntPtr.Zero)
            _manager.UnregisterContainerHwnd(_contentHostHwnd);
        _containerHwnd = IntPtr.Zero;
        _contentHostHwnd = IntPtr.Zero;
    }

    private void ContainerWindow_StateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";

        if (_shepherdActiveWindow != null || IsSplitPresented)
        {
            // A minimized container has no visible content area to overlay;
            // hide the docked guest(s) along with it. Restoring re-positions and
            // re-shows them (LayoutSplitPanes / LayoutShepherdActiveWindow use
            // SWP_SHOWWINDOW). In split mode BOTH guests must disappear and both
            // must return on restore.
            if (WindowState == WindowState.Minimized)
            {
                if (IsSplitPresented)
                {
                    WindowHideOutcome leftOutcome = _shepherd.Hide(_splitController.Left!);
                    LogHidePending(_splitController.Left!, leftOutcome);
                    WindowHideOutcome rightOutcome = _shepherd.Hide(_splitController.Right!);
                    LogHidePending(_splitController.Right!, rightOutcome);
                }
                else if (_shepherdActiveWindow != null)
                {
                    WindowHideOutcome outcome = _shepherd.Hide(_shepherdActiveWindow);
                    LogHidePending(_shepherdActiveWindow, outcome);
                }
            }
            else
            {
                // Re-glue through the coalescer, NOT synchronously: StateChanged
                // fires before WPF has re-arranged the content marker to the new
                // native size, so the marker's rect is still the pre-transition
                // one here. A synchronous LayoutSplitPanes/LayoutShepherdActiveWindow
                // would read that stale rect and glue both guests to the old
                // pane rectangles for the whole DWM transition (on slower
                // machines the two panes visibly overlap the new content area —
                // the "split panes overlap after maximize/restore" defect).
                // RequestRelayout coalesces the transition into a single
                // post-layout pass (the Render-priority callback runs after the
                // layout pass that resized the marker), re-glueing both panes
                // atomically against the final authoritative rect. On
                // restore-without-resize (e.g. Normal->Minimized->Normal) no
                // layout pass runs; the Render callback itself is the final
                // glue there — either way the pass reads the CURRENT marker
                // rect, never a cached one.
                RequestRelayout();
                // One bounded diagnostic per transition: record the rect read at
                // this (provably stale) moment so a friend-machine episode is
                // diagnosable from the log. Not per-frame; skip in the minimize
                // branch (no geometry to read).
                if (_containerHwnd != IntPtr.Zero)
                {
                    NativeMethods.RECT hostRect = GetContentAreaScreenRect();
                    _log.Log($"STATE[transition] winState={WindowState} hostRect={hostRect.left},{hostRect.top},{hostRect.Width}x{hostRect.Height} (pre-layout read; the coalesced pass applies the final rect)");
                }
            }
        }

        // Lightweight state snapshot after the transition settles. Retained (low
        // volume: once per maximize/restore) as a field-diagnosis aid.
        _stateSettledTimer?.Stop();
        var settledTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        settledTimer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_stateSettledTimer, settledTimer))
            {
                settledTimer.Stop();
                return;
            }

            settledTimer.Stop();
            _stateSettledTimer = null;
            LogStateSnapshot("settled");
        };
        _stateSettledTimer = settledTimer;
        settledTimer.Start();
    }


    private void LogStateSnapshot(string phase)
    {
        try
        {
            // This diagnostic is scheduled from Loaded/StateChanged paths;
            // reuse the lifecycle-cached HWND rather than asking WPF to resolve
            // it again during a hot transition.
            IntPtr hwnd = _containerHwnd;
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
                guestDesc += $" exe={active.ExePath}";
                if (NativeMethods.IsWindow(active.Hwnd))
                {
                    NativeMethods.GetWindowRect(active.Hwnd, out NativeMethods.RECT guestRect);
                    NativeMethods.RECT docked = GetContentAreaScreenRect();
                    bool isDocked = guestRect.left == docked.left && guestRect.top == docked.top
                        && guestRect.right == docked.right && guestRect.bottom == docked.bottom;
                    guestDesc += $" docked={isDocked}";
                }
            }

            _log.Log($"STATE[{phase}] {EnvironmentFingerprint.Platform} winState={WindowState} container={winRect.left},{winRect.top},{winRect.Width}x{winRect.Height} dpi={dpi} " +
                     $"monitor={mi.rcMonitor.left},{mi.rcMonitor.top},{mi.rcMonitor.Width}x{mi.rcMonitor.Height} work={mi.rcWork.left},{mi.rcWork.top},{mi.rcWork.Width}x{mi.rcWork.Height} " +
                     $"host=({hostDesc}) guest={guestDesc}");
        }
        catch (Exception ex)
        {
            _log.LogException("LogStateSnapshot", ex);
        }
    }

    // Re-entrancy guard for TabsListBox_SelectionChanged. The selection funnel
    // (SetActiveTab -> ActiveTab -> tab IsActive -> the ListBoxItem's TwoWay
    // IsSelected binding -> a NEW SelectionChanged) can echo a selection change
    // back into this handler while the outer call is still on the stack. The
    // IsActive<->IsSelected TwoWay binding then ping-pongs the non-member's
    // selection forever (re-entrant SetActiveTab -> IsActive flip -> IsSelected
    // flip -> SelectionChanged -> ...) and overflows the stack. Every such echo
    // is redundant — the outer call already updated ActiveTab/ActiveIndex — so
    // dropping it changes nothing except terminating the cycle.
    private bool _inSelectionSync;

    private void TabsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inSelectionSync)
            return;
        if (TabsListBox.SelectedItem is TabViewModel tab)
        {
            // SelectedItem is bound OneWay to ActiveTab (ContainerWindow.xaml),
            // so a programmatic switch first runs SetActiveTab -> SwitchActiveTab
            // (which updates Group.ActiveIndex) and only then echoes back here as
            // a selection change. In that echo case the switch already happened —
            // calling SetActiveTab again would log a second "Switched group" and
            // reset the save debounce for nothing. A genuine user click arrives
            // before any ActiveIndex update, so the indices never match and the
            // switch proceeds exactly as before.
            if (Group.ActiveIndex == _viewModel.Tabs.IndexOf(tab))
                return;
            _inSelectionSync = true;
            try
            {
                _viewModel.SetActiveTab(tab);
            }
            finally
            {
                _inSelectionSync = false;
            }
        }
    }

    /// <summary>
    /// Drops the presentation-side captured tabs after App has released the
    /// native guests during session ending. The active/split references are
    /// cleared first so closing an inline capture surface cannot trigger a
    /// relayout against already-standalone windows.
    /// </summary>
    internal void ClearReleasedTabsAfterSessionEnding()
    {
        _shepherdActiveWindow = null;
        // Clear split logical state without re-hiding guests the app has already
        // released during session ending. HandleMemberRemoved nulls the
        // relationship and bumps generation without issuing native hides.
        if (_splitController.IsRelationshipDefined)
            _splitController.HandleMemberRemoved(_splitController.Left!);
        _guestMoveSizeActive = false;
        _guestMoveSizeGeneration++;
        _activateReassertTimer?.Stop();
        _stateSettledTimer?.Stop();
        _restoreMinimizedTimer?.Stop();
        CloseCapturePanel();
        _viewModel.ClearReleasedTabsAfterSessionEnding();
    }

    /// <summary>
    /// Right-clicking a tab opens its context menu (Pop out / Close window)
    /// WITHOUT switching the active tab. WPF's ListBoxItem selects the item on
    /// right-button-down by default, which would silently activate the tab under
    /// the cursor before the menu even opens — making "Pop out" on a background
    /// tab switch the user away from the tab they are looking at. That defeats
    /// the keep-active guarantee in GroupViewModel.ReleaseTab, which is only
    /// reachable when the released tab is genuinely inactive, so it is unreachable
    /// via the context menu while right-click selects first. Swallowing the
    /// routed event at the ListBox leaves selection untouched, and the tab's
    /// context menu is opened manually at the cursor instead.
    /// </summary>
    private void TabsListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src)
            return;
        if (ItemsControl.ContainerFromElement(TabsListBox, src) is not ListBoxItem item)
            return;

        ContextMenu menu;
        FrameworkElement? placementTarget;
        TabViewModel? initiatingTab;

        if (item.DataContext is SplitCompositeViewModel composite)
        {
            // Right-click on the split composite: resolve WHICH half was clicked
            // and target that specific member (its Pop out / Close window), so
            // the menu is never ambiguous about which window an action affects.
            TabViewModel? member = null;
            FrameworkElement? half = null;
            for (DependencyObject? cur = src; cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is Border { Tag: string tag } b && (tag == "LEFT" || tag == "RIGHT"))
                {
                    member = tag == "LEFT" ? composite.Left : composite.Right;
                    half = b;
                    break;
                }
            }
            if (member == null || half == null)
                return;
            initiatingTab = member;
            placementTarget = half;
            menu = new ContextMenu();
            var popOut = new MenuItem { Header = "Pop out", Command = member.PopOutCommand };
            System.Windows.Automation.AutomationProperties.SetAutomationId(popOut, "PopOut");
            var closeWindow = new MenuItem { Header = "Close window", Command = member.CloseWindowCommand };
            System.Windows.Automation.AutomationProperties.SetAutomationId(closeWindow, "CloseWindow");
            menu.Items.Add(popOut);
            menu.Items.Add(closeWindow);
            _trackedTabContextMenus.Add(menu);
            menu.Closed += TabContextMenu_Closed;
        }
        else
        {
            if (FindTabContextMenuOwner(item) is not FrameworkElement owner || owner.ContextMenu is not ContextMenu existing)
                return;
            menu = existing;
            placementTarget = owner;
            // The right-clicked tab is the split initiator (becomes the LEFT
            // pane). The menu's DataContext is this tab's TabViewModel.
            initiatingTab = (owner as FrameworkElement)?.DataContext as TabViewModel;
        }

        e.Handled = true;
        ConfigureSplitMenuItems(menu, initiatingTab);
        // Open on a dispatcher callback rather than inline: a context menu
        // opened synchronously inside a mouse-down handler (while the right
        // button is still held) can fail to display or close immediately —
        // deferring past the current input event is the reliable pattern.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                menu.PlacementTarget = placementTarget;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                // Track the open menu so the WM_ACTIVATE reassert guard can
                // see it before the 120ms timer fires (a context menu opened
                // by right-clicking a tab must not be closed by a reassert
                // that steals foreground to the guest).
                _openTabContextMenu = menu;
                if (_trackedTabContextMenus.Add(menu))
                    menu.Closed += TabContextMenu_Closed;
                _log.Log("CHROME[tab-menu-open-request]");
                BeginChromePopup();
                menu.IsOpen = true;
                _log.Log($"CHROME[tab-menu-opened] isOpen={menu.IsOpen}");
            }));
    }

    private void TabContextMenu_Closed(object? sender, RoutedEventArgs e)
    {
        _log.Log("CHROME[tab-menu-closed]");
        if (ReferenceEquals(_openTabContextMenu, sender))
            _openTabContextMenu = null;
        // Stop tracking a closed menu now rather than retaining it (and the
        // TabViewModel its items root) until container close.
        if (sender is ContextMenu closedMenu)
        {
            closedMenu.Closed -= TabContextMenu_Closed;
            _trackedTabContextMenus.Remove(closedMenu);
        }
        EndChromePopup();
    }

    /// <summary>
    /// Builds the split-screen entries on a tab's context menu before it opens.
    /// Idempotent: any previously-added split items (Tag prefix "SPLIT-") are
    /// removed first, then re-added for the current group state.
    /// </summary>
    private void ConfigureSplitMenuItems(ContextMenu menu, TabViewModel? initiatingTab)
    {
        var stale = menu.Items.OfType<MenuItem>()
            .Where(mi => mi.Tag is string s && s.StartsWith("SPLIT-", StringComparison.Ordinal))
            .ToList();
        foreach (var mi in stale)
            menu.Items.Remove(mi);

        int tabCount = _viewModel.Tabs.Count;
        int insertIndex = 0;

        if (tabCount < 2 || initiatingTab == null)
        {
            // Fewer than two eligible captured tabs: show Split screen disabled
            // so the user can see the feature exists but understands a second
            // tab is required (spec §5 — no silent failure after clicking).
            var disabled = new MenuItem { Header = "Split screen", Tag = "SPLIT-ACTION", IsEnabled = false };
            System.Windows.Automation.AutomationProperties.SetAutomationId(disabled, "SplitScreen");
            menu.Items.Insert(insertIndex++, disabled);
            return;
        }

        bool isCurrentSplitMember = initiatingTab != null && IsSplitMember(initiatingTab.Model);
        if (!isCurrentSplitMember && tabCount == 2)
        {
            // Exactly two tabs: direct action (auto-selects the sole other tab).
            var action = new MenuItem { Header = "Split screen", Tag = "SPLIT-ACTION", DataContext = initiatingTab };
            System.Windows.Automation.AutomationProperties.SetAutomationId(action, "SplitScreen");
            action.Click += SplitScreenMenuItem_Click;
            menu.Items.Insert(insertIndex++, action);
        }
        else if (!isCurrentSplitMember)
        {
            // Three or more tabs: submenu of candidate partners (excluding the
            // initiating tab). Selecting one puts initiating -> LEFT, candidate -> RIGHT.
            var submenu = new MenuItem { Header = "Split screen", Tag = "SPLIT-SUBMENU", DataContext = initiatingTab };
            System.Windows.Automation.AutomationProperties.SetAutomationId(submenu, "SplitScreen");
            foreach (var candidate in _viewModel.Tabs.Where(t => !ReferenceEquals(t, initiatingTab)))
            {
                var child = new MenuItem
                {
                    Header = candidate.Title,
                    Icon = candidate.Icon,
                    Tag = "SPLIT-CANDIDATE",
                    DataContext = candidate,
                };
                System.Windows.Automation.AutomationProperties.SetAutomationId(child, "SplitCandidate");
                child.Click += SplitCandidateMenuItem_Click;
                submenu.Items.Add(child);
            }
            menu.Items.Insert(insertIndex++, submenu);
        }

        // When a split relationship is defined, offer a way out from the split
        // members' menus. The initiating member deliberately omitted the
        // reconfiguration entry above; Exit remains available in both the
        // presented and dormant states.
        if (IsSplitRelationshipDefined)
        {
            var exitItem = new MenuItem { Header = "Exit split screen", Tag = "SPLIT-EXIT" };
            System.Windows.Automation.AutomationProperties.SetAutomationId(exitItem, "ExitSplitScreen");
            exitItem.Click += ExitSplitMenuItem_Click;
            menu.Items.Add(exitItem);
        }
    }

    private void SplitScreenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TabViewModel leftTab)
            StartSplitFrom(leftTab);
    }

    private void SplitCandidateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TabViewModel rightTab
            && mi.Parent is MenuItem parent && parent.DataContext is TabViewModel leftTab)
            StartSplitFrom(leftTab, rightTab);
    }

    private void ExitSplitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExitSplit();
    }

    /// <summary>
    /// True when the user is interacting with the container's own chrome rather
    /// than intending to foreground the guest: an open tab context menu (Pop out /
    /// Close window), the open accent-color menu, or the group rename box being
    /// edited. The WM_ACTIVATE reassert uses this to avoid stealing foreground
    /// from a chrome interaction 120ms after the click that opened it — doing so
    /// would close the menu / drop focus out of the rename box.
    /// </summary>
    private bool IsContainerChromeInteractionActive()
    {
        if (_openTabContextMenu is { IsOpen: true }) return true;
        if (ColorContextMenu.IsOpen) return true;
        if (GroupContextMenu.IsOpen) return true;
        if (IsCapturePanelOpen) return true;
        if (_viewModel.IsRenaming) return true;
        // The close-group/delete-group confirm dialog is an owned modal over the
        // container: the 120ms WM_ACTIVATE reassert would otherwise raise the
        // docked guest ABOVE it and cover its buttons (observed live: clicking
        // the container's × opens the prompt, the reassert fires 120ms later,
        // WindowFromPoint at the Yes button resolves to the guest).
        if (_closePromptOpen) return true;
        return false;
    }

    /// <summary>
    /// Finds the element that owns the tab's context menu by walking the visual
    /// tree under a tab's ListBoxItem — the menu is declared on the Border in
    /// the tab's DataTemplate (Views/ContainerWindow.xaml), not on the
    /// ListBoxItem itself.
    /// </summary>
    private static FrameworkElement? FindTabContextMenuOwner(DependencyObject item)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(item);
        while (stack.Count > 0)
        {
            DependencyObject cur = stack.Pop();
            if (cur is FrameworkElement fe && fe.ContextMenu != null)
                return fe;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(cur); i++)
                stack.Push(VisualTreeHelper.GetChild(cur, i));
        }
        return null;
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
        BeginChromePopup();
        ColorContextMenu.PlacementTarget = (UIElement)sender;
        ColorContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ColorContextMenu.IsOpen = true;
    }

    private void ColorContextMenu_Closed(object? sender, RoutedEventArgs e) => EndChromePopup();

    private void GroupButton_Click(object sender, RoutedEventArgs e)
    {
        GroupContextMenu.Items.Clear();
        foreach (Group group in _manager.Groups)
        {
            var item = new MenuItem
            {
                Header = group.Name,
                IsChecked = ReferenceEquals(group, Group),
                Tag = group
            };
            item.Click += GroupMenuItem_Click;
            GroupContextMenu.Items.Add(item);
        }

        if (GroupContextMenu.Items.Count > 0)
            GroupContextMenu.Items.Add(new Separator());

        var newGroup = new MenuItem { Header = "+ New group", Tag = "NEW-GROUP" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(newGroup, "NewGroup");
        newGroup.Click += NewGroupMenuItem_Click;
        GroupContextMenu.Items.Add(newGroup);

        var renameGroup = new MenuItem { Header = "Rename group", Tag = "RENAME-GROUP" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(renameGroup, "RenameGroup");
        renameGroup.Click += RenameGroupMenuItem_Click;
        GroupContextMenu.Items.Add(renameGroup);

        var deleteGroup = new MenuItem { Header = "Delete group", Tag = "DELETE-GROUP" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(deleteGroup, "DeleteGroup");
        deleteGroup.Click += DeleteGroupMenuItem_Click;
        GroupContextMenu.Items.Add(deleteGroup);

        BeginChromePopup();
        GroupContextMenu.PlacementTarget = (UIElement)sender;
        GroupContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        // Open on a dispatcher callback (matching the tab context-menu pattern)
        // so the popup HWND is created after the current input event settles;
        // together with the GroupContextMenu.IsOpen check in
        // IsContainerChromeInteractionActive this keeps the 120ms WM_ACTIVATE
        // reassert from stealing foreground from the guest while the menu is open.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => GroupContextMenu.IsOpen = true));
    }

    private void GroupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: Group group } && !ReferenceEquals(group, Group))
            GroupSelectedRequested?.Invoke(this, new GroupSelectionEventArgs(group));
    }

    private void NewGroupMenuItem_Click(object sender, RoutedEventArgs e) =>
        NewGroupRequested?.Invoke(this, EventArgs.Empty);

    private void RenameGroupMenuItem_Click(object sender, RoutedEventArgs e) => BeginRename();

    private void DeleteGroupMenuItem_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DeleteGroupCommand.Execute(null);

    private void GroupContextMenu_Closed(object? sender, RoutedEventArgs e) => EndChromePopup();

    /// <summary>
    /// Context menus are separate WPF popup HWNDs. Bring the container above its
    /// guests while one is being created so the popup is owned by the visible
    /// TabDock surface; the guest is never hidden. The corresponding close path
    /// always reconciles the guest stack, which prevents a guest remaining under
    /// the container marker after the popup disappears.
    /// </summary>
    private void BeginChromePopup()
    {
        if (_chromePopupActive)
            return;
        _chromePopupActive = true;
        _log.Log("CHROME[raise]");
        IntPtr hwnd = _containerHwnd;
        if (hwnd != IntPtr.Zero)
            _shepherd.RaiseContainerForChrome(hwnd);
    }

    private void EndChromePopup()
    {
        if (!_chromePopupActive)
            return;
        _chromePopupActive = false;
        _log.Log("CHROME[restore-request]");
        // Let WPF finish destroying/closing the popup HWND before restoring the
        // guest stack. This is one explicit transition, not a repair timer.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_chromePopupActive || IsCapturePanelOpen || _containerHwnd == IntPtr.Zero)
                return;
            if (IsSplitPresented)
                LayoutSplitPanes();
            else
                LayoutShepherdActiveWindow(forceZOrder: true);
        }));
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
        // Toggle the inline capture surface: a second click on the same button
        // closes it, matching the behaviour of the other chrome popups. The
        // close-confirm prompt still takes precedence.
        if (_closePromptOpen)
            return;
        if (IsCapturePanelOpen)
            CloseCapturePanel();
        else
            _viewModel.RequestAddWindows();
    }

    private void TabClose_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.DataContext is TabViewModel tab)
        {
            _log.Log($"TAB[popout-button] guest=0x{tab.Model.Hwnd.ToInt64():X}");
            EndDrag();
            _viewModel.ReleaseTab(tab);
            e.Handled = true;
        }
    }

    /// <summary>
    /// LEFT/RIGHT half click on the composite split tab: focus/activate that
    /// member while keeping split mode and BOTH panes visible. The partner must
    /// never be hidden by a half click (the pre-composite tab-selection path
    /// could misinterpret one split member as needing to hide the other).
    /// </summary>
    private void SplitHalf_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border half || half.Tag is not string side || (side != "LEFT" && side != "RIGHT"))
            return;
        // The half's own × button handles its click; ignore clicks that land on
        // it (PreviewMouseLeftButtonDown tunnels root-first, so this half
        // handler runs before the button's own handler).
        for (DependencyObject? cur = e.OriginalSource as DependencyObject; cur != null; cur = VisualTreeHelper.GetParent(cur))
        {
            if (cur is Button)
                return;
        }
        if (half.DataContext is not SplitCompositeViewModel composite)
            return;
        TabViewModel target = side == "LEFT" ? composite.Left : composite.Right;
        // Route through the canonical member-focus operation so a half-click on
        // the ALREADY-active member still re-asserts it as the z-top focused
        // member (a direct pane click may have left the foreground member on the
        // other member; SetActiveTab alone would no-op and never re-glue).
        FocusSplitMember(target);
        e.Handled = true;
    }

    /// <summary>
    /// Middle-click on a composite half pops that specific member out (browser
    /// tab semantics per half).
    /// </summary>
    private void SplitHalf_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;
        if (sender is not Border half || half.Tag is not string side || (side != "LEFT" && side != "RIGHT"))
            return;
        if (half.DataContext is not SplitCompositeViewModel composite)
            return;
        e.Handled = true;
        EndDrag();
        _viewModel.ReleaseTab(side == "LEFT" ? composite.Left : composite.Right);
    }

    /// <summary>
    /// × on a composite half pops that specific member out; the split ends and
    /// the survivor is promoted to the single full-width guest via the existing
    /// HandleSplitMemberRemoved path.
    /// </summary>
    private void SplitHalfClose_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string side || (side != "LEFT" && side != "RIGHT"))
            return;
        if (button.DataContext is not SplitCompositeViewModel composite)
            return;
        TabViewModel target = side == "LEFT" ? composite.Left : composite.Right;
        _log.Log($"TAB[popout-button] guest=0x{target.Model.Hwnd.ToInt64():X} (split half)");
        EndDrag();
        _viewModel.ReleaseTab(target);
        e.Handled = true;
    }

    /// <summary>
    /// Opens capture inside the existing container chrome. The guest rect is
    /// derived from the marker below this panel, so no popup HWND can cover the
    /// panel and no temporary TabDock modal is required for routine capture.
    /// </summary>
    public void OpenCapturePanel()
    {
        if (_capturePicker != null)
            return;

        // Raise the container above its guests while the inline capture surface
        // is open so the panel (sited between the tab strip and the content
        // area) is never partially occluded by a guest whose z-order drifted.
        BeginChromePopup();
        _capturePicker = new CapturePickerViewModel(_manager, _icons, _log);
        _capturePicker.SelectedGroupOption = _capturePicker.Groups
            .FirstOrDefault(g => g.Id == Group.Id) ?? _capturePicker.Groups.FirstOrDefault();
        _capturePicker.GroupingRequested += InlineCapture_GroupingRequested;
        _capturePicker.Canceled += InlineCapture_Canceled;
        CapturePanel.DataContext = _capturePicker;
        CapturePanel.Visibility = Visibility.Visible;
        UpdateLayout();
        RelayoutGuests();
    }

    private void InlineCapture_GroupingRequested(object? sender, EventArgs e)
    {
        if (_capturePicker == null)
            return;

        foreach (var candidate in _capturePicker.Windows.Where(w => w.IsSelected).ToList())
        {
            string? error = CaptureWindow(candidate.ToCaptureTarget());
            if (error != null)
            {
                _log.Log($"Inline capture failed for 0x{candidate.Hwnd.ToInt64():X}: {error}");
                MessageBox.Show(this, error, "Could not capture window", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        CloseCapturePanel();
    }

    private void InlineCapture_Canceled(object? sender, EventArgs e) => CloseCapturePanel();

    private void CloseCapturePanel()
    {
        if (_capturePicker == null)
            return;

        // Reconcile the guest stack now that the panel is gone (the container
        // was raised above its guests while the panel was open so it could not
        // be occluded; this restores the normal guest-over-container pairing).
        EndChromePopup();
        _capturePicker.GroupingRequested -= InlineCapture_GroupingRequested;
        _capturePicker.Canceled -= InlineCapture_Canceled;
        _capturePicker.Dispose();
        _capturePicker = null;
        CapturePanel.DataContext = null;
        CapturePanel.Visibility = Visibility.Collapsed;
        UpdateLayout();
        RelayoutGuests();
    }

    /// <summary>
    /// Captures a top-level window and adds it as a new tab in this container.
    /// Returns an error message if capture fails (e.g. UIPI, elevated window, own
    /// window, or a window that is already docked in a group).
    /// </summary>
    public string? CaptureWindow(IntPtr hwnd) => CaptureWindow(hwnd, expected: null);

    public string? CaptureWindow(WindowCaptureTarget expected)
        => CaptureWindow(expected.Hwnd, expected);

    private string? CaptureWindow(IntPtr hwnd, WindowCaptureTarget? expected)
    {
        if (!_manager.CaptureAllowed)
            return "Capture is temporarily disabled because TabDock's native guest-lifecycle monitor is unavailable. Restart TabDock after the monitor recovers.";

        if (expected != null && !MatchesCaptureTarget(expected))
            return "The selected window changed before capture; refresh the picker and try again.";

        if (_manager.IsOwnWindow(hwnd))
            return "Cannot capture a TabDock window (no nesting).";

        // Defence in depth alongside the capture picker's own filter: two
        // CapturedWindow members for one HWND would fight over positioning and
        // hiding it, and only one of them would ever be torn down — the
        // destroy/hide WinEvent handlers resolve the HWND through GroupManager's
        // O(1) TryGetCapturedMember index (PERF25-02), which maps each HWND to a
        // single member. Enforced here so every entry point is covered, not
        // just the picker.
        if (_manager.IsCapturedWindow(hwnd))
            return "That window is already in a TabDock group.";

        CapturedWindow? cw = _shepherd.Capture(hwnd, out string? error);
        if (cw == null)
            return error ?? "Capture failed.";

        if (expected != null && !MatchesCaptureTarget(expected))
        {
            // Shepherd.Capture rechecks identity within its own admission
            // window, but the picker target can still be replaced between that
            // check and this add-to-group boundary. Do not retain a capture
            // that no longer matches what the user selected.
            // Release only if the HWND still identifies the just-captured
            // window; otherwise the raw handle may already belong to an
            // unrelated replacement window.
            if (MatchesCapturedWindow(cw))
                _shepherd.Release(cw);
            else
                _log.Log($"Capture cleanup refused for recycled HWND 0x{hwnd.ToInt64():X}; leaving the replacement window untouched.");
            return "The selected window changed during capture; it was left standalone.";
        }

        try
        {
            AddCapturedWindow(cw);
            if (!_manager.CaptureAllowed)
            {
                // The member-collection notification starts the WinEvent
                // monitor synchronously. If installation fails at that exact
                // admission boundary, do not let a newly captured guest remain
                // in an unsupported lifecycle mode while the bounded retry
                // timer runs. Release the just-captured identity immediately;
                // the retry policy may recover for a later user attempt.
                if (MatchesCapturedWindow(cw))
                    ReleaseCapturedWindow(cw, show: true);
                else
                    _log.Log($"Capture cleanup refused for recycled HWND 0x{hwnd.ToInt64():X} after monitor admission failure; replacement left untouched.");
                return "Capture was refused because TabDock's native guest-lifecycle monitor could not be installed.";
            }
            DiagnosticRuntime.Record("guest.capture", _containerHwnd, cw.Hwnd,
                group: Group.Id.ToString("N"), action: "capture", result: "success");
            return null;
        }
        catch (Exception addEx)
        {
            _log.LogException($"CaptureWindow add failed for 0x{hwnd.ToInt64():X}", addEx);

            // A managed insertion failure must not leave the native capture
            // orphaned. Prefer the group-aware release when the member reached
            // Group.Members; otherwise release the just-captured object directly.
            if (_manager.TryGetCapturedMember(cw.Hwnd, out Group? owner, out CapturedWindow? member)
                && ReferenceEquals(member, cw))
            {
                _manager.ReleaseMember(owner, cw, show: true);
            }
            else
            {
                _shepherd.Release(cw, show: true);
            }

            return "The window was captured but could not be added to the group; it was restored standalone.";
        }
    }

    private static bool MatchesCaptureTarget(WindowCaptureTarget expected)
    {
        if (!NativeMethods.IsWindow(expected.Hwnd))
            return false;

        // Title is deliberately not compared: it is mutable display metadata,
        // and a browser tab/editor renaming itself between picker selection
        // and capture must not veto an otherwise-identical target.
        NativeMethods.GetWindowThreadProcessId(expected.Hwnd, out uint pid);
        string? className = NativeMethods.GetClassNameString(expected.Hwnd);
        string? exePath = NativeMethods.GetProcessImagePath(pid);
        return pid == expected.ProcessId
            && string.Equals(className, expected.ClassName, StringComparison.Ordinal)
            && string.Equals(exePath, expected.ExePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCapturedWindow(CapturedWindow captured)
    {
        if (!NativeMethods.IsWindow(captured.Hwnd))
            return false;

        NativeMethods.GetWindowThreadProcessId(captured.Hwnd, out uint pid);
        string? className = NativeMethods.GetClassNameString(captured.Hwnd);
        string? exePath = NativeMethods.GetProcessImagePath(pid);
        return pid == captured.ProcessId
            && string.Equals(className, captured.OriginalClassName, StringComparison.Ordinal)
            && string.Equals(exePath, captured.ExePath, StringComparison.OrdinalIgnoreCase);
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
        _shepherd.HideProvenance?.ForgetWindow(window.Hwnd);
        var tab = _viewModel.Tabs.FirstOrDefault(t => t.Model == window);
        if (tab != null)
        {
            _viewModel.ReleaseTab(tab, show);
            DiagnosticRuntime.Record("guest.release", _containerHwnd, window.Hwnd,
                group: Group.Id.ToString("N"), action: "release", result: "requested",
                data: new Dictionary<string, string> { ["show"] = show.ToString() });
        }
    }

    #region Shepherd active-tab sync

    private void LogHidePending(CapturedWindow window, WindowHideOutcome outcome)
    {
        if (outcome != WindowHideOutcome.RecoveryPending)
            return;

        _log.Log($"SHEPHERD[hide-pending] presentation retained guest 0x{window.Hwnd.ToInt64():X}; recovery evidence remains durable.");
        DiagnosticRuntime.Record("repair.visibility", guest: window.Hwnd,
            action: "ShowWindow(SW_HIDE)", result: "recovery-pending");
    }

    /// <summary>
    /// Reacts to an ActiveTab change by hiding the outgoing guest through its
    /// journal-safe transaction before showing the incoming one. If identity
    /// or journal durability is uncertain, the logical active tab is rolled
    /// back so an old visible guest cannot coexist with a new logical guest.
    ///
    /// Q3: When SuspendPresentedPairForUserSelection pre-seeds
    /// _shepherdActiveWindow to the target BEFORE calling SetActiveTab, this
    /// notification would otherwise repeat the entire hide-old/show-new
    /// transaction transactionally already done. The ReferenceEquals guard
    /// above makes that notification a no-op in that path — no duplicate
    /// native work (the single-pass invariant).
    /// </summary>
    private void SyncShepherdActiveWindow()
    {
        CapturedWindow? newWindow = _viewModel.ActiveTab?.Model;
        CapturedWindow? oldWindow = _shepherdActiveWindow;
        if (ReferenceEquals(oldWindow, newWindow))
            return;

        DiagnosticRuntime.Record("presentation.active-member", _containerHwnd, newWindow?.Hwnd ?? IntPtr.Zero,
            group: Group.Id.ToString("N"), action: "active-tab-change", result: "logical-state-updated",
            data: new Dictionary<string, string>
            {
                ["oldGuest"] = DiagnosticEnvironmentService.FormatHwnd(oldWindow?.Hwnd ?? IntPtr.Zero),
                ["newGuest"] = DiagnosticEnvironmentService.FormatHwnd(newWindow?.Hwnd ?? IntPtr.Zero),
            });

        if (IsSplitRelationshipDefined && !IsSplitPresented
            && newWindow != null && IsSplitMember(newWindow))
        {
            TabViewModel? memberTab = _viewModel.Tabs.FirstOrDefault(t => ReferenceEquals(t.Model, newWindow));
            if (memberTab != null)
                ResumeSplitPair(memberTab);
            return;
        }

        if (IsSplitPresented)
        {
            if (newWindow != null && IsSplitMember(newWindow))
            {
                // Clicking one of the two split members keeps split active: that
                // member becomes the active/focused one and its partner stays
                // visible. Re-glue both panes with the new member on top.
                var newTab = _viewModel.Tabs.FirstOrDefault(t => t.Model == newWindow);
                if (newTab != null)
                {
                    FocusSplitMember(newTab);
                    return;
                }
            }

            if (newWindow == null)
            {
                // Teardown (e.g. CloseGroup cleared the tab list while the
                // split fields were still set): fall through to the guarded
                // exit — its member hides are Tabs-guarded and its survivor
                // lookup no-ops on an empty list.
                ExitSplit();
                return;
            }

            // A NON-member tab was activated while the split pair is the
            // selected tab-strip unit (click/Ctrl+Tab on a third tab, or a
            // freshly captured window). The pair PERSISTS: no SPLIT[exit], no
            // ordinary tab-visibility transition, no member hidden, no
            // release. Revert the logical active tab to the focused member
            // through the canonical FocusSplitMember — it re-syncs
            // Group.ActiveIndex and the half highlight, re-asserts the pair's
            // glue, and never touches membership.
            //
            // A newly captured window was never hidden by tab switching (it is
            // not an inactive tab), so hide it journal-safely to keep the
            // visible set exactly { LEFT, RIGHT }; an ordinary third tab is
            // already hidden, making this a no-op for it.
            if (NativeMethods.IsWindow(newWindow.Hwnd) && NativeMethods.IsWindowVisible(newWindow.Hwnd))
            {
                WindowHideOutcome hideOutcome = _shepherd.Hide(newWindow);
                LogHidePending(newWindow, hideOutcome);
                if (hideOutcome == WindowHideOutcome.Hidden)
                    _log.Log($"SPLIT[persist] non-member=0x{newWindow.Hwnd.ToInt64():X} was newly visible; hidden (split pair remains the visible set)");
                else if (hideOutcome == WindowHideOutcome.RecoveryPending)
                    _log.Log($"SPLIT[persist] non-member=0x{newWindow.Hwnd.ToInt64():X} remains visible while native hide recovery is pending.");
            }
            var focusedTab = _viewModel.Tabs.FirstOrDefault(t => ReferenceEquals(t.Model, _splitController.Foreground))
                ?? _viewModel.Tabs.FirstOrDefault(t => ReferenceEquals(t.Model, _splitController.Left));
            if (focusedTab != null)
                FocusSplitMember(focusedTab);
            return;
        }

        bool oldWindowHidden = false;
        if (oldWindow != null
            && newWindow != null
            && _viewModel.Tabs.Any(t => t.Model == oldWindow))
        {
            WindowHideOutcome hideOutcome = _shepherd.Hide(oldWindow);
            if (hideOutcome == WindowHideOutcome.RecoveryPending)
            {
                _log.Log($"SHEPHERD[active-switch-pending] retained old active guest 0x{oldWindow.Hwnd.ToInt64():X}; incoming guest was not shown.");
                DiagnosticRuntime.Record("presentation.active-member", _containerHwnd, oldWindow.Hwnd,
                    group: Group.Id.ToString("N"), action: "active-tab-change", result: "recovery-pending");
                TabViewModel? oldTab = _viewModel.Tabs.FirstOrDefault(t => ReferenceEquals(t.Model, oldWindow));
                if (oldTab != null)
                    _viewModel.SetActiveTab(oldTab);
                return;
            }
            oldWindowHidden = true;
        }

        _shepherdActiveWindow = newWindow;
        // The single visible guest changed: its native minimum may differ, so
        // recompute the container's minimum size and clear refusals.
        _constraintDirty = true;
        _refusedPaneByHwnd.Clear();

        if (newWindow != null && NativeMethods.IsWindow(newWindow.Hwnd))
        {
            LayoutShepherdActiveWindow();
        }

        if (oldWindow != null && !oldWindowHidden && _viewModel.Tabs.Any(t => t.Model == oldWindow))
            LogHidePending(oldWindow, _shepherd.Hide(oldWindow));
    }

    /// <summary>
    /// The ONE canonical "focus a split member" operation (goal §6/§49): every
    /// entry point that focuses a member of the active pair — composite half
    /// click, direct guest click (WinEvent foreground/reorder), WM_ACTIVATE
    /// reassert, Ctrl+Tab, tab-strip activation echo — routes through here so
    /// LEFT and RIGHT are peers after split creation and no path can treat the
    /// partner via the ordinary single-tab visibility transition.
    ///
    /// It updates the focused split member (SplitPresentationController.Foreground),
    /// the logical active member (<see cref="_shepherdActiveWindow"/> + the
    /// view-model active tab, which drives the half highlight and
    /// Group.ActiveIndex), emits the bounded SPLIT[focus] diagnostic (only when
    /// the focused member actually changes), and re-glues both panes with the
    /// new member on top. It must NOT change split membership, hide the
    /// partner, or leave the composite selection.
    /// </summary>
    private void FocusSplitMember(TabViewModel member)
    {
        if (member == null || !IsSplitRelationshipDefined || !IsSplitMember(member.Model))
            return;
        if (!IsSplitPresented)
        {
            ResumeSplitPair(member);
            return;
        }

        bool changed = !ReferenceEquals(_splitController.Foreground, member.Model)
            || !ReferenceEquals(_shepherdActiveWindow, member.Model);

        _splitController.FocusMember(member.Model);
        _shepherdActiveWindow = member.Model;
        // No-op when the member is already the logical active tab; still
        // re-syncs Group.ActiveIndex through the view model.
        _viewModel.SetActiveTab(member);
        if (changed)
        {
            _log.Log($"SPLIT[focus] guest=0x{member.Model.Hwnd.ToInt64():X}");
            DiagnosticRuntime.Record("split.focus", _containerHwnd, member.Model.Hwnd,
                group: Group.Id.ToString("N"), action: "focus", result: "logical-state-updated");
        }
        LayoutSplitPanes();
        // Give the clicked member REAL foreground after the panes are laid out
        // (strip clicks never raise a guest natively, so without this the
        // focused member stays behind the container's chrome and keystrokes
        // keep going to TabDock). SetForeground early-returns when the member
        // is already the foreground window, so direct pane clicks and repeat
        // clicks are no-ops and the reassert path is unaffected.
        _shepherd.SetForeground(member.Model);
    }

    /// <summary>
    /// Re-measures the currently visible guest(s)' effective native minimum
    /// track sizes (cached; never per-frame) and recomputes the container's
    /// minimum content size. Called before every relayout pass; re-measures only
    /// when the visible set / geometry changed (_constraintDirty) or a debounced
    /// periodic re-probe fired, so a dynamic native minimum (browser sidebar,
    /// toolbar) is respected without a resize war or per-frame probing.
    /// </summary>
    private void RefreshSizeConstraint()
    {
        if (WindowState == WindowState.Minimized)
            return;
        if (!_constraintDirty)
            return;

        // Determine the visible guest set and which minima map to LEFT/RIGHT.
        CapturedWindow? left = null, right = null;
        if (IsSplitPresented)
        {
            left = _splitController.Left;
            right = _splitController.Right;
        }
        else if (_shepherdActiveWindow != null && NativeMethods.IsWindow(_shepherdActiveWindow.Hwnd))
        {
            left = _shepherdActiveWindow;
            right = null;
        }

        // Guest minima are scaled by the monitor the guests are PRESENTED on
        // (this container's monitor), not by whichever monitor a guest last
        // sat on — mixed-DPI moves must not under/over-constrain the panes.
        IntPtr dpiTargetMonitor = _containerHwnd != IntPtr.Zero
            ? NativeMethods.MonitorFromWindow(_containerHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST)
            : IntPtr.Zero;
        int lw = 0, lh = 0, rw = 0, rh = 0;
        if (left != null)
        {
            var (mw, mh, ok) = _shepherd.GetEffectiveMinTrackSize(left, dpiTargetMonitor);
            if (ok) { lw = mw; lh = mh; }
        }
        if (right != null)
        {
            var (mw, mh, ok) = _shepherd.GetEffectiveMinTrackSize(right, dpiTargetMonitor);
            if (ok) { rw = mw; rh = mh; }
        }

        _constraintMinLeftW = lw;
        _constraintMinLeftH = lh;
        _constraintMinRightW = rw;
        _constraintMinRightH = rh;
        _constraintDirty = false;
    }

    /// <summary>
    /// Computes the container's minimum OUTER track size (physical pixels) from
    /// the cached guest minima. Content min comes from SplitGeometry's
    /// MinContentWidth/MinContentHeight (normal = the active guest's min; split =
    /// the exact partition's width/height); the outer size adds the chrome delta
    /// (current outer width/height minus the content rect). Returns false when it
    /// cannot be computed (no content rect, no guests) so the caller leaves the
    /// min track untouched.
    /// </summary>
    private bool ComputeContainerMinTrack(out int minTrackW, out int minTrackH)
    {
        minTrackW = 0;
        minTrackH = 0;
        if (!TryGetContentAreaScreenRect(out NativeMethods.RECT content))
            return false;
        if (content.Width <= 0 || content.Height <= 0)
            return false;

        bool split = IsSplitPresented;
        int contentMinW = SplitGeometry.MinContentWidth(split, _constraintMinLeftW, _constraintMinRightW);
        int contentMinH = SplitGeometry.MinContentHeight(split, _constraintMinLeftH, _constraintMinRightH);
        if (contentMinW <= 0 && contentMinH <= 0)
            return false;

        NativeMethods.RECT outer = new NativeMethods.RECT();
        if (_containerHwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(_containerHwnd, out outer))
            return false;
        int chromeW = Math.Max(0, outer.Width - content.Width);
        int chromeH = Math.Max(0, outer.Height - content.Height);
        minTrackW = contentMinW + chromeW;
        minTrackH = contentMinH + chromeH;
        return true;
    }

    /// <summary>Records that <paramref name="guest"/> refused <paramref name="rect"/> (bounded, one diagnostic per refusal).</summary>
    private void MarkRefusingPane(CapturedWindow guest, NativeMethods.RECT rect)
    {
        if (_refusedPaneByHwnd.TryGetValue(guest.Hwnd.ToInt64(), out NativeMethods.RECT prior)
            && PaneContainmentPolicy.IsExactSameRect(prior, rect))
            return; // already recorded this exact refusal
        _refusedPaneByHwnd[guest.Hwnd.ToInt64()] = rect;
        _log.Log($"SHEPHERD[size-constraint] guest=0x{guest.Hwnd.ToInt64():X} refused pane {rect.left},{rect.top},{rect.Width}x{rect.Height}; guest cannot fit the assigned pane (native minimum).");
    }

    /// <summary>Clears the refusal record for <paramref name="guest"/> (re-glue succeeded or rect changed).</summary>
    private void ClearRefusingPane(CapturedWindow guest)
        => _refusedPaneByHwnd.Remove(guest.Hwnd.ToInt64());

    /// <summary>
    /// True when <paramref name="guest"/>'s observed rect matches <paramref name="rect"/>
    /// within the 1px glue epsilon — used to confirm a re-glue actually took, which is
    /// the requested-vs-observed distinction this containment fix is built on.
    /// </summary>
    private static bool ObservedMatches(IntPtr hwnd, NativeMethods.RECT rect)
    {
        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
            return false;
        NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT guest);
        const int epsilon = 1;
        return Math.Abs(guest.left - rect.left) <= epsilon
            && Math.Abs(guest.top - rect.top) <= epsilon
            && Math.Abs(guest.right - rect.right) <= epsilon
            && Math.Abs(guest.bottom - rect.bottom) <= epsilon;
    }

    /// <summary>
    /// Positions and shows the active guest to exactly cover the content area,
    /// layered directly above this container. Called on tab switch, container
    /// move/resize/restore, and drag-out-threshold snap-back.
    /// </summary>

    private void LayoutShepherdActiveWindow(bool forceZOrder = false)
    {
        if (_shepherdActiveWindow == null)
            return;
        // Q10: If this is called while split is presented (stale callback,
        // re-entrancy), do nothing — split mode owns both panes. RelayoutGuests
        // gates this correctly, but direct callers must also be safe (Q7).
        if (IsSplitPresented)
            return;
        if (WindowState == WindowState.Minimized)
            return;
        // A guest's own minimize is handled by RestoreMinimizedWindow. Do not
        // let a concurrent container relayout re-show an iconic guest between
        // the guest's minimize and the bounded restore decision; tray-style
        // guests commonly follow minimize with Hide() in the same close path.
        if (NativeMethods.IsIconic(_shepherdActiveWindow.Hwnd))
            return;

        IntPtr containerHwnd = _containerHwnd;
        if (containerHwnd == IntPtr.Zero)
            return;

        // A plain container drag raises LocationChanged for every mouse tick
        // without dirtying anything (a move changes where the content marker is,
        // not how big it is), so only force a synchronous layout pass when one is
        // actually pending — an unconditional UpdateLayout made each of those
        // ticks re-enter WPF's layout manager for nothing.
        if (!IsMeasureValid || !IsArrangeValid)
            UpdateLayout();
        NativeMethods.RECT rect = GetContentAreaScreenRect();
        if (rect.Width == 0 || rect.Height == 0)
            return;
        // Guard against redundant re-glues: this method is also reached from
        // LayoutUpdated, which fires on every layout pass — including ones that
        // do not move or resize the content marker at all (e.g. tab-strip
        // reorders during a drag). Re-issuing PositionAndShow there would churn
        // the guest/container z-order in the middle of the gesture. Skip the
        // native calls when the guest ALREADY covers the target rect exactly
        // (within a 1px epsilon for rounding) AND is visible. The test is
        // "guest where it should be", not "target unchanged": NoteGuestMoveSize's
        // snap-back re-glues a guest dragged a few pixels away from an
        // otherwise-unchanged docked rect, and a tab switch targets a different
        // (previously hidden) guest at the same rect — both must still re-glue.
        const int epsilon = 1;
        if (NativeMethods.IsWindowVisible(_shepherdActiveWindow.Hwnd))
        {
            NativeMethods.GetWindowRect(_shepherdActiveWindow.Hwnd, out NativeMethods.RECT guest);
            if (Math.Abs(guest.left - rect.left) <= epsilon &&
                Math.Abs(guest.top - rect.top) <= epsilon &&
                Math.Abs(guest.right - rect.right) <= epsilon &&
                Math.Abs(guest.bottom - rect.bottom) <= epsilon)
            {
                if (forceZOrder)
                {
                    _shepherd.PositionAndShow(_shepherdActiveWindow, containerHwnd, rect);
                    return;
                }
                // Geometry alone cannot prove the guest is correctly
                // presented: a guest can cover its rect exactly while sitting
                // BELOW the container (the container's z-order raise at the
                // end of a native drag can land after the last per-frame glue;
                // the WM_EXITSIZEMOVE reconciliation and every later
                // rest-time relayout land here). While chrome is intentionally
                // raised above the guests (context menu, group/color menu,
                // capture panel, rename box) the container being above the
                // guest is BY DESIGN and the popup-close path reconciles the
                // stack — skip the repair for the whole chrome-active window.
                if (_chromePopupActive || IsContainerChromeInteractionActive())
                    return;
                // Verify the local pairing invariant before declaring the
                // guest glued: the container must sit BELOW the guest (the
                // shepherd's upward walk skipping invisible helper windows —
                // a strict adjacency probe would false-fail forever on
                // topmost guests in a separate z-order band and on hidden IME
                // helpers, and would reorder unrelated TabDock containers).
                // When the container is above the guest, pin it behind the
                // guest — one write, no geometry churn, idempotent once
                // healthy.
                if (!_shepherd.IsContainerBelowGuest(containerHwnd, _shepherdActiveWindow.Hwnd))
                    _shepherd.PairZOrderBehind(containerHwnd, _shepherdActiveWindow);
                return;
            }
        }
        // Bounded non-compliance guard: if this guest already refused this exact
        // pane rect (its native minimum exceeds the pane), do NOT re-fight it
        // every frame (that is a resize war). Keep the guest pinned above the
        // container so the docked look holds, and skip the geometry write. The
        // refusal clears when the rect changes (container grows) or the guest
        // becomes compliant, so a wider pane is re-glued normally. Only applies
        // while the guest is actually visible at that refused rect — a guest
        // hidden by container minimize must always get a fresh PositionAndShow
        // on restore, even if the restored rect matches a stale refusal,
        // otherwise it never becomes visible again (the refusal was recorded
        // for "do not re-fight a currently-docked guest", not "never show a
        // hidden one").
        if (_refusedPaneByHwnd.TryGetValue(_shepherdActiveWindow.Hwnd.ToInt64(), out NativeMethods.RECT refusedActive)
            && PaneContainmentPolicy.ShouldSuppressRepositioning(
                guestCurrentlyVisible: NativeMethods.IsWindowVisible(_shepherdActiveWindow.Hwnd),
                refusedRect: refusedActive,
                requestedRect: rect))
        {
            _shepherd.PairZOrderBehind(containerHwnd, _shepherdActiveWindow);
            return;
        }
        _shepherd.PositionAndShow(_shepherdActiveWindow, containerHwnd, rect);
        // Requested-vs-observed confirmation: PositionAndShow issues SetWindowPos
        // with the desired rect, but the guest may refuse it (native minimum). If
        // the observed rect still differs, mark the guest refusing so the next
        // pass does not repeat the write.
        if (!ObservedMatches(_shepherdActiveWindow.Hwnd, rect))
            MarkRefusingPane(_shepherdActiveWindow, rect);
        else
            ClearRefusingPane(_shepherdActiveWindow);
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
        TryGetContentAreaScreenRect(out NativeMethods.RECT rect);
        return rect;
    }

    private bool TryGetContentAreaScreenRect(out NativeMethods.RECT rect)
    {
        rect = new NativeMethods.RECT();
        IntPtr hostHwnd = ContentHost.HostWindowHandle;
        if (!NativeMethods.IsWindow(hostHwnd))
            return false;
        if (!NativeMethods.GetClientRect(hostHwnd, out NativeMethods.RECT rc)
            || rc.Width <= 0 || rc.Height <= 0)
        {
            return false;
        }

        var topLeft = new NativeMethods.POINT { x = 0, y = 0 };
        if (!NativeMethods.ClientToScreen(hostHwnd, ref topLeft))
            return false;

        rect = new NativeMethods.RECT
        {
            left = topLeft.x,
            top = topLeft.y,
            right = topLeft.x + rc.Width,
            bottom = topLeft.y + rc.Height,
        };
        return true;
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
        // Foreground/reorder WinEvents can arrive while an owned chrome dialog
        // (notably the close-group confirmation) is open. Re-pairing here would
        // raise a docked guest above that dialog and cover its buttons; the
        // popup-close path performs the authoritative reconciliation instead.
        if (IsContainerChromeInteractionActive())
            return;

        if (IsSplitPresented)
        {
            // A split member became the system foreground (e.g. the user clicked
            // it directly). Keep the container paired below BOTH guests: pin it
            // behind the OTHER member so the clicked one stays on top — and
            // record the clicked member as the focused one through the canonical
            // FocusSplitMember so the tab highlight and the 120ms reassert both
            // follow the direct click instead of reverting to the tab-active
            // member (which made initiator and partner behave asymmetrically).
            // FocusSplitMember re-glues both panes when not already glued and
            // otherwise only re-pins the container below the partner — the same
            // single SetWindowPos this branch used to issue.
            CapturedWindow? fg = _splitController.Left != null && _splitController.Left.Hwnd == foregroundHwnd ? _splitController.Left
                : _splitController.Right != null && _splitController.Right.Hwnd == foregroundHwnd ? _splitController.Right : null;
            if (fg == null)
                return;
            var fgTab = _viewModel.Tabs.FirstOrDefault(t => t.Model == fg);
            if (fgTab != null)
                FocusSplitMember(fgTab);
            return;
        }

        if (_shepherdActiveWindow == null || _shepherdActiveWindow.Hwnd != foregroundHwnd)
            return;

        IntPtr containerHwnd = _containerHwnd;
        if (containerHwnd == IntPtr.Zero)
            return;

        _shepherd.PairZOrderBehind(containerHwnd, _shepherdActiveWindow);
    }

    #endregion

    /// <summary>
    /// Returns true when <paramref name="upper"/> occurs anywhere above
    /// <paramref name="lower"/> in the top-level z-order. The split compositor
    /// cares about relative order, not strict adjacency: IME, accessibility,
    /// overlay and shell helper HWNDs can legally sit between two TabDock guests.
    /// </summary>
    private static bool IsWindowAbove(IntPtr upper, IntPtr lower)
        => ZOrder.IsOrderedAbove(upper, lower, h => NativeMethods.GetWindow(h, NativeMethods.GW_HWNDNEXT));

    #region Split screen

    /// <summary>True while the runtime split relationship has two valid members.</summary>
    private bool IsSplitRelationshipDefined => _splitController.IsRelationshipDefined;

    /// <summary>True while the defined pair is the current two-pane presentation.</summary>
    private bool IsSplitPresented => _splitController.IsPresented;

    private void InvalidateSplitPresentationSettle()
    {
        // Generation now lives in SplitPresentationController and is bumped by
        // its transition methods; the container only owns the settle arming.
        DisarmSplitPresentationSettle();
    }

    private bool IsSplitMember(CapturedWindow? window)
        => _splitController.IsMember(window);

    /// <summary>
    /// True if <paramref name="window"/> is one of the two currently-visible
    /// split members. Consulted by GuestLifecycleService to decide whether a
    /// hide of a non-active member is guest-initiated (in split mode both
    /// members are visible, so a hide of either is a self-hide, not a
    /// TabDock tab-switch hide).
    /// </summary>
    public bool IsInSplit(CapturedWindow window)
        => IsSplitPresented && IsSplitMember(window);

    /// <summary>
    /// Splits the full content rect into LEFT/RIGHT pane rects in physical
    /// pixels. Integer division: the left pane gets <c>Width/2</c>, the right
    /// pane gets the remainder (the extra pixel on odd widths), so the two
    /// panes abut exactly with no overlap and no gap. No DPI conversion — the
    /// caller's rect is already in device pixels.
    /// </summary>
    private static (NativeMethods.RECT Left, NativeMethods.RECT Right) SplitRect(NativeMethods.RECT content)
        => SplitGeometry.Partition(content);

    private NativeMethods.RECT SplitPaneRect(CapturedWindow member)
    {
        NativeMethods.RECT content = GetContentAreaScreenRect();
        var (left, right) = SplitRect(content);
        return ReferenceEquals(member, _splitController.Left) ? left : right;
    }

    /// <summary>
    /// True when the guest does not yet cover <paramref name="rect"/> (within
    /// the same 1px epsilon LayoutShepherdActiveWindow uses) or is not visible,
    /// i.e. it needs a native re-glue. Per-pane analogue of the single-guest
    /// redundant-glue guard.
    /// </summary>
    private static bool NeedsPanePosition(CapturedWindow member, NativeMethods.RECT rect)
    {
        if (!NativeMethods.IsWindow(member.Hwnd))
            return false; // cannot position a dead window
        const int epsilon = 1;
        if (NativeMethods.IsWindowVisible(member.Hwnd))
        {
            NativeMethods.GetWindowRect(member.Hwnd, out NativeMethods.RECT guest);
            if (Math.Abs(guest.left - rect.left) <= epsilon &&
                Math.Abs(guest.top - rect.top) <= epsilon &&
                Math.Abs(guest.right - rect.right) <= epsilon &&
                Math.Abs(guest.bottom - rect.bottom) <= epsilon)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Positions and shows both split guests over their panes and pins the
    /// container strictly BELOW both. This is the one split positioning policy
    /// (spec: no two conflicting z-order loops). The foreground member is kept
    /// on top; the partner is chained between it and the container; the
    /// container is pinned behind the lower (partner) guest. Both guests are
    /// positioned each time the pair is not already exactly glued, which keeps
    /// the z-order deterministic regardless of prior state.
    /// </summary>
    private void LayoutSplitPanes()
    {
        if (!IsSplitPresented)
            return;
        // Q7/Q10: If split members are unexpectedly null despite IsSplitPresented
        // (teardown race), skip gracefully rather than throwing.
        if (_splitController.Left == null || _splitController.Right == null)
            return;
        if (_guestMoveSizeActive)
            return;
        if (WindowState == WindowState.Minimized)
            return;
        // While container chrome is raised, including the owned close-group
        // confirmation dialog, leave the intentional popup z-order untouched.
        // EndChromePopup/close-prompt teardown schedules the normal glue pass.
        if (IsContainerChromeInteractionActive())
            return;

        IntPtr containerHwnd = _containerHwnd;
        if (containerHwnd == IntPtr.Zero)
            return;

        // Content rect unavailable or zero-sized (host not yet created,
        // minimized marker) — nothing to lay out (Q10). Early return avoids
        // a layout fight/oscillation from positioning with a stale rect.
        if (!TryGetContentAreaScreenRect(out NativeMethods.RECT content) || content.Width == 0 || content.Height == 0)
            return;
        var (leftRect, rightRect) = SplitRect(content);

        CapturedWindow top = _splitController.Foreground ?? _splitController.Right!;
        if (!ReferenceEquals(top, _splitController.Left) && !ReferenceEquals(top, _splitController.Right))
            top = _splitController.Right!;
        CapturedWindow bottom = ReferenceEquals(top, _splitController.Left) ? _splitController.Right! : _splitController.Left!;

        NativeMethods.RECT topRect = ReferenceEquals(top, _splitController.Left) ? leftRect : rightRect;
        NativeMethods.RECT bottomRect = ReferenceEquals(bottom, _splitController.Left) ? leftRect : rightRect;

        if (!NeedsPanePosition(top, topRect) && !NeedsPanePosition(bottom, bottomRect))
        {
            // Both guests already cover their panes exactly. Do NOT re-position
            // them (that would churn z-order mid-gesture), but DO re-assert the
            // "container below both" invariant: activating the container (e.g.
            // the tab-strip click or context-menu click that entered the split,
            // or an alt-tab back to it) raises the container above both guests,
            // and the guards above would otherwise leave it there — clicking a
            // pane would then hit the container's content area instead of the
            // guest. Pushing the container to the bottom of the z-order restores
            // the invariant cheaply.
            //
            // The cheap pin alone is NOT enough when the focused member changed
            // since the last glue (composite half-click, Ctrl+Tab, tab-strip
            // echo — none of which raise a guest natively): the local stack was
            // [oldTop, partner, container], and pinning the container below the
            // NEW bottom (the old top) wedges the container BETWEEN the panes,
            // leaving the newly focused member BELOW the container's opaque
            // content area — the "one pane fails to render after clicking the
            // partner" defect. Verify the pair's internal order first
            // (GetWindow(GW_HWNDNEXT) = the window below the top pane); only
            // when [top, bottom, container] already holds is the pin sufficient.
            if (!IsWindowAbove(top.Hwnd, bottom.Hwnd))
            {
                _shepherd.PositionGuestsDeferred(top, topRect, bottom, bottomRect, containerHwnd);
                return;
            }
            _shepherd.PairZOrderBehind(containerHwnd, bottom);
            return;
        }

        // Establish the local stack in one order: top guest, partner guest,
        // container. SetWindowPos places the target BELOW hWndInsertAfter, so
        // the foreground member is raised first, the partner is inserted below
        // it, and the container is then inserted below the partner. Keeping the
        // policy local avoids pushing TabDock below unrelated desktop windows.
        //
        // Bounded non-compliance guard: if a member already refused its current
        // pane rect (its native minimum exceeds the pane), do NOT re-fight it
        // every frame (resize war). Pin the container below the panes so the
        // docked look holds, and skip the geometry write. Refusals clear when
        // the rect changes or the guest becomes compliant, so a wider pane is
        // re-glued normally. Only applies while the member is actually visible
        // at that refused rect — members hidden by container minimize must
        // always get a fresh PositionGuestsDeferred on restore, even when the
        // restored panes match a stale refusal, otherwise they never become
        // visible again.
        bool topSuppressed = _refusedPaneByHwnd.TryGetValue(top.Hwnd.ToInt64(), out NativeMethods.RECT topRefused)
            && PaneContainmentPolicy.ShouldSuppressRepositioning(
                guestCurrentlyVisible: NativeMethods.IsWindowVisible(top.Hwnd),
                refusedRect: topRefused,
                requestedRect: topRect);
        bool bottomSuppressed = _refusedPaneByHwnd.TryGetValue(bottom.Hwnd.ToInt64(), out NativeMethods.RECT bottomRefused)
            && PaneContainmentPolicy.ShouldSuppressRepositioning(
                guestCurrentlyVisible: NativeMethods.IsWindowVisible(bottom.Hwnd),
                refusedRect: bottomRefused,
                requestedRect: bottomRect);
        if (topSuppressed || bottomSuppressed)
        {
            _shepherd.PairZOrderBehind(containerHwnd, bottom);
            return;
        }
        _shepherd.PositionGuestsDeferred(top, topRect, bottom, bottomRect, containerHwnd);
        // Requested-vs-observed confirmation: PositionGuestsDeferred issues the
        // desired pane rects, but a guest may refuse (native minimum). Mark any
        // member whose observed rect still differs so the next pass skips it.
        if (!ObservedMatches(top.Hwnd, topRect))
            MarkRefusingPane(top, topRect);
        else
            ClearRefusingPane(top);
        if (!ObservedMatches(bottom.Hwnd, bottomRect))
            MarkRefusingPane(bottom, bottomRect);
        else
            ClearRefusingPane(bottom);
    }

    /// <summary>
    /// Enters split mode with <paramref name="left"/> in the left pane and
    /// <paramref name="right"/> in the right pane. If a split is already active
    /// it is replaced first (any departing members are hidden journal-safely).
    /// The initiating tab (left) becomes the active/focused member.
    /// </summary>
    private void EnterSplit(CapturedWindow left, CapturedWindow right)
    {
        if (left == null || right == null || ReferenceEquals(left, right))
            return;
        // Use strong identity (token+PID+class), not bare IsWindow, so a recycled
        // HWND that happens to be live does not enter split as the wrong window.
        if (!_shepherd.IsCurrentCapturedWindow(left) || !_shepherd.IsCurrentCapturedWindow(right))
            return;

        // Remember the previously visible guest so it can be hidden if it is not
        // part of the new pair (a pre-split single active tab, or a member of a
        // replaced split pair).
        CapturedWindow? priorVisible = _shepherdActiveWindow;

        // Hide the guest that was visible before the split if it is not one of
        // the new pair before changing split/model state. A pending hide must
        // leave the old presentation authoritative instead of creating a
        // third visible guest beside the new pair.
        if (priorVisible != null && priorVisible != left && priorVisible != right
            && _viewModel.Tabs.Any(t => t.Model == priorVisible))
        {
            WindowHideOutcome hideOutcome = _shepherd.Hide(priorVisible);
            LogHidePending(priorVisible, hideOutcome);
            if (hideOutcome == WindowHideOutcome.RecoveryPending)
                return;
        }

        // DefinePair is the runtime authority for split membership/presented/
        // foreground/generation; it also hides departing members when replacing
        // an existing pair (the sole native hide for the replace path). A
        // RecoveryPending hide commits nothing, so the old presentation stays
        // authoritative and must be re-presented instead of building the new
        // pair beside a half-hidden old one.
        SplitTransitionResult define = _splitController.DefinePair(left, right, left);
        if (!define.Committed)
        {
            _log.Log($"SPLIT[enter-blocked] outcome={define.Native} left=0x{left.Hwnd.ToInt64():X} right=0x{right.Hwnd.ToInt64():X}; prior presentation retained.");
            DiagnosticRuntime.Record("split.enter", _containerHwnd, left.Hwnd,
                group: Group.Id.ToString("N"), action: "enter",
                result: define.Native == SplitNativeTransitionOutcome.RecoveryPending
                    ? "recovery-pending-prior-retained"
                    : "rejected");
            if (IsSplitPresented)
                LayoutSplitPanes();
            else
                LayoutShepherdActiveWindow();
            return;
        }
        InvalidateSplitPresentationSettle();
        // The visible set changed: recompute the container's minimum size from
        // the new pair's native minima, and clear refusals (fresh panes).
        _constraintDirty = true;
        _refusedPaneByHwnd.Clear();

        var leftTab = _viewModel.Tabs.FirstOrDefault(t => t.Model == left);
        if (leftTab != null)
        {
            _shepherdActiveWindow = left;
            _viewModel.SetActiveTab(leftTab);
        }

        // Present the pair as one composite tab-strip item ([ A | B ]); the
        // RIGHT member's ordinary tab is suppressed while the pair exists.
        var rightTab = _viewModel.Tabs.FirstOrDefault(t => t.Model == right);
        if (leftTab != null && rightTab != null)
            _viewModel.SetSplitComposite(leftTab, rightTab);

        _log.Log($"SPLIT[enter] left=0x{left.Hwnd.ToInt64():X} right=0x{right.Hwnd.ToInt64():X}");
        DiagnosticRuntime.Record("split.enter", _containerHwnd, left.Hwnd,
            group: Group.Id.ToString("N"), action: "enter",
            result: "logical-state-updated",
            data: new Dictionary<string, string> { ["rightGuest"] = DiagnosticEnvironmentService.FormatHwnd(right.Hwnd) });
        LayoutSplitPanes();
    }

    /// <summary>Restores the unchanged pair and focuses the requested member.</summary>
    private void ResumeSplitPair(TabViewModel focusedTab)
    {
        CapturedWindow focused = focusedTab.Model;
        if (!IsSplitRelationshipDefined || !IsSplitMember(focused))
            return;

        CapturedWindow? current = _shepherdActiveWindow;
        if (current != null && !IsSplitMember(current)
            && NativeMethods.IsWindowVisible(current.Hwnd))
        {
            WindowHideOutcome outcome = _shepherd.Hide(current);
            LogHidePending(current, outcome);
            if (outcome == WindowHideOutcome.RecoveryPending)
            {
                // The ListBox selection has already moved to the requested
                // member by the time ActiveTab change reaches this method.
                // Keep the dormant single-guest presentation authoritative
                // until the current guest can be journal-safely hidden; do not
                // leave the logical member selected over a still-visible C.
                TabViewModel? currentTab = _viewModel.Tabs.FirstOrDefault(t => ReferenceEquals(t.Model, current));
                if (currentTab != null && !ReferenceEquals(_viewModel.ActiveTab, currentTab))
                    _viewModel.SetActiveTab(currentTab);
                DiagnosticRuntime.Record("split.resume", _containerHwnd, current.Hwnd,
                    group: Group.Id.ToString("N"), action: "single-to-pair", result: "recovery-pending");
                return;
            }
        }

        // ResumeMember is the runtime authority for presented/foreground/
        // generation; the container keeps its settle arming and the active-window
        // sync (the single guest that was on top before the split re-presented).
        _splitController.ResumeMember(focused);
        DisarmSplitPresentationSettle();
        _shepherdActiveWindow = focused;
        _constraintDirty = true;
        _refusedPaneByHwnd.Clear();
        _viewModel.SetActiveTab(focusedTab);
        LayoutSplitPanes();
        // Resuming from a composite-half click does not pass through the
        // presented-pair FocusSplitMember branch, so explicitly perform the
        // same identity-checked foreground request after both panes are back.
        _shepherd.SetForeground(focused);
        _log.Log($"SPLIT[resume] left=0x{_splitController.Left!.Hwnd.ToInt64():X} right=0x{_splitController.Right!.Hwnd.ToInt64():X} focused=0x{focused.Hwnd.ToInt64():X}");
        DiagnosticRuntime.Record("split.resume", _containerHwnd, focused.Hwnd,
            group: Group.Id.ToString("N"), action: "single-to-pair", result: "pair-restored");
    }

    /// <summary>
    /// Leaves split mode and returns to normal one-visible-guest behavior. The
    /// surviving member (<paramref name="keepActive"/>, or the current active
    /// member if it is part of the pair, else the left member) becomes the
    /// single visible guest at full content width; departing members are hidden
    /// through the journal-safe path. No guest is released; membership and tab
    /// order are preserved.
    /// </summary>
    private void ExitSplit(CapturedWindow? keepActive = null)
    {
        if (!IsSplitRelationshipDefined)
            return;

        var oldLeft = _splitController.Left;
        var oldRight = _splitController.Right;

        // A dormant relationship has an unrelated active guest. Clearing it
        // must not promote either former member or hide the current guest.
        if (!IsSplitPresented)
        {
            CapturedWindow? current = _shepherdActiveWindow;
            // Hide only still-visible former members (a dormant pair's members
            // are already hidden by the suspend that created it, so this is
            // normally a no-op). Native hide + journaling is preserved exactly;
            // the controller is the runtime authority for split state, cleared
            // below without re-hiding already-hidden members.
            foreach (CapturedWindow? member in new[] { oldLeft, oldRight })
            {
                if (member == null || !NativeMethods.IsWindow(member.Hwnd)
                    || !NativeMethods.IsWindowVisible(member.Hwnd))
                    continue;
                WindowHideOutcome hideOutcome = _shepherd.Hide(member);
                LogHidePending(member, hideOutcome);
                if (hideOutcome == WindowHideOutcome.RecoveryPending)
                {
                    // Preserve the dormant relationship and current guest until
                    // the visible former member can be hidden safely.
                    LayoutShepherdActiveWindow();
                    return;
                }
            }
            _splitController.HandleMemberRemoved(_splitController.Left!);
            DisarmSplitPresentationSettle();
            _constraintDirty = true;
            _refusedPaneByHwnd.Clear();
            _viewModel.ClearSplitComposite();
            _log.Log("SPLIT[exit] dormant pair cleared");
            DiagnosticRuntime.Record("split.exit", _containerHwnd, current?.Hwnd ?? IntPtr.Zero,
                group: Group.Id.ToString("N"), action: "exit-dormant", result: "single-guest-retained");
            if (current != null && _viewModel.Tabs.Any(t => ReferenceEquals(t.Model, current)))
                LayoutShepherdActiveWindow();
            return;
        }

        // Decide the survivor against the still-intact pair, BEFORE clearing it.
        CapturedWindow? survivor = (keepActive != null && _viewModel.Tabs.Any(t => t.Model == keepActive))
            ? keepActive
            : (IsSplitMember(_shepherdActiveWindow) ? _shepherdActiveWindow : oldLeft);

        // Hide every departing member (the non-survivor) while the split model
        // is still intact; the survivor is kept visible for promotion below.
        // Native hide + journaling is preserved exactly.
        foreach (var m in new[] { oldLeft, oldRight })
        {
            if (m != null && m != survivor && _viewModel.Tabs.Any(t => t.Model == m))
            {
                WindowHideOutcome hideOutcome = _shepherd.Hide(m);
                LogHidePending(m, hideOutcome);
                if (hideOutcome == WindowHideOutcome.RecoveryPending)
                    return;
            }
        }
        // Controller is the runtime authority for split state; commit the pure
        // policy exit (survivor becomes the ordinary active guest) without
        // re-hiding — the departing member was just hidden above.
        _splitController.CommitExplicitExit(survivor);
        DisarmSplitPresentationSettle();
        // Back to single-guest mode: refresh the constraint and clear refusals.
        _constraintDirty = true;
        _refusedPaneByHwnd.Clear();

        // Restore the ordinary one-tab-per-member strip.
        _viewModel.ClearSplitComposite();

        _log.Log("SPLIT[exit]");
        DiagnosticRuntime.Record("split.exit", _containerHwnd, survivor?.Hwnd ?? IntPtr.Zero,
            group: Group.Id.ToString("N"), action: "exit", result: "logical-state-updated");
        if (survivor != null)
        {
            var survivorTab = _viewModel.Tabs.FirstOrDefault(t => t.Model == survivor);
            if (survivorTab != null)
            {
                _shepherdActiveWindow = survivor;
                _viewModel.SetActiveTab(survivorTab);
                LayoutShepherdActiveWindow();
            }
        }
    }

    /// <summary>
    /// Starts a split from the initiating (right-clicked) tab, which becomes the
    /// LEFT pane. For exactly two tabs the sole other tab is auto-selected; for
    /// three or more the caller supplies <paramref name="chosenRight"/>.
    /// </summary>
    private void StartSplitFrom(TabViewModel leftTab, TabViewModel? chosenRight = null)
    {
        if (leftTab == null)
            return;
        // Re-validate against the CURRENT tab set: the context menu snapshots
        // the initiating tab at menu-open time, and the guest may have died or
        // been popped out in the ~200ms before the user clicks the split item.
        // A stale initiator would otherwise enter split with a released window
        // as LEFT: no composite gets installed and the released guest keeps
        // being glued into the left pane (adversarial review R1-F3).
        if (!_viewModel.Tabs.Contains(leftTab))
            return;
        if (chosenRight != null && !_viewModel.Tabs.Contains(chosenRight))
            return;
        CapturedWindow left = leftTab.Model;
        TabViewModel? rightTab = chosenRight
            ?? _viewModel.Tabs.FirstOrDefault(t => !ReferenceEquals(t, leftTab));
        if (rightTab == null)
            return; // fewer than 2 eligible tabs — the menu should be disabled
        EnterSplit(left, rightTab.Model);
    }

    /// <summary>
    /// Called when a split member leaves the group by any route (pop-out,
    /// drag-out, self-close, self-hide, group close). Ends split mode and
    /// promotes the surviving member to normal single visible guest. The
    /// departing member was already released/hidden by the removal path.
    /// </summary>
    private void HandleSplitMemberRemoved(CapturedWindow removed)
    {
        if (!IsSplitRelationshipDefined)
            return;
        bool wasPresented = IsSplitPresented;
        CapturedWindow? current = _shepherdActiveWindow;
        _log.Log($"SPLIT[member-gone] member=0x{removed.Hwnd.ToInt64():X} left split");
        // HandleMemberRemoved is the runtime authority: it clears the
        // relationship (no native hide — the departing member was already
        // released/hidden by the removal path) and returns the survivor.
        CapturedWindow? survivor = _splitController.HandleMemberRemoved(removed);
        DisarmSplitPresentationSettle();
        // Back to single-guest mode: refresh the constraint and clear refusals.
        _constraintDirty = true;
        _refusedPaneByHwnd.Clear();

        // Restore the ordinary one-tab-per-member strip.
        _viewModel.ClearSplitComposite();

        if (!wasPresented)
        {
            if (current != null && _viewModel.Tabs.Any(t => ReferenceEquals(t.Model, current)))
            {
                _shepherdActiveWindow = current;
                LayoutShepherdActiveWindow();
            }
            return;
        }

        if (survivor != null && _viewModel.Tabs.Any(t => t.Model == survivor))
        {
            var survivorTab = _viewModel.Tabs.First(t => t.Model == survivor);
            _shepherdActiveWindow = survivor;
            _viewModel.SetActiveTab(survivorTab);
            LayoutShepherdActiveWindow();
        }
    }

    private void Tabs_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Only a genuine REMOVE ends a member's membership. A Move (reorder)
        // also carries OldItems but must NOT be mistaken for a removal — doing
        // so tore down the split on every tab reorder. Reset (Clear) is handled
        // by container close / group teardown, not here.
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            return;
        if (IsSplitRelationshipDefined)
        {
            foreach (var item in e.OldItems!)
            {
                if (item is TabViewModel tvm && IsSplitMember(tvm.Model))
                    HandleSplitMemberRemoved(tvm.Model);
            }
        }
    }

    #endregion

    /// <summary>
    /// Restores a captured window that minimized itself (e.g. via the guest app's
    /// own custom-drawn minimize button or an in-app shortcut). A captured child
    /// has no taskbar presence, so leaving it iconic shows a black content area
    /// with no way to bring it back. Only the active tab is restored eagerly;
    /// inactive tabs are restored when they are next activated. In split mode a
    /// minimizing member is restored inside its own pane.
    /// </summary>
    public void RestoreMinimizedWindow(CapturedWindow window)
    {
        // In split mode either visible member may be restored inside its pane;
        // otherwise only the active tab is restored eagerly.
        if (IsSplitPresented)
        {
            if (!IsSplitMember(window))
                return;
        }
        else if (_viewModel.ActiveTab?.Model != window)
        {
            return;
        }
        // Strong identity: recycled HWND that happens to be iconic must not restore the wrong window.
        if (!_shepherd.IsCurrentCapturedWindow(window) || !NativeMethods.IsIconic(window.Hwnd))
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
        _restoreMinimizedTimer?.Stop();
        var restoreTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        restoreTimer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_restoreMinimizedTimer, restoreTimer))
            {
                restoreTimer.Stop();
                return;
            }

            restoreTimer.Stop();
            _restoreMinimizedTimer = null;

            // The tab can also be released inside this same 200ms window — popped
            // out, tray-closed, or torn down along with the whole container. By
            // then Release has already restored the guest to its capture-time
            // placement, which is legitimately a MINIMIZED one for a window that
            // was captured while iconic, so an unguarded SW_RESTORE here silently
            // undoes it. The active-tab check alone is not enough: a closed
            // container keeps its view model intact, so also require the shepherd
            // sync to still own this guest (ContainerWindow_Closed nulls it).
            if (IsSplitPresented)
            {
                if (!IsSplitMember(window))
                    return;
            }
            else if (_shepherdActiveWindow != window || _viewModel.ActiveTab?.Model != window)
            {
                return;
            }
            // Re-validate strong identity at tick time — the 200ms defer outlives HWND recycling.
            if (!_shepherd.IsCurrentCapturedWindow(window)
                || !NativeMethods.IsIconic(window.Hwnd)
                || !NativeMethods.IsWindowVisible(window.Hwnd))
                return;

            if (!_shepherd.RestoreMinimized(window))
                return;
            if (IsSplitPresented)
                LayoutSplitPanes();
            else
                LayoutShepherdActiveWindow();
        };
        _restoreMinimizedTimer = restoreTimer;
        restoreTimer.Start();
    }

    /// <summary>
    /// Tracks a captured guest's interactive move/size modal loop. Shepherded
    /// guests remain independent top-level HWNDs, so Windows can start a native
    /// move/size loop on their real frame. That interaction is not a pop-out
    /// gesture: while captured, TabDock owns the geometry and re-glues the guest
    /// to its pane when the native loop ends. Explicit Pop out remains available
    /// through the tab UI.
    /// </summary>
    public void NoteGuestMoveSize(CapturedWindow window, bool started)
    {
        if (started)
        {
            _guestMoveSizeActive = true;
            _guestMoveSizeGeneration++;
            DiagnosticRuntime.Record("guest.movesize.start", _containerHwnd, window.Hwnd,
                group: Group.Id.ToString("N"), action: "observe", result: "callback-received");
            return;
        }
        _guestMoveSizeActive = false;
        long finalGeneration = ++_guestMoveSizeGeneration;

        // In split mode either visible member may be dragged out by its own real
        // title bar; otherwise only the active tab is tracked.
        if (IsSplitPresented)
        {
            if (!IsSplitMember(window))
                return;
        }
        else if (_viewModel.ActiveTab?.Model != window)
        {
            return;
        }
        // Strong identity before any geometry read — recycled HWND must not be measured as this member.
        if (!_shepherd.IsCurrentCapturedWindow(window) || !NativeMethods.IsWindowVisible(window.Hwnd)
            || NativeMethods.IsIconic(window.Hwnd) || NativeMethods.IsZoomed(window.Hwnd))
            return;

        _constraintDirty = true;
        _refusedPaneByHwnd.Clear();

        // Measure against the member's OWN pane rect in split mode so the
        // re-glue path is deterministic even when the other pane is foreground.
        NativeMethods.RECT docked = IsSplitPresented ? SplitPaneRect(window) : GetContentAreaScreenRect();
        NativeMethods.GetWindowRect(window.Hwnd, out NativeMethods.RECT guest);
        bool moved = guest.left != docked.left || guest.top != docked.top
            || guest.right != docked.right || guest.bottom != docked.bottom;
        if (moved)
            _log.Log($"SHEPHERD[re-glue] guest=0x{window.Hwnd.ToInt64():X} native move/size ended outside assigned pane; restoring.");

        if (IsSplitPresented)
            LayoutSplitPanes();
        else
            LayoutShepherdActiveWindow(forceZOrder: true);

        // WinEvent dispatch is already posted to the WPF thread, but USER32 can
        // finish the final drag normalization after the movesize-end callback's
        // first synchronous layout. One bounded render-priority pass observes
        // that final native state without a blind sleep or a timer. The
        // generation and member checks prevent a released/recycled HWND from
        // receiving stale repair work.
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            // Strong identity at render time — a recycled HWND that happens to
            // be live/visible must not be re-glued as the old guest.
            if (finalGeneration != _guestMoveSizeGeneration
                || _containerHwnd == IntPtr.Zero
                || _guestMoveSizeActive
                || !_shepherd.IsCurrentCapturedWindow(window)
                || !NativeMethods.IsWindowVisible(window.Hwnd))
                return;

            if (IsSplitPresented && IsSplitMember(window))
            {
                _constraintDirty = true;
                _refusedPaneByHwnd.Clear();
                LayoutSplitPanes();
            }
            else if (!IsSplitPresented && ReferenceEquals(_shepherdActiveWindow, window))
            {
                LayoutShepherdActiveWindow(forceZOrder: true);
            }
        }));

        NativeMethods.GetWindowRect(window.Hwnd, out NativeMethods.RECT after);
        DiagnosticRuntime.Record("guest.movesize.end", _containerHwnd, window.Hwnd,
            group: Group.Id.ToString("N"), action: moved ? "re-glue" : "observe",
            result: "dispatched",
            data: new Dictionary<string, string>
            {
                ["assignedPane"] = $"{docked.left},{docked.top},{docked.Width}x{docked.Height}",
                ["guestBefore"] = $"{guest.left},{guest.top},{guest.Width}x{guest.Height}",
                ["guestAfter"] = $"{after.left},{after.top},{after.Width}x{after.Height}",
                ["reGlueRequested"] = moved.ToString(),
            });
    }

    /// <summary>
    /// Refreshes the title of the one tab whose guest was renamed. A name
    /// change concerns exactly one member, so invalidating every tab's Title
    /// binding (as the previous RefreshTabTitles did) made WPF re-read and
    /// re-measure every tab in the strip for each rename — and guests that
    /// mirror document content into their caption rename constantly.
    /// </summary>
    public void RefreshTabTitle(CapturedWindow window)
    {
        foreach (var tab in _viewModel.Tabs)
        {
            if (tab.Model == window)
            {
                tab.RefreshTitle();
                return;
            }
        }
    }

    #region Drag reorder / drag-out release

    private void TabsListBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;

        ListBoxItem? item = FindListBoxItem(e.OriginalSource);
        if (item?.DataContext is not TabViewModel tab)
            return;

        // Middle-click is a browser-style Pop out. Handle it at the tab strip so
        // it cannot become a left-drag/reorder gesture or a guest close request.
        e.Handled = true;
        EndDrag();
        _viewModel.ReleaseTab(tab);
    }

    private void TabsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggedItem = FindListBoxItem(e.OriginalSource);
        if (_draggedItem == null)
            return;
        // During split the strip holds a composite item that does not map
        // one-to-one to Tabs indices, so disable strip dragging (reorder /
        // drag-out) for the duration of the split to avoid index mismatches
        // (documented in the goal: the composite is not draggable as a unit in
        // this pass). The composite halves handle their own clicks/middle-clicks;
        // the window-level split interaction hook owns non-member presentation
        // switches while the pair is presented.
        if (IsSplitPresented)
        {
            // A NON-member tab's ordinary left-click must NOT select it while
            // the split pair is the selected tab-strip unit: selection would
            // activate the non-member, and the revert (which re-activates the
            // focused member) would then fight the ListBox's IsSelected<->IsActive
            // TwoWay binding — a re-entrant SelectionChanged<->SetActiveTab
            // ping-pong that overflows the stack. Swallow the click entirely
            // (the pair and the visible set stay untouched). The tab's own ×
            // button is excluded: popping the member out is a structural
            // operation and must keep working.
            if (_draggedItem.DataContext is TabViewModel)
            {
                for (DependencyObject? cur = e.OriginalSource as DependencyObject; cur != null; cur = VisualTreeHelper.GetParent(cur))
                {
                    if (cur is Button)
                        return;
                }
                e.Handled = true;
            }
            _draggedItem = null;
            return;
        }

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
        bool committedReorder = _isDragging;
        Mouse.Capture(null);
        _draggedTab = null;
        _draggedItem = null;
        _isDragging = false;
        _dragMidpoints = null;
        _dragMidpointsCount = 0;
        _dragMidpointsValid = false;
        if (committedReorder)
            _viewModel.CommitReorder();
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

    /// <summary>
    /// Production delegate from the tested <see cref="SplitPresentationController"/>
    /// seams to the real <see cref="WindowShepherdService"/>. The controller owns
    /// split membership/presented/foreground/generation and calls these to
    /// perform the native hide/show/position/foreground transitions, so there is
    /// exactly one runtime authority for split state.
    /// </summary>
    private sealed class ShepherdPresentationOps : IPresentationOperations
    {
        private readonly ContainerWindow _owner;
        public ShepherdPresentationOps(ContainerWindow owner) => _owner = owner;

        public WindowHideOutcome Hide(CapturedWindow window)
            => _owner._shepherd.Hide(window);

        public void PositionAndShow(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect)
            => _owner._shepherd.PositionAndShow(window, containerHwnd, screenRect);

        public void PositionGuestsDeferred(CapturedWindow top, NativeMethods.RECT topRect, CapturedWindow bottom, NativeMethods.RECT bottomRect, IntPtr containerHwnd)
            => _owner._shepherd.PositionGuestsDeferred(top, topRect, bottom, bottomRect, containerHwnd);

        public void SetForeground(CapturedWindow window)
            => _owner._shepherd.SetForeground(window);

        public void PairZOrderBehind(IntPtr containerHwnd, CapturedWindow guest)
            => _owner._shepherd.PairZOrderBehind(containerHwnd, guest);

        public bool IsCurrentCapturedWindow(CapturedWindow window)
            => _owner._shepherd.IsCurrentCapturedWindow(window);
    }
}
