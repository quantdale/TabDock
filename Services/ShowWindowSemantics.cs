namespace TabDock.Services;

/// <summary>
/// Pure post-state interpretation for ShowWindow. Its BOOL return is the
/// window's PREVIOUS visibility, not whether the requested command succeeded.
/// </summary>
internal static class ShowWindowSemantics
{
    public static bool RestoreSucceeded(bool previouslyVisible, bool iconicAfter, bool zoomedAfter)
    {
        _ = previouslyVisible;
        return !iconicAfter && !zoomedAfter;
    }

    public static bool VisibilitySucceeded(bool previouslyVisible, bool visibleAfter, bool expectedVisible)
    {
        _ = previouslyVisible;
        return visibleAfter == expectedVisible;
    }
}
