using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Narrow source-contract guards for interaction regressions whose failure
/// mechanism lives at the WPF/Win32 wiring boundary rather than in a cheap
/// headless unit seam. These deliberately assert the production call-site
/// invariants so a future refactor cannot silently restore the exact defects
/// fixed by 3591ee3 while lower-level model tests remain green.
/// </summary>
public sealed class InteractionSourceContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void CtrlTab_UsesAuthoritativeActiveTabBinding_NotDisplayTabsIndexWrites()
    {
        string code = Read("Views/ContainerWindow.xaml.cs");
        string handler = Slice(
            code,
            "private void ContainerWindow_PreviewKeyDown",
            "private void ContainerWindow_Closing");

        // Regression 3591ee3: Tabs is the identity/order collection, while the
        // ListBox is bound to DisplayTabs. Once A+B become one split-composite
        // slot, a Tabs-space integer is not a valid DisplayTabs-space integer.
        // The handler must switch the authoritative ActiveTab and let bindings
        // select the visual item; writing SelectedIndex here reintroduces the
        // exact bug even though GroupViewModelDisplayTabsTests still passes.
        Assert.Matches(
            new Regex(@"_viewModel\s*\.\s*SetActiveTab\s*\(\s*_viewModel\s*\.\s*Tabs\s*\[\s*next\s*\]\s*\)\s*;"),
            handler);
        Assert.DoesNotMatch(
            new Regex(@"TabsListBox\s*\.\s*SelectedIndex\s*="),
            handler);

        string xaml = Read("Views/ContainerWindow.xaml");
        Assert.Contains("ItemsSource=\"{Binding DisplayTabs}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding ActiveTab, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsSelected\" Value=\"{Binding IsActive, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusedPaneCache_OnlyShortCircuitsGuestsThatAreStillVisible()
    {
        string code = Read("Views/ContainerWindow.xaml.cs");

        string single = Slice(
            code,
            "private void LayoutShepherdActiveWindow",
            "private void LayoutSplitPanes");
        Assert.Matches(
            new Regex(
                @"NativeMethods\s*\.\s*IsWindowVisible\s*\(\s*_shepherdActiveWindow\s*\.\s*Hwnd\s*\)\s*&&\s*IsRefusingPane\s*\(\s*_shepherdActiveWindow\s*,\s*rect\s*\)"),
            single);
        Assert.Matches(
            new Regex(@"_shepherd\s*\.\s*PositionAndShow\s*\(\s*_shepherdActiveWindow\s*,\s*containerHwnd\s*,\s*rect\s*\)\s*;"),
            single);

        string split = Slice(
            code,
            "private void LayoutSplitPanes",
            "private void RefreshSizeConstraint");
        Assert.Matches(
            new Regex(
                @"NativeMethods\s*\.\s*IsWindowVisible\s*\(\s*top\s*\.\s*Hwnd\s*\)\s*&&\s*IsRefusingPane\s*\(\s*top\s*,\s*topRect\s*\)"),
            split);
        Assert.Matches(
            new Regex(
                @"NativeMethods\s*\.\s*IsWindowVisible\s*\(\s*bottom\s*\.\s*Hwnd\s*\)\s*&&\s*IsRefusingPane\s*\(\s*bottom\s*,\s*bottomRect\s*\)"),
            split);
        Assert.Matches(
            new Regex(@"_shepherd\s*\.\s*PositionGuestsDeferred\s*\(\s*top\s*,\s*topRect\s*,\s*bottom\s*,\s*bottomRect\s*,\s*containerHwnd\s*\)\s*;"),
            split);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot, relativePath));

    private static string Slice(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker not found: {startMarker}");
        int end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker not found after {startMarker}: {endMarker}");
        return text.Substring(start, end - start);
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
