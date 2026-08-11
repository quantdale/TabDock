using System;
using System.Collections.Generic;
using System.Linq;
using TabDock.Models;

namespace TabDock.Views;

/// <summary>
/// Result returned by the capture-picker dialog.
/// </summary>
public sealed class CapturePickerResult
{
    /// <summary>
    /// Full process/class/title/executable identities selected by the user.
    /// </summary>
    public IReadOnlyList<WindowCaptureTarget> SelectedTargets { get; init; } = Array.Empty<WindowCaptureTarget>();

    /// <summary>Compatibility projection for callers that only need the handles.</summary>
    public IReadOnlyList<IntPtr> SelectedHwnds => SelectedTargets.Select(t => t.Hwnd).ToArray();

    /// <summary>
    /// The group to add the selected windows to.
    /// <see cref="Guid.Empty"/> means a new group should be created.
    /// </summary>
    public Guid TargetGroupId { get; init; } = Guid.Empty;
}
