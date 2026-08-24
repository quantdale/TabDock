using System;
using TabDock.Models;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Launcher row activation: the open command routes the targeted group (row
/// parameter, else the current selection) through OpenGroupRequested for App's
/// registry-first OpenContainer path.
/// </summary>
public class MainViewModelTests : IDisposable
{
    private readonly ReleaseTestFixture _fixture = ReleaseTestFixture.Create();

    private MainViewModel CreateViewModel(out GroupManager groups)
    {
        var persistence = new PersistenceService(_fixture.Log, _fixture.StatePath);
        groups = new GroupManager(_fixture.Service, persistence, _fixture.Log);
        return new MainViewModel(groups);
    }

    [Fact]
    public void OpenCommand_WithRowParameter_RaisesEventWithThatGroup()
    {
        MainViewModel vm = CreateViewModel(out GroupManager groups);
        Group target = groups.CreateGroup("target");
        Group other = groups.CreateGroup("other");
        vm.SelectedGroup = other;

        Group? received = null;
        vm.OpenGroupRequested += (_, group) => received = group;
        vm.OpenSelectedGroupCommand.Execute(target);

        Assert.Same(target, received);
    }

    [Fact]
    public void OpenCommand_WithoutParameter_UsesSelection()
    {
        MainViewModel vm = CreateViewModel(out GroupManager groups);
        Group selected = groups.CreateGroup("selected");
        vm.SelectedGroup = selected;

        Group? received = null;
        vm.OpenGroupRequested += (_, group) => received = group;
        vm.OpenSelectedGroupCommand.Execute(null);

        Assert.Same(selected, received);
    }

    [Fact]
    public void OpenCommand_NoSelectionAndNoParameter_RaisesNothing()
    {
        MainViewModel vm = CreateViewModel(out _);

        bool raised = false;
        vm.OpenGroupRequested += (_, _) => raised = true;
        vm.OpenSelectedGroupCommand.Execute(null);

        Assert.False(raised);
    }

    [Fact]
    public void RemovingSelectedGroup_RepairsLauncherSelectionToLiveProjection()
    {
        MainViewModel vm = CreateViewModel(out GroupManager groups);
        Group first = groups.CreateGroup("first");
        Group selected = groups.CreateGroup("selected");
        Group last = groups.CreateGroup("last");
        vm.SelectedGroup = selected;

        groups.RemoveGroup(selected);

        Assert.DoesNotContain(selected, vm.Groups);
        Assert.Same(last, vm.SelectedGroup);
        Assert.Contains(vm.SelectedGroup, vm.Groups);
        Assert.Equal(2, vm.Groups.Count);
    }

    [Fact]
    public void RemovingOnlyGroup_ClearsLauncherSelection()
    {
        MainViewModel vm = CreateViewModel(out GroupManager groups);
        Group only = groups.CreateGroup("only");
        vm.SelectedGroup = only;

        groups.RemoveGroup(only);

        Assert.Empty(vm.Groups);
        Assert.Null(vm.SelectedGroup);
    }

    public void Dispose() => _fixture.Dispose();
}
