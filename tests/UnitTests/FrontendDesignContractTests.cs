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

    [Fact]
    public void SharedDesignSystemTemplatesSystemThemedControls()
    {
        string app = Read("App.xaml");

        // ComboBox, ContextMenu, MenuItem, ToolTip, and ScrollBar must be fully
        // templated: the default templates leak the user's system theme/accent
        // color into these surfaces on some desktops.
        Assert.Contains("x:Key=\"TdFocusVisualStyle\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdScrollThumb\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdVerticalScrollBar\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdHorizontalScrollBar\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ScrollBar}\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Popup\"", Slice(app, "x:Key=\"TdComboBox\"", "x:Key=\"TdCheckBox\""), StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ComboBoxItem}\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Popup\"", Slice(app, "TargetType=\"{x:Type MenuItem}\"", "TargetType=\"{x:Type Separator}\""), StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SubmenuArrow\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ToolTip}\"", app, StringComparison.Ordinal);

        // Keyboard focus is communicated by border color, never by growing the
        // border thickness (which shifts content by a pixel).
        Assert.DoesNotContain("TargetName=\"ButtonBorder\" Property=\"BorderThickness\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetName=\"InputBorder\" Property=\"BorderThickness\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardWindowsKeepDarkDwmChromeAgainstSystemTheme()
    {
        string native = Read("NativeMethods.cs");
        string helper = Read("Infrastructure/WindowChromeTheme.cs");
        string launcher = Read("Views/MainWindow.xaml.cs");
        string picker = Read("Views/CapturePickerWindow.xaml.cs");

        Assert.Contains("DWMWA_USE_IMMERSIVE_DARK_MODE = 20", native, StringComparison.Ordinal);
        Assert.Contains("DWMWA_CAPTION_COLOR = 35", native, StringComparison.Ordinal);
        Assert.Contains("DWMWA_BORDER_COLOR = 34", native, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute", helper, StringComparison.Ordinal);
        Assert.Contains("WindowChromeTheme.ApplyDarkChrome(this)", launcher, StringComparison.Ordinal);
        Assert.Contains("WindowChromeTheme.ApplyDarkChrome(this)", picker, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerEmptyStateLivesInAnAirspaceSafeOverlay()
    {
        string xaml = Read("Views/ContainerWindow.xaml");

        Assert.Contains("<Popup x:Name=\"EmptyStateOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Placement=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Condition Binding=\"{Binding Tabs.Count}\" Value=\"0\" />", xaml, StringComparison.Ordinal);

        // The prompt must not be a WPF sibling of the native host: the child
        // HWND always paints above siblings (airspace), hiding it entirely.
        int hostIndex = xaml.IndexOf("x:Name=\"ContentHost\"", StringComparison.Ordinal);
        int emptyTextIndex = xaml.IndexOf("This workspace is empty", StringComparison.Ordinal);
        Assert.True(hostIndex >= 0 && emptyTextIndex > hostIndex, "empty-state copy must follow the content host");
        Assert.Contains(
            "<Popup x:Name=\"EmptyStateOverlay\"",
            xaml.Substring(hostIndex, emptyTextIndex - hostIndex),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DenseRowsExposeHoverAndKeyboardFocusStates()
    {
        string picker = Read("Views/CapturePickerWindow.xaml");
        string container = Read("Views/ContainerWindow.xaml");

        Assert.Contains("IsMouseOver, RelativeSource={RelativeSource AncestorType=ListBoxItem}", picker, StringComparison.Ordinal);
        Assert.Contains("IsMouseOver, RelativeSource={RelativeSource AncestorType=ListBoxItem}", container, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocusWithin, RelativeSource={RelativeSource AncestorType=ListBoxItem}", picker, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocusWithin, RelativeSource={RelativeSource AncestorType=ListBoxItem}", container, StringComparison.Ordinal);
    }

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
