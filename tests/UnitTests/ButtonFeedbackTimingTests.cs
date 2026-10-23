using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TabDock.Infrastructure;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class ButtonFeedbackTimingTests
{
    [Theory]
    [InlineData(false, "TdButton")]
    [InlineData(true, "TdButton")]
    [InlineData(false, "TdPrimaryButton")]
    [InlineData(true, "TdGhostButton")]
    public void RealizedTemplate_HoverAndPress_UseCurrentOverlays(bool reducedMotion, string styleKey)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(reducedMotion))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                ResourceDictionary resources = SharedTemplateLoader.Load();
                int commands = 0;
                var button = new Button
                {
                    Command = new RelayCommand(_ => commands++),
                    Style = (Style)resources[styleKey],
                    Content = styleKey,
                    Width = 120,
                    Height = 36,
                };
                host.Root.Children.Add(button);
                host.Realize();
                Border? hover = Part<Border>(button, "HoverOverlay");
                Border? pressed = Part<Border>(button, "PressedOverlay");
                Assert.NotNull(hover);
                Assert.NotNull(pressed);
                Raise(button, Mouse.MouseEnterEvent);
                Wait(dispatcher, () => hover.Opacity > 0.9);
                Assert.True(hover.Opacity > 0.9);
                Raise(button, Mouse.MouseLeaveEvent);
                Wait(dispatcher, () => hover.Opacity < 0.1);
                Assert.True(hover.Opacity < 0.1);

                SetPressed(button, true);
                Wait(dispatcher, () => pressed.Opacity > 0.9);
                Assert.True(pressed.Opacity > 0.9);
                Assert.True(StaDispatcherFixture.Focus(button));
                SetPressed(button, false);
                Wait(dispatcher, () => pressed.Opacity < 0.1);
                Assert.True(button.IsKeyboardFocusWithin);
                Assert.Equal(0, commands); // cosmetic state must not invoke the action
                typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, null);
                Assert.Equal(1, commands); // managed activation remains exactly once
            }
        });
    }

    [Fact]
    public void MissingOptionalOverlay_DoesNotThrowOrRetryForever()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(true))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                var button = new Button { Width = 80, Height = 28, Content = "plain" };
                var template = new ControlTemplate(typeof(Button));
                var root = new FrameworkElementFactory(typeof(Border));
                root.SetValue(MotionProfile.EnableButtonFeedbackProperty, true);
                root.SetValue(Border.BackgroundProperty, Brushes.Gray);
                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                root.AppendChild(presenter);
                template.VisualTree = root;
                template.Seal();
                button.Template = template;
                host.Root.Children.Add(button);
                host.Realize();
                Raise(button, Mouse.MouseEnterEvent);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.True(StaDispatcherFixture.Focus(button));
                Assert.True(button.IsEnabled);
            }
        });
    }

    [Fact]
    public void ReplacedTemplateParts_AreCollectibleWhileButtonSurvives()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var policy = MotionProfile.OverrideReducedMotion(false);
            using var host = HiddenVisualHost.Create();
            var button = new Button { Style = (Style)SharedTemplateLoader.Load()["TdButton"], Content = "replace" };
            host.Root.Children.Add(button);
            host.Realize();
            WeakReference[] parts = ReplaceTemplate(button, host);
            StaDispatcherFixture.CollectSettled(dispatcher);
            Assert.All(parts, part => Assert.False(part.IsAlive));
            Assert.True(button.IsEnabled);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] ReplaceTemplate(Button button, HiddenVisualHost host)
    {
        Border oldRoot = Part<Border>(button, "ButtonBorder")!;
        Border oldHover = Part<Border>(button, "HoverOverlay")!;
        var refs = new[] { new WeakReference(oldRoot), new WeakReference(oldHover) };
        MotionProfile.TryQueueOwnedCallback(oldRoot, () => oldHover.Opacity = .2);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        host.Realize();
        return refs;
    }

    private static void Raise(UIElement element, RoutedEvent routedEvent)
    {
        Assert.NotNull(Mouse.PrimaryDevice);
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = routedEvent });
    }

    private static void SetPressed(Button button, bool pressed)
    {
        FieldInfo? field = typeof(ButtonBase).GetField(
            "IsPressedPropertyKey",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
        var key = (DependencyPropertyKey)field!.GetValue(null)!;
        button.SetValue(key, pressed);
    }

    private static T? Part<T>(FrameworkElement root, string? name) where T : FrameworkElement
    {
        root.ApplyTemplate();
        return Find<T>(root, name);
    }

    private static T? Find<T>(DependencyObject parent, string? name) where T : FrameworkElement
    {
        if (parent is T self && (name is null || self.Name == name))
            return self;
        if (name is null)
            return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            T? found = Find<T>(VisualTreeHelper.GetChild(parent, i), name);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static void Wait(Dispatcher dispatcher, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!done() && DateTime.UtcNow < deadline)
            StaDispatcherFixture.Pump(dispatcher, TimeSpan.FromMilliseconds(40));
    }
}
