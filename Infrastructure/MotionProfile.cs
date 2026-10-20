using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TabDock.Infrastructure;

public static class MotionProfile
{
    public static readonly TimeSpan FeedbackDuration = TimeSpan.FromMilliseconds(90);
    public static readonly TimeSpan StateDuration = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan NavigationDuration = TimeSpan.FromMilliseconds(220);

    private static readonly CubicEase StandardEase = new() { EasingMode = EasingMode.EaseOut };

    public static bool IsReducedMotion { get; } = DetectReducedMotion();

    public static TimeSpan Feedback => IsReducedMotion ? TimeSpan.Zero : FeedbackDuration;
    public static TimeSpan State => IsReducedMotion ? TimeSpan.Zero : StateDuration;
    public static TimeSpan Navigation => IsReducedMotion ? TimeSpan.Zero : NavigationDuration;

    private static bool DetectReducedMotion()
    {
        string? env = Environment.GetEnvironmentVariable("TABDOCK_REDUCE_MOTION");
        if (!string.IsNullOrWhiteSpace(env) &&
            !env.Equals("0", StringComparison.Ordinal) &&
            !env.Equals("false", StringComparison.OrdinalIgnoreCase))
            return true;
        return !SystemParameters.ClientAreaAnimation;
    }

    private static void Fade(UIElement element, double to, TimeSpan duration)
    {
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(to, duration) { EasingFunction = StandardEase });
    }

    private static void ScaleTo(FrameworkElement element, double scale, TimeSpan duration)
    {
        if (element.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform();
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = transform;
        }
        var animation = new DoubleAnimation(scale, duration) { EasingFunction = StandardEase };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone());
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && match.Name == name)
                return match;
            T? found = FindVisualChild<T>(child, name);
            if (found is not null)
                return found;
        }
        return null;
    }

    public static readonly DependencyProperty EnableButtonFeedbackProperty =
        DependencyProperty.RegisterAttached(
            "EnableButtonFeedback", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnEnableButtonFeedbackChanged));

    public static void SetEnableButtonFeedback(DependencyObject element, bool value) =>
        element.SetValue(EnableButtonFeedbackProperty, value);

    private static void OnEnableButtonFeedbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border element || e.NewValue is not true)
            return;
        if (element.TemplatedParent is Button button)
            AttachButtonFeedback(button, element);
        else
            element.Loaded += (_, _) =>
            {
                if (element.TemplatedParent is Button b)
                    AttachButtonFeedback(b, element);
            };
    }

    private static void AttachButtonFeedback(Button button, Border border)
    {
        if (border.Tag is "wired")
            return;
        border.Tag = "wired";
        Border? hover = FindVisualChild<Border>(border, "HoverOverlay");
        Border? pressed = FindVisualChild<Border>(border, "PressedOverlay");
        button.MouseEnter += (_, _) => { if (hover is not null) Fade(hover, 1, Feedback); };
        button.MouseLeave += (_, _) =>
        {
            if (hover is not null) Fade(hover, 0, State);
            if (pressed is not null) Fade(pressed, 0, State);
            ScaleTo(border, 1, State);
        };
        var descriptor = System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(Button.IsPressedProperty, typeof(Button));
        descriptor.AddValueChanged(button, (_, _) =>
        {
            if (button.IsPressed)
            {
                if (pressed is not null) Fade(pressed, 1, Feedback);
                ScaleTo(border, 0.985, Feedback);
            }
            else
            {
                if (pressed is not null) Fade(pressed, 0, State);
                ScaleTo(border, 1, State);
            }
        });
    }

    public static readonly DependencyProperty EnableRowFeedbackProperty =
        DependencyProperty.RegisterAttached(
            "EnableRowFeedback", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnEnableRowFeedbackChanged));

    public static void SetEnableRowFeedback(DependencyObject element, bool value) =>
        element.SetValue(EnableRowFeedbackProperty, value);

    private static void OnEnableRowFeedbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border element || e.NewValue is not true)
            return;
        AttachRowFeedback(element);
    }

    private static void AttachRowFeedback(Border border)
    {
        if (border.Tag is "wired")
            return;
        border.Tag = "wired";
        FrameworkElement? row = FindAncestorRow(border);
        if (row is null)
        {
            border.Loaded += (_, _) =>
            {
                FrameworkElement? late = FindAncestorRow(border);
                if (late is not null && border.Tag is "wired")
                {
                    border.Tag = "wired-late";
                    WireRow(late, border);
                }
            };
            return;
        }
        WireRow(row, border);
    }

    private static FrameworkElement? FindAncestorRow(DependencyObject element)
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListBoxItem || current is ListViewItem)
                return (FrameworkElement)current;
        }
        return null;
    }

    private static void WireRow(FrameworkElement row, Border border)
    {
        Border? hover = FindVisualChild<Border>(border, "RowHoverOverlay");
        Border? selected = FindVisualChild<Border>(border, "RowSelectedOverlay");
        Border? focus = FindVisualChild<Border>(border, "RowFocusOverlay");
        row.MouseEnter += (_, _) => { if (hover is not null) Fade(hover, 1, Feedback); };
        row.MouseLeave += (_, _) => { if (hover is not null) Fade(hover, 0, State); };
        var selectedDescriptor = System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(System.Windows.Controls.ListBoxItem.IsSelectedProperty,
                typeof(System.Windows.Controls.ListBoxItem));
        selectedDescriptor.AddValueChanged(row, (_, _) =>
        {
            if (selected is not null)
            {
                bool isSelected = row is System.Windows.Controls.ListBoxItem li
                    ? li.IsSelected
                    : row is ListViewItem lvi && lvi.IsSelected;
                Fade(selected, isSelected ? 1 : 0, State);
            }
        });
        var focusDescriptor = System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(FrameworkElement.IsKeyboardFocusWithinProperty, typeof(FrameworkElement));
        focusDescriptor.AddValueChanged(row, (_, _) =>
        {
            if (focus is not null) Fade(focus, row.IsKeyboardFocusWithin ? 1 : 0, State);
        });
        bool initially = row is System.Windows.Controls.ListBoxItem initLi && initLi.IsSelected
            || row is ListViewItem initLvi && initLvi.IsSelected;
        if (initially && selected is not null)
            selected.Opacity = 1;
    }

    public static readonly DependencyProperty EnableCheckmarkFeedbackProperty =
        DependencyProperty.RegisterAttached(
            "EnableCheckmarkFeedback", typeof(bool), typeof(TextBlock),
            new PropertyMetadata(false, OnEnableCheckmarkFeedbackChanged));

    public static void SetEnableCheckmarkFeedback(DependencyObject element, bool value) =>
        element.SetValue(EnableCheckmarkFeedbackProperty, value);

    private static void OnEnableCheckmarkFeedbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock check || e.NewValue is not true)
            return;
        if (check.TemplatedParent is CheckBox box)
            AttachCheckmarkFeedback(box, check);
        else
            check.Loaded += (_, _) =>
            {
                if (check.TemplatedParent is CheckBox b)
                    AttachCheckmarkFeedback(b, check);
            };
    }

    private static void AttachCheckmarkFeedback(CheckBox box, TextBlock check)
    {
        if (check.Tag is "wired")
            return;
        check.Tag = "wired";
        if (box.IsChecked == true)
            check.Visibility = Visibility.Visible;
        var descriptor = System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(ToggleButton.IsCheckedProperty, typeof(ToggleButton));
        descriptor.AddValueChanged(box, (_, _) =>
        {
            if (box.IsChecked == true)
            {
                check.Visibility = Visibility.Visible;
                check.Opacity = 0;
                Fade(check, 1, Feedback);
                if (check.RenderTransform is not ScaleTransform st)
                {
                    st = new ScaleTransform();
                    check.RenderTransformOrigin = new Point(0.5, 0.5);
                    check.RenderTransform = st;
                }
                st.ScaleX = 0.6;
                st.ScaleY = 0.6;
                var animation = new DoubleAnimation(1, Feedback) { EasingFunction = StandardEase };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone());
            }
            else
            {
                check.Visibility = Visibility.Collapsed;
                check.Opacity = 0;
            }
        });
    }

    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.RegisterAttached("HoverBackground", typeof(Brush), typeof(MotionProfile));

    public static void SetHoverBackground(DependencyObject element, Brush value) =>
        element.SetValue(HoverBackgroundProperty, value);

    public static Brush GetHoverBackground(DependencyObject element) =>
        (Brush)element.GetValue(HoverBackgroundProperty);

    public static readonly DependencyProperty SelectedBackgroundProperty =
        DependencyProperty.RegisterAttached("SelectedBackground", typeof(Brush), typeof(MotionProfile));

    public static void SetSelectedBackground(DependencyObject element, Brush value) =>
        element.SetValue(SelectedBackgroundProperty, value);

    public static Brush GetSelectedBackground(DependencyObject element) =>
        (Brush)element.GetValue(SelectedBackgroundProperty);

    public static readonly DependencyProperty SelectedBorderProperty =
        DependencyProperty.RegisterAttached("SelectedBorder", typeof(Brush), typeof(MotionProfile));

    public static void SetSelectedBorder(DependencyObject element, Brush value) =>
        element.SetValue(SelectedBorderProperty, value);

    public static Brush GetSelectedBorder(DependencyObject element) =>
        (Brush)element.GetValue(SelectedBorderProperty);

    public static readonly DependencyProperty FocusBorderProperty =
        DependencyProperty.RegisterAttached("FocusBorder", typeof(Brush), typeof(MotionProfile));

    public static void SetFocusBorder(DependencyObject element, Brush value) =>
        element.SetValue(FocusBorderProperty, value);

    public static Brush GetFocusBorder(DependencyObject element) =>
        (Brush)element.GetValue(FocusBorderProperty);

    public static readonly DependencyProperty EnableBorderStatesProperty =
        DependencyProperty.RegisterAttached(
            "EnableBorderStates", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnEnableBorderStatesChanged));

    public static void SetEnableBorderStates(DependencyObject element, bool value) =>
        element.SetValue(EnableBorderStatesProperty, value);

    private static void OnEnableBorderStatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border || e.NewValue is not true)
            return;
        AttachBorderStates(border);
    }

    private static void AttachBorderStates(Border border)
    {
        if (border.Tag is "wired" || border.Tag is "wired-late")
            return;
        border.Tag = "wired";
        border.Loaded += (_, _) =>
        {
            if (border.Background is not SolidColorBrush bg)
                return;
            var normalBg = new SolidColorBrush(bg.Color);
            border.Background = normalBg;
            SolidColorBrush? normalBorder = null;
            if (border.BorderBrush is SolidColorBrush bb)
            {
                normalBorder = new SolidColorBrush(bb.Color);
                border.BorderBrush = normalBorder;
            }
            WireBorderStates(border, normalBg, normalBorder);
        };
    }

    private static void WireBorderStates(Border border, SolidColorBrush normalBg, SolidColorBrush? normalBorder)
    {
        bool hasSelectedBg = GetSelectedBackground(border) is SolidColorBrush;
        Color hoverBg = (GetHoverBackground(border) as SolidColorBrush)?.Color ?? normalBg.Color;
        Color selectedBg = (GetSelectedBackground(border) as SolidColorBrush)?.Color ?? normalBg.Color;
        bool hasSelectedBorder = GetSelectedBorder(border) is SolidColorBrush;
        Color? selectedBorder = (GetSelectedBorder(border) as SolidColorBrush)?.Color;
        Color? focusBorder = (GetFocusBorder(border) as SolidColorBrush)?.Color;

        void Update()
        {
            bool selected = (hasSelectedBg || hasSelectedBorder) && IsSelectedNow();
            Color bgTarget = selected ? selectedBg : border.IsMouseOver ? hoverBg : normalBg.Color;
            normalBg.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(bgTarget, State) { EasingFunction = StandardEase });
            if (normalBorder is null)
                return;
            Color borderTarget = focusBorder != null && border.IsKeyboardFocusWithin
                ? focusBorder.Value
                : selected && selectedBorder != null
                    ? selectedBorder.Value
                    : normalBorder.Color;
            normalBorder.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(borderTarget, State) { EasingFunction = StandardEase });
        }

        bool IsSelectedNow()
        {
            FrameworkElement? row = FindAncestorRow(border);
            return row is ListBoxItem li && li.IsSelected
                || row is ListViewItem lvi && lvi.IsSelected;
        }

        void OnMouse(object? s, EventArgs e) => Update();
        border.MouseEnter += OnMouse;
        border.MouseLeave += OnMouse;
        var focusDesc = System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(FrameworkElement.IsKeyboardFocusWithinProperty, typeof(FrameworkElement));
        focusDesc.AddValueChanged(border, (_, _) => Update());
        FrameworkElement? ancestor = FindAncestorRow(border);
        if (ancestor is null)
        {
            border.Loaded += (_, _) =>
            {
                FrameworkElement? late = FindAncestorRow(border);
                if (late is not null)
                    WireSelected(late);
            };
        }
        else
        {
            WireSelected(ancestor);
        }

        void WireSelected(FrameworkElement row)
        {
            var selectedDesc = System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(ListBoxItem.IsSelectedProperty, typeof(ListBoxItem));
            selectedDesc.AddValueChanged(row, (_, _) => Update());
        }
        Update();
    }

    public static readonly DependencyProperty PopupMotionProperty =
        DependencyProperty.RegisterAttached(
            "PopupMotion", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnPopupMotionChanged));

    public static void SetPopupMotion(DependencyObject element, bool value) =>
        element.SetValue(PopupMotionProperty, value);

    private static void OnPopupMotionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup || e.NewValue is not true)
            return;
        if (IsReducedMotion)
            popup.PopupAnimation = PopupAnimation.None;
        else
            popup.PopupAnimation = PopupAnimation.Fade;
    }

    public static void PlayEntrance(FrameworkElement element)
    {
        if (IsReducedMotion)
            return;
        element.Opacity = 0;
        var translate = new TranslateTransform();
        element.RenderTransform = translate;
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, Navigation) { EasingFunction = StandardEase });
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, Navigation) { EasingFunction = StandardEase });
    }

    public static readonly DependencyProperty EnableChevronRotateProperty =
        DependencyProperty.RegisterAttached(
            "EnableChevronRotate", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnEnableChevronRotateChanged));

    public static void SetEnableChevronRotate(DependencyObject element, bool value) =>
        element.SetValue(EnableChevronRotateProperty, value);

    private static void OnEnableChevronRotateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock glyph || e.NewValue is not true)
            return;
        if (glyph.TemplatedParent is ToggleButton toggle)
            AttachChevronRotate(toggle, glyph);
        else
            glyph.Loaded += (_, _) =>
            {
                if (glyph.TemplatedParent is ToggleButton t)
                    AttachChevronRotate(t, glyph);
            };
    }

    private static void AttachChevronRotate(ToggleButton toggle, TextBlock glyph)
    {
        if (glyph.Tag is "wired")
            return;
        glyph.Tag = "wired";
        if (glyph.RenderTransform is not RotateTransform rotate)
        {
            rotate = new RotateTransform();
            glyph.RenderTransformOrigin = new Point(0.5, 0.5);
            glyph.RenderTransform = rotate;
        }
        var descriptor = System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(ToggleButton.IsCheckedProperty, typeof(ToggleButton));
        descriptor.AddValueChanged(toggle, (_, _) =>
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(toggle.IsChecked == true ? 180 : 0, State)
                { EasingFunction = StandardEase });
        });
    }

    public static readonly DependencyProperty EnableEntranceProperty =
        DependencyProperty.RegisterAttached(
            "EnableEntrance", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnEnableEntranceChanged));

    public static void SetEnableEntrance(DependencyObject element, bool value) =>
        element.SetValue(EnableEntranceProperty, value);

    private static void OnEnableEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
            return;
        element.Loaded += (_, _) => PlayEntrance(element);
    }
}
