using TabDock;

namespace TabDock.Services;

/// <summary>
/// Pure decision for the one native-state repair needed before Shepherd applies
/// an exact pane rectangle. A borderless, non-zoomed window whose current outer
/// rectangle is outside its assigned pane is the observable shape Chromium and
/// other browsers use for F11 fullscreen; restore it before the native minimum
/// track guard can refuse the pane size.
/// </summary>
internal static class NativePresentationRestorePolicy
{
    public static bool NeedsRestore(
        NativeMethods.RECT observed,
        NativeMethods.RECT assigned,
        uint style,
        bool isIconic,
        bool isZoomed)
        => isIconic
            || isZoomed
            || IsBorderlessGeometryMismatch(observed, assigned, style);

    public static bool IsBorderlessGeometryMismatch(
        NativeMethods.RECT observed,
        NativeMethods.RECT assigned,
        uint style)
    {
        if (PaneContainmentPolicy.MatchesWithinEpsilon(observed, assigned))
            return false;

        bool hasCaption = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
        bool hasThickFrame = (style & NativeMethods.WS_THICKFRAME) != 0;
        return !hasCaption && !hasThickFrame;
    }
}
