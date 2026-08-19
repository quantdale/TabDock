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
/// Split interaction transitions that intentionally sit above the ordinary tab
/// selection/drag handlers. The split composite remains a persistent
/// relationship while the user interacts with either member or merely
/// hovers/right-clicks another tab; an explicit LEFT click on an ordinary
/// non-member suspends the pair and presents that tab normally.
///
/// This is kept in a partial file because the transition is a UI policy fix; it
/// does not change the Shepherd/no-reparent native mutation core.
/// </summary>
public partial class ContainerWindow
{
    private bool _splitInteractionHooksAttached;
    private bool _splitPresentationSettlePending;
    private long _splitPresentationSettleGeneration;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_splitInteractionHooksAttached)
            return;

        _viewModel.DisplayTabs.CollectionChanged += SplitDisplayTabs_CollectionChanged;

        // The ordinary ListBox preview handler deliberately marks a non-member
        // click handled while a pair is presented. That is correct as a guard
        // against WPF selecting C before the journal-safe pair hide completes,
        // but it also means a window-level hit-test miss used to make the click
        // disappear completely. Listen on the ListBox itself with
        // handledEventsToo=true so this transition runs after that guard. The
        // guard clears its drag candidate before returning, so this handler
        // resolves the item from the pointer position rather than depending on
        // transient drag state or OriginalSource template shape.
        TabsListBox.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(TabsListBox_PreviewMouseLeftButtonDown_SplitInteraction),
            true);

        _splitInteractionHooksAttached = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_splitInteractionHooksAttached)
        {
            _viewModel.DisplayTabs.CollectionChanged -= SplitDisplayTabs_CollectionChanged;
            TabsListBox.RemoveHandler(
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(TabsListBox_PreviewMouseLeftButtonDown_SplitInteraction));
            _splitInteractionHooksAttached = false;
        }
        DisarmSplitPresentationSettle();
        base.OnClosed(e);
    }

    /// <summary>
    /// Runs after the strip's existing split guard even when that guard marked
    /// the routed event handled. A third/fourth ordinary tab click is therefore
    /// never dependent on the earlier window-level hit-test path.
    /// </summary>
    private void TabsListBox_PreviewMouseLeftButtonDown_SplitInteraction(object sender, MouseButtonEventArgs e)
    {
        if (!IsSplitPresented)
            return;

        // Resolve from the actual pointer hit. The ordinary split guard has
        // already cleared _draggedItem before this handled-events-too callback.
        // InputHitTest yields a visual element even when OriginalSource is a
        // content element nested inside the tab template.
        DependencyObject? hit = TabsListBox.InputHitTest(e.GetPosition(TabsListBox)) as DependencyObject;
        ListBoxItem? item = hit != null ? FindListBoxItem(hit) : FindListBoxItem(e.OriginalSource);

        if (item?.DataContext is not TabViewModel target || IsSplitMember(target.Model))
            return;

        // Buttons inside a tab retain their structural action (pop out/close).
        // Use the pointer hit rather than OriginalSource where possible because
        // text/content elements do not always participate in the visual tree in
        // the same way as the owning control.
        DependencyObject? source = hit ?? e.OriginalSource as DependencyObject;
        for (DependencyObject? current = source; current != null && current != item;)
        {
            if (current is Button)
                return;

            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        // The existing split guard has already prevented WPF's ListBox from
        // selecting the target. Keep it handled while we perform the native
        // transaction so a RecoveryPending hide cannot expose a logical C/D
        // selection over an authoritative pair.
        e.Handled = true;
        EndDrag();
        _log.Log($"SPLIT[third-tab] activate guest=0x{target.Model.Hwnd.ToInt64():X}");

        if (SuspendPresentedPairForUserSelection(target))
        {
            DiagnosticRuntime.Record("split.third-tab", _containerHwnd, target.Model.Hwnd,
                group: Group.Id.ToString("N"), action: "activate-non-member", result: "pair-suspended-single-pass");
            return;
        }

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
    /// The shepherd active reference is set before ActiveTab. That is deliberate:
    /// ActiveTab notification normally enters SyncShepherdActiveWindow, whose
    /// ordinary single-tab path would otherwise hide the already-hidden focused
    /// split member a second time and re-run presentation work. With the target
    /// already authoritative, that notification becomes a no-op and this method
    /// performs the single explicit layout below.
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

        CapturedWindow? previousActive = _shepherdActiveWindow;
        foreach (CapturedWindow member in new[] { _splitLeft!, _splitRight! })
        {
            WindowHideOutcome outcome = _shepherd.Hide(member);
            LogHidePending(member, outcome);
            if (outcome == WindowHideOutcome.RecoveryPending)
            {
                // A member may already have hidden before its partner became
                // uncertain. Restore the presentation-side active reference and
                // re-present the still-authoritative pair exactly once.
                _shepherdActiveWindow = previousActive;
                LayoutSplitPanes();
                DiagnosticRuntime.Record("split.suspend", _containerHwnd, guest.Hwnd,
                    group: Group.Id.ToString("N"), action: "pair-to-single", result: "recovery-pending-pair-retained");
                return false;
            }
        }

        // Hiding two top-level guests is not atomic with respect to process/HWND
        // lifetime. Re-prove C/D after those native calls and before committing
        // dormant state. If the target changed while the pair was being hidden,
        // re-present the still-defined pair and leave logical selection alone.
        if (!_shepherd.IsCurrentCapturedWindow(guest))
        {
            _shepherdActiveWindow = previousActive;
            LayoutSplitPanes();
            DiagnosticRuntime.Record("split.suspend", _containerHwnd, guest.Hwnd,
                group: Group.Id.ToString("N"), action: "pair-to-single", result: "target-changed-pair-retained");
            return false;
        }

        // Disarm the settle and bump generation BEFORE clearing _splitPairPresented
        // so a CompositionTarget.Rendering already queued cannot fire after the
        // pair is dormant. The bump invalidates that stale generation even if
        // Disarm races with the dispatcher's invocation list (Q8). Ordering:
        // bump -> disarm -> dormant prevents a window where a stale settle
        // re-arms or re-presents a dormant pair.
        _splitPresentationGeneration++;
        DisarmSplitPresentationSettle();
        _splitPairPresented = false;
        _constraintDirty = true;
        _refusedPaneByHwnd.Clear();

        // Pre-seed presentation authority before SetActiveTab. This prevents the
        // ActiveTab notification from entering the ordinary hide-old/show-new
        // path and repeating work that this transaction has already completed.
        _shepherdActiveWindow = guest;
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

        if (_splitPresentationSettlePending)
            return;

        _splitPresentationSettlePending = true;
        _splitPresentationSettleGeneration = _splitPresentationGeneration;
        CompositionTarget.Rendering += SplitPresentationSettle_Rendering;
    }

    private void SplitPresentationSettle_Rendering(object? sender, EventArgs e)
    {
        if (!_splitPresentationSettlePending)
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
            _splitLeft?.Hwnd.ToString("X"),
            _splitRight?.Hwnd.ToString("X"),
            IsSplitPresented,
            _splitForeground?.Hwnd.ToString("X") ?? _splitLeft?.Hwnd.ToString("X"),
            _splitPresentationGeneration);
        if (!TabDock.Models.SplitPresentationPolicy.IsCurrentSettle(
                settleState,
                _splitPresentationSettleGeneration)
            || !IsSplitPresented)
        {
            DisarmSplitPresentationSettle();
            return;
        }
        // Extra explicit guard: verify the ContainerWindow fields match the
        // controller-level state used by IsCurrentSettle — the two generations
        // must agree and presentation still true before LayoutSplitPanes (Q5).
        if (_splitPresentationSettleGeneration != _splitPresentationGeneration)
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

        CapturedWindow? focused = _splitForeground ?? _splitLeft;
        if (focused == null || !IsSplitMember(focused))
        {
            DisarmSplitPresentationSettle();
            return;
        }

        DisarmSplitPresentationSettle();
        LayoutSplitPanes();
        if (IsSplitPresented
            && _splitPresentationSettleGeneration == _splitPresentationGeneration
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
        if (!_splitPresentationSettlePending)
            return;
        _splitPresentationSettlePending = false;
        CompositionTarget.Rendering -= SplitPresentationSettle_Rendering;
    }
}
