using System;
using System.Threading;

namespace TabDock.ValidationDriver;

/// <summary>
/// DPI-acceptance regression coverage (POST-AUDIT DPI COMPATIBILITY FINDING).
///
/// The pre-existing fail-closed guard in WindowShepherdService.Capture refused a
/// KNOWN DPI-unaware guest whenever GetDpiForSystem() != 96, on the premise that an
/// unaware window "would be stretched and misplaced no matter what rect we hand it."
/// Native evidence (see the DPI-acceptance goal) proved that premise false for OUTER-rect
/// positioning: a PerMonitorV2 caller's SetWindowPos with a physical pane rectangle is
/// honored exactly against any top-level window, including a DPI-unaware one
/// (GetWindowRect round-trips the physical rect; the unaware window is DWM-bitmap-stretched
/// — blurry — exactly as it looks standing alone, not a TabDock geometry defect).
///
/// These scenarios assert the revised product policy:
///   * Known DPI-unaware guest  -> captured normally (no blanket refusal).
///   * Known DPI-aware guest    -> captured normally.
///   * Probe failed / UNKNOWN   -> still fails closed (unchanged).
///
/// They are meaningful ONLY at non-100% display scaling: at 100% an unaware and a
/// per-monitor-aware guest are numerically indistinguishable (scale factor 1). On a
/// 100%-scaling machine they self-skip with an explicit reason rather than producing a
/// false green. They drive real input (supervised), so they are registered in
/// StandaloneExtraScenarios (explicit invocation), never swept into "all".
/// </summary>
internal static partial class Scenarios
{
    /// <summary>
    /// True when the current process (PerMonitorV2) reports non-100% system scaling —
    /// the only regime where the DPI-unaware acceptance policy is distinguishable and
    /// worth asserting. Reads the same probe the product guard uses.
    /// </summary>
    private static bool DpiScalingIsNon100() => NativeMethods.GetDpiForSystem() != NativeMethods.USER_DEFAULT_SCREEN_DPI;

    private static void SkipDpi(string scenario)
    {
        GuardedProc.Log($"SKIPPED {scenario}: system scaling is 100% (GetDpiForSystem={NativeMethods.GetDpiForSystem()}); DPI-unaware vs aware are indistinguishable at scale 1. Run on a non-100% display to exercise the DPI-acceptance policy.");
    }

    /// <summary>
    /// capture-dpi-unaware-guest: a KNOWN DPI-unaware guest must be captured normally
    /// (no blanket refusal) and docked into a group. At non-100% scaling this directly
    /// guards the user-visible "not DPI-aware / 100% scaling" refusal regression. At
    /// 100% it self-skips.
    /// </summary>
    private static void CaptureDpiUnawareGuest(Ctx ctx, Options opt)
    {
        if (!DpiScalingIsNon100())
        {
            SkipDpi("capture-dpi-unaware-guest");
            return;
        }
        GuestInfo pig = SpawnPig(ctx, "DUM", "--dpi", "unaware");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(NativeMethods.IsWindow(container), "container opened while capturing DPI-unaware guest (capture accepted, not refused)");
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 5000), "DPI-unaware guest docked into the group (no geometry drift on acceptance)");
    }

    /// <summary>
    /// capture-dpi-system-guest: a SYSTEM-aware guest must also capture normally under the
    /// revised policy (control case; ensures the policy change did not regress aware guests).
    /// </summary>
    private static void CaptureDpiSystemGuest(Ctx ctx, Options opt)
    {
        if (!DpiScalingIsNon100())
        {
            SkipDpi("capture-dpi-system-guest");
            return;
        }
        GuestInfo pig = SpawnPig(ctx, "DSY", "--dpi", "system");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(NativeMethods.IsWindow(container), "container opened while capturing system-aware guest");
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 5000), "system-aware guest docked into the group");
    }
}