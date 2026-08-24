using System;

namespace TabDock.Services;

internal enum WinEventRoutingDecision
{
    Ignore,
    DirectCaptured,
    DesktopReorderCaptured,
}

/// <summary>Inputs needed to route one WinEvent without calling USER32.</summary>
internal readonly record struct WinEventRoutingInput(
    uint EventType,
    IntPtr Hwnd,
    int IdObject,
    int IdChild,
    IntPtr DesktopWindow,
    bool Captured);

/// <summary>
/// Pure admission policy for the native event boundary. It deliberately does
/// not resolve HWND identity: callers perform the one current membership probe
/// required for callback admission and pass that result here.
/// </summary>
internal static class WinEventRoutingPolicy
{
    public static WinEventRoutingDecision Decide(WinEventRoutingInput input)
    {
        if (input.Hwnd == IntPtr.Zero)
            return WinEventRoutingDecision.Ignore;

        bool desktopReorder = input.EventType == NativeMethods.EVENT_OBJECT_REORDER
            && input.Hwnd == input.DesktopWindow
            && input.IdObject == NativeMethods.OBJID_CLIENT
            && input.IdChild == NativeMethods.CHILDID_SELF;
        if (desktopReorder)
            return input.Captured
                ? WinEventRoutingDecision.DesktopReorderCaptured
                : WinEventRoutingDecision.Ignore;

        if (input.IdObject != 0 || input.IdChild != 0 || !input.Captured)
            return WinEventRoutingDecision.Ignore;

        return WinEventRoutingDecision.DirectCaptured;
    }
}
