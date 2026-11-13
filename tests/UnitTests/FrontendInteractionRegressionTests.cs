using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using TabDock.Infrastructure;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using TabDock.Views;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>Managed routed events and real production layouts; no native input or visible windows.</summary>
[Collection("MotionFeedbackLifecycle")]
public sealed class FrontendInteractionRegressionTests
{
    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void LauncherRowButton_OpensItsOwnWorkspaceExactlyOnce(Key key)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var host = HiddenVisualHost.Create();
            Group a = fixture.Manager.CreateGroup("A");
            Group b = fixture.Manager.CreateGroup("B");
            var vm = new MainViewModel(fixture.Manager) { SelectedGroup = a };
            var requests = new List<Group>();
            vm.OpenGroupRequested += (_, group) => requests.Add(group);
            var window = new MainWindow(vm, SharedTemplateLoader.Load());
            try
            {
                // Keep the logical Window ancestor for the production row command binding.
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(850, 580));
                root.Arrange(new Rect(0, 0, 850, 580));
                root.UpdateLayout();
                var list = (ListView)window.FindName("GroupsListView");
                var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(b);
                Button button = Descendants<Button>(row).Single();
                StaDispatcherFixture.Focus(button);
                list.SelectedItem = a;
                SendManagedKey(button, key, host);
                Assert.Same(b, Assert.Single(requests));
                Assert.Same(a, vm.SelectedGroup);

                requests.Clear();
                var selectedRow = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(a);
                StaDispatcherFixture.Focus(selectedRow);
                SendManagedKey(selectedRow, Key.Enter, host);
                Assert.Same(a, Assert.Single(requests));
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("Views/CapturePickerWindow.xaml", false)]
    [InlineData("Views/CapturePickerWindow.xaml", true)]
    [InlineData("Views/ContainerWindow.xaml", false)]
    [InlineData("Views/ContainerWindow.xaml", true)]
    public void CaptureRowSpace_TogglesCaptureStateWithoutStealingCheckboxInput(string path, bool reducedMotion)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var host = HiddenVisualHost.Create();
            using var motion = MotionProfile.OverrideReducedMotion(reducedMotion);
            using var picker = fixture.Picker(dispatcher);
            int submissions = 0;
            picker.GroupingRequested += (_, _) => submissions++;
            FrameworkElement root = LoadLayout(path);
            root.DataContext = picker;
            if (path.Contains("Container", StringComparison.Ordinal))
            {
                var panel = (Border)root.FindName("CapturePanel");
                panel.DataContext = picker;
                panel.Visibility = Visibility.Visible;
            }
            var list = Descendants<ListBox>(root).Single(l => l.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path.Path == "FilteredWindows");
            // Extract only the real picker list, retaining production attached behavior and template.
            ((Grid)list.Parent).Children.Remove(list);
            list.Width = 450;
            list.Height = 180;
            host.Root.Children.Add(list);
            list.DataContext = picker;
            host.Realize();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.Contains(Descendants<TextBlock>(row), text => text.IsVisible && text.Text == "\uE737");
            TextBlock title = Descendants<TextBlock>(row).Single(text => text.Text == picker.Windows[0].Title);
            Assert.Equal(picker.Windows[0].Title, title.ToolTip);
            StaDispatcherFixture.Focus(row);
            SendManagedKey(row, Key.Space, host);
            Assert.True(picker.Windows[0].IsSelected);
            Assert.Equal(1, picker.SelectedCount);
            Assert.True(picker.GroupSelectedCommand.CanExecute(null));
            Border fill = ProductionTemplateContinuityTests.FindRole(row, MotionProfile.MotionOverlayRole.Fill)!;
            TabIconNonblockingTests.PumpUntil(dispatcher, () => fill.Opacity > .999);
            picker.GroupSelectedCommand.Execute(null);
            Assert.Equal(1, submissions);
            SendManagedKey(row, Key.Space, host);
            Assert.False(picker.Windows[0].IsSelected);
            Assert.False(picker.GroupSelectedCommand.CanExecute(null));
            TabIconNonblockingTests.PumpUntil(dispatcher, () => fill.Opacity < .001);

            CheckBox check = Descendants<CheckBox>(row).Single();
            StaDispatcherFixture.Focus(check);
            SendManagedKey(check, Key.Space, host);
            Assert.True(picker.Windows[0].IsSelected);
            Assert.Equal(1, picker.SelectedCount);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InlineMinimumLayout_ShowsACompleteCandidateAndContainedActions(bool overflowingTabs)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var picker = fixture.Picker(dispatcher);
            using var host = HiddenVisualHost.Create();
            using var motion = MotionProfile.OverrideReducedMotion(true);
            FrameworkElement root = LoadLayout("Views/ContainerWindow.xaml");
            root.Width = 420;
            root.Height = 300;
            root.DataContext = fixture.Workspace;
            var panel = (Border)root.FindName("CapturePanel");
            panel.DataContext = picker;
            panel.Visibility = Visibility.Visible;
            var tabs = (ListBox)root.FindName("TabsListBox");
            if (overflowingTabs)
                tabs.ItemsSource = Enumerable.Range(0, 12).Select(i => new TabViewModel(new CapturedWindow { OriginalTitle = "Long fixture tab " + i })).ToArray();
            host.Root.Children.Add(root);
            host.Realize();
            StaDispatcherFixture.Settle(dispatcher);
            root.UpdateLayout();
            var list = Descendants<ListBox>(panel).Single();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var viewport = Descendants<ScrollContentPresenter>(list).First();
            Assert.True(row.ActualHeight > 0);
            RenderIfRequested(root, $"inline-minimum-{overflowingTabs}");
            Assert.True(viewport.ActualHeight >= row.ActualHeight,
                $"Candidate height {row.ActualHeight:F1} exceeds viewport {viewport.ActualHeight:F1}; tabs overflow={overflowingTabs}.");
            foreach (Button button in Descendants<Button>(panel).Where(b => AutomationProperties.GetAutomationId(b) is "CaptureCancel" or "CaptureAddSelected"))
            {
                Rect bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                Assert.InRange(bounds.Bottom, 1, root.ActualHeight);
                Assert.InRange(bounds.Right, 1, root.ActualWidth);
            }
        });
    }

    [Theory]
    [InlineData("Views/MainWindow.xaml", 604, 460, true, false)]
    [InlineData("Views/MainWindow.xaml", 604, 460, true, true)]
    [InlineData("Views/MainWindow.xaml", 604, 460, false, true)]
    [InlineData("Views/MainWindow.xaml", 884, 600, false, false)]
    [InlineData("Views/CapturePickerWindow.xaml", 544, 420, true, false)]
    [InlineData("Views/CapturePickerWindow.xaml", 544, 420, true, true)]
    [InlineData("Views/CapturePickerWindow.xaml", 544, 420, false, true)]
    [InlineData("Views/CapturePickerWindow.xaml", 744, 610, false, false)]
    [InlineData("Views/ContainerWindow.xaml", 420, 300, true, false)]
    [InlineData("Views/ContainerWindow.xaml", 420, 300, true, true)]
    [InlineData("Views/ContainerWindow.xaml", 420, 300, false, true)]
    [InlineData("Views/ContainerWindow.xaml", 980, 640, false, false)]
    public void SurfaceStateLayouts_KeepActionsInsideTheirLayoutRegions(string path, int width, int height, bool empty, bool blocked)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var picker = fixture.Picker(dispatcher, empty);
            using var host = HiddenVisualHost.Create();
            using var motion = MotionProfile.OverrideReducedMotion(true);
            if (blocked)
                fixture.Manager.SetCaptureAllowed(false, "Capture is disabled because the native guest-lifecycle monitor is unavailable. Restart TabDock after the monitor recovers.");
            var main = new MainViewModel(fixture.Manager);
            if (!empty)
                fixture.Manager.CreateGroup("Design studio — a long workspace name to exercise trimming rather than overlap");
            if (blocked)
                main.SetPendingRecoveryAttention(new PendingRecoveryAttention(0, true, "fixture"));
            FrameworkElement root = LoadLayout(path);
            root.Width = width;
            root.Height = height;
            root.DataContext = path.Contains("MainWindow", StringComparison.Ordinal) ? main : picker;
            if (path.Contains("Container", StringComparison.Ordinal))
            {
                root.DataContext = fixture.Workspace;
                var panel = (Border)root.FindName("CapturePanel");
                panel.DataContext = picker;
                panel.Visibility = Visibility.Visible;
            }
            host.Root.Children.Add(root);
            host.Realize();
            StaDispatcherFixture.Settle(dispatcher);
            root.UpdateLayout();
            string name = $"{Path.GetFileNameWithoutExtension(path)}-{width}x{height}-empty{empty}-blocked{blocked}";
            RenderIfRequested(root, name);
            Button[] buttons = Descendants<Button>(root).Where(b => b.IsVisible && b.ActualHeight > 0).ToArray();
            Assert.NotEmpty(buttons);
            foreach (Button button in buttons)
            {
                for (DependencyObject? ancestor = VisualTreeHelper.GetParent(button); ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
                {
                    if (ancestor is Grid grid)
                    {
                        Rect bounds = button.TransformToAncestor(grid).TransformBounds(new Rect(button.RenderSize));
                        Assert.True(bounds.Top >= -1 && bounds.Left >= -1 && bounds.Bottom <= grid.ActualHeight + 1 && bounds.Right <= grid.ActualWidth + 1,
                            $"{name}: {button.Content} escapes Grid {grid.Name}: {bounds} in {grid.RenderSize}.");
                    }
                    if (ReferenceEquals(ancestor, root)) break;
                }
            }
        });
    }

    [Theory]
    [InlineData(true, "No windows available", false)]
    [InlineData(false, "No matching windows", false)]
    [InlineData(true, "No windows available", true)]
    [InlineData(false, "No matching windows", true)]
    public void PickerEmptyState_IdentifiesTheActualRecoveryContext(bool noCandidates, string heading, bool blocked)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var picker = fixture.Picker(dispatcher, noCandidates);
            using var host = HiddenVisualHost.Create();
            using var motion = MotionProfile.OverrideReducedMotion(true);
            FrameworkElement root = LoadLayout("Views/CapturePickerWindow.xaml");
            root.Width = 544;
            root.Height = 420;
            root.DataContext = picker;
            if (!noCandidates) picker.SearchText = "no-such-window";
            if (blocked)
                fixture.Manager.SetCaptureAllowed(false, "Capture is disabled because the native guest-lifecycle monitor is unavailable. Restart TabDock after the monitor recovers.");
            host.Root.Children.Add(root);
            host.Realize();
            StaDispatcherFixture.Settle(dispatcher);
            RenderIfRequested(root, $"picker-empty-context-{noCandidates}-blocked{blocked}");
            TextBlock title = Descendants<TextBlock>(root).Single(text => text.IsVisible && text.Text == heading);
            var emptyPanel = (FrameworkElement)title.Parent;
            var region = (Grid)emptyPanel.Parent;
            Rect bounds = emptyPanel.TransformToAncestor(region).TransformBounds(new Rect(emptyPanel.RenderSize));
            Assert.True(bounds.Top >= -1 && bounds.Bottom <= region.ActualHeight + 1,
                $"Empty-state content {bounds} exceeds results region {region.RenderSize}.");
            if (!noCandidates)
            {
                picker.SearchText = string.Empty;
                host.Realize();
                Assert.DoesNotContain(Descendants<TextBlock>(root), text => text.IsVisible && text.Text == heading);
            }
        });
    }

    [Fact]
    public void FilteredSelectionSummary_DisclosesHiddenTargetsAndTracksAllStateChanges()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var picker = fixture.Picker(dispatcher);
            var summaries = new List<string>();
            picker.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CapturePickerViewModel.SelectionSummary))
                    summaries.Add(picker.SelectionSummary);
            };
            picker.Windows[0].IsSelected = true;
            picker.SearchText = "Terminal";
            Assert.Equal("1 selected · 1 hidden", picker.SelectionSummary);
            Assert.Contains("1 hidden by search", picker.GroupSelectedHelpText);
            Assert.Equal(picker.SelectionSummary, summaries.Last());
            picker.SelectAllVisibleCommand.Execute(null);
            Assert.Equal("2 selected · 1 hidden", picker.SelectionSummary);
            picker.SearchText = "no-such-window";
            Assert.Equal("2 selected · 2 hidden", picker.SelectionSummary);
            Assert.Equal(2, picker.SelectedCount);
            Assert.True(picker.GroupSelectedCommand.CanExecute(null));
            picker.SearchText = string.Empty;
            Assert.Equal("2 windows selected", picker.SelectionSummary);
            Assert.Equal(picker.SelectionSummary, summaries.Last());
            picker.SearchText = "Terminal";
            picker.ClearSelectionCommand.Execute(null);
            Assert.Equal("Select one or more windows", picker.SelectionSummary);
            Assert.False(picker.GroupSelectedCommand.CanExecute(null));
            Assert.DoesNotContain("hidden", picker.GroupSelectedHelpText);
            Assert.Equal(picker.SelectionSummary, summaries.Last());
        });
    }

    [Fact]
    public void HiddenSelectionRefresh_UsesCurrentIdentityAndCandidateSet()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var picker = fixture.Picker(dispatcher);
            picker.SelectAllVisibleCommand.Execute(null);
            picker.SearchText = "Terminal";
            picker.Refresh();
            Assert.Equal(1, picker.HiddenSelectedCount);
            Assert.Equal("2 selected · 1 hidden", picker.SelectionSummary);
            fixture.Manager.CreateGroup("Other workspace").Members.Add(new CapturedWindow { Hwnd = new IntPtr(11) });
            picker.Refresh();
            Assert.Equal(0, picker.HiddenSelectedCount);
            Assert.Equal("1 window selected", picker.SelectionSummary);
            Assert.Equal(new IntPtr(12), Assert.Single(picker.Windows).Hwnd);
        });
    }

    [Theory]
    [InlineData("Views/CapturePickerWindow.xaml", 544, 420)]
    [InlineData("Views/CapturePickerWindow.xaml", 744, 610)]
    [InlineData("Views/ContainerWindow.xaml", 420, 300)]
    [InlineData("Views/ContainerWindow.xaml", 980, 640)]
    public void HiddenSelectionSummary_RendersWithoutClippingAtMinimumAndDefaultSizes(string path, int width, int height)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var picker = new CapturePickerViewModel(fixture.Manager, fixture.Icons, fixture.Log, () => Enumerable.Range(1, 500)
                .Select(i => new CapturePickerViewModel.WindowInfo(new IntPtr(i), (uint)i, "Fixture", "Editor " + i, "", (uint)(1000 + i), 2000 + i)), dispatcher);
            using var host = HiddenVisualHost.Create();
            using var motion = MotionProfile.OverrideReducedMotion(true);
            picker.SelectAllVisibleCommand.Execute(null);
            picker.SearchText = "no-such-window";
            FrameworkElement root = LoadLayout(path);
            root.Width = width;
            root.Height = height;
            root.DataContext = picker;
            if (path.Contains("Container", StringComparison.Ordinal))
            {
                root.DataContext = fixture.Workspace;
                var panel = (Border)root.FindName("CapturePanel");
                panel.DataContext = picker;
                panel.Visibility = Visibility.Visible;
            }
            host.Root.Children.Add(root);
            host.Realize();
            StaDispatcherFixture.Settle(dispatcher);
            TextBlock summary = Descendants<TextBlock>(root).Single(t => AutomationProperties.GetAutomationId(t) == "CaptureSelectionSummary");
            Assert.Equal("500 selected · 500 hidden", summary.Text);
            Assert.True(summary.DesiredSize.Width <= summary.ActualWidth + 1, "Hidden selection summary is clipped.");
            Button submit = Descendants<Button>(root).Single(b => AutomationProperties.GetAutomationId(b) is "CaptureGroupThese" or "CaptureAddSelected");
            Assert.True(submit.IsEnabled);
            Assert.Contains("500 hidden by search", AutomationProperties.GetHelpText(submit));
            RenderIfRequested(root, $"hidden-selection-{Path.GetFileNameWithoutExtension(path)}-{width}");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestinationChoices_ExposeUnselectedKeyboardFocusWithoutMovingContent(bool reducedMotion)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var host = HiddenVisualHost.Create();
            using var motion = MotionProfile.OverrideReducedMotion(reducedMotion);
            ResourceDictionary palette = SharedTemplateLoader.Load();
            var combo = new ComboBox { Style = (Style)palette["TdComboBox"] };
            var selected = new ComboBoxItem { Content = "Current workspace", Style = combo.ItemContainerStyle, IsSelected = true, Width = 260 };
            var other = new HighlightedComboBoxItem { Content = "Destination workspace", Style = combo.ItemContainerStyle, Width = 260 };
            host.Root.Children.Add(selected);
            host.Root.Children.Add(other);
            host.Realize();
            Size before = other.RenderSize;
            var edge = (Border)other.Template.FindName("ItemBorder", other);
            Assert.True(StaDispatcherFixture.Focus(other));
            StaDispatcherFixture.Settle(dispatcher);
            Assert.True(edge.BorderBrush is SolidColorBrush, "Destination row has no visible focus boundary.");
            Assert.Equal(((SolidColorBrush)palette["TdFocusBrush"]).Color, ((SolidColorBrush)edge.BorderBrush).Color);
            Assert.False(other.IsSelected);
            Assert.True(selected.IsSelected);
            Assert.Equal(before, other.RenderSize);
            StaDispatcherFixture.Focus(selected);
            StaDispatcherFixture.Settle(dispatcher);
            Assert.NotEqual(((SolidColorBrush)palette["TdFocusBrush"]).Color, ((SolidColorBrush)edge.BorderBrush).Color);
            other.SetHighlighted(true);
            StaDispatcherFixture.Settle(dispatcher);
            Assert.Equal(((SolidColorBrush)palette["TdFocusBrush"]).Color, ((SolidColorBrush)edge.BorderBrush).Color);
            Assert.False(other.IsSelected);
            other.SetHighlighted(false);
            StaDispatcherFixture.Settle(dispatcher);
            Assert.NotEqual(((SolidColorBrush)palette["TdFocusBrush"]).Color, ((SolidColorBrush)edge.BorderBrush).Color);
            var region = new Grid { Width = 280, Height = 100, Background = (Brush)palette["TdSurfaceRaisedBrush"] };
            // Render the same production item templates without opening a popup.
            host.Root.Children.Remove(selected);
            host.Root.Children.Remove(other);
            region.RowDefinitions.Add(new RowDefinition());
            region.RowDefinitions.Add(new RowDefinition());
            region.Children.Add(selected);
            Grid.SetRow(other, 1);
            region.Children.Add(other);
            host.Root.Children.Add(region);
            host.Realize();
            StaDispatcherFixture.Focus(other);
            StaDispatcherFixture.Settle(dispatcher);
            RenderIfRequested(region, $"destination-keyboard-{reducedMotion}");
        });
    }

    [Fact]
    public void EnabledUncheckedSelectionControl_HasThreeToOneBoundaryContrast()
    {
        StaDispatcherFixture.Run(_ =>
        {
            ResourceDictionary palette = SharedTemplateLoader.Load();
            var check = new CheckBox { Style = (Style)palette["TdCheckBox"] };
            check.ApplyTemplate();
            var border = (Border)check.Template.FindName("CheckBorder", check);
            Color edge = ((SolidColorBrush)border.BorderBrush).Color;
            foreach (string adjacent in new[] { "TdInputBrush", "TdSurfaceRaisedBrush", "TdAccentSoftBrush" })
            {
                Color background = ((SolidColorBrush)palette[adjacent]).Color;
                double a = Luminance(edge), b = Luminance(background);
                double ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
                Assert.True(ratio >= 3, $"Unchecked control contrast against {adjacent}: {ratio:F2}:1");
            }
        });
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double s = value / 255d;
            return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    private static void SendManagedKey(UIElement target, Key key, HiddenVisualHost host)
    {
        PresentationSource source = PresentationSource.FromVisual(host.Root)!;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyUpEvent });
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }

    internal static FrameworkElement LoadLayout(string path)
    {
        string repo = FindRepo();
        XDocument document = XDocument.Load(Path.Combine(repo, path));
        XElement window = document.Root!;
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var wrapper = new XElement(p + "Grid", new XAttribute(x + "Name", "Root"));
        XElement app = XDocument.Load(Path.Combine(repo, "App.xaml")).Root!;
        foreach (XAttribute attribute in window.Attributes().Concat(app.Attributes()).Where(a => a.IsNamespaceDeclaration).DistinctBy(a => a.Name))
            wrapper.Add(new XAttribute(attribute.Name, attribute.Value
                + (attribute.Value.StartsWith("clr-namespace:TabDock.", StringComparison.Ordinal) ? ";assembly=TabDock" : "")));
        wrapper.SetAttributeValue("UseLayoutRounding", window.Attribute("UseLayoutRounding")?.Value);
        wrapper.SetAttributeValue("SnapsToDevicePixels", window.Attribute("SnapsToDevicePixels")?.Value);
        // Root attributes are parsed before its resources; resolve the same
        // inherited window tokens after the dictionary has been constructed.
        wrapper.SetAttributeValue("Background", window.Attribute("Background")?.Value.Replace("{StaticResource ", "{DynamicResource ", StringComparison.Ordinal));
        wrapper.SetAttributeValue(p + "TextElement.FontFamily", window.Attribute("FontFamily")?.Value.Replace("{StaticResource ", "{DynamicResource ", StringComparison.Ordinal));
        wrapper.SetAttributeValue(p + "TextElement.Foreground", window.Attribute("Foreground")?.Value.Replace("{StaticResource ", "{DynamicResource ", StringComparison.Ordinal));
        var resources = new XElement(p + "Grid.Resources");
        resources.Add(app.Element(p + "Application.Resources")!.Elements());
        XElement? localResources = window.Element(p + "Window.Resources");
        if (localResources != null) resources.Add(localResources.Elements());
        wrapper.Add(resources);
        wrapper.Add(window.Elements().Last());
        var events = new HashSet<string> { "Click", "Closed", "LostFocus", "KeyDown", "SelectionChanged", "PreviewKeyDown", "PreviewMouseLeftButtonDown", "PreviewMouseRightButtonDown", "MouseLeftButtonDown" };
        foreach (XAttribute attribute in wrapper.Descendants().Attributes().Where(a => events.Contains(a.Name.LocalName)).ToArray()) attribute.Remove();
        // A WPF-only layout probe: do not construct a native content marker or open popups.
        foreach (XElement marker in wrapper.Descendants().Where(e => e.Name.LocalName == "NativeHwndHost").ToArray())
            marker.Name = p + "Border";
        string xaml = System.Text.RegularExpressions.Regex.Replace(wrapper.ToString(),
            @"clr-namespace:TabDock\.([^"";]+)""", "clr-namespace:TabDock.$1;assembly=TabDock\"");
        return (FrameworkElement)XamlReader.Parse(xaml);
    }

    internal static void RenderIfRequested(FrameworkElement root, string name)
    {
        string? output = Environment.GetEnvironmentVariable("TABDOCK_REVIEW_RENDER_ROOT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (DrawingContext context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.Fill }, null, new Rect(root.RenderSize));
        bitmap.Render(drawing);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }

    private static string FindRepo()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }

    private sealed class HighlightedComboBoxItem : ComboBoxItem
    {
        // WPF's protected setter is a managed-state seam, not simulated input.
        public void SetHighlighted(bool value) => IsHighlighted = value;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "TabDock-frontend-regression-" + Guid.NewGuid().ToString("N"));
        public LoggingService Log { get; }
        public IconService Icons { get; }
        public GroupManager Manager { get; }
        public GroupViewModel Workspace { get; }
        public Fixture()
        {
            Log = new LoggingService(Path.Combine(_root, "logs"));
            Icons = new IconService(Log, _ => null);
            var shepherd = new WindowShepherdService(Log, Path.Combine(_root, "journal.json"));
            Manager = new GroupManager(shepherd, new PersistenceService(Log, Path.Combine(_root, "state.json")), Log);
            Workspace = new GroupViewModel(new Group { Name = "Design studio" }, Manager, Icons, Log);
        }
        public CapturePickerViewModel Picker(System.Windows.Threading.Dispatcher dispatcher, bool empty = false) => new(Manager, Icons, Log, () => empty ? Array.Empty<CapturePickerViewModel.WindowInfo>() : new[]
        {
            new CapturePickerViewModel.WindowInfo(new IntPtr(11), 11, "Fixture", "Editor — a long fixture title", "", 21, 31),
            new CapturePickerViewModel.WindowInfo(new IntPtr(12), 12, "Fixture", "Terminal", "", 22, 32),
        }, dispatcher);
        public void Dispose() { Workspace.Detach(); Log.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
