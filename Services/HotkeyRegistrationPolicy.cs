namespace TabDock.Services;

/// <summary>
/// Registration-state projection for the paired global tab shortcuts. A
/// partial pair is unavailable so the UI never advertises only one direction.
/// </summary>
public static class HotkeyRegistrationPolicy
{
    public static bool IsTabNavigationPairAvailable(bool previousRegistered, bool nextRegistered)
        => previousRegistered && nextRegistered;
}
