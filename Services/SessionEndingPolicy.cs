namespace TabDock.Services;

/// <summary>
/// Session-ending is deliberately a one-way lifecycle transition. Once guest
/// release/normalization starts, TabDock exits rather than pretending it can
/// resume after Windows cancels the originating logoff/shutdown request.
/// </summary>
internal static class SessionEndingPolicy
{
    public static bool TryBeginTeardown(ref bool started)
    {
        if (started)
            return false;
        started = true;
        return true;
    }
}
