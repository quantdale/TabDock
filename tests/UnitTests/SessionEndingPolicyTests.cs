using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former SessionEndingPolicySelfTest (Wave 4): session
/// teardown is a one-way, idempotent transition on caller-owned state.
/// </summary>
public class SessionEndingPolicyTests
{
    [Fact]
    public void TryBeginTeardown_IsOneWayAndIdempotent()
    {
        bool started = false;

        Assert.True(SessionEndingPolicy.TryBeginTeardown(ref started));
        Assert.True(started);
        Assert.False(SessionEndingPolicy.TryBeginTeardown(ref started));
        Assert.True(started);
    }
}
