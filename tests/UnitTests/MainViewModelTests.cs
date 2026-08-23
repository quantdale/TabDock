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

    public void Dispose() => _fixture.Dispose();
}
