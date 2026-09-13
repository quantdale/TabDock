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
        Assert.Contains("AutomationProperties.AutomationId=\"LauncherEmptyStateHeading\"", xaml, StringComparison.Ordinal);
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

        // Declaration order alone is not nesting: the copy must live inside the
        // popup, so reintroducing a WPF sibling (hidden by the native host's
        // airspace) cannot satisfy this contract.
        int popupStart = xaml.IndexOf("<Popup x:Name=\"EmptyStateOverlay\"", StringComparison.Ordinal);
        int popupEnd = popupStart < 0 ? -1 : xaml.IndexOf("</Popup>", popupStart, StringComparison.Ordinal);
        Assert.True(popupStart >= 0 && popupEnd > popupStart, "empty-state overlay popup must be closed");
        Assert.True(emptyTextIndex > popupStart && emptyTextIndex < popupEnd,
            "empty-state copy must be nested inside the airspace-safe popup");
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

    [Fact]
    public void InlineCapturePanelNeverInheritsAWorkspaceDataContext()
    {
        string xaml = Read("Views/ContainerWindow.xaml");

        // The panel's rows bind to CapturePickerViewModel, which only exists
        // while the panel is open. A local null DataContext keeps the initial
        // load from resolving those paths against the container's GroupViewModel
        // (one logged binding error per path otherwise).
        int panel = xaml.IndexOf("x:Name=\"CapturePanel\"", StringComparison.Ordinal);
        Assert.True(panel >= 0, "CapturePanel not found");
        Assert.Contains("DataContext=\"{x:Null}\"", xaml.Substring(panel, 400), StringComparison.Ordinal);

        // Explicit item alignments remove the theme style's FindAncestor
        // ItemsControl binding, which logs an error during container realization.
        string tabStrip = Slice(xaml, "x:Name=\"TabsListBox\"", "ListBox.Resources");
        Assert.Contains("HorizontalContentAlignment\" Value=\"Stretch\"", tabStrip, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment\" Value=\"Stretch\"", tabStrip, StringComparison.Ordinal);
    }

    [Fact]
    public void AppLevelListBoxItemDefaultsOverrideThemeBindings()
    {
        string app = Read("App.xaml");

        // The theme ListBoxItem style binds content alignment through
        // FindAncestor ItemsControl; those bindings log an error per realized
        // item before the list's own ItemContainerStyle applies. Explicit
        // app-level defaults replace them (verified at runtime: a full drive of
        // launcher, picker, and container logs zero WPF binding errors).
        string listBoxItemStyle = Slice(app, "TargetType=\"{x:Type ListBoxItem}\"", "TdSectionLabel");
        Assert.Contains("HorizontalContentAlignment\" Value=\"Stretch\"", listBoxItemStyle, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment\" Value=\"Stretch\"", listBoxItemStyle, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessibilityContractsExposeNamesAndAvoidDeadTabStops()
    {
        string picker = Read("Views/CapturePickerWindow.xaml");
        string container = Read("Views/ContainerWindow.xaml");

        // The capture lists are critical controls and must be announced with a
        // meaningful name, not a bare "list".
        Assert.Contains("AutomationProperties.Name=\"Capturable windows\"", picker, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Capturable windows to add\"", container, StringComparison.Ordinal);

        // The native content marker must never be a keyboard tab stop: focusing
        // it parks focus on a pane with no interactive behavior.
        int host = container.IndexOf("x:Name=\"ContentHost\"", StringComparison.Ordinal);
        Assert.True(host >= 0, "ContentHost not found");
        Assert.Contains("Focusable=\"False\"", container.Substring(host, 200), StringComparison.Ordinal);

        // The blocked/disabled primary action explains itself through help text
        // and a disabled-hover tooltip.
        Assert.Contains("AutomationProperties.HelpText=\"{Binding GroupSelectedHelpText}\"", picker, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ShowOnDisabled=\"True\"", picker, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding GroupSelectedHelpText}\"", container, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ShowOnDisabled=\"True\"", container, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ShowOnDisabled=\"True\"", Read("Views/MainWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void DenseTabStripGrowsForOverflowInsteadOfSqueezingTabs()
    {
        string xaml = Read("Views/ContainerWindow.xaml");
        string code = Read("Views/ContainerWindow.xaml.cs");

        // A fixed 42px strip leaves the tabs only 22px once the horizontal
        // scrollbar appears (measured on the real app: icons clipped, active
        // tab off-screen). MinHeight lets the strip grow by the scrollbar row.
        string stripHeader = Slice(xaml, "Dense tab strip", "x:Name=\"TabsListBox\"");
        Assert.Contains("MinHeight=\"42\"", stripHeader, StringComparison.Ordinal);
        Assert.DoesNotContain(" Height=\"42\"", stripHeader, StringComparison.Ordinal);

        // The active tab must be scrolled into view when the strip overflows.
        Assert.Contains("TabsListBox.ScrollIntoView(activeTab)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedTokensReplaceRawPaletteAndFontLiteralsInViews()
    {
        string app = Read("App.xaml");
        string launcher = Read("Views/MainWindow.xaml");
        string picker = Read("Views/CapturePickerWindow.xaml");
        string container = Read("Views/ContainerWindow.xaml");

        Assert.Contains("x:Key=\"TdSplitActiveBrush\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdSplitHoverBrush\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdContentVoidBrush\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdUiFont\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdMonoFont\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdIconFont\"", app, StringComparison.Ordinal);

        // Split-half tints and font stacks resolve through shared tokens, not
        // per-view literals that drift from the palette.
        Assert.Contains("{StaticResource TdSplitActiveBrush}", container, StringComparison.Ordinal);
        Assert.Contains("{StaticResource TdSplitHoverBrush}", container, StringComparison.Ordinal);
        Assert.Contains("{StaticResource TdContentVoidBrush}", container, StringComparison.Ordinal);
        Assert.DoesNotContain("#07090D", container, StringComparison.Ordinal);
        foreach (string view in new[] { launcher, picker, container })
        {
            Assert.DoesNotContain("Cascadia Mono", view, StringComparison.Ordinal);
            Assert.DoesNotContain("Segoe UI Variable", view, StringComparison.Ordinal);
            Assert.DoesNotContain("Segoe Fluent Icons", view, StringComparison.Ordinal);
        }

        // Removed speculative tokens stay removed until a consumer exists.
        Assert.DoesNotContain("x:Key=\"TdSuccessBrush\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"TdRaisedCard\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void DwmChromePaletteMatchesSharedTokenDefinitions()
    {
        string app = Read("App.xaml");
        string helper = Read("Infrastructure/WindowChromeTheme.cs");

        // WindowChromeTheme packs COLORREF values for DWM; keep them in lockstep
        // with the App.xaml tokens the WPF window chrome renders with.
        Assert.Contains("x:Key=\"TdChromeBrush\" Color=\"#0C0F14\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdTextBrush\" Color=\"#F7F8FA\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TdBorderBrush\" Color=\"#272D38\"", app, StringComparison.Ordinal);
        Assert.Contains("ToColorRef(0x0C, 0x0F, 0x14)", helper, StringComparison.Ordinal);
        Assert.Contains("ToColorRef(0xF7, 0xF8, 0xFA)", helper, StringComparison.Ordinal);
        Assert.Contains("ToColorRef(0x27, 0x2D, 0x38)", helper, StringComparison.Ordinal);
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
