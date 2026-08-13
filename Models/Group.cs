using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TabDock.Models;

/// <summary>
/// A flat tab group. Members are live captured windows; there is no nesting.
/// PersistedTabMetadata holds the layout intent across reboots (HWNDs are not
/// stable, so live re-attachment is intentionally not attempted automatically).
/// Every group is captured via WindowShepherdService: the guest stays an
/// unmodified top-level window; the container positions, shows, hides, and
/// z-orders it instead of adopting it (docs/internal/deep-audit-2026-07-17.md,
/// section 6). There used to be a second, reparenting-based backend
/// (WindowCaptureService); it was deleted because cross-process SetParent +
/// AttachThreadInput is what caused the recurring keyboard-input bugs — see
/// the audit doc for the full root-cause analysis.
/// </summary>
public sealed class Group : INotifyPropertyChanged
{
    private string _name = "Group";
    private string _accentColor = "#2196F3";
    private int _activeIndex;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string AccentColor
    {
        get => _accentColor;
        set => SetProperty(ref _accentColor, value);
    }

    public int ActiveIndex
    {
        get => _activeIndex;
        set
        {
            if (value < 0 && Members.Count > 0)
                value = 0;
            if (value >= Members.Count)
                value = Members.Count - 1;
            SetProperty(ref _activeIndex, value);
        }
    }

    /// <summary>
    /// Live captured windows currently in this group.
    /// </summary>
    public ObservableCollection<CapturedWindow> Members { get; } = new();

    /// <summary>
    /// Tab metadata saved from the previous session. HWNDs are not restored,
    /// but this intent can be used for future matching / re-population UI.
    /// </summary>
    public List<PersistedTabMetadata> PersistedTabs { get; } = new();

    /// <summary>
    /// Whether this group represents actual tab/layout intent. A newly
    /// created group is an interactive shell until its first live member is
    /// captured; restored groups retain this flag through their persisted tab
    /// metadata even though their HWNDs are not restored automatically.
    /// </summary>
    public bool HasMaterializedTabs => Members.Count > 0 || PersistedTabs.Count > 0;

    /// <summary>
    /// The active-tab index saved from the previous session, kept alongside
    /// <see cref="PersistedTabs"/> and for exactly the same reason: a restored
    /// group has no live <see cref="Members"/>, so assigning the loaded index to
    /// <see cref="ActiveIndex"/> is clamped straight to -1 against an empty
    /// collection, and the next (debounced, frequent) save wrote that -1 back —
    /// discarding the intent on the first restore instead of carrying it forward.
    /// Written back verbatim by PersistenceService while the group is unpopulated.
    /// </summary>
    public int PersistedActiveIndex { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

/// <summary>
/// Serializable snapshot of a tab's metadata (no HWND).
/// </summary>
public sealed class PersistedTabMetadata
{
    public string ExePath { get; set; } = string.Empty;
    public string OriginalTitle { get; set; } = string.Empty;
    public string CustomLabel { get; set; } = string.Empty;
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public bool WasMaximized { get; set; }
}
