namespace TabDock.Services;

/// <summary>
/// Classifies the one native minimize event that must not start guest
/// recovery: an invisible HWND that already has a live, matching expectation
/// for a TabDock-issued hide. The expectation is deliberately checked by the
/// lifecycle service against the current captured object; this policy only
/// expresses the visibility decision.
/// </summary>
internal static class GuestMinimizeRecoveryPolicy
{
    /// <summary>
    /// Returns <see langword="true"/> when minimize recovery would race an
    /// intentional TabDock hide that has already made the guest invisible.
    /// </summary>
    public static bool ShouldSuppressRestore(bool matchingExpectedHide, bool guestVisible)
        => matchingExpectedHide && !guestVisible;
}
