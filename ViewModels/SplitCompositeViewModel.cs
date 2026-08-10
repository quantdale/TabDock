using System;
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
    }

    /// <summary>The member rendered in the LEFT half (and focused by a left-half click).</summary>
    public TabViewModel Left { get; }

    /// <summary>The member rendered in the RIGHT half (and focused by a right-half click).</summary>
    public TabViewModel Right { get; }

    /// <summary>
    /// The tab-strip item container style binds IsSelected TwoWay to IsActive on
    /// every item. The composite has no single active member (each half's
    /// highlight is driven by its own member's IsActive), so expose a settable
    /// no-op here purely so that binding does not fail on composite items.
    /// </summary>
    public bool IsActive { get; set; }
}
