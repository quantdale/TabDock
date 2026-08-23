using System;

namespace TabDock.Services;

/// <summary>
/// The canonical capture-admission state projected by <see cref="GroupManager"/>
/// to presentation surfaces. The reason is diagnostic/product copy, not a
/// second admission rule.
/// </summary>
public readonly record struct CaptureAdmissionState(bool Allowed, string Reason);

/// <summary>Raised whenever capture admission or its current reason changes.</summary>
public sealed class CaptureAdmissionChangedEventArgs : EventArgs
{
    public CaptureAdmissionChangedEventArgs(CaptureAdmissionState state) => State = state;

    public CaptureAdmissionState State { get; }
}
