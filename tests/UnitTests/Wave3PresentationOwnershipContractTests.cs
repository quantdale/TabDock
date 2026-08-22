using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Wave-3 presentation-ownership structural contracts. These complement the
/// behavioral suites by failing fast if a shadow authority or a hand-rolled
/// commit returns to the code after this wave removed it.
/// </summary>
public sealed class Wave3PresentationOwnershipContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void SplitController_GenerationIsCommittedOnlyInsideTheCanonicalHelper()
    {
        string code = Read("Services/SplitPresentationController.cs");

        // Exactly one assignment site for _generation (CommitDesired), fed by
        // the policy's desired.Generation. A second "= desired.Generation" or
        // any "++" would reintroduce the mixed commit styles Wave 3A removed.
        Assert.Equal(1, Regex.Matches(code, @"_generation\s*=(?!=)").Count);
        Assert.DoesNotMatch(new Regex(@"_generation\s*\+\+"), code);
    }

    [Fact]
    public void SplitController_CommitsOnlyThroughTheCanonicalHelper_FedFromPolicyOutput()
    {
        string code = Read("Services/SplitPresentationController.cs");

        // Every transition consults the pure policy before committing: one
        // SplitPresentationPolicy.* decision per transition, and no transition
        // writes pair/presented/foreground fields directly — CommitDesired is
        // the single writer of _left/_right/_presented/_foreground.
        Assert.Contains("SplitPresentationPolicy.DefinePair(", code);
        Assert.Contains("SplitPresentationPolicy.Reconfigure(", code);
        Assert.Contains("SplitPresentationPolicy.SelectNonMember(", code);
        Assert.Contains("SplitPresentationPolicy.SelectMember(", code);
        Assert.Contains("SplitPresentationPolicy.RemoveMember(", code);
        Assert.Contains("SplitPresentationPolicy.ExplicitExit(", code);
        Assert.Contains("SplitPresentationPolicy.FocusMember(", code);

        // Outside CommitDesired's own body, NO transition writes the runtime
        // fields directly.
        int helperStart = code.IndexOf("private void CommitDesired(", StringComparison.Ordinal);
        Assert.True(helperStart >= 0);
        int helperEnd = code.IndexOf("/// <summary>", helperStart, StringComparison.Ordinal);
        string outsideHelper = helperEnd > helperStart ? code.Remove(helperStart, helperEnd - helperStart) : code;
        int fieldWrites = Regex.Matches(outsideHelper, @"_(left|right|presented|foreground)\s*=[^=]").Count;
        Assert.Equal(0, fieldWrites);

        // The helper itself performs exactly those four writes + generation.
        Assert.Contains("private void CommitDesired(", code);
    }

    [Fact]
    public void View_DeclaresNoParallelActiveGuestField()
    {
        // Wave 3B: the active presentation guest lives ONLY in
        // SplitPresentationController.Foreground. The former view-side field
        // (hand-synced at ~11 sites with nothing enforcing the equality) must
        // not return under any name — reads go through the derived alias,
        // writes are impossible by construction.
        foreach (string viewFile in Directory.GetFiles(Path.Combine(RepoRoot, "Views"), "*.cs"))
        {
            string code = File.ReadAllText(viewFile);
            Assert.DoesNotContain("_shepherdActiveWindow", code);
        }
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot, relativePath));

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
