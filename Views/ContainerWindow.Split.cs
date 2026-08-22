using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;

namespace TabDock.Views;

/// <summary>
/// The split-presentation concern of <see cref="ContainerWindow"/>: everything
/// above the ordinary tab selection/drag handlers that keeps the split
/// composite a persistent relationship while the user interacts with either
/// member or merely hovers/right-clicks another tab. An explicit LEFT click on
/// an ordinary non-member suspends the pair and presents that tab normally.
///
/// Concerns owned here, in order:
/// 1. Lifecycle hooks — split-interaction wiring plus settle disarm (below).
/// 2. Non-member activation — the third-tab click that suspends a presented
///    pair through SplitInteractionPolicy + SuspendForGuest.
/// 3. Post-popup presentation settle — the CompositionTarget one-shot that
///    re-glues and re-foregrounds the pair after chrome teardown.
///
/// All state decisions live in SplitPresentationController /
/// SplitInteractionPolicy / SplitPresentationPolicy; this partial only owns
/// WPF wiring for them. It does not touch the Shepherd/no-reparent native
/// mutation core.
/// </summary>
public partial class ContainerWindow
{
    private bool _splitInteractionHooksAttached;

    // ------------------------------------------------------------------
    // 1. Lifecycle hooks.
    //
    // Settle teardown is deliberately reachable from TWO sites: OnClosed here
    // (this file's own hooks) and ContainerWindow_Closed in the main partial
    // (which must not depend on partial OnClosed ordering). Both funnel into
    // the ONE idempotent DisarmSplitPresentationSettle helper — there is no
    // second unsubscribe path for CompositionTarget.Rendering anywhere.
    // ------------------------------------------------------------------

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_splitInteractionHooksAttached)
            return;

        // This handler is wired in XAML, so it is registered during
        // InitializeComponent BEFORE the ordinary drag/selection guard that
        // ContainerWindow.xaml.cs subscribes in code afterwards. WPF invokes
        // instance handlers on the same element in subscription order, so this
        // handler sees the preview event first and owns pair -> C/D activation
        // via its own hit-test; it is not a handledEventsToo recovery path.
        _viewModel.DisplayTabs.CollectionChanged += SplitDisplayTabs_CollectionChanged;
        _splitInteractionHooksAttached = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_splitInteractionHooksAttached)
        {
            _viewModel.DisplayTabs.CollectionChanged -= SplitDisplayTabs_CollectionChanged;
            _splitInteractionHooksAttached = false;
        }
        DisarmSplitPresentationSettle();
        base.OnClosed(e);
    }

    // ------------------------------------------------------------------
    // 2. Non-member activation (third-tab click suspends the pair).
    // ------------------------------------------------------------------

    /// <summary>
    /// Registered by XAML before the code-behind drag/selection guard, so on a
    /// LEFT click over an ordinary (non-member) tab while a pair is presented,
    /// this handler runs first and suspends the pair directly — the click path
    /// never depends on the later guard or on its handled state.
    /// </summary>
    private void TabsListBox_PreviewMouseLeftButtonDown_SplitInteraction(object sender, MouseButtonEventArgs e)
    {
        if (!IsSplitPresented)
            return;

        // Resolve from the actual pointer hit rather than trusting
        // OriginalSource alone: the source can be a content element nested
        // inside the tab template, so InputHitTest (with OriginalSource as
        // fallback) locates the tab item.
        DependencyObject? hit = TabsListBox.InputHitTest(e.GetPosition(TabsListBox)) as DependencyObject;
        ListBoxItem? item = hit != null ? FindListBoxItem(hit) : FindListBoxItem(e.OriginalSource);

        if (item?.DataContext is not TabViewModel target || IsSplitMember(target.Model))
            return;

        // Buttons inside a tab retain their structural action (pop out/close).
        // Resolve the hit through the SAME SplitInteractionPolicy the
        // deterministic tests exercise, so production and tests share one
        // classifier for the split interaction (no parallel decision model).
        DependencyObject? source = hit ?? e.OriginalSource as DependencyObject;
        bool isButtonHit = false;
        for (DependencyObject? current = source; current != null && current != item;)
        {
            if (current is Button) { isButtonHit = true; break; }
            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        if (isButtonHit)
            return;

        SplitPresentationState state = _splitController.ToState();
        bool isStaleIdentity = !_shepherd.IsCurrentCapturedWindow(target.Model);
        SplitInteractionAction action = SplitInteractionPolicy.Classify(
            state,
            isSplitPresented: true,
            isTargetSplitMember: false,
            isButtonHit: false,
            isStaleIdentity: isStaleIdentity,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);

        if (action != SplitInteractionAction.SuspendPairForGuest)
            return;

        // One authoritative WPF routed-event transaction: classify, mark handled,
        // suspend the pair (via the controller), activate C/D, layout/show once,
        // foreground once.
        e.Handled = true;
        EndDrag();
        int tid = RuntimeTelemetry.Instance.BeginTransition();
        RuntimeTelemetry.Instance.Mark(tid, RuntimeTelemetry.TransitionStage.Classified);
        _log.Log($"SPLIT[third-tab] activate guest=0x{target.Model.Hwnd.ToInt64():X}");

        if (SuspendPresentedPairForUserSelection(target))
        {
            RuntimeTelemetry.Instance.Mark(tid, RuntimeTelemetry.TransitionStage.TargetVisible);
            RuntimeTelemetry.Instance.Mark(tid, RuntimeTelemetry.TransitionStage.ForegroundRequested);
            RuntimeTelemetry.Instance.Mark(tid, RuntimeTelemetry.TransitionStage.Stable);
            RuntimeTelemetry.Instance.CompleteTransition(tid);
            DiagnosticRuntime.Record("split.third-tab", _containerHwnd, target.Model.Hwnd,
                group: Group.Id.ToString("N"), action: "activate-non-member", result: "pair-suspended-single-pass");
            return;
        }

        RuntimeTelemetry.Instance.CompleteTransition(tid);
        // SuspendPresentedPairForUserSelection repairs a partially-hidden pair
        // before returning false. Do not issue a second LayoutSplitPanes here:
        // duplicate native positioning in the same input turn was unnecessary
        // presentation churn and could be visible as a render/jitter pulse.
        DiagnosticRuntime.Record("split.third-tab", _containerHwnd, target.Model.Hwnd,
            group: Group.Id.ToString("N"), action: "activate-non-member", result: "pair-retained");
    }

    /// <summary>
    /// User-selection variant of pair suspension. It preserves the same
    /// journal-safe/fail-closed ordering as SuspendSplitPairForGuest, but makes
    /// the pair -> single-guest presentation exactly one native transition:
    /// each pair member is hidden once, C/D is positioned/shown once, and then
    /// foreground is requested once.
    ///
    /// SuspendForGuest commits Foreground=guest BEFORE returning, so by the
    /// time SetActiveTab raises the ActiveTab notification the presentation
    /// authority already names the target: SyncShepherdActiveWindow's
    /// ReferenceEquals guard makes that notification a no-op and this method
    /// performs the single explicit layout below. (Pre-Wave-3 this required
    /// hand-pre-seeding a separate view field; the ordering trick is now just
    /// "commit, then project".)
    /// </summary>
    private bool SuspendPresentedPairForUserSelection(TabViewModel targetTab)
    {
        CapturedWindow guest = targetTab.Model;
        if (!IsSplitPresented || IsSplitMember(guest))
            return false;

        // A user click is a cold/destructive presentation boundary, so use the
        // full Shepherd identity gate rather than IsWindow alone. A recycled
        // HWND must never cause us to hide the valid pair and then attempt to
        // present an unrelated replacement window.
        if (!_shepherd.IsCurrentCapturedWindow(guest))
        {
            DiagnosticRuntime.Record("split.suspend", _containerHwnd, guest.Hwnd,
                group: Group.Id.ToString("N"), action: "pair-to-single", result: "target-identity-rejected");
            return false;
        }

        // SuspendForGuest hides both members via the production shim and
        // re-validates current identity; on failure it commits NOTHING — the
        // authoritative pair (and its foreground) is retained untouched, so
        // there is no rollback write to make — re-present it exactly once.
        // Each native hide registers its provenance with GuestHideProvenance,
        // so the queued EVENT_OBJECT_HIDE for either member can never be
        // misread as a guest tray-close.
        if (!_splitController.SuspendForGuest(guest))
        {
            LayoutSplitPanes();
            DiagnosticRuntime.Record("split.suspend", _containerHwnd, guest.Hwnd,
                group: Group.Id.ToString("N"), action: "pair-to-single", result: "recovery-pending-pair-retained");
            return false;
        }

        // Controller owns presented/foreground/generation (it bumped the
        // generation and disarmed its own settle while suspending); the
        // container keeps constraint/refusal concerns.
        DisarmSplitPresentationSettle();
        _constraintDirty = true;
        _paneContainment.InvalidateAll();

        // Project the committed authority into the UI selection. The sync this
        // triggers observes Foreground == ActiveTab.Model and no-ops.
        if (!ReferenceEquals(_viewModel.ActiveTab, targetTab))
            _viewModel.SetActiveTab(targetTab);

        LayoutShepherdActiveWindow();
        _shepherd.SetForeground(guest);

        _log.Log($"SPLIT[suspend] guest=0x{guest.Hwnd.ToInt64():X}");
        _log.Log($"SPLIT[single] guest=0x{guest.Hwnd.ToInt64():X} pair=dormant");
        DiagnosticRuntime.Record("split.suspend", _containerHwnd, guest.Hwnd,
            group: Group.Id.ToString("N"), action: "pair-to-single", result: "pair-retained-single-pass");
        return true;
    }

    // ------------------------------------------------------------------
    // 3. Post-popup presentation settle (CompositionTarget one-shot).
    // ------------------------------------------------------------------

    /// <summary>
    /// Split creation is normally invoked from a WPF ContextMenu. EnterSplit
    /// updates logical membership immediately, but its first LayoutSplitPanes
    /// call intentionally no-ops while TabDock chrome is raised. Watch the
    /// display projection for the new composite and perform one post-popup
    /// settle on the first render frame after chrome is no longer active.
    ///
    /// Why CompositionTarget.Rendering (not LayoutUpdated): LayoutUpdated fires
    /// once per layout pass, but a popup close may finish its z-restore at
    /// DispatcherPriority.Input AFTER layout — a LayoutUpdated settle would
    /// still race that pending Input restore and steal foreground from chrome.
    /// Rendering runs after Input, so waiting for it guarantees the popup
    /// teardown's restore is done. IsContainerChromeInteractionActive()
    /// keeps the one-shot armed if chrome is still active.
    ///
    /// The settle is deliberately ordinary positioning plus SetForeground: no
    /// WM_SIZE synthesis, style mutation, reparenting, or frame-change flags.
    /// This gives Chromium-family guests the same real activation/repaint they
    /// otherwise receive only after the user's first click in the pane.
    /// </summary>
    private void SplitDisplayTabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsSplitPresented)
        {
            DisarmSplitPresentationSettle();
            return;
        }

        if (_splitController.SettlePending)
            return;

        _splitController.ArmSettle();
        CompositionTarget.Rendering += SplitPresentationSettle_Rendering;
    }

    private void SplitPresentationSettle_Rendering(object? sender, EventArgs e)
    {
        if (!_splitController.SettlePending)
        {
            CompositionTarget.Rendering -= SplitPresentationSettle_Rendering;
            return;
        }
        // Validate the stale-callback guards BEFORE any native work (Q5/Q6/Q7):
        // - Generation must still match the current ContainerWindow generation
        //   (a Suspension or exit bumped it, making this callback stale).
        // - Presentation must still be active (exit/suspend made it dormant).
        // IsCurrentSettle + IsSplitPresented together prevent a dormant pair
        // from being accidentally resurrected (Q7) or a stale generation
        // from running after mode changed (Q6).
        var settleState = new TabDock.Models.SplitPresentationState(
            _splitController.Left?.Hwnd.ToString("X"),
            _splitController.Right?.Hwnd.ToString("X"),
            IsSplitPresented,
            _splitController.Foreground?.Hwnd.ToString("X") ?? _splitController.Left?.Hwnd.ToString("X"),
            _splitController.Generation);
        if (!TabDock.Models.SplitPresentationPolicy.IsCurrentSettle(
                settleState,
                _splitController.SettleGeneration)
            || !IsSplitPresented)
        {
            DisarmSplitPresentationSettle();
            return;
        }
        // Extra explicit guard: verify the ContainerWindow fields match the
        // controller-level state used by IsCurrentSettle — the two generations
        // must agree and presentation still true before LayoutSplitPanes (Q5).
        if (_splitController.SettleGeneration != _splitController.Generation)
        {
            DisarmSplitPresentationSettle();
            return;
        }

        // ContextMenu.Closed -> EndChromePopup queues the normal z-order restore
        // at Input priority. Rendering runs after that transition; if another
        // TabDock-owned chrome surface is still active, keep the one-shot armed
        // instead of stealing foreground from it. Input-priority z-restore races
        // are avoided because Rendering fires after Input, but an interactively
        // held popup (right-click menu still open) must still defer.
        if (IsContainerChromeInteractionActive())
            return;

        CapturedWindow? focused = _splitController.Foreground ?? _splitController.Left;
        if (focused == null || !IsSplitMember(focused))
        {
            DisarmSplitPresentationSettle();
            return;
        }

        DisarmSplitPresentationSettle();
        LayoutSplitPanes();
        if (IsSplitPresented
            && _splitController.SettleGeneration == _splitController.Generation
            && IsSplitMember(focused))
        {
            _shepherd.SetForeground(focused);
            _log.Log($"SPLIT[settled] foreground=0x{focused.Hwnd.ToInt64():X}");
            DiagnosticRuntime.Record("split.settled", _containerHwnd, focused.Hwnd,
                group: Group.Id.ToString("N"), action: "post-popup-layout-and-foreground", result: "requested");
        }
    }

    private void DisarmSplitPresentationSettle()
    {
        // Idempotent: callers may race (suspend, exit, mode change, Closed).
        // Removing the handler when not pending is a no-op; each arm adds
        // exactly one subscription, each disarm removes exactly one.
        if (!_splitController.SettlePending)
            return;
        _splitController.DisarmSettle();
        CompositionTarget.Rendering -= SplitPresentationSettle_Rendering;
    }
}
