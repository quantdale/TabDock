using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using TabDock.Infrastructure;
using Xunit;

namespace TabDock.UnitTests;

[CollectionDefinition("MotionFeedbackLifecycle", DisableParallelization = true)]
public sealed class MotionFeedbackLifecycleCollection;

/// <summary>
/// Actual styled-control motion lifecycle. Synthetic resource counters are not
/// consulted and cannot satisfy these assertions.
/// </summary>
[Collection("MotionFeedbackLifecycle")]
public sealed class MotionFeedbackLifecycleTests
{
    private const int ChurnCount = 30;

    [Fact]
    public void DiscardedStyledControls_AreCollected_InBothMotionModes()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            ResourceDictionary resources = SharedTemplateLoader.Load();
            foreach (bool reduced in new[] { false, true })
            {
                using (MotionProfile.OverrideReducedMotion(reduced))
                {
                    ChurnRefs refs = CreateAndReleaseChurn(resources, dispatcher);
                    StaDispatcherFixture.CollectSettled(dispatcher);
                    Assert.Equal(0, StaDispatcherFixture.Alive(refs.PlainBorders));
                    Assert.Equal(0, StaDispatcherFixture.Alive(refs.PlainButtons));
                    Assert.True(
                        StaDispatcherFixture.Alive(refs.MotionBorders) == 0,
                        $"reduced={reduced} motion borders alive={StaDispatcherFixture.Alive(refs.MotionBorders)}/{ChurnCount}");
                    Assert.True(
                        StaDispatcherFixture.Alive(refs.MotionButtons) == 0,
                        $"reduced={reduced} motion buttons alive={StaDispatcherFixture.Alive(refs.MotionButtons)}/{ChurnCount}");
                    Assert.True(
                        StaDispatcherFixture.Alive(refs.DisabledButtons) == 0,
                        $"reduced={reduced} disabled buttons alive={StaDispatcherFixture.Alive(refs.DisabledButtons)}/{ChurnCount}");
                }
            }
        });
    }

    [Fact]
    public void Reload_DoesNotMultiplyObservers_AndInactiveCountReturnsToZero()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            ResourceDictionary resources = SharedTemplateLoader.Load();
            using (MotionProfile.OverrideReducedMotion(true))
            {
                using HiddenVisualHost host = HiddenVisualHost.Create();
                var button = new Button
                {
                    Style = (Style)resources["TdButton"],
                    Content = "Reload",
                    Width = 120,
                    Height = 36,
                };
                host.Root.Children.Add(button);
                host.Realize();
                Border border = RequirePart<Border>(button, "ButtonBorder");
                int first = MotionProfile.GetDescriptorObserverCount(border);
                Assert.True(
                    first > 0,
                    $"expected an active descriptor observer after load; loaded={border.IsLoaded}; owner={MotionProfile.HasAttachmentOwner(border)}; parentLoaded={button.IsLoaded}");

                host.Root.Children.Remove(button);
                host.Root.UpdateLayout();
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(0, MotionProfile.GetActiveObserverCount(border));
                Assert.Equal(0, MotionProfile.GetDescriptorObserverCount(border));

                host.Root.Children.Add(button);
                host.Root.UpdateLayout();
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(first, MotionProfile.GetDescriptorObserverCount(border));

                SetPressed(button, true);
                StaDispatcherFixture.Settle(dispatcher);
                Border pressed = RequirePart<Border>(button, "PressedOverlay");
                Assert.Equal(1, pressed.Opacity);

                host.Root.Children.Remove(button);
                host.Root.UpdateLayout();
                StaDispatcherFixture.Settle(dispatcher);
                double retired = pressed.Opacity;
                SetPressed(button, false);
                SetPressed(button, true);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(retired, pressed.Opacity);
                Assert.Equal(0, MotionProfile.GetDescriptorObserverCount(border));
            }
        });
    }

    [Fact]
    public void DisableAndTemplateReplacement_RejectObsoleteCallbacks()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            ResourceDictionary resources = SharedTemplateLoader.Load();
            using (MotionProfile.OverrideReducedMotion(true))
            {
                using HiddenVisualHost host = HiddenVisualHost.Create();
                var button = new Button
                {
                    Style = (Style)resources["TdButton"],
                    Content = "Replace",
                    Width = 120,
                    Height = 36,
                };
                host.Root.Children.Add(button);
                host.Realize();
                Border border = RequirePart<Border>(button, "ButtonBorder");
                bool applied = false;
                Assert.True(MotionProfile.TryQueueOwnedCallback(border, () =>
                {
                    applied = true;
                    border.Opacity = 0.25;
                }));
                int generation = MotionProfile.GetGeneration(border);
                button.Template = ReplacementTemplate();
                host.Root.UpdateLayout();
                StaDispatcherFixture.Settle(dispatcher);
                Assert.False(applied);
                Assert.NotEqual(0.25, border.Opacity);
                Assert.True(MotionProfile.GetGeneration(border) != generation || !MotionProfile.HasAttachmentOwner(border));
                Assert.Equal(0, MotionProfile.GetDescriptorObserverCount(border));

                var reenabled = new Button
                {
                    Style = (Style)resources["TdButton"],
                    Content = "Disable",
                    Width = 120,
                    Height = 36,
                };
                host.Root.Children.Add(reenabled);
                host.Realize();
                Border live = RequirePart<Border>(reenabled, "ButtonBorder");
                MotionProfile.SetEnableButtonFeedback(live, false);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.False(MotionProfile.HasAttachmentOwner(live));
                SetPressed(reenabled, true);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(0, RequirePart<Border>(reenabled, "PressedOverlay").Opacity);
            }
        });
    }

    [Fact]
    public void RowCheckmarkChevronAndEntrance_ReloadWithoutDuplicateObservers()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            ResourceDictionary resources = SharedTemplateLoader.Load();
            using (MotionProfile.OverrideReducedMotion(true))
            {
                using HiddenVisualHost host = HiddenVisualHost.Create();
                var list = new ListView
                {
                    Width = 220,
                    Height = 80,
                    ItemContainerStyle = (Style)resources["TdListItem"],
                };
                list.Items.Add("Row");
                host.Root.Children.Add(list);
                host.Realize();
                var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.NotNull(row);
                Border rowBorder = RequirePart<Border>(row, "RowBorder");
                int rowObservers = MotionProfile.GetDescriptorObserverCount(rowBorder);
                Assert.True(rowObservers > 0);
                row.IsSelected = true;
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(1, RequirePart<Border>(row, "RowSelectedOverlay").Opacity);
                host.Root.Children.Remove(list);
                host.Root.UpdateLayout();
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(0, MotionProfile.GetDescriptorObserverCount(rowBorder));

                var box = new CheckBox
                {
                    Style = (Style)resources["TdCheckBox"],
                    Content = "Check",
                };
                host.Root.Children.Add(box);
                host.Realize();
                TextBlock mark = RequirePart<TextBlock>(box, "CheckMark");
                Assert.True(MotionProfile.GetDescriptorObserverCount(mark) > 0);
                box.IsChecked = true;
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(Visibility.Visible, mark.Visibility);
                host.Root.Children.Remove(box);
                host.Root.UpdateLayout();
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(0, MotionProfile.GetDescriptorObserverCount(mark));

                var toggle = new ToggleButton { Width = 80, Height = 28 };
                var glyph = new TextBlock { Name = "ChevronGlyph", Text = "v" };
                MotionProfile.SetEnableChevronRotate(glyph, true);
                toggle.Template = ChevronTemplate(glyph);
                host.Root.Children.Add(toggle);
                host.Realize();
                TextBlock realized = RequirePart<TextBlock>(toggle, "ChevronGlyph");
                Assert.True(MotionProfile.GetDescriptorObserverCount(realized) > 0);
                toggle.IsChecked = true;
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(180, ((RotateTransform)realized.RenderTransform).Angle);
                host.Root.Children.Remove(toggle);
                host.Root.UpdateLayout();
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(0, MotionProfile.GetDescriptorObserverCount(realized));

                var entrance = new Border
                {
                    Width = 40,
                    Height = 20,
                    Background = Brushes.Gray,
                };
                MotionProfile.SetEnableEntrance(entrance, true);
                host.Root.Children.Add(entrance);
                host.Realize();
                Assert.Equal(1, MotionProfile.GetEntrancePlayCount(entrance));
                host.Root.Children.Remove(entrance);
                host.Realize();
                host.Root.Children.Add(entrance);
                host.Realize();
                Assert.Equal(2, MotionProfile.GetEntrancePlayCount(entrance));
                Assert.Equal(0, MotionProfile.GetDescriptorObserverCount(entrance));
                host.Root.Children.Remove(entrance);
                host.Root.UpdateLayout();
                Assert.Equal(0, MotionProfile.GetActiveObserverCount(entrance));
            }
        });
    }

    [Fact]
    public void DeferredButtonAttachment_ResolvesCurrentOverlayParts()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            ResourceDictionary resources = SharedTemplateLoader.Load();
            using (MotionProfile.OverrideReducedMotion(true))
            {
                using HiddenVisualHost host = HiddenVisualHost.Create();
                var button = new Button
                {
                    Style = (Style)resources["TdButton"],
                    Content = "Parts",
                    Width = 100,
                    Height = 32,
                };
                host.Root.Children.Add(button);
                host.Realize();
                StaDispatcherFixture.Settle(dispatcher);
                Border hover = RequirePart<Border>(button, "HoverOverlay");
                Border pressed = RequirePart<Border>(button, "PressedOverlay");
                Assert.NotNull(hover);
                Assert.NotNull(pressed);
                Assert.NotNull(Mouse.PrimaryDevice);
                button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = Mouse.MouseEnterEvent,
                });
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(1, hover.Opacity);
                SetPressed(button, true);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(1, pressed.Opacity);
            }
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ChurnRefs CreateAndReleaseChurn(ResourceDictionary resources, Dispatcher dispatcher)
    {
        using HiddenVisualHost host = HiddenVisualHost.Create();
        var motionBorders = new WeakReference[ChurnCount];
        var motionButtons = new WeakReference[ChurnCount];
        var plainBorders = new WeakReference[ChurnCount];
        var plainButtons = new WeakReference[ChurnCount];
        var disabledButtons = new WeakReference[ChurnCount];
        for (int i = 0; i < ChurnCount; i++)
        {
            var motionBorder = new Border
            {
                Width = 8,
                Height = 8,
                Background = new SolidColorBrush(Colors.Gray),
                BorderBrush = new SolidColorBrush(Colors.DimGray),
                BorderThickness = new Thickness(1),
            };
            MotionProfile.SetEnableBorderStates(motionBorder, true);
            MotionProfile.SetHoverBackground(motionBorder, Brushes.WhiteSmoke);
            host.Root.Children.Add(motionBorder);
            motionBorders[i] = new WeakReference(motionBorder);

            var plainBorder = new Border { Width = 8, Height = 8, Background = Brushes.Black };
            host.Root.Children.Add(plainBorder);
            plainBorders[i] = new WeakReference(plainBorder);

            var motionButton = new Button
            {
                Style = (Style)resources["TdButton"],
                Content = i.ToString(),
                Width = 48,
                Height = 24,
            };
            host.Root.Children.Add(motionButton);
            motionButtons[i] = new WeakReference(motionButton);

            var plainButton = new Button { Content = "plain", Width = 48, Height = 24 };
            host.Root.Children.Add(plainButton);
            plainButtons[i] = new WeakReference(plainButton);

            var disabled = new Button
            {
                Style = (Style)resources["TdButton"],
                Content = "off",
                Width = 48,
                Height = 24,
            };
            host.Root.Children.Add(disabled);
            disabledButtons[i] = new WeakReference(disabled);
        }

        host.Realize();
        StaDispatcherFixture.Settle(dispatcher);
        var sampleButton = (Button)motionButtons[0].Target!;
        var sampleBorder = (Border)motionBorders[0].Target!;
        if (MotionProfile.GetDescriptorObserverCount(RequirePart<Border>(sampleButton, "ButtonBorder")) == 0
            || MotionProfile.GetDescriptorObserverCount(sampleBorder) == 0)
        {
            throw new InvalidOperationException("motion churn released controls before descriptor observers attached");
        }
        for (int i = 0; i < ChurnCount; i++)
        {
            var disabled = (Button)disabledButtons[i].Target!;
            Border border = RequirePart<Border>(disabled, "ButtonBorder");
            MotionProfile.SetEnableButtonFeedback(border, false);
        }
        StaDispatcherFixture.Settle(dispatcher);
        host.Root.Children.Clear();
        host.Root.UpdateLayout();
        StaDispatcherFixture.Settle(dispatcher);
        return new ChurnRefs(motionBorders, motionButtons, plainBorders, plainButtons, disabledButtons);
    }

    private static void SetPressed(Button button, bool pressed)
    {
        FieldInfo? field = typeof(ButtonBase).GetField(
            "IsPressedPropertyKey",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
        Assert.NotNull(field);
        var key = (DependencyPropertyKey)field.GetValue(null)!;
        button.SetValue(key, pressed);
    }

    private static T RequirePart<T>(FrameworkElement root, string name) where T : FrameworkElement
    {
        root.ApplyTemplate();
        if (root is Control control)
            control.ApplyTemplate();
        T? found = FindNamed<T>(root, name);
        Assert.NotNull(found);
        return found;
    }

    private static T? FindNamed<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        if (parent is T self && self.Name == name)
            return self;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            T? found = FindNamed<T>(VisualTreeHelper.GetChild(parent, i), name);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static ControlTemplate ReplacementTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        template.VisualTree = border;
        template.Seal();
        return template;
    }

    private static ControlTemplate ChevronTemplate(TextBlock glyph)
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetValue(FrameworkElement.NameProperty, "ChevronGlyph");
        factory.SetValue(TextBlock.TextProperty, glyph.Text);
        factory.SetValue(MotionProfile.EnableChevronRotateProperty, true);
        template.VisualTree = factory;
        template.Seal();
        return template;
    }

    private readonly struct ChurnRefs
    {
        public ChurnRefs(
            WeakReference[] motionBorders,
            WeakReference[] motionButtons,
            WeakReference[] plainBorders,
            WeakReference[] plainButtons,
            WeakReference[] disabledButtons)
        {
            MotionBorders = motionBorders;
            MotionButtons = motionButtons;
            PlainBorders = plainBorders;
            PlainButtons = plainButtons;
            DisabledButtons = disabledButtons;
        }

        public WeakReference[] MotionBorders { get; }
        public WeakReference[] MotionButtons { get; }
        public WeakReference[] PlainBorders { get; }
        public WeakReference[] PlainButtons { get; }
        public WeakReference[] DisabledButtons { get; }
    }
}

internal static class SharedTemplateLoader
{
    public static ResourceDictionary Load()
    {
        string path = Path.Combine(FindRepoRoot(), "App.xaml");
        string xaml = File.ReadAllText(path);
        const string startToken = "<Application.Resources>";
        const string endToken = "</Application.Resources>";
        int start = xaml.IndexOf(startToken, StringComparison.Ordinal);
        int end = xaml.IndexOf(endToken, StringComparison.Ordinal);
        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException("App.xaml resources were not found.");
        string inner = xaml.Substring(start + startToken.Length, end - start - startToken.Length);
        string wrapped =
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            "xmlns:converters=\"clr-namespace:TabDock.Converters;assembly=TabDock\" " +
            "xmlns:infrastructure=\"clr-namespace:TabDock.Infrastructure;assembly=TabDock\">" +
            inner +
            "</ResourceDictionary>";
        return (ResourceDictionary)XamlReader.Parse(wrapped);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TabDock.sln not found above test output.");
    }
}

internal sealed class HiddenVisualHost : IDisposable
{
    private readonly HwndSource _source;
    private bool _disposed;

    private HiddenVisualHost(HwndSource source, StackPanel root)
    {
        _source = source;
        Root = root;
    }

    public StackPanel Root { get; }

    public static HiddenVisualHost Create()
    {
        var parameters = new HwndSourceParameters("TabDock.MotionLifecycle")
        {
            WindowStyle = unchecked((int)0x80000000) | 0x10000000 | 0x02000000,
            ExtendedWindowStyle = 0x00000080 | 0x08000000,
            PositionX = -32000,
            PositionY = -32000,
            Width = 240,
            Height = 160,
        };
        var source = new HwndSource(parameters);
        ShowWindow(source.Handle, 8);
        var root = new StackPanel { Width = 240, Height = 160 };
        source.RootVisual = root;
        root.Measure(new Size(240, 160));
        root.Arrange(new Rect(0, 0, 240, 160));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        return new HiddenVisualHost(source, root);
    }

    public void Realize()
    {
        Root.Measure(new Size(240, 160));
        Root.Arrange(new Rect(0, 0, 240, 160));
        Root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Root.Children.Clear();
        _source.RootVisual = null;
        _source.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
