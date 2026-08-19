using TabDock.Models;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Headless coverage for Group's positional active-index contract. The clamp
/// behavior is the model-level half of the active-tab-preservation fix: the
/// index must never name a slot outside the live Members collection, and a
/// restored (unpopulated) group must be able to carry -1 without throwing.
/// </summary>
public class GroupTests
{
    private static Group Populated(int count)
    {
        var g = new Group();
        for (int i = 0; i < count; i++)
            g.Members.Add(new CapturedWindow { ExePath = $"app{i}.exe" });
        return g;
    }

    [Fact]
    public void ActiveIndex_DefaultsToZero()
    {
        Assert.Equal(0, new Group().ActiveIndex);
    }

    [Fact]
    public void ActiveIndex_ClampsAboveRangeToLastMember()
    {
        var g = Populated(3);
        g.ActiveIndex = 5;
        Assert.Equal(2, g.ActiveIndex); // Members.Count - 1
    }

    [Fact]
    public void ActiveIndex_ClampsNegativeToZeroWhenPopulated()
    {
        var g = Populated(3);
        g.ActiveIndex = -10;
        Assert.Equal(0, g.ActiveIndex);
    }

    [Fact]
    public void ActiveIndex_AcceptsInRangeValue()
    {
        var g = Populated(3);
        g.ActiveIndex = 1;
        Assert.Equal(1, g.ActiveIndex);
    }

    [Fact]
    public void ActiveIndex_CanBeNegativeWhenEmpty()
    {
        // A restored group has no live Members; the loaded index must survive as
        // -1 instead of being forced to 0 (which would point at a non-existent member).
        var g = new Group();
        g.ActiveIndex = -1;
        Assert.Equal(-1, g.ActiveIndex);
    }

    [Fact]
    public void PersistedActiveIndex_RoundTripsVerbatim()
    {
        var g = new Group();
        g.PersistedActiveIndex = 2;
        Assert.Equal(2, g.PersistedActiveIndex);

        // An empty, unpopulated group carries the intent without clamping it
        // (the live ActiveIndex clamps against an empty Members, but the
        // persisted copy is written back verbatim by PersistenceService).
        g.PersistedActiveIndex = 0;
        Assert.Equal(0, g.PersistedActiveIndex);
    }

    [Fact]
    public void HasMaterializedTabs_TrueOnceMembersAdded()
    {
        var g = new Group();
        Assert.False(g.HasMaterializedTabs);
        g.Members.Add(new CapturedWindow());
        Assert.True(g.HasMaterializedTabs);
    }
}
