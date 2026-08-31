using System;
using System.IO;
using System.Linq;
using Xunit;

namespace TabDock.UnitTests;

public sealed class CaptionCenteringTests
{
    private static string RepoRoot => FindRepoRoot();
    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void CaptionUsesTrueCenteredLayout()
    {
        string xaml = Read("Views/ContainerWindow.xaml");
        // The caption's outer Grid must be * Auto * so the middle Auto column is truly centered to the window.
        Assert.Contains("<ColumnDefinition Width=\"*\" />", xaml, StringComparison.Ordinal);
        // Must have exactly one Auto column for the title (the centered region) inside the caption Grid.
        // Check that the title TextBlock lives in Grid.Column="1" with HorizontalAlignment Center and TextAlignment Center.
        Assert.Contains("Grid.Column=\"1\" HorizontalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        // MaxWidth trimming must still exist so a long title does not cover side controls.
        Assert.Contains("MaxWidth=\"220\"", xaml, StringComparison.Ordinal);
        // Rename TextBox must also be centered in the same column.
        int renameIdx = xaml.IndexOf("x:Name=\"RenameBox\"", StringComparison.Ordinal);
        Assert.True(renameIdx > 0, "RenameBox must exist");
        string renameSlice = xaml.Substring(Math.Max(0, renameIdx - 400), 400);
        Assert.Contains("HorizontalAlignment=\"Center\"", renameSlice, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptionTitleIsNotLeftAlignedRemainder()
    {
        string xaml = Read("Views/ContainerWindow.xaml");
        // Old layout had title in Column 1 with no HorizontalAlignment Center and * remainder column was the only centering.
        // Ensure the new layout does not have a left-aligned TextBlock in the caption without center.
        // Count occurrences of Text="{Binding Name}" that also have HorizontalAlignment Center within same element.
        int count = 0;
        int idx = 0;
        while ((idx = xaml.IndexOf("Text=\"{Binding Name}\"", idx, StringComparison.Ordinal)) >= 0)
        {
            string window = xaml.Substring(Math.Max(0, idx - 300), 500);
            if (window.Contains("HorizontalAlignment=\"Center\""))
                count++;
            idx += 10;
        }
        Assert.True(count >= 1, "Title TextBlock must be centered");
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "TabDock.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("repo root not found");
        }
        throw new InvalidOperationException("repo root not found");
    }
}
