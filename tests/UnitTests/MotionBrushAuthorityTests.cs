using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using TabDock.Infrastructure;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class MotionBrushAuthorityTests
{
    private static readonly Color ActiveFill = Color.FromRgb(0x2A, 0x3E, 0x68);
    private static readonly Color FocusEdge = Color.FromRgb(0x9C, 0xB8, 0xFF);
    private static readonly Color BaseFill = Color.FromRgb(0x15, 0x1A, 0x22);
    private static readonly Color UpdatedFill = Color.FromRgb(0x11, 0x15, 0x1C);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplitActiveStyle_UpdatesAfterMotionAttachment(bool reducedMotion)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(reducedMotion))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                var model = new ActiveMember { IsActive = false };
                Border border = SplitHalf(model);
                host.Root.Children.Add(border);
                host.Realize();
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BackgroundProperty));
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BorderBrushProperty));

                model.IsActive = true;
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(Channel(ActiveFill), Channel(((SolidColorBrush)border.Background).Color));
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BackgroundProperty));

                model.IsActive = false;
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(Channel(Colors.Transparent), Channel(((SolidColorBrush)border.Background).Color));
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BackgroundProperty));
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PickerFocusTrigger_SurvivesAttachmentAndRecycling(bool reducedMotion)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(reducedMotion))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                var item = new ListBoxItem { Width = 180, Height = 36, Focusable = true };
                Border border = PickerRow();
                item.Content = border;
                host.Root.Children.Add(item);
                host.Realize();
                Assert.True(item.Focus());
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(Channel(FocusEdge), Channel(((SolidColorBrush)border.BorderBrush).Color));
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BorderBrushProperty));

                host.Root.Children.Remove(item);
                host.Realize();
                host.Root.Children.Add(item);
                host.Realize();
                Assert.True(item.Focus());
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(Channel(FocusEdge), Channel(((SolidColorBrush)border.BorderBrush).Color));
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BackgroundProperty));
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BorderBrushProperty));
            }
        });
    }

    [Fact]
    public void LiveBinding_RemainsTheBaseSourceUnderFeedback()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(true))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                var source = new BrushSource { Fill = new SolidColorBrush(BaseFill) };
                var border = new Border
                {
                    Width = 120,
                    Height = 28,
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1),
                    Child = Overlays(),
                };
                border.SetBinding(Border.BackgroundProperty, new Binding(nameof(BrushSource.Fill)) { Source = source });
                MotionProfile.SetHoverBackground(border, Brushes.White);
                MotionProfile.SetEnableBorderStates(border, true);
                host.Root.Children.Add(border);
                host.Realize();
                Assert.NotNull(BindingOperations.GetBindingExpression(border, Border.BackgroundProperty));

                RaiseMouse(border, System.Windows.Input.Mouse.MouseEnterEvent);
                StaDispatcherFixture.Settle(dispatcher);
                source.Fill = new SolidColorBrush(UpdatedFill);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.NotNull(BindingOperations.GetBindingExpression(border, Border.BackgroundProperty));
                Assert.Same(source.Fill, border.Background);

                RaiseMouse(border, System.Windows.Input.Mouse.MouseLeaveEvent);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(Channel(UpdatedFill), Channel(((SolidColorBrush)border.Background).Color));
                Assert.Same(source.Fill, border.Background);
            }
        });
    }

    [Fact]
    public void ActualTextBoxTemplate_DoesNotReplaceTemplateBoundBrushes()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(true))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                ResourceDictionary resources = SharedTemplateLoader.Load();
                var box = new TextBox
                {
                    Style = (Style)resources["TdTextBox"],
                    Width = 140,
                    Text = "focus",
                };
                host.Root.Children.Add(box);
                host.Realize();
                box.ApplyTemplate();
                var border = (Border)box.Template.FindName("InputBorder", box);
                Assert.NotNull(border);
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BackgroundProperty));
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BorderBrushProperty));
                Assert.True(box.Focus());
                StaDispatcherFixture.Settle(dispatcher);
                Assert.Equal(DependencyProperty.UnsetValue, border.ReadLocalValue(Border.BorderBrushProperty));
                Border? edge = FindRole(border, MotionProfile.MotionOverlayRole.Edge);
                Assert.NotNull(edge);
                Assert.True(edge.Opacity > 0.5);
            }
        });
    }

    [Fact]
    public void SharedFrozenBrush_IsNotMutatedByFeedback()
    {
        StaDispatcherFixture.Run(_ =>
        {
            using (MotionProfile.OverrideReducedMotion(true))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                var shared = new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x36));
                shared.Freeze();
                Color before = shared.Color;
                Assert.True(shared.IsFrozen);
                var border = new Border
                {
                    Width = 80,
                    Height = 24,
                    Background = new SolidColorBrush(BaseFill),
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    Child = Overlays(),
                };
                MotionProfile.SetHoverBackground(border, shared);
                MotionProfile.SetEnableBorderStates(border, true);
                host.Root.Children.Add(border);
                host.Realize();
                RaiseMouse(border, System.Windows.Input.Mouse.MouseEnterEvent);
                Assert.True(shared.IsFrozen);
                Assert.Equal(before, shared.Color);
            }
        });
    }

    private static Border SplitHalf(ActiveMember model)
    {
        var border = new Border
        {
            Width = 140,
            Height = 32,
            DataContext = model,
            Child = Overlays(),
        };
        MotionProfile.SetHoverBackground(border, new SolidColorBrush(Color.FromRgb(0x22, 0x30, 0x4F)));
        MotionProfile.SetEnableBorderStates(border, true);
        border.Style = SplitStyle();
        return border;
    }

    private static Style SplitStyle()
    {
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        var active = new DataTrigger { Binding = new Binding(nameof(ActiveMember.IsActive)), Value = true };
        active.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(ActiveFill)));
        style.Triggers.Add(active);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(FocusEdge)));
        style.Triggers.Add(focus);
        return style;
    }

    private static Border PickerRow()
    {
        var border = new Border
        {
            Width = 160,
            Height = 28,
            Child = Overlays(),
        };
        MotionProfile.SetHoverBackground(border, new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x36)));
        MotionProfile.SetSelectedBackground(border, new SolidColorBrush(Color.FromRgb(0x18, 0x23, 0x3F)));
        MotionProfile.SetSelectedBorder(border, new SolidColorBrush(Color.FromRgb(0x67, 0x98, 0xFF)));
        MotionProfile.SetEnableBorderStates(border, true);
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(BaseFill)));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Gray));
        style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        var focus = new DataTrigger
        {
            Binding = new Binding(nameof(UIElement.IsKeyboardFocusWithin))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1),
            },
            Value = true,
        };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(FocusEdge)));
        style.Triggers.Add(focus);
        border.Style = style;
        return border;
    }

    private static Grid Overlays()
    {
        var grid = new Grid();
        var fill = new Border { IsHitTestVisible = false, Opacity = 0 };
        MotionProfile.SetOverlayRole(fill, MotionProfile.MotionOverlayRole.Fill);
        var edge = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
        MotionProfile.SetOverlayRole(edge, MotionProfile.MotionOverlayRole.Edge);
        grid.Children.Add(fill);
        grid.Children.Add(edge);
        return grid;
    }

    private static void RaiseMouse(IInputElement element, RoutedEvent routedEvent)
    {
        Assert.NotNull(System.Windows.Input.Mouse.PrimaryDevice);
        element.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = routedEvent,
        });
    }

    private static string Channel(Color color) => $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

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

    private sealed class ActiveMember : INotifyPropertyChanged
    {
        private bool _isActive;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }
    }

    private sealed class BrushSource : INotifyPropertyChanged
    {
        private Brush _fill = Brushes.Transparent;
        public event PropertyChangedEventHandler? PropertyChanged;
        public Brush Fill
        {
            get => _fill;
            set
            {
                _fill = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Fill)));
            }
        }
    }
}
