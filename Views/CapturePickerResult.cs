using System;
using System.Collections.Generic;

namespace TabDock.Views;

/// <summary>
/// Result returned by the capture-picker dialog.
/// </summary>
public sealed class CapturePickerResult
{
    /// <summary>
    /// HWNDs the user selected to capture.
    /// </summary>
    public IReadOnlyList<IntPtr> SelectedHwnds { get; init; } = Array.Empty<IntPtr>();

    /// <summary>
    /// The group to add the selected windows to.
    /// <see cref="Guid.Empty"/> means a new group should be created.
    /// </summary>
    public Guid TargetGroupId { get; init; } = Guid.Empty;
}
