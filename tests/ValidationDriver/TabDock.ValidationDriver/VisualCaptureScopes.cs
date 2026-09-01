using System;

namespace TabDock.ValidationDriver;

internal readonly record struct VisualScopeResolution(
    VisualRect RequestedRect,
    VisualRect ActualRect,
    bool WasClipped)
{
    public int Width => ActualRect.Width;
    public int Height => ActualRect.Height;
}

/// <summary>
/// Resolves declared semantic scopes without widening them into desktop capture.
/// Native acquisition is deliberately kept in VisualCaptureNative.cs; this pure
/// boundary owns the geometry and stale-identity rules shared by every backend.
/// </summary>
internal static class VisualScopeResolver
{
    public static bool TryResolveWindow(
        VisualCaptureScope scope,
        VisualRect windowRect,
        VisualRect clientRect,
        VisualRect monitorWorkArea,
        out VisualScopeResolution resolution,
        out string reason)
    {
        resolution = default;
        reason = string.Empty;
        try
        {
            scope.Validate();
            windowRect.Validate(nameof(windowRect));
            clientRect.Validate(nameof(clientRect));
            monitorWorkArea.Validate(nameof(monitorWorkArea));
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }

        if (scope.Kind == VisualCaptureScopeKind.VIRTUAL_DESKTOP)
        {
            reason = "virtual-desktop-scope-is-not-a-window-scope";
            return false;
        }
        if (scope.ContextMargin != 0 && scope.Kind != VisualCaptureScopeKind.TARGET_WITH_CONTEXT)
        {
            reason = "context-margin-requires-target-with-context-scope";
            return false;
        }

        VisualRect targetBoundary = scope.Kind == VisualCaptureScopeKind.HOST_CLIENT
            ? clientRect
            : windowRect;
        VisualRect requested = scope.Kind == VisualCaptureScopeKind.TARGET_WITH_CONTEXT
            ? (scope.RequestedRect ?? windowRect).Inflate(scope.ContextMargin)
            : scope.RequestedRect ?? targetBoundary;
        VisualRect clipBoundary = scope.Kind == VisualCaptureScopeKind.TARGET_WITH_CONTEXT
            ? monitorWorkArea
            : targetBoundary;
        VisualRect actual = requested.Intersect(clipBoundary);
        if (!actual.IsPositive)
        {
            reason = "requested-scope-does-not-intersect-approved-boundary";
            return false;
        }

        resolution = new VisualScopeResolution(
            requested,
            actual,
            requested.Left != actual.Left
                || requested.Top != actual.Top
                || requested.Right != actual.Right
                || requested.Bottom != actual.Bottom);
        return true;
    }

    public static bool SameStableIdentity(VisualTargetIdentity expected, VisualTargetIdentity actual)
        => string.Equals(expected.Hwnd, actual.Hwnd, StringComparison.OrdinalIgnoreCase)
            && expected.ProcessId == actual.ProcessId
            && expected.WindowThreadId == actual.WindowThreadId
            && string.Equals(expected.ClassName, actual.ClassName, StringComparison.Ordinal)
            && expected.ProcessStartTimeUtcTicks == actual.ProcessStartTimeUtcTicks
            && string.Equals(expected.Role, actual.Role, StringComparison.Ordinal)
            && string.Equals(expected.Ownership, actual.Ownership, StringComparison.Ordinal);
}
