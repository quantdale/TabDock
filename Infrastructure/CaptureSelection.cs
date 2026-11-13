using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabDock.ViewModels;

namespace TabDock.Infrastructure;

/// <summary>Maps focused capture-row Space input to checked state, not ListBox navigation selection.</summary>
public static class CaptureSelection
{
    public static readonly DependencyProperty EnableRowKeyboardProperty = DependencyProperty.RegisterAttached(
        "EnableRowKeyboard", typeof(bool), typeof(CaptureSelection), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnableRowKeyboard(DependencyObject element) => (bool)element.GetValue(EnableRowKeyboardProperty);
    public static void SetEnableRowKeyboard(DependencyObject element, bool value) => element.SetValue(EnableRowKeyboardProperty, value);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ListBox list)
            return;
        list.PreviewKeyDown -= OnPreviewKeyDown;
        if ((bool)e.NewValue)
            list.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Only the navigation container needs forwarding. A focused checkbox
        // already owns native Space semantics; intercepting it would toggle twice.
        if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None
            || e.OriginalSource is not ListBoxItem item
            || !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), sender)
            || item.DataContext is not CapturePickerViewModel.WindowInfo candidate)
            return;

        if (!e.IsRepeat)
            candidate.IsSelected = !candidate.IsSelected;
        e.Handled = true;
    }
}
