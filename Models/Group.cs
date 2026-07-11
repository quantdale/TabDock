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
