using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Structural contracts for the redesigned WPF surfaces. These protect
/// semantic bindings, native-host placement, stable ValidationDriver IDs, and
/// keyboard/accessibility affordances without snapshotting cosmetic XAML.
/// </summary>
public sealed class ReleaseCandidateUiContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Container_PreservesNativeAndPresentationAuthorities()
    {
        string xaml = Read("Views/ContainerWindow.xaml");

        Assert.Single(Regex.Matches(xaml, "x:Name=\"ContentHost\""));
        Assert.Contains("ItemsSource=\"{Binding DisplayTabs}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding ActiveTab, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsSelected\" Value=\"{Binding IsActive, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Focusable=\"False\"", Slice(xaml, "x:Name=\"TabsListBox\"", "<!-- Inline capture"), StringComparison.Ordinal);

        foreach (string automationId in new[]
        {
            "GroupSelector",
            "SplitAffordance",
            "AddWindowButton",
            "CaptureRefresh",
            "CaptureAddSelected",
            "CaptureCancel",
            "TabClose",
            "SplitCompositeItem",
            "SplitHalfLeft",
            "SplitHalfRight",
            "SplitCloseLeft",
            "SplitCloseRight",
        })
        {
            Assert.Contains($"AutomationProperties.AutomationId=\"{automationId}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Container_ExposesKeyboardActivationAndDifferentiatedSplitNames()
    {
        string xaml = Read("Views/ContainerWindow.xaml");
        string code = Read("Views/ContainerWindow.xaml.cs");

        Assert.Contains("PreviewKeyDown=\"TabsListBox_PreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"WorkspaceTabs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"TabClose_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"SplitHalfClose_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"SplitHalf_KeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Focusable=\"True\" KeyboardNavigation.IsTabStop=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Left.AutomationName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Right.AutomationName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("private void SplitHalf_KeyDown", code, StringComparison.Ordinal);
        Assert.Contains("private void TabClose_Click", code, StringComparison.Ordinal);
        Assert.Contains("private void SplitHalfClose_Click", code, StringComparison.Ordinal);
        Assert.Contains("Key.Enter or Key.Space", code, StringComparison.Ordinal);
        Assert.Contains("if (cur is Button)", code, StringComparison.Ordinal);
        Assert.Contains("Key.Apps", code, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocusWithin", Slice(xaml, "Tag=\"LEFT\"", "Tag=\"RIGHT\""), StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocusWithin", Slice(xaml, "Tag=\"RIGHT\"", "</DataTemplate>"), StringComparison.Ordinal);
    }

    [Fact]
    public void Picker_UsesTargetSpecificNamesAndVisibleFocusContracts()
    {
        string standalone = Read("Views/CapturePickerWindow.xaml");
        string container = Read("Views/ContainerWindow.xaml");

        Assert.DoesNotContain("AutomationProperties.Name=\"Select window\"", standalone, StringComparison.Ordinal);
        Assert.Contains("StringFormat='Select {0}'", standalone, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText=\"Select this window for capture\"", standalone, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocusWithin, RelativeSource={RelativeSource AncestorType=ListBoxItem}", standalone, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Capture admission status\"", standalone, StringComparison.Ordinal);

        Assert.DoesNotContain("AutomationProperties.Name=\"Select window\"", container, StringComparison.Ordinal);
        Assert.Contains("StringFormat='Select {0}'", container, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocusWithin, RelativeSource={RelativeSource AncestorType=ListBoxItem}", container, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedStylesProvideFocusAndDarkControlStates()
    {
        string app = Read("App.xaml");
        string container = Read("Views/ContainerWindow.xaml");

        Assert.Contains("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\">", app, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"IsKeyboardFocusWithin\" Value=\"True\">", app, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{StaticResource TdSurfaceRaisedBrush}\" />", app, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Background\" Value=\"White\" />", Slice(app, "x:Key=\"TdComboBox\"", "x:Key=\"TdListItem\""), StringComparison.Ordinal);

        string captionStyle = Slice(container, "x:Key=\"CaptionButtonStyle\"", "x:Key=\"ChromePillButtonStyle\"");
        Assert.Contains("BorderBrush=\"{TemplateBinding BorderBrush}\"", captionStyle, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"{TemplateBinding BorderThickness}\"", captionStyle, StringComparison.Ordinal);
    }

    [Fact]
    public void PickerHandoff_UsesCanonicalStrongIdentityGate()
    {
        string model = Read("Models/WindowCaptureTarget.cs");
        string picker = Read("ViewModels/CapturePickerViewModel.cs");
        string container = Read("Views/ContainerWindow.xaml.cs");

        Assert.Contains("ProcessStartTimeUtcTicks", model, StringComparison.Ordinal);
        Assert.Contains("WindowThreadId", model, StringComparison.Ordinal);
        Assert.Contains("ProcessStartTimeUtcTicks", picker, StringComparison.Ordinal);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", picker, StringComparison.Ordinal);
        Assert.Contains("WindowIdentityGate.EvaluateBeforeCaptureToken", container, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProcessImagePath(pid)", Slice(container, "private static bool MatchesCaptureTarget", "    public void AddCapturedWindow"), StringComparison.Ordinal);
    }

    [Fact]
    public void CriticalAutomationIdsRemainPresentAndKnownRepetitionIsExplicit()
    {
        string[] files =
        {
            Read("Views/MainWindow.xaml"),
            Read("Views/CapturePickerWindow.xaml"),
            Read("Views/ContainerWindow.xaml"),
        };
        string combined = string.Join(Environment.NewLine, files);
        var ids = Regex.Matches(combined, "AutomationProperties.AutomationId=\\\"([^\\\"]+)\\\"")
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .ToList();

        foreach (string id in new[]
        {
            "LauncherCaptureButton",
            "PendingRecoveryBanner",
            "CaptureRefresh",
            "CaptureGroupThese",
            "CaptureAddSelected",
            "CaptureCancel",
            "GroupSelector",
            "AddWindowButton",
            "SplitAffordance",
            "ContentHost",
        })
        {
            Assert.Contains(id, ids);
        }

        // These IDs intentionally repeat for repeated rows or the same shared
        // picker in standalone/inline surfaces; all other critical IDs are
        // expected to be unique in the static markup.
        var allowedRepeated = new HashSet<string>(StringComparer.Ordinal)
        {
            "CaptureRefresh",
            "CaptureCancel",
            "CaptureSelectionSummary",
            "CaptureAdmissionStatus",
            "TabClose",
        };
        foreach (IGrouping<string, string> duplicate in ids.GroupBy(id => id).Where(group => group.Count() > 1))
            Assert.Contains(duplicate.Key, allowedRepeated);
    }

    [Fact]
    public void ResponsiveSurfacesKeepCriticalContentReachable()
    {
        string launcher = Read("Views/MainWindow.xaml");
        string picker = Read("Views/CapturePickerWindow.xaml");
        string container = Read("Views/ContainerWindow.xaml");

        Assert.Contains("TextWrapping=\"Wrap\"", launcher, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", launcher, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", picker, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", picker, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Auto\"", container, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"WorkspaceTabs\"", container, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"220\"", container, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationDriverTargetsCurrentStableUiContracts()
    {
        string scenarios = Read("tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs");
        string picker = Read("tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Picker.cs");
        string split = Read("tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.Split.cs");
        string keyboard = Read("tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.KeyboardInput.cs");

        Assert.Contains("IsCapturePickerTitle", scenarios, StringComparison.Ordinal);
        Assert.Contains("CaptureGroupThese", scenarios, StringComparison.Ordinal);
        Assert.Contains("GroupSelector", split, StringComparison.Ordinal);
        Assert.Contains("WorkspaceTabs", keyboard, StringComparison.Ordinal);
        Assert.DoesNotContain("t == \"Capture windows\"", scenarios, StringComparison.Ordinal);
        Assert.DoesNotContain("Group ▾", picker, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not locate TabDock.csproj above the test output directory.");
    }
}
