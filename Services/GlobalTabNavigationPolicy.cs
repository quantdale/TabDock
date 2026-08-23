using System;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// The result of proving which TabDock group, if any, owns the foreground
/// context for a global tab-navigation notification.
/// </summary>
public readonly record struct GlobalTabNavigationTarget(Guid GroupId, CapturedWindow? CapturedGuest)
{
    public bool IsCapturedGuest => CapturedGuest != null;
}

/// <summary>
/// Pure foreground-scope gate for the global tab-navigation path. It does not
/// inspect or mutate native state itself; production supplies the current
/// identity/group/container proof functions and tests supply deterministic
/// fakes. Unproven identity is always rejected.
/// </summary>
public static class GlobalTabNavigationPolicy
{
    public static bool TryResolve(
        IntPtr foregroundHwnd,
        Func<IntPtr, CapturedWindow?> resolveCapturedGuest,
        Func<CapturedWindow, Guid?> resolveCapturedGuestGroup,
        Func<IntPtr, Guid?> resolveContainerGroup,
        Func<CapturedWindow, bool> isCurrentCapturedGuest,
        out GlobalTabNavigationTarget target)
    {
        target = default;
        if (foregroundHwnd == IntPtr.Zero)
            return false;

        CapturedWindow? capturedGuest = resolveCapturedGuest(foregroundHwnd);
        if (capturedGuest != null)
        {
            if (!isCurrentCapturedGuest(capturedGuest))
                return false;

            Guid? groupId = resolveCapturedGuestGroup(capturedGuest);
            if (!groupId.HasValue || groupId.Value == Guid.Empty)
                return false;

            target = new GlobalTabNavigationTarget(groupId.Value, capturedGuest);
            return true;
        }

        Guid? containerGroup = resolveContainerGroup(foregroundHwnd);
        if (!containerGroup.HasValue || containerGroup.Value == Guid.Empty)
            return false;

        target = new GlobalTabNavigationTarget(containerGroup.Value, null);
        return true;
    }
}
