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

    /// <summary>
    /// Intercept a third-tab LEFT click before the child ListBox split guard can
    /// swallow it. Buttons are deliberately excluded so per-tab close/pop-out
    /// retains its existing structural behavior. Hover and right-click are not
    /// handled here and therefore continue to leave the split pair untouched.
    /// </summary>
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (TryActivateOrdinaryTabFromSplit(e))
            return;

        base.OnPreviewMouseLeftButtonDown(e);
    }

    private bool TryActivateOrdinaryTabFromSplit(MouseButtonEventArgs e)
    {
        if (!IsSplitPresented || e.OriginalSource is not DependencyObject source)
            return false;

        ListBoxItem? item = ItemsControl.ContainerFromElement(TabsListBox, source) as ListBoxItem;
        if (item?.DataContext is not TabViewModel target || IsSplitMember(target.Model))
            return false;

        // Do not turn a close-button click into a tab activation. Walk only up
        // to the owning item so an unrelated ancestor cannot affect the result.
        for (DependencyObject? current = source; current != null && current != item;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is Button)
                return false;
        }

        // Stop WPF's ListBox selection path before doing native work. The
        // suspend transition changes ActiveTab only after both split members
        // have been hidden journal-safely, so an uncertain hide cannot leave a
        // third tab selected while the old pair remains authoritative.
        e.Handled = true;
        _log.Log($"SPLIT[third-tab] activate guest=0x{target.Model.Hwnd.ToInt64():X}");

        SuspendSplitPairForGuest(target.Model);
        if (!IsSplitRelationshipDefined || !IsSplitPresented)
        {
            DiagnosticRuntime.Record("split.third-tab", _containerHwnd, target.Model.Hwnd,
                group: Group.Id.ToString("N"), action: "activate-non-member", result: "pair-suspended");
            return true;
        }

        // ExitSplit fails closed when a member hide is RecoveryPending. A prior
        // member may already have completed its hide before the later member
        // became uncertain, so re-present the still-authoritative pair before
        // returning to the input loop instead of leaving a half-blank split.
        LayoutSplitPanes();
        DiagnosticRuntime.Record("split.third-tab", _containerHwnd, target.Model.Hwnd,
            group: Group.Id.ToString("N"), action: "activate-non-member", result: "recovery-pending-pair-retained");
        return true;
    }

    /// <summary>
    /// Split creation is normally invoked from a WPF ContextMenu. EnterSplit
    /// updates logical membership immediately, but its first LayoutSplitPanes
    /// call intentionally no-ops while TabDock chrome is raised. Watch the
    /// display projection for the new composite and perform one post-popup
    /// settle on the first render frame after chrome is no longer active.
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
        if (!IsSplitPresented
            || _splitPresentationSettleGeneration != _splitPresentationGeneration)
        {
            DisarmSplitPresentationSettle();
            return;
        }

        // ContextMenu.Closed -> EndChromePopup queues the normal z-order restore
        // at Input priority. Rendering runs after that transition; if another
        // TabDock-owned chrome surface is still active, keep the one-shot armed
        // instead of stealing foreground from it.
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
        if (!_splitPresentationSettlePending)
            return;
        _splitPresentationSettlePending = false;
        CompositionTarget.Rendering -= SplitPresentationSettle_Rendering;
    }
}
