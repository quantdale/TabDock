using System;
using System.Collections.Specialized;

namespace TabDock.Views;

/// <summary>
/// Owns the post-capture presentation boundary for newly admitted guests.
///
/// Capture is intentionally a two-stage operation: GroupViewModel first adds the
/// tab/member and synchronously selects it, while the independent top-level guest
/// HWND is then presented by the Shepherd. During a brand-new container's first
/// capture, USER32/WPF can still be finishing the container/content-host show and
/// z-order transaction after that synchronous presentation attempt. The result can
/// be a correctly captured, correctly tabbed guest whose HWND is geometrically in
/// place but still below the opaque container, leaving the content area black
/// until a later transition (for example split -> unsplit) reasserts presentation.
///
/// This partial does not create another presentation authority. It only requests
/// one coalesced Render-priority reconciliation through ContainerWindow's existing
/// PresentationLayoutCoordinator after a tab is added. ensureFinalPass preserves
/// one additional pass when another relayout is already pending, which is exactly
/// the existing late-z-order-settle contract used by other native transitions.
/// </summary>
public partial class ContainerWindow
{
    private bool _capturePresentationHookAttached;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        if (_capturePresentationHookAttached)
            return;

        _viewModel.Tabs.CollectionChanged += CaptureTabs_CollectionChanged;
        Closed += CapturePresentation_Closed;
        _capturePresentationHookAttached = true;
    }

    private void CaptureTabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Only admission needs this boundary. Removes/moves already flow through
        // their established release, split, and reorder presentation paths.
        if (e.Action != NotifyCollectionChangedAction.Add
            || e.NewItems == null
            || e.NewItems.Count == 0)
        {
            return;
        }

        // GroupViewModel.AddCapturedWindow raises Tabs.CollectionChanged before
        // SetActiveTab. Queue rather than mutate natively here: the Render callback
        // runs after the synchronous capture transaction and re-reads the CURRENT
        // presentation authority/active guest. Multiple captures coalesce; when a
        // relayout is already pending, ensureFinalPass latches exactly one follow-up.
        _constraintDirty = true;
        _paneContainment.InvalidateAll();
        RequestRelayout(ensureFinalPass: true);
    }

    private void CapturePresentation_Closed(object? sender, EventArgs e)
    {
        if (!_capturePresentationHookAttached)
            return;

        _viewModel.Tabs.CollectionChanged -= CaptureTabs_CollectionChanged;
        Closed -= CapturePresentation_Closed;
        _capturePresentationHookAttached = false;
    }
}
