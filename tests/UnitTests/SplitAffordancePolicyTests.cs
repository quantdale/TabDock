using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

public sealed class SplitAffordancePolicyTests
{
    private static CapturedWindow Window(int id) => new()
    {
        Hwnd = new IntPtr(0x9000 + id),
        ProcessId = (uint)(10000 + id),
        WindowThreadId = (uint)(11000 + id),
        WindowIdentityToken = 12000 + id,
        ExePath = $"guest{id}.exe",
        OriginalClassName = "Pig",
        OriginalTitle = $"Guest {id}",
    };

    [Fact]
    public void FewerThanTwoTabs_DisablesTheAffordanceProjection()
    {
        var state = SplitAffordancePolicy.Build(new[] { Window(1) }, Window(1), null, null, presented: false);

        Assert.False(state.IsEnabled);
        Assert.Empty(state.Actions);
        Assert.Contains("another", state.ToolTip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoRelationship_ListsOnlyEligiblePartnerTabs()
    {
        var a = Window(1);
        var b = Window(2);
        var c = Window(3);
        var state = SplitAffordancePolicy.Build(
            new[] { a, b, c },
            active: a,
            left: null,
            right: null,
            presented: false,
            isEligible: candidate => !ReferenceEquals(candidate, c));

        Assert.True(state.IsEnabled);
        Assert.Single(state.Actions);
        Assert.Equal(SplitAffordanceActionKind.CreatePair, state.Actions[0].Kind);
        Assert.Same(a, state.Actions[0].Source);
        Assert.Same(b, state.Actions[0].Target);
    }

    [Fact]
    public void PresentedRelationship_OffersFocusAndEndActions()
    {
        var left = Window(1);
        var right = Window(2);
        var state = SplitAffordancePolicy.Build(
            new[] { left, right }, left, left, right, presented: true);

        Assert.Equal(
            new[]
            {
                SplitAffordanceActionKind.FocusLeft,
                SplitAffordanceActionKind.FocusRight,
                SplitAffordanceActionKind.EndRelationship,
            },
            state.Actions.Select(action => action.Kind));
    }

    [Fact]
    public void DormantRelationship_OffersResumeShowAndEndActions()
    {
        var left = Window(1);
        var right = Window(2);
        var unrelated = Window(3);
        var state = SplitAffordancePolicy.Build(
            new[] { left, right, unrelated }, unrelated, left, right, presented: false);

        Assert.Equal(
            new[]
            {
                SplitAffordanceActionKind.ResumeLeft,
                SplitAffordanceActionKind.ResumeRight,
                SplitAffordanceActionKind.EndRelationship,
            },
            state.Actions.Select(action => action.Kind));
    }

    [Fact]
    public void ControllerRelationshipSurvivesUnrelatedSelectionAndProjectsDormantActions()
    {
        var left = Window(1);
        var right = Window(2);
        var unrelated = Window(3);
        var controller = new SplitPresentationController();
        Assert.True(controller.DefinePair(left, right, left).Committed);
        Assert.True(controller.SuspendForGuest(unrelated));
        controller.SelectGuest(unrelated);

        var state = SplitAffordancePolicy.Build(
            new[] { left, right, unrelated }, unrelated,
            controller.Left, controller.Right, controller.IsPresented);

        Assert.Same(left, controller.Left);
        Assert.Same(right, controller.Right);
        Assert.Same(unrelated, controller.Foreground);
        Assert.Contains(state.Actions, action => action.Kind == SplitAffordanceActionKind.ResumeLeft);
        Assert.Contains(state.Actions, action => action.Kind == SplitAffordanceActionKind.EndRelationship);
    }

    [Fact]
    public void ActionBecomesInvalidWhenTargetLeavesWhileMenuIsOpen()
    {
        var left = Window(1);
        var right = Window(2);
        var action = SplitAffordancePolicy.Build(
            new[] { left, right }, left, left, right, presented: true).Actions[0];

        Assert.False(SplitAffordancePolicy.IsActionCurrent(
            action,
            new[] { left },
            left,
            right,
            presented: true));
    }

    [Fact]
    public void MemberRemovalClearsControllerAndInvalidatesOpenMenuActions()
    {
        var left = Window(1);
        var right = Window(2);
        var controller = new SplitPresentationController();
        Assert.True(controller.DefinePair(left, right, left).Committed);
        SplitAffordanceAction end = SplitAffordancePolicy.Build(
            new[] { left, right }, left, left, right, presented: true).Actions[^1];

        CapturedWindow? survivor = controller.HandleMemberRemoved(right);

        Assert.Same(left, survivor);
        Assert.False(controller.IsRelationshipDefined);
        Assert.False(SplitAffordancePolicy.IsActionCurrent(
            end,
            new[] { left },
            controller.Left,
            controller.Right,
            controller.IsPresented));
    }

    [Fact]
    public void StaleRelationshipProjectionFailsClosed()
    {
        var left = Window(1);
        var right = Window(2);
        var state = SplitAffordancePolicy.Build(
            new[] { left, right }, left, left, right, presented: true,
            isEligible: candidate => !ReferenceEquals(candidate, right));

        Assert.False(state.IsEnabled);
        Assert.Empty(state.Actions);
    }
}

public sealed class SplitAffordanceSourceContractTests
{
    [Fact]
    public void ContainerChromeContainsPersistentAccessibleSplitControl()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "Views", "ContainerWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "Views", "ContainerWindow.xaml.cs"));

        Assert.Contains("Content=\"Split ▾\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SplitAffordance\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanUseSplitAffordance}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SplitAffordancePolicy.Build", code, StringComparison.Ordinal);
        Assert.Contains("SplitPresentationController", code, StringComparison.Ordinal);
        Assert.Contains("SplitAffordancePolicy.IsActionCurrent", code, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TabDock.sln not found above test output directory.");
    }
}
