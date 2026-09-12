using System;
using System.IO;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Guards the frontend overhaul's structural contracts without coupling tests to
/// screenshot pixels. Functional automation IDs and shared design-system tokens
/// stay stable while the visual layer remains free to evolve.
/// </summary>
public sealed class FrontendDesignContractTests
{
    private static string RepoRoot => FindRepoRoot();

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void SharedDesignSystemDefinesCoreSurfaceAndInteractionTokens()
    {
        string xaml = Read("App.xaml");

        Assert.Contains("x:Key=\"TdChromeBrush\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdSurfaceInteractiveBrush\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdFocusBrush\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdToolbarButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdHeading\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ContextMenu}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ToolTip}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherPreservesDriverAndOperationalAutomationContracts()
    {
        string xaml = Read("Views/MainWindow.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"LauncherCaptureButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"PendingRecoveryBanner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"PendingRecoverySummary\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CaptureAdmissionStatus\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupsListView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Build your first workspace", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CapturePickerPreservesAutomationIdsAndSharedSelectionStyling()
    {
        string xaml = Read("Views/CapturePickerWindow.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"CaptureRefresh\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CaptureSelectionSummary\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CaptureGroupThese\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CaptureCancel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TdCheckBox}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerPreservesPresentationAndDriverContracts()
    {
        string xaml = Read("Views/ContainerWindow.xaml");

        Assert.Contains("CaptionHeight=\"38\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContentHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ContentHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"GroupSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"WorkspaceTabs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SplitAffordance\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SplitHalfLeft\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SplitHalfRight\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CaptureAddSelected\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TdCheckBox}\"", xaml, StringComparison.Ordinal);
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
