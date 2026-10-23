using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TabDock.Infrastructure;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class MotionReattachmentRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentCheckedChevronRowAndPressedState_AreInitializedOnReattach(bool reduced)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var policy = MotionProfile.OverrideReducedMotion(reduced);
            using var host = HiddenVisualHost.Create();
            ResourceDictionary resources = SharedTemplateLoader.Load();
            var box = new CheckBox { Style = (Style)resources["TdCheckBox"], IsChecked = true };
            var combo = new ComboBox { Style = (Style)resources["TdComboBox"], Width = 100 };
            var row = new ListViewItem { Style = (Style)resources["TdListItem"], Content = "row", IsSelected = true };
            var button = new Button { Style = (Style)resources["TdButton"], Content = "press" };
            host.Root.Children.Add(box);
            host.Root.Children.Add(combo);
            host.Root.Children.Add(row);
            host.Root.Children.Add(button);
            host.Realize();
            TextBlock mark = Find<TextBlock>(box, "CheckMark")!;
            TextBlock glyph = Find<TextBlock>(combo, "ChevronGlyph")!;
            ToggleButton toggle = (ToggleButton)glyph.TemplatedParent;
            toggle.IsChecked = true;
            box.IsChecked = false;
            box.IsChecked = true;
            SetPressed(button, true);
            host.Root.Children.Clear();
            host.Realize();
            row.IsSelected = false;
            host.Root.Children.Add(box);
            host.Root.Children.Add(combo);
            host.Root.Children.Add(row);
            host.Root.Children.Add(button);
            host.Realize();
            // Template bindings may restore combo checked from IsDropDownOpen;
            // drive and retain the actual ToggleButton state before its reload.
            toggle.IsChecked = true;
            glyph.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            glyph.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            TabIconNonblockingTests.PumpUntil(dispatcher, () => mark.Opacity > .999
                && Find<Border>(row, "RowSelectedOverlay")!.Opacity < .001
                && Find<Border>(button, "PressedOverlay")!.Opacity > .999);
            Assert.Equal(Visibility.Visible, mark.Visibility);
            Assert.True(mark.Opacity > .999);
            Assert.Equal(1, ((ScaleTransform)mark.RenderTransform).ScaleX);
            Assert.Equal(180, ((RotateTransform)glyph.RenderTransform).Angle);
            Assert.True(Find<Border>(row, "RowSelectedOverlay")!.Opacity < .001);
            Border buttonBorder = Find<Border>(button, "ButtonBorder")!;
            MotionProfile.SetEnableButtonFeedback(buttonBorder, false);
            MotionProfile.SetEnableButtonFeedback(buttonBorder, true);
            StaDispatcherFixture.Settle(dispatcher);
            TabIconNonblockingTests.PumpUntil(dispatcher, () => Find<Border>(button, "PressedOverlay")!.Opacity > .999);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingEntrance_MidwayOrSettled_PreservesVisibleBaseOpacity(bool midway)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var policy = MotionProfile.OverrideReducedMotion(false);
            using var host = HiddenVisualHost.Create();
            var border = new Border { Width = 30, Height = 20, Opacity = .7, Background = Brushes.Red };
            MotionProfile.SetEnableEntrance(border, true);
            host.Root.Children.Add(border);
            host.Realize();
            if (midway) StaDispatcherFixture.Pump(dispatcher, TimeSpan.FromMilliseconds(40));
            else TabIconNonblockingTests.PumpUntil(dispatcher, () => Math.Abs(border.Opacity - .7) < .001);
            MotionProfile.SetEnableEntrance(border, false);
            Assert.Equal(.7, border.Opacity, 3);
        });
    }

    [Fact]
    public void FixtureTimeout_ShutsDownDispatcherWithoutDisposingWorkerSignal()
    {
        bool released = false;
        Assert.Throws<TimeoutException>(() => StaDispatcherFixture.Run(dispatcher =>
        {
            try { StaDispatcherFixture.Pump(dispatcher, TimeSpan.FromSeconds(1)); }
            finally { released = true; }
        }, TimeSpan.FromMilliseconds(50)));
        Assert.True(released);
    }

    internal static T? Find<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        if (parent is T element && element.Name == name) return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            T? found = Find<T>(VisualTreeHelper.GetChild(parent, i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static void SetPressed(Button button, bool value)
    {
        var key = (DependencyPropertyKey)typeof(ButtonBase).GetField("IsPressedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        button.SetValue(key, value);
    }
}
