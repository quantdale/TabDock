using System;
using System.IO;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Guards the first-render presentation repair. A freshly-created container can
/// begin admitting captured windows immediately after Window.Show(), before its
/// native content marker has completed the first rendered layout. The first
/// ActiveTab notification must therefore be reconciled again once that boundary
/// is reached; otherwise the tab strip can be correct while the content area
/// remains black until a later split/resize transition happens to re-glue it.
/// </summary>
public sealed class InitialCapturePresentationContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Container_FirstRenderedFrame_ReconcilesActiveGuestThenForcesFinalRelayout()
    {
        string code = File.ReadAllText(Path.Combine(RepoRoot, "Views", "ContainerWindow.Split.cs"));

        int start = code.IndexOf("protected override void OnContentRendered(EventArgs e)", StringComparison.Ordinal);
        Assert.True(start >= 0, "ContainerWindow must keep an explicit first-render lifecycle hook.");

        int end = code.IndexOf("protected override void OnClosed(EventArgs e)", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not isolate the OnContentRendered implementation.");

        string body = code[start..end];
        const string sync = "SyncShepherdActiveWindow();";
        const string finalRelayout = "RequestRelayout(ensureFinalPass: true);";

        int syncIndex = body.IndexOf(sync, StringComparison.Ordinal);
        int relayoutIndex = body.IndexOf(finalRelayout, StringComparison.Ordinal);

        Assert.True(syncIndex >= 0,
            "The first rendered frame must reconcile an ActiveTab selected before native presentation was ready.");
        Assert.True(relayoutIndex > syncIndex,
            "After reconciling logical presentation, the first rendered frame must force a final post-layout relayout.");
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
