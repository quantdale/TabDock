using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TabDock.Infrastructure;

/// <summary>
/// Shared motion tokens and opt-in feedback behaviors. Attachments are owned
/// by the element that opted in; they are not rooted by a static registry.
/// </summary>
public static class MotionProfile
{
    public static readonly TimeSpan FeedbackDuration = TimeSpan.FromMilliseconds(90);
    public static readonly TimeSpan StateDuration = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan NavigationDuration = TimeSpan.FromMilliseconds(220);

    private static readonly CubicEase StandardEase = CreateEase();
    private static readonly bool DetectedReducedMotion = DetectReducedMotion();
    private static bool? _reducedMotionOverride;

    private static readonly DependencyProperty AttachmentOwnerProperty =
        DependencyProperty.RegisterAttached(
            "AttachmentOwner",
            typeof(MotionAttachmentOwner),
            typeof(MotionProfile),
            new PropertyMetadata(null));

    public static bool IsReducedMotion => _reducedMotionOverride ?? DetectedReducedMotion;

    public static TimeSpan Feedback => IsReducedMotion ? TimeSpan.Zero : FeedbackDuration;
    public static TimeSpan State => IsReducedMotion ? TimeSpan.Zero : StateDuration;
    public static TimeSpan Navigation => IsReducedMotion ? TimeSpan.Zero : NavigationDuration;

    /// <summary>
    /// Test-only override. Production detection stays a one-time startup read;
    /// this does not watch the live Windows preference.
    /// </summary>
    internal static IDisposable OverrideReducedMotion(bool enabled)
    {
        bool? previous = _reducedMotionOverride;
        _reducedMotionOverride = enabled;
        return new ReducedMotionScope(previous);
    }

    internal static int GetActiveObserverCount(DependencyObject element) =>
        Owner(element)?.ActiveObserverCount ?? 0;

    internal static int GetDescriptorObserverCount(DependencyObject element) =>
        Owner(element)?.DescriptorObserverCount ?? 0;

    internal static int GetGeneration(DependencyObject element) =>
        Owner(element)?.Generation ?? 0;

    internal static int GetEntrancePlayCount(DependencyObject element) =>
        Owner(element)?.EntrancePlayCount ?? 0;

    internal static bool HasAttachmentOwner(DependencyObject element) => Owner(element) is not null;

    /// <summary>
    /// Queues a generation-checked callback on the element's attachment owner.
    /// Disable, unload, and template replacement retire the current generation.
    /// </summary>
    internal static bool TryQueueOwnedCallback(DependencyObject element, Action action)
    {
        MotionAttachmentOwner? owner = Owner(element);
        return owner is not null && owner.TryQueue(action);
    }

    /// <summary>
    /// Replaces authoritative border base tokens without sampling an animated
    /// endpoint. Later live style/binding updates call this seam; it does not
    /// observe picker selection or theme resources itself.
    /// </summary>
    internal static bool TryUpdateBorderBaseTokens(DependencyObject element, Color? background, Color? border)
    {
        MotionAttachmentOwner? owner = Owner(element);
        return owner is not null && owner.TryUpdateBorderBaseTokens(background, border);
    }

    private static MotionAttachmentOwner? Owner(DependencyObject element) =>
        element.GetValue(AttachmentOwnerProperty) as MotionAttachmentOwner;

    private static CubicEase CreateEase()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    private static bool DetectReducedMotion()
    {
        string? env = Environment.GetEnvironmentVariable("TABDOCK_REDUCE_MOTION");
        if (!string.IsNullOrWhiteSpace(env) &&
            !env.Equals("0", StringComparison.Ordinal) &&
            !env.Equals("false", StringComparison.OrdinalIgnoreCase))
            return true;
        return !SystemParameters.ClientAreaAnimation;
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

    private static Border? FindOverlay(DependencyObject parent, MotionOverlayRole role)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border border && GetOverlayRole(border) == role)
                return border;
            Border? found = FindOverlay(child, role);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static FrameworkElement? FindAncestorRow(DependencyObject element)
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListBoxItem or ListViewItem)
                return (FrameworkElement)current;
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
        if (d is not FrameworkElement element)
            return;
        if (e.NewValue is true)
            MotionAttachmentOwner.Enable(element, MotionBehavior.Button);
        else
            MotionAttachmentOwner.Disable(element, MotionBehavior.Button);
    }

    public static readonly DependencyProperty EnableRowFeedbackProperty =
        DependencyProperty.RegisterAttached(
            "EnableRowFeedback", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnEnableRowFeedbackChanged));

    public static void SetEnableRowFeedback(DependencyObject element, bool value) =>
        element.SetValue(EnableRowFeedbackProperty, value);

    private static void OnEnableRowFeedbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        if (e.NewValue is true)
            MotionAttachmentOwner.Enable(element, MotionBehavior.Row);
        else
            MotionAttachmentOwner.Disable(element, MotionBehavior.Row);
    }

    public static readonly DependencyProperty EnableCheckmarkFeedbackProperty =
        DependencyProperty.RegisterAttached(
            "EnableCheckmarkFeedback", typeof(bool), typeof(TextBlock),
            new PropertyMetadata(false, OnEnableCheckmarkFeedbackChanged));

    public static void SetEnableCheckmarkFeedback(DependencyObject element, bool value) =>
        element.SetValue(EnableCheckmarkFeedbackProperty, value);

    private static void OnEnableCheckmarkFeedbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        if (e.NewValue is true)
            MotionAttachmentOwner.Enable(element, MotionBehavior.Checkmark);
        else
            MotionAttachmentOwner.Disable(element, MotionBehavior.Checkmark);
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

    /// <summary>
    /// Optional explicit selection input. Unset means the ancestor list-item
    /// selection is the source. Picker checked-state mapping belongs to the
    /// later capture-selection change and should bind this property rather
    /// than add another brush writer.
    /// </summary>
    public enum MotionOverlayRole
    {
        None = 0,
        Fill = 1,
        Edge = 2,
    }

    public static readonly DependencyProperty OverlayRoleProperty =
        DependencyProperty.RegisterAttached(
            "OverlayRole", typeof(MotionOverlayRole), typeof(MotionProfile),
            new PropertyMetadata(MotionOverlayRole.None));

    public static void SetOverlayRole(DependencyObject element, MotionOverlayRole value) =>
        element.SetValue(OverlayRoleProperty, value);

    public static MotionOverlayRole GetOverlayRole(DependencyObject element) =>
        (MotionOverlayRole)element.GetValue(OverlayRoleProperty);

    public static readonly DependencyProperty IsMotionSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsMotionSelected", typeof(bool?), typeof(MotionProfile),
            new PropertyMetadata(null));

    public static void SetIsMotionSelected(DependencyObject element, bool? value) =>
        element.SetValue(IsMotionSelectedProperty, value);

    public static bool? GetIsMotionSelected(DependencyObject element) =>
        (bool?)element.GetValue(IsMotionSelectedProperty);

    public static readonly DependencyProperty EnableBorderStatesProperty =
        DependencyProperty.RegisterAttached(
            "EnableBorderStates", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnEnableBorderStatesChanged));

    public static void SetEnableBorderStates(DependencyObject element, bool value) =>
        element.SetValue(EnableBorderStatesProperty, value);

    private static void OnEnableBorderStatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        if (e.NewValue is true)
            MotionAttachmentOwner.Enable(element, MotionBehavior.Border);
        else
            MotionAttachmentOwner.Disable(element, MotionBehavior.Border);
    }

    public static readonly DependencyProperty PopupMotionProperty =
        DependencyProperty.RegisterAttached(
            "PopupMotion", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnPopupMotionChanged));

    public static void SetPopupMotion(DependencyObject element, bool value) =>
        element.SetValue(PopupMotionProperty, value);

    private static void OnPopupMotionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup)
            return;
        if (e.NewValue is true)
            MotionAttachmentOwner.Enable(popup, MotionBehavior.Popup);
        else
            MotionAttachmentOwner.Disable(popup, MotionBehavior.Popup);
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
        if (d is not FrameworkElement element)
            return;
        if (e.NewValue is true)
            MotionAttachmentOwner.Enable(element, MotionBehavior.Chevron);
        else
            MotionAttachmentOwner.Disable(element, MotionBehavior.Chevron);
    }

    public static readonly DependencyProperty EnableEntranceProperty =
        DependencyProperty.RegisterAttached(
            "EnableEntrance", typeof(bool), typeof(MotionProfile),
            new PropertyMetadata(false, OnEnableEntranceChanged));

    public static void SetEnableEntrance(DependencyObject element, bool value) =>
        element.SetValue(EnableEntranceProperty, value);

    private static void OnEnableEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        if (e.NewValue is true)
            MotionAttachmentOwner.Enable(element, MotionBehavior.Entrance);
        else
            MotionAttachmentOwner.Disable(element, MotionBehavior.Entrance);
    }

    [Flags]
    private enum MotionBehavior
    {
        None = 0,
        Button = 1,
        Row = 2,
        Checkmark = 4,
        Border = 8,
        Popup = 16,
        Chevron = 32,
        Entrance = 64,
    }

    /// <summary>
    /// Per-element owner stored in a private attached slot. Lifetime hooks live
    /// on the element; descriptor observers are removed with the exact handler
    /// that was added. Nothing in this type is stored in a static list.
    /// </summary>
    private sealed class MotionAttachmentOwner
    {
        private readonly FrameworkElement _element;
        private readonly List<Action> _removals = new();
        private readonly List<DispatcherOperation> _queued = new();
        private readonly List<ClockRef> _clocks = new();
        private MotionBehavior _behaviors;
        private MotionBehavior _wired;
        private bool _hooksInstalled;
        private bool _disposed;
        private bool _templateWatched;
        private int _generation = 1;
        private PopupAnimation? _previousPopupAnimation;
        private Color _baseBackground;
        private Color _baseBorder;
        private bool _hasBaseBackground;
        private bool _hasBaseBorder;
        private bool _pointerOver;
        private SolidColorBrush? _renderBackground;
        private SolidColorBrush? _renderBorder;
        private Action? _refreshBorder;

        private MotionAttachmentOwner(FrameworkElement element) => _element = element;

        public int ActiveObserverCount { get; private set; }
        public int DescriptorObserverCount { get; private set; }
        public int Generation => _generation;
        public int EntrancePlayCount { get; private set; }

        public static void Enable(FrameworkElement element, MotionBehavior behavior)
        {
            MotionAttachmentOwner owner = GetOrCreate(element);
            owner.EnableCore(behavior);
        }

        public static void Disable(FrameworkElement element, MotionBehavior behavior)
        {
            if (element.GetValue(AttachmentOwnerProperty) is not MotionAttachmentOwner owner)
                return;
            owner.DisableCore(behavior);
        }

        public bool TryQueue(Action action)
        {
            if (_disposed || action is null)
                return false;
            int generation = _generation;
            DispatcherOperation operation = _element.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                () =>
                {
                    if (_disposed || generation != _generation || _wired == MotionBehavior.None)
                        return;
                    action();
                });
            _queued.Add(operation);
            return true;
        }

        public bool TryUpdateBorderBaseTokens(Color? background, Color? border)
        {
            if (_disposed)
                return false;
            if (background is Color fill)
            {
                _baseBackground = fill;
                _hasBaseBackground = true;
                if (_renderBackground is not null)
                    _renderBackground.Color = fill;
            }
            if (border is Color edge)
            {
                _baseBorder = edge;
                _hasBaseBorder = true;
                if (_renderBorder is not null)
                    _renderBorder.Color = edge;
            }
            _refreshBorder?.Invoke();
            return true;
        }

        private static MotionAttachmentOwner GetOrCreate(FrameworkElement element)
        {
            if (element.GetValue(AttachmentOwnerProperty) is MotionAttachmentOwner existing)
                return existing;
            var created = new MotionAttachmentOwner(element);
            element.SetValue(AttachmentOwnerProperty, created);
            return created;
        }

        private void EnableCore(MotionBehavior behavior)
        {
            if (_disposed)
                return;
            _behaviors |= behavior;
            InstallHooks();
            if (behavior == MotionBehavior.Popup && _element is Popup popup)
                ApplyPopup(popup);
            if (_element.IsLoaded)
                QueueActivate();
        }

        private void DisableCore(MotionBehavior behavior)
        {
            _behaviors &= ~behavior;
            if (behavior == MotionBehavior.Popup && _element is Popup popup)
                RestorePopup(popup);
            if (_behaviors == MotionBehavior.None)
            {
                Dispose();
                return;
            }

            if (_wired != MotionBehavior.None)
            {
                DeactivateObservers();
                if (_element.IsLoaded)
                    QueueActivate();
            }
        }

        private void InstallHooks()
        {
            if (_hooksInstalled)
                return;
            _element.Loaded += OnLoaded;
            _element.Unloaded += OnUnloaded;
            _hooksInstalled = true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_disposed)
                return;
            ActivateNow();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_disposed)
                return;
            DeactivateObservers();
        }

        private void QueueActivate()
        {
            int generation = _generation;
            DispatcherOperation operation = _element.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () =>
                {
                    if (_disposed || generation != _generation || _behaviors == MotionBehavior.None)
                        return;
                    ActivateNow();
                });
            _queued.Add(operation);
        }

        private void ActivateNow()
        {
            if (_disposed || _behaviors == MotionBehavior.None)
                return;
            if ((_behaviors & MotionBehavior.Button) != 0 && (_wired & MotionBehavior.Button) == 0 && WireButton())
                _wired |= MotionBehavior.Button;
            if ((_behaviors & MotionBehavior.Row) != 0 && (_wired & MotionBehavior.Row) == 0 && WireRow())
                _wired |= MotionBehavior.Row;
            if ((_behaviors & MotionBehavior.Checkmark) != 0 && (_wired & MotionBehavior.Checkmark) == 0 && WireCheckmark())
                _wired |= MotionBehavior.Checkmark;
            if ((_behaviors & MotionBehavior.Border) != 0 && (_wired & MotionBehavior.Border) == 0 && WireBorder())
                _wired |= MotionBehavior.Border;
            if ((_behaviors & MotionBehavior.Chevron) != 0 && (_wired & MotionBehavior.Chevron) == 0 && WireChevron())
                _wired |= MotionBehavior.Chevron;
            if ((_behaviors & MotionBehavior.Entrance) != 0 && (_wired & MotionBehavior.Entrance) == 0 && WireEntrance())
                _wired |= MotionBehavior.Entrance;
        }

        private void DeactivateObservers()
        {
            if (_wired == MotionBehavior.None && _removals.Count == 0 && _queued.Count == 0 && _clocks.Count == 0)
                return;
            _generation++;
            AbortQueued();
            for (int i = _removals.Count - 1; i >= 0; i--)
                _removals[i]();
            _removals.Clear();
            StopClocks();
            _wired = MotionBehavior.None;
            _templateWatched = false;
            _pointerOver = false;
            _refreshBorder = null;
            _renderBackground = null;
            _renderBorder = null;
            ActiveObserverCount = 0;
            DescriptorObserverCount = 0;
        }

        private void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _behaviors = MotionBehavior.None;
            DeactivateObservers();
            if (_hooksInstalled)
            {
                _element.Loaded -= OnLoaded;
                _element.Unloaded -= OnUnloaded;
                _hooksInstalled = false;
            }
            if (_element is Popup popup)
                RestorePopup(popup);
            if (ReferenceEquals(_element.GetValue(AttachmentOwnerProperty), this))
                _element.ClearValue(AttachmentOwnerProperty);
        }

        private void AbortQueued()
        {
            for (int i = 0; i < _queued.Count; i++)
            {
                DispatcherOperation operation = _queued[i];
                if (operation.Status == DispatcherOperationStatus.Pending)
                    operation.Abort();
            }
            _queued.Clear();
        }

        private void Subscribe(DependencyObject component, DependencyProperty property, Type ownerType, EventHandler handler)
        {
            System.ComponentModel.DependencyPropertyDescriptor descriptor =
                System.ComponentModel.DependencyPropertyDescriptor.FromProperty(property, ownerType);
            descriptor.AddValueChanged(component, handler);
            _removals.Add(() => descriptor.RemoveValueChanged(component, handler));
            ActiveObserverCount++;
            DescriptorObserverCount++;
        }

        private void AddRemoval(Action removal)
        {
            _removals.Add(removal);
            ActiveObserverCount++;
        }

        private void WatchTemplate(FrameworkElement templatedParent)
        {
            if (_templateWatched || templatedParent is not Control)
                return;
            _templateWatched = true;
            EventHandler handler = (_, _) => DeactivateObservers();
            Subscribe(templatedParent, Control.TemplateProperty, typeof(Control), handler);
        }

        private void Track(DependencyObject target, DependencyProperty property)
        {
            for (int i = 0; i < _clocks.Count; i++)
            {
                if (ReferenceEquals(_clocks[i].Target, target) && _clocks[i].Property == property)
                    return;
            }
            _clocks.Add(new ClockRef(target, property));
        }

        private void StopClocks()
        {
            for (int i = 0; i < _clocks.Count; i++)
            {
                ClockRef clock = _clocks[i];
                switch (clock.Target)
                {
                    case Animatable animatable:
                        animatable.BeginAnimation(clock.Property, null);
                        break;
                    case UIElement element:
                        element.BeginAnimation(clock.Property, null);
                        break;
                }
            }
            _clocks.Clear();
        }

        private void Fade(UIElement element, double to, TimeSpan duration)
        {
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(to, duration) { EasingFunction = StandardEase });
            Track(element, UIElement.OpacityProperty);
        }

        private void ScaleTo(FrameworkElement element, double scale, TimeSpan duration)
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
            Track(transform, ScaleTransform.ScaleXProperty);
            Track(transform, ScaleTransform.ScaleYProperty);
        }

        private void ApplyPopup(Popup popup)
        {
            _previousPopupAnimation ??= popup.PopupAnimation;
            popup.PopupAnimation = IsReducedMotion ? PopupAnimation.None : PopupAnimation.Fade;
        }

        private void RestorePopup(Popup popup)
        {
            if (_previousPopupAnimation is not PopupAnimation previous)
                return;
            popup.PopupAnimation = previous;
            _previousPopupAnimation = null;
        }

        private bool WireButton()
        {
            if (_element is not Border border || border.TemplatedParent is not Button button)
                return false;
            WatchTemplate(button);
            Border? hover = FindVisualChild<Border>(border, "HoverOverlay");
            Border? pressed = FindVisualChild<Border>(border, "PressedOverlay");
            MouseEventHandler enter = (_, _) =>
            {
                if (hover is not null)
                    Fade(hover, 1, Feedback);
            };
            MouseEventHandler leave = (_, _) =>
            {
                if (hover is not null)
                    Fade(hover, 0, State);
                if (pressed is not null)
                    Fade(pressed, 0, State);
                ScaleTo(border, 1, State);
            };
            button.MouseEnter += enter;
            button.MouseLeave += leave;
            AddRemoval(() => button.MouseEnter -= enter);
            AddRemoval(() => button.MouseLeave -= leave);
            Subscribe(button, Button.IsPressedProperty, typeof(Button), (_, _) =>
            {
                if (button.IsPressed)
                {
                    if (pressed is not null)
                        Fade(pressed, 1, Feedback);
                    ScaleTo(border, 0.985, Feedback);
                }
                else
                {
                    if (pressed is not null)
                        Fade(pressed, 0, State);
                    ScaleTo(border, 1, State);
                }
            });
            return true;
        }

        private bool WireRow()
        {
            if (_element is not Border border)
                return false;
            FrameworkElement? row = FindAncestorRow(border);
            if (row is null)
                return false;
            Border? hover = FindVisualChild<Border>(border, "RowHoverOverlay");
            Border? selected = FindVisualChild<Border>(border, "RowSelectedOverlay");
            Border? focus = FindVisualChild<Border>(border, "RowFocusOverlay");
            MouseEventHandler enter = (_, _) =>
            {
                if (hover is not null)
                    Fade(hover, 1, Feedback);
            };
            MouseEventHandler leave = (_, _) =>
            {
                if (hover is not null)
                    Fade(hover, 0, State);
            };
            row.MouseEnter += enter;
            row.MouseLeave += leave;
            AddRemoval(() => row.MouseEnter -= enter);
            AddRemoval(() => row.MouseLeave -= leave);
            Subscribe(row, ListBoxItem.IsSelectedProperty, typeof(ListBoxItem), (_, _) =>
            {
                if (selected is null)
                    return;
                bool isSelected = row is ListBoxItem li
                    ? li.IsSelected
                    : row is ListViewItem lvi && lvi.IsSelected;
                Fade(selected, isSelected ? 1 : 0, State);
            });
            Subscribe(row, FrameworkElement.IsKeyboardFocusWithinProperty, typeof(FrameworkElement), (_, _) =>
            {
                if (focus is not null)
                    Fade(focus, row.IsKeyboardFocusWithin ? 1 : 0, State);
            });
            bool initially = row is ListBoxItem initLi && initLi.IsSelected
                || row is ListViewItem initLvi && initLvi.IsSelected;
            if (initially && selected is not null)
                selected.Opacity = 1;
            return true;
        }

        private bool WireCheckmark()
        {
            if (_element is not TextBlock check || check.TemplatedParent is not CheckBox box)
                return false;
            WatchTemplate(box);
            if (box.IsChecked == true)
                check.Visibility = Visibility.Visible;
            Subscribe(box, ToggleButton.IsCheckedProperty, typeof(ToggleButton), (_, _) =>
            {
                if (box.IsChecked == true)
                {
                    check.Visibility = Visibility.Visible;
                    check.Opacity = 0;
                    Fade(check, 1, Feedback);
                    if (check.RenderTransform is not ScaleTransform scale)
                    {
                        scale = new ScaleTransform();
                        check.RenderTransformOrigin = new Point(0.5, 0.5);
                        check.RenderTransform = scale;
                    }
                    scale.ScaleX = 0.6;
                    scale.ScaleY = 0.6;
                    var animation = new DoubleAnimation(1, Feedback) { EasingFunction = StandardEase };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone());
                    Track(scale, ScaleTransform.ScaleXProperty);
                    Track(scale, ScaleTransform.ScaleYProperty);
                }
                else
                {
                    check.Visibility = Visibility.Collapsed;
                    check.Opacity = 0;
                }
            });
            return true;
        }

        private bool WireBorder()
        {
            if (_element is not Border border || border.Background is not SolidColorBrush)
                return false;
            CaptureAuthoritativeBase(border);
            Border? fillOverlay = FindOverlay(border, MotionOverlayRole.Fill);
            Border? edgeOverlay = FindOverlay(border, MotionOverlayRole.Edge);
            SolidColorBrush? fillBrush = null;
            SolidColorBrush? edgeBrush = null;
            if (fillOverlay is not null)
            {
                fillBrush = new SolidColorBrush(_hasBaseBackground ? _baseBackground : Colors.Transparent);
                fillOverlay.Background = fillBrush;
                fillOverlay.IsHitTestVisible = false;
                _renderBackground = fillBrush;
            }
            if (edgeOverlay is not null)
            {
                edgeBrush = new SolidColorBrush(_hasBaseBorder ? _baseBorder : Colors.Transparent);
                edgeOverlay.BorderBrush = edgeBrush;
                edgeOverlay.Background = Brushes.Transparent;
                edgeOverlay.IsHitTestVisible = false;
                _renderBorder = edgeBrush;
            }

            // Precedence: selected fill over hover fill over the untouched
            // semantic brush; focus border over selected border over the
            // untouched semantic border. Overlays hide when they would only
            // repeat the authoritative base.
            void Update()
            {
                bool selected = HasSelectionSource(border) && IsSelectedNow();
                bool hovered = _pointerOver || border.IsMouseOver;
                bool focused = IsFocusNow();
                Color hoverBackground = (GetHoverBackground(border) as SolidColorBrush)?.Color ?? _baseBackground;
                Color selectedBackground = (GetSelectedBackground(border) as SolidColorBrush)?.Color ?? _baseBackground;
                bool showFill = fillBrush is not null && (selected || hovered);
                if (fillBrush is not null && fillOverlay is not null)
                {
                    AnimateColor(fillBrush, selected ? selectedBackground : hoverBackground);
                    Fade(fillOverlay, showFill ? 1 : 0, State);
                }
                if (edgeBrush is null || edgeOverlay is null)
                    return;
                Color? selectedBorder = (GetSelectedBorder(border) as SolidColorBrush)?.Color;
                Color? focusBorder = (GetFocusBorder(border) as SolidColorBrush)?.Color;
                bool showEdge = focused && focusBorder != null || selected && selectedBorder != null;
                Color borderTarget = focused && focusBorder != null
                    ? focusBorder.Value
                    : selectedBorder ?? _baseBorder;
                AnimateColor(edgeBrush, borderTarget);
                Fade(edgeOverlay, showEdge ? 1 : 0, State);
            }

            bool IsSelectedNow()
            {
                if (GetIsMotionSelected(border) is bool explicitSelection)
                    return explicitSelection;
                FrameworkElement? row = FindAncestorRow(border);
                return row is ListBoxItem listBoxItem && listBoxItem.IsSelected
                    || row is ListViewItem listViewItem && listViewItem.IsSelected;
            }

            bool IsFocusNow()
            {
                if (border.IsKeyboardFocusWithin)
                    return true;
                if (border.TemplatedParent is UIElement parent && parent.IsKeyboardFocusWithin)
                    return true;
                FrameworkElement? row = FindAncestorRow(border);
                return row is not null && row.IsKeyboardFocusWithin;
            }

            _refreshBorder = Update;
            MouseEventHandler enter = (_, _) =>
            {
                _pointerOver = true;
                Update();
            };
            MouseEventHandler leave = (_, _) =>
            {
                _pointerOver = false;
                Update();
            };
            border.MouseEnter += enter;
            border.MouseLeave += leave;
            AddRemoval(() => border.MouseEnter -= enter);
            AddRemoval(() => border.MouseLeave -= leave);
            Subscribe(border, Border.BackgroundProperty, typeof(Border), (_, _) =>
            {
                CaptureAuthoritativeBase(border);
                Update();
            });
            Subscribe(border, Border.BorderBrushProperty, typeof(Border), (_, _) =>
            {
                CaptureAuthoritativeBase(border);
                Update();
            });
            Subscribe(border, IsMotionSelectedProperty, typeof(Border), (_, _) => Update());
            Subscribe(border, FrameworkElement.IsKeyboardFocusWithinProperty, typeof(FrameworkElement), (_, _) => Update());
            if (border.TemplatedParent is UIElement templatedParent)
                Subscribe(templatedParent, FrameworkElement.IsKeyboardFocusWithinProperty, typeof(FrameworkElement), (_, _) => Update());
            FrameworkElement? ancestor = FindAncestorRow(border);
            if (ancestor is not null)
            {
                Subscribe(ancestor, ListBoxItem.IsSelectedProperty, typeof(ListBoxItem), (_, _) => Update());
                if (!ReferenceEquals(ancestor, border.TemplatedParent))
                    Subscribe(ancestor, FrameworkElement.IsKeyboardFocusWithinProperty, typeof(FrameworkElement), (_, _) => Update());
            }
            Update();
            return true;
        }

        private void CaptureAuthoritativeBase(Border border)
        {
            if (border.Background is SolidColorBrush fill && !ReferenceEquals(fill, _renderBackground))
            {
                _baseBackground = fill.Color;
                _hasBaseBackground = true;
            }
            if (border.BorderBrush is SolidColorBrush edge && !ReferenceEquals(edge, _renderBorder))
            {
                _baseBorder = edge.Color;
                _hasBaseBorder = true;
            }
        }

        private static bool HasSelectionSource(Border border) =>
            GetSelectedBackground(border) is SolidColorBrush || GetSelectedBorder(border) is SolidColorBrush;

        private void AnimateColor(SolidColorBrush brush, Color target)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(target, State) { EasingFunction = StandardEase });
            Track(brush, SolidColorBrush.ColorProperty);
        }

        private bool WireChevron()
        {
            if (_element is not TextBlock glyph || glyph.TemplatedParent is not ToggleButton toggle)
                return false;
            WatchTemplate(toggle);
            if (glyph.RenderTransform is not RotateTransform rotate)
            {
                rotate = new RotateTransform();
                glyph.RenderTransformOrigin = new Point(0.5, 0.5);
                glyph.RenderTransform = rotate;
            }
            Subscribe(toggle, ToggleButton.IsCheckedProperty, typeof(ToggleButton), (_, _) =>
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(toggle.IsChecked == true ? 180 : 0, State) { EasingFunction = StandardEase });
                Track(rotate, RotateTransform.AngleProperty);
            });
            return true;
        }

        private bool WireEntrance()
        {
            EntrancePlayCount++;
            PlayEntrance(_element);
            Track(_element, UIElement.OpacityProperty);
            if (_element.RenderTransform is TranslateTransform translate)
                Track(translate, TranslateTransform.YProperty);
            return true;
        }
    }

    private readonly struct ClockRef
    {
        public ClockRef(DependencyObject target, DependencyProperty property)
        {
            Target = target;
            Property = property;
        }

        public DependencyObject Target { get; }
        public DependencyProperty Property { get; }
    }

    private sealed class ReducedMotionScope : IDisposable
    {
        private readonly bool? _previous;
        private bool _disposed;

        public ReducedMotionScope(bool? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _reducedMotionOverride = _previous;
        }
    }
}
