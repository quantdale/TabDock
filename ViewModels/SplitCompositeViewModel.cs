using System;
using System.ComponentModel;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.ViewModels;

/// <summary>
/// Presentation-only wrapper that projects a split pair into ONE tab-strip item
/// reading <c>[ A | B ]</c> instead of two unrelated ordinary tabs. The two
/// captured windows keep their own domain identity (separate
/// <see cref="CapturedWindow"/> / <see cref="TabViewModel"/>); this class only
/// renders them as a single visual slot so the strip communicates "LEFT pane +
/// RIGHT pane". The composite occupies the visual position of the LEFT member
/// and the RIGHT member's ordinary tab representation is suppressed while the
/// pair exists (see GroupViewModel.DisplayTabs).
/// </summary>
public sealed class SplitCompositeViewModel : ViewModelBase
{
    public SplitCompositeViewModel(TabViewModel left, TabViewModel right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
        Left.PropertyChanged += Member_PropertyChanged;
        Right.PropertyChanged += Member_PropertyChanged;
    }

    /// <summary>The member rendered in the LEFT half (and focused by a left-half click).</summary>
    public TabViewModel Left { get; }

    /// <summary>The member rendered in the RIGHT half (and focused by a right-half click).</summary>
    public TabViewModel Right { get; }

    /// <summary>Accessible name that distinguishes the composite's LEFT and RIGHT members.</summary>
    public string AutomationName => $"Split tab: LEFT {Left.Title}; RIGHT {Right.Title}";

    /// <summary>
    /// Stable UI Automation identifier for the generated ListBoxItem. Keeping
    /// this on the item data, rather than only on a nested Border, makes the
    /// composite discoverable while it is dormant as well as presented.
    /// </summary>
    public string AutomationId => "SplitCompositeItem";

    /// <summary>
    /// The tab-strip item container style binds IsSelected TwoWay to IsActive on
    /// every item. The composite is selected whenever either member is the
    /// logical active guest; the individual half highlights still come from the
    /// members' own IsActive values.
    /// </summary>
    private bool _isActive;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    internal void RefreshActiveState()
    {
        IsActive = Left.IsActive || Right.IsActive;
        OnPropertyChanged(nameof(AutomationName));
    }

    /// <summary>
    /// A guest title can change asynchronously while the split projection stays
    /// alive. Forward the member's title/accessibility invalidation so UIA does
    /// not announce the old LEFT/RIGHT names until the next split transition.
    /// </summary>
    private void Member_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabViewModel.Title) or nameof(TabViewModel.AutomationName))
            OnPropertyChanged(nameof(AutomationName));
    }

    /// <summary>Stops the projection from retaining member listeners after split exit.</summary>
    internal void Detach()
    {
        Left.PropertyChanged -= Member_PropertyChanged;
        Right.PropertyChanged -= Member_PropertyChanged;
    }
}
