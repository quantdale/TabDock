using System;

namespace TabDock.ValidationDriver;

/// <summary>One native point that may be used only to activate a verified target.</summary>
internal readonly record struct ForegroundActivationPoint(int X, int Y);

internal enum ForegroundQualificationKind
{
    Refused,
    AlreadyForeground,
    ActivatedByGuardedClick,
}

internal readonly record struct ForegroundQualificationResult(
    ForegroundQualificationKind Kind,
    string Reason)
{
    public bool IsValid => Kind != ForegroundQualificationKind.Refused;
    public bool UsedActivationClick => Kind == ForegroundQualificationKind.ActivatedByGuardedClick;

    public static ForegroundQualificationResult Refused(string reason)
        => new(ForegroundQualificationKind.Refused, reason);
}

/// <summary>
/// Native-free seam for the foreground admission sequence. Arrangement is never
/// treated as proof: a target must still be current, lease-valid, and foreground
/// verified. The only fallback is one click at a separately proven safe point.
/// </summary>
internal interface IForegroundQualificationRuntime
{
    bool LeaseIsActive { get; }

    bool IsTargetCurrent(WindowIdentity expected);

    bool TryArrangeForeground(WindowIdentity expected);

    bool IsTargetForeground(WindowIdentity expected);

    bool TryGetSafeActivationPoint(
        WindowIdentity expected,
        out ForegroundActivationPoint point);

    bool VerifyActivationPoint(
        WindowIdentity expected,
        ForegroundActivationPoint point);

    bool ClickActivationPoint(
        WindowIdentity expected,
        ForegroundActivationPoint point);

    bool VerifyForegroundAfterActivation(WindowIdentity expected);
}

/// <summary>
/// Shared fail-closed foreground qualification. It deliberately has no retry
/// loop: one ordinary arrangement attempt, followed by at most one guarded
/// activation click, is enough to distinguish arrangement from proof.
/// </summary>
internal sealed class ForegroundQualification
{
    private readonly IForegroundQualificationRuntime _runtime;

    public ForegroundQualification(IForegroundQualificationRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public ForegroundQualificationResult Qualify(WindowIdentity expected)
    {
        if (!_runtime.LeaseIsActive)
            return ForegroundQualificationResult.Refused("lease-invalid-before-arrangement");

        if (!_runtime.IsTargetCurrent(expected))
            return ForegroundQualificationResult.Refused("target-identity-not-current");

        bool arrangementSucceeded;
        try
        {
            arrangementSucceeded = _runtime.TryArrangeForeground(expected);
        }
        catch (Exception ex)
        {
            return ForegroundQualificationResult.Refused(
                "foreground-arrangement-" + ex.GetType().Name);
        }

        // SetForegroundWindow's return value is only an arrangement result. The
        // observed foreground plus the lease checkpoint are the admission proof.
        if (_runtime.IsTargetForeground(expected))
        {
            if (!_runtime.LeaseIsActive)
                return ForegroundQualificationResult.Refused("lease-invalid-after-arrangement");
            if (_runtime.VerifyForegroundAfterActivation(expected))
            {
                return new ForegroundQualificationResult(
                    ForegroundQualificationKind.AlreadyForeground,
                    arrangementSucceeded
                        ? "set-foreground-and-proof-succeeded"
                        : "foreground-already-established-after-arrangement-failure");
            }

            return ForegroundQualificationResult.Refused("foreground-proof-failed-after-arrangement");
        }

        if (!_runtime.LeaseIsActive)
            return ForegroundQualificationResult.Refused("lease-invalid-before-activation");

        if (!_runtime.TryGetSafeActivationPoint(expected, out ForegroundActivationPoint point))
            return ForegroundQualificationResult.Refused("no-safe-activation-point");

        if (!_runtime.LeaseIsActive)
            return ForegroundQualificationResult.Refused("lease-invalid-before-activation-point");

        if (!_runtime.VerifyActivationPoint(expected, point))
            return ForegroundQualificationResult.Refused("activation-point-proof-failed");

        if (!_runtime.LeaseIsActive)
            return ForegroundQualificationResult.Refused("lease-invalid-immediately-before-activation");

        // Exactly one guarded click is permitted. The runtime must revalidate the
        // point at dispatch and must send no input when that proof fails.
        if (!_runtime.ClickActivationPoint(expected, point))
            return ForegroundQualificationResult.Refused("activation-click-refused");

        if (!_runtime.LeaseIsActive)
            return ForegroundQualificationResult.Refused("lease-invalid-after-activation");

        if (!_runtime.VerifyForegroundAfterActivation(expected))
            return ForegroundQualificationResult.Refused("foreground-remained-wrong-after-activation");

        return new ForegroundQualificationResult(
            ForegroundQualificationKind.ActivatedByGuardedClick,
            "set-foreground-arrangement-failed; guarded-activation-click-proved-target");
    }
}
