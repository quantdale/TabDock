using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using TabDock.Infrastructure;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class CaptureSelectionHighlightTests
{
    [Theory]
    [InlineData(false, "Views/CapturePickerWindow.xaml")]
    [InlineData(true, "Views/CapturePickerWindow.xaml")]
    [InlineData(false, "Views/ContainerWindow.xaml")]
    [InlineData(true, "Views/ContainerWindow.xaml")]
    public void CheckedUnfocusedRow_UsesCaptureSelection_NotListSelection(bool reducedMotion, string templatePath)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(reducedMotion))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                ResourceDictionary resources = PickerTemplateLoader.Load(templatePath);
                var selected = new SolidColorBrush(((SolidColorBrush)resources["TdAccentSoftBrush"]).Color);
                var list = new ListBox
                {
                    Width = 420,
                    Height = 180,
                    ItemTemplate = (DataTemplate)resources["PickerRow"],
                };
                var checkedRow = Candidate(0x301, "Checked target");
                var other = Candidate(0x302, "Focused target");
                checkedRow.IsSelected = true;
                list.Items.Add(checkedRow);
                list.Items.Add(other);
                host.Root.Children.Add(list);
                host.Realize();
                list.SelectedIndex = 1;
                list.UpdateLayout();
                WaitUntil(dispatcher, () => IsHighlighted(list, 0) && !IsHighlighted(list, 1));

                Assert.False(((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0)).IsSelected);
                Assert.True(IsHighlighted(list, 0));
                Assert.False(IsHighlighted(list, 1));
                var focusedItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
                Assert.True(focusedItem.Focus());
                StaDispatcherFixture.Settle(dispatcher);
                Assert.True(checkedRow.IsSelected);
                Assert.False(other.IsSelected);
                WaitUntil(dispatcher, () => Same(OverlayColor(list, 0), selected.Color));
                Assert.True(IsHighlighted(list, 0));

                other.IsSelected = true;
                WaitUntil(dispatcher, () => IsHighlighted(list, 0) && IsHighlighted(list, 1));
                Assert.True(IsHighlighted(list, 0));
                Assert.True(IsHighlighted(list, 1));
                Assert.Equal(1, list.SelectedIndex);

                checkedRow.IsSelected = false;
                other.IsSelected = false;
                WaitUntil(dispatcher, () => !IsHighlighted(list, 0) && !IsHighlighted(list, 1));
                Assert.False(IsHighlighted(list, 0));
                Assert.False(IsHighlighted(list, 1));

                list.Items.Remove(checkedRow);
                host.Realize();
                list.Items.Insert(0, checkedRow);
                checkedRow.IsSelected = true;
                host.Realize();
                WaitUntil(dispatcher, () => list.ItemContainerGenerator.ContainerFromIndex(0) is not null && IsHighlighted(list, 0));
                Assert.True(IsHighlighted(list, 0));

                var rebound = Candidate(0x303, "Replacement");
                list.Items[0] = rebound;
                host.Realize();
                WaitUntil(dispatcher, () => list.ItemContainerGenerator.ContainerFromIndex(0) is not null && !IsHighlighted(list, 0));
                Assert.False(IsHighlighted(list, 0));
                rebound.IsSelected = true;
                WaitUntil(dispatcher, () => IsHighlighted(list, 0));
                Assert.True(IsHighlighted(list, 0));
            }
        });
    }

    [Fact]
    public void WorkspaceTabTemplate_DoesNotOverrideListSelection()
    {
        string container = File.ReadAllText(Path.Combine(RepoRoot(), "Views/ContainerWindow.xaml"));
        int tab = container.IndexOf("DataType=\"{x:Type vm:TabViewModel}\"", StringComparison.Ordinal);
        int split = container.IndexOf("DataType=\"{x:Type vm:SplitCompositeViewModel}\"", StringComparison.Ordinal);
        Assert.True(tab >= 0 && split > tab);
        string tabTemplate = container.Substring(tab, split - tab);
        Assert.DoesNotContain("IsMotionSelected", tabTemplate, StringComparison.Ordinal);
    }

    private static bool IsHighlighted(ListBox list, int index)
    {
        if (list.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem)
            return false;
        Border? overlay = Overlay(list, index);
        return overlay is not null && overlay.Opacity > 0.5;
    }

    private static Color OverlayColor(ListBox list, int index)
    {
        Border overlay = Overlay(list, index)!;
        return ((SolidColorBrush)overlay.Background).Color;
    }

    private static Border? Overlay(ListBox list, int index)
    {
        if (list.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item)
            return null;
        return FindRole(item, MotionProfile.MotionOverlayRole.Fill);
    }

    private static Border? FindRole(DependencyObject parent, MotionProfile.MotionOverlayRole role)
    {
        if (parent is Border border && MotionProfile.GetOverlayRole(border) == role)
            return border;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            Border? found = FindRole(VisualTreeHelper.GetChild(parent, i), role);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static CapturePickerViewModel.WindowInfo Candidate(int hwnd, string title)
        => new(new IntPtr(hwnd), (uint)hwnd, "PickerWindow", title, @"C:\Apps\picker.exe");

    private static void WaitUntil(Dispatcher dispatcher, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!done() && DateTime.UtcNow < deadline)
            StaDispatcherFixture.Pump(dispatcher, TimeSpan.FromMilliseconds(40));
        StaDispatcherFixture.Settle(dispatcher);
    }

    private static bool Same(Color left, Color right) => Channel(left) == Channel(right);
    private static string Channel(Color color) => $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TabDock.sln was not found.");
    }
}

internal static class PickerTemplateLoader
{
    public static ResourceDictionary Load(string relativePath)
    {
        string root = FindRepoRoot();
        string app = File.ReadAllText(Path.Combine(root, "App.xaml"));
        string resources = Between(app, "<Application.Resources>", "</Application.Resources>");
        string view = File.ReadAllText(Path.Combine(root, relativePath));
        int marker = view.IndexOf("MotionProfile.IsMotionSelected", StringComparison.Ordinal);
        if (marker < 0)
            throw new InvalidOperationException($"{relativePath} does not bind capture selection.");
        int start = view.LastIndexOf("<DataTemplate>", marker, StringComparison.Ordinal);
        int end = view.IndexOf("</DataTemplate>", marker, StringComparison.Ordinal);
        string template = view.Substring(start, end + "</DataTemplate>".Length - start)
            .Replace("<DataTemplate>", "<DataTemplate x:Key=\"PickerRow\">", StringComparison.Ordinal);
        string wrapped =
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            "xmlns:converters=\"clr-namespace:TabDock.Converters;assembly=TabDock\" " +
            "xmlns:infrastructure=\"clr-namespace:TabDock.Infrastructure;assembly=TabDock\" " +
            "xmlns:infra=\"clr-namespace:TabDock.Infrastructure;assembly=TabDock\">" +
            resources +
            template +
            "</ResourceDictionary>";
        return (ResourceDictionary)XamlReader.Parse(wrapped);
    }

    private static string Between(string text, string startToken, string endToken)
    {
        int start = text.IndexOf(startToken, StringComparison.Ordinal);
        int end = text.IndexOf(endToken, StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new InvalidOperationException($"Could not find {startToken}.");
        return text.Substring(start + startToken.Length, end - start - startToken.Length);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TabDock.sln was not found.");
    }
}
