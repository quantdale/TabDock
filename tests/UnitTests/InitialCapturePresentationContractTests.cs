using System;
using System.IO;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Structural guard for the initial-capture presentation race. Behavioral
/// coalescing/final-pass semantics are covered by RequestRelayoutFinalPassTests;
/// this test pins the capture boundary to that existing scheduler contract.
/// </summary>
public sealed class InitialCapturePresentationContractTests
{
    [Fact]
    public void NewlyAddedCapturedTab_RequestsFinalPresentationReconciliation()
    {
        string code = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "Views",
            "ContainerWindow.CapturePresentation.cs"));

        Assert.Contains("_viewModel.Tabs.CollectionChanged += CaptureTabs_CollectionChanged", code);
        Assert.Contains("NotifyCollectionChangedAction.Add", code);
        Assert.Contains("RequestRelayout(ensureFinalPass: true);", code);
        Assert.Contains("_viewModel.Tabs.CollectionChanged -= CaptureTabs_CollectionChanged", code);

        // The fix must remain inside the existing frame-coalesced presentation
        // authority; do not regress into timing guesses or a second native path.
        Assert.DoesNotContain("Thread.Sleep", code);
        Assert.DoesNotContain("Task.Delay", code);
        Assert.DoesNotContain("DispatcherTimer", code);
        Assert.DoesNotContain("PositionAndShow(", code);
        Assert.DoesNotContain("SetWindowPos(", code);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TabDock.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate TabDock.csproj above test base directory '{AppContext.BaseDirectory}'.");
    }
}
