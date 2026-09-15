namespace TabDock.Services;

/// <summary>
/// Owns the one-shot boundary between a newly shown WPF container and its first
/// fully rendered frame. The container uses this policy to decide whether an
/// active Shepherd guest needs one post-render reconciliation.
/// </summary>
internal sealed class InitialPresentationReconciliationPolicy
{
    private bool _firstContentRendered;

    public bool ShouldReconcileAfterFirstContentRendered(bool hasActiveGuest)
    {
        if (_firstContentRendered)
            return false;

        _firstContentRendered = true;
        return hasActiveGuest;
    }
}
