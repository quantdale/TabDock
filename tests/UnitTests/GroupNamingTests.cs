using System;
using System.Linq;
using TabDock.Models;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Group names build every visible surface (container menus, launcher rows),
/// so CreateGroup must never emit a duplicate default name. Suffixing resolves
/// against current names only; later manual renames may collide by choice.
/// </summary>
public class GroupNamingTests : IDisposable
{
    private readonly ReleaseTestFixture _fixture = ReleaseTestFixture.Create();
    private GroupManager CreateManager()
    {
        var persistence = new PersistenceService(_fixture.Log, _fixture.StatePath);
        return new GroupManager(_fixture.Service, persistence, _fixture.Log);
    }

    [Fact]
    public void RepeatedDefaultCreation_SuffixesNamesUniquely()
    {
        GroupManager groups = CreateManager();

        var first = groups.CreateGroup();
        var second = groups.CreateGroup();
        var third = groups.CreateGroup();

        Assert.Equal("Group", first.Name);
        Assert.Equal("Group 2", second.Name);
        Assert.Equal("Group 3", third.Name);
        Assert.Equal(3, groups.Groups.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ExplicitDuplicateName_IsSuffixed_NotRejected()
    {
        GroupManager groups = CreateManager();
        groups.CreateGroup("Work");

        var duplicate = groups.CreateGroup("Work");

        Assert.Equal("Work 2", duplicate.Name);
    }

    [Fact]
    public void CaseInsensitiveCollision_StillSuffixes()
    {
        GroupManager groups = CreateManager();
        groups.CreateGroup("work");

        var duplicate = groups.CreateGroup("WORK");

        Assert.Equal("WORK 2", duplicate.Name);
    }

    [Fact]
    public void SuffixSequence_SkipsManuallyTakenNames()
    {
        GroupManager groups = CreateManager();
        groups.CreateGroup("Project");
        // A user-created "Project 2" must not make the next default collide
        // with it; the suffix walk continues to the first free name.
        groups.CreateGroup("Project 2");

        var next = groups.CreateGroup("Project");

        Assert.Equal("Project 3", next.Name);
    }

    public void Dispose() => _fixture.Dispose();
}
