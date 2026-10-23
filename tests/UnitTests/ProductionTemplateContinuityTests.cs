using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabDock.Infrastructure;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using TabDock.Views;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class ProductionTemplateContinuityTests
{
    [Theory]
    [InlineData(false, "Views/CapturePickerWindow.xaml")]
    [InlineData(true, "Views/CapturePickerWindow.xaml")]
    [InlineData(false, "Views/ContainerWindow.xaml")]
    [InlineData(true, "Views/ContainerWindow.xaml")]
    public void PickerCommandsRefreshAndReusedRow_PreserveCurrentCheckedVisual(bool reduced, string path)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var policy = MotionProfile.OverrideReducedMotion(reduced);
            using var host = HiddenVisualHost.Create();
            int generation = 0;
            using var picker = new CapturePickerViewModel(fixture.Manager, fixture.Icons, fixture.Log, () => new[]
            {
                new CapturePickerViewModel.WindowInfo(new IntPtr(11), 11, "fixture", generation == 0 ? "Editor" : "Editor updated", "", 21, generation < 2 ? 31 : 99),
                new CapturePickerViewModel.WindowInfo(new IntPtr(12), 12, "fixture", "Terminal", "", 22, 32),
            }, dispatcher);
            ResourceDictionary palette = SharedTemplateLoader.Load();
            var rowStyle = new Style(typeof(ListBoxItem));
            rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            rowStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            rowStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            var list = new ListBox
            {
                Width = 230, Height = 140,
                Background = (Brush)palette["TdSurfaceBrush"], BorderThickness = new Thickness(0),
                ItemContainerStyle = rowStyle,
                ItemsSource = picker.FilteredWindows,
                ItemTemplate = (DataTemplate)PickerTemplateLoader.Load(path)["PickerRow"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            host.Root.Children.Add(list);
            host.Realize();
            picker.SelectAllVisibleCommand.Execute(null);
            TabIconNonblockingTests.PumpUntil(dispatcher, () => Highlight(list, 0) && Highlight(list, 1));
            Assert.Equal(2, picker.SelectedCount);
            picker.ClearSelectionCommand.Execute(null);
            TabIconNonblockingTests.PumpUntil(dispatcher, () => !Highlight(list, 0) && !Highlight(list, 1));
            picker.SearchText = "Editor";
            host.Realize();
            picker.SelectAllVisibleCommand.Execute(null);
            TabIconNonblockingTests.PumpUntil(dispatcher, () => Highlight(list, 0));
            picker.SearchText = "Terminal";
            host.Realize();
            TabIconNonblockingTests.PumpUntil(dispatcher, () => !Highlight(list, 0));
            generation = 1;
            picker.Refresh();
            picker.SearchText = "Editor";
            host.Realize();
            TabIconNonblockingTests.PumpUntil(dispatcher, () => Highlight(list, 0));
            Assert.Single(picker.Windows.Where(w => w.IsSelected).Select(w => w.ToCaptureTarget()));
            generation = 2;
            picker.Refresh();
            host.Realize();
            TabIconNonblockingTests.PumpUntil(dispatcher, () => !Highlight(list, 0));
            Assert.Empty(picker.Windows.Where(w => w.IsSelected).Select(w => w.ToCaptureTarget()));

            // Retain the exact visual and rebind it as virtualization does.
            var content = new ContentPresenter { ContentTemplate = list.ItemTemplate, Width = 220 };
            var old = picker.Windows[0];
            old.IsSelected = true;
            content.Content = old;
            host.Root.Children.Add(content);
            host.Realize();
            Border row = FindRole(content, MotionProfile.MotionOverlayRole.Fill)!.FindSemanticBorder();
            TabIconNonblockingTests.PumpUntil(dispatcher, () => FindRole(content, MotionProfile.MotionOverlayRole.Fill)!.Opacity > .999);
            var replacement = picker.Windows[1];
            content.Content = replacement;
            host.Realize();
            // WPF may regenerate a ContentPresenter template; explicitly reuse
            // the same row to prove old binding listeners cannot drive it.
            row.DataContext = replacement;
            old.IsSelected = false;
            old.IsSelected = true;
            StaDispatcherFixture.Settle(dispatcher);
            TabIconNonblockingTests.PumpUntil(dispatcher, () => MotionProfile.GetIsMotionSelected(row) == false);
            Assert.False(MotionProfile.GetIsMotionSelected(row));

            Border overlay = FindRole(list, MotionProfile.MotionOverlayRole.Fill)!;
            Border semantic = overlay.FindSemanticBorder();
            Assert.InRange(Math.Abs(overlay.ActualWidth - (semantic.ActualWidth - semantic.BorderThickness.Left - semantic.BorderThickness.Right)), 0, 1);
            Assert.InRange(Math.Abs(overlay.ActualHeight - (semantic.ActualHeight - semantic.BorderThickness.Top - semantic.BorderThickness.Bottom)), 0, 1);
            picker.SelectAllVisibleCommand.Execute(null);
            host.Realize();
            TabIconNonblockingTests.PumpUntil(dispatcher, () => Highlight(list, 0));
            StaDispatcherFixture.Focus((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0));
            StaDispatcherFixture.Pump(dispatcher, MotionProfile.StateDuration + MotionProfile.FeedbackDuration);
            RenderIfRequested(list, path.Contains("Container") ? "inline" : "standalone", reduced);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualSplitTemplate_ActiveAndManagedFocusFollowCurrentHalf(bool reduced)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var policy = MotionProfile.OverrideReducedMotion(reduced);
            using var host = HiddenVisualHost.Create();
            var vm = new GroupViewModel(new Group(), fixture.Manager, fixture.Icons, fixture.Log);
            var window = new ContainerWindow(vm, fixture.Manager, fixture.Shepherd, fixture.Log, fixture.Icons, SharedTemplateLoader.Load());
            var left = new TabViewModel(new CapturedWindow { OriginalTitle = "Left" });
            var right = new TabViewModel(new CapturedWindow { OriginalTitle = "Right" });
            var pair = new SplitCompositeViewModel(left, right);
            try
            {
                var tabs = (ListBox)window.FindName("TabsListBox");
                var template = (DataTemplate)tabs.Resources[new DataTemplateKey(typeof(SplitCompositeViewModel))];
                var presenter = new ContentPresenter { Content = pair, ContentTemplate = template, Resources = window.Resources, Width = 230 };
                host.Root.Children.Add(presenter);
                host.Realize();
                Border a = FindAutomation(presenter, "SplitHalfLeft")!;
                Border b = FindAutomation(presenter, "SplitHalfRight")!;
                left.IsActive = true;
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal("#FF2A3E68", ((SolidColorBrush)a.Background).Color.ToString());
                left.IsActive = false;
                right.IsActive = true;
                StaDispatcherFixture.Focus(b);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal("#FF2A3E68", ((SolidColorBrush)b.Background).Color.ToString());
                Assert.Equal("#FF9CB8FF", ((SolidColorBrush)b.BorderBrush).Color.ToString());
                Assert.Equal(DependencyProperty.UnsetValue, a.ReadLocalValue(Border.BackgroundProperty));
                Assert.Equal(DependencyProperty.UnsetValue, b.ReadLocalValue(Border.BackgroundProperty));
                Assert.Equal("LEFT", a.Tag);
                Assert.Equal("RIGHT", b.Tag);
            }
            finally { pair.Detach(); vm.Detach(); window.Close(); }
        });
    }

    private static bool Highlight(ListBox list, int index)
    {
        list.UpdateLayout();
        var item = list.ItemContainerGenerator.ContainerFromIndex(index) as DependencyObject;
        return item is not null && FindRole(item, MotionProfile.MotionOverlayRole.Fill)?.Opacity > .999;
    }

    internal static Border? FindRole(DependencyObject node, MotionProfile.MotionOverlayRole role)
    {
        if (node is Border border && MotionProfile.GetOverlayRole(border) == role) return border;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            Border? found = FindRole(VisualTreeHelper.GetChild(node, i), role);
            if (found != null) return found;
        }
        return null;
    }

    private static Border? FindAutomation(DependencyObject node, string id)
    {
        if (node is Border border && AutomationProperties.GetAutomationId(border) == id) return border;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            Border? found = FindAutomation(VisualTreeHelper.GetChild(node, i), id);
            if (found != null) return found;
        }
        return null;
    }

    private static void RenderIfRequested(ListBox list, string name, bool reduced)
    {
        string? output = Environment.GetEnvironmentVariable("TABDOCK_REVIEW_RENDER_ROOT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        foreach (int width in new[] { 280, 600 })
        {
            list.Width = width;
            list.Height = 200;
            list.Measure(new Size(width, 200));
            list.Arrange(new Rect(0, 0, width, 200));
            list.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, 200, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (DrawingContext context = drawing.RenderOpen())
                context.DrawRectangle(new VisualBrush(list) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, 200));
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, $"{name}-{width}-{reduced}.png"));
            encoder.Save(stream);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "TabDock-template-continuity-" + Guid.NewGuid().ToString("N"));
        public LoggingService Log { get; }
        public IconService Icons { get; }
        public GroupManager Manager { get; }
        public WindowShepherdService Shepherd { get; }
        public Fixture()
        {
            Log = new LoggingService(Path.Combine(_root, "logs"));
            Icons = new IconService(Log, _ => null);
            Shepherd = new WindowShepherdService(Log, Path.Combine(_root, "journal.json"));
            Manager = new GroupManager(Shepherd, new PersistenceService(Log, Path.Combine(_root, "state.json")), Log);
        }
        public void Dispose() { Log.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}

internal static class SemanticBorderTestExtensions
{
    internal static Border FindSemanticBorder(this Border overlay)
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(overlay);
        while (parent is not null)
        {
            if (parent is Border border && (bool)border.GetValue(MotionProfile.EnableBorderStatesProperty)) return border;
            parent = VisualTreeHelper.GetParent(parent);
        }
        throw new InvalidOperationException("overlay has no semantic owner");
    }
}
