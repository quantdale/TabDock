using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TabDock.Services;

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
    /// The gate is PER-MONITOR, matching production: it enumerates the real monitors,
    /// reads each one's effective DPI through the contract-correct PMv2 helper,
    /// deliberately places the controlled guest on a
/// non-100% monitor when one exists (so a mixed-DPI machine exercises the policy
/// without touching the primary's scaling), verifies the placement with
/// MonitorFromWindow, re-reads the ACTUAL monitor the guest landed on, and only then
/// RUNs or SKIPs. A failed monitor-DPI probe is a hard FAIL, never a "100% => skip".
/// On a machine with no non-100% monitor the scenario self-skips with an explicit
/// reason rather than producing a false green. They drive real input (supervised),
/// so they are registered in StandaloneExtraScenarios (explicit invocation), never
/// swept into "all".
/// </summary>
internal static partial class Scenarios
{
    /// <summary>One enumerated monitor's geometry plus its effective DPI.</summary>
    private sealed class DpiMonitor
    {
        public IntPtr Handle;
        public NativeMethods.RECT Bounds;
        public NativeMethods.RECT Work;
        /// <summary>Set only when the contract-correct monitor probe succeeded; 0 also means "probe failed".</summary>
        public uint Dpi;

        public bool DpiProbeFailed => Dpi == 0;
        public int ScalePercent => Dpi == 0 ? 0 : (int)Math.Round(Dpi * 100.0 / NativeMethods.USER_DEFAULT_SCREEN_DPI);

        public string Describe() =>
            $"monitor=0x{Handle.ToInt64():X}\n" +
            $"    bounds={Bounds.left},{Bounds.top},{Bounds.Width}x{Bounds.Height}\n" +
            $"    work={Work.left},{Work.top},{Work.Width}x{Work.Height}\n" +
            $"    dpi={Dpi}\n" +
            $"    scale={ScalePercent}%";
    }

    /// <summary>Enumerates every display monitor and its effective DPI. A monitor whose
    /// The PMv2 helper probe fails records Dpi==0 (DpiProbeFailed).</summary>
    private static List<DpiMonitor> EnumerateDpiMonitors()
    {
        var list = new List<DpiMonitor>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr h, IntPtr hdc, ref NativeMethods.RECT r, IntPtr d) =>
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                NativeMethods.GetMonitorInfo(h, ref mi);
                uint dpi = MonitorDpiService.GetEffectiveDpi(h);
                list.Add(new DpiMonitor { Handle = h, Bounds = mi.rcMonitor, Work = mi.rcWork, Dpi = dpi });
                return true;
            }, IntPtr.Zero);
        return list;
    }

    /// <summary>Classifies a window's DPI-awareness context for diagnostics.</summary>
    private static string DescribeGuestAwareness(IntPtr hwnd)
    {
        IntPtr ctx = NativeMethods.GetWindowDpiAwarenessContext(hwnd);
        if (ctx == IntPtr.Zero) return "UNKNOWN";
        if (NativeMethods.AreDpiAwarenessContextsEqual(ctx, NativeMethods.DpiAwarenessContextUnaware)) return "DPI_UNAWARE";
        if (NativeMethods.AreDpiAwarenessContextsEqual(ctx, NativeMethods.DpiAwarenessContextPerMonitorV2)) return "PER_MONITOR_AWARE_V2";
        return "OTHER_AWARE";
    }

    /// <summary>Centers a top-level window inside a target work area (used to deliberately
    /// place the controlled guest on a chosen non-100% monitor).</summary>
    private static void MoveWindowOntoMonitor(IntPtr hwnd, NativeMethods.RECT work)
    {
        if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT cur))
        {
            int w = cur.right - cur.left;
            int h = cur.bottom - cur.top;
            int x = work.left + Math.Max(0, (work.Width - w) / 2);
            int y = work.top + Math.Max(0, (work.Height - h) / 2);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, x, y, w, h, NativeMethods.SWP_NOACTIVATE);
        }
        Thread.Sleep(250); // let DWM retarget the window to the new monitor
    }

    /// <summary>
    /// Runs the per-monitor DPI gate for one scenario: enumerate monitors and their
    /// effective DPI, log diagnostics, deliberately place the guest on a non-100%
    /// monitor when available, verify with MonitorFromWindow, re-read the ACTUAL
    /// monitor's DPI, and only then RUN (returns the guest) or SKIP (returns null).
    /// A monitor-DPI probe failure FAILS the scenario (throws); it is never converted
    /// into a 100%-scaling skip.
    /// </summary>
    private static GuestInfo? PrepareDpiScenario(
        string scenario, Ctx ctx, string tag, string dpiArg, string expectedAwareness)
    {
        List<DpiMonitor> monitors = EnumerateDpiMonitors();

        var diag = new StringBuilder();
        diag.Append($"DPI test monitors (scenario={scenario}):");
        foreach (DpiMonitor m in monitors) diag.Append($"\n  {m.Describe()}");
        GuardedProc.Log(diag.ToString());

        if (monitors.Count == 0)
            throw new InvalidOperationException($"DPI scenario '{scenario}': EnumDisplayMonitors returned no monitors; cannot classify display scaling.");

        DpiMonitor? probeFailed = monitors.FirstOrDefault(m => m.DpiProbeFailed);
        if (probeFailed != null)
            throw new InvalidOperationException(
                $"DPI scenario '{scenario}': monitor DPI probe FAILED for 0x{probeFailed.Handle.ToInt64():X} " +
                $"(contract-correct PMv2 monitor probe failed). FAILING rather than converting the probe failure into a 100%-scaling skip.");

        DpiMonitor? non100 = monitors.FirstOrDefault(m => m.Dpi != NativeMethods.USER_DEFAULT_SCREEN_DPI);
        if (non100 == null)
        {
            GuardedProc.Log($"SKIPPED {scenario}: no non-100% monitor available; detected DPIs: {string.Join(", ", monitors.Select(m => m.Dpi.ToString()))}. DPI-unaware vs aware are indistinguishable at scale 1.");
            ctx.SkipCapability($"{scenario}: no non-100% monitor available (detected DPIs: {string.Join(", ", monitors.Select(m => m.Dpi.ToString()))})");
            return null;
        }

        GuestInfo pig = SpawnPig(ctx, tag, "--dpi", dpiArg);

        // Deliberately place the controlled guest on the chosen non-100% monitor.
        MoveWindowOntoMonitor(pig.Hwnd, non100.Work);

        // Verify placement and re-read the ACTUAL monitor the guest landed on.
        IntPtr actualHandle = NativeMethods.MonitorFromWindow(pig.Hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        DpiMonitor? actualMon = monitors.FirstOrDefault(m => m.Handle == actualHandle) ?? non100;
        string awareness = DescribeGuestAwareness(pig.Hwnd);

        GuardedProc.Log($"Selected monitor:\n  {actualMon.Describe()}");
        GuardedProc.Log($"Guest awareness: {awareness}");

        if (actualMon.DpiProbeFailed)
            throw new InvalidOperationException($"DPI scenario '{scenario}': the guest's actual monitor DPI probe FAILED (0x{actualMon.Handle.ToInt64():X}); cannot classify display scaling.");

        if (actualMon.Dpi == NativeMethods.USER_DEFAULT_SCREEN_DPI)
        {
            GuardedProc.Log($"SKIPPED {scenario}: attempted to place the guest on a non-100% monitor but its actual monitor reads 96 DPI (0x{actualMon.Handle.ToInt64():X}). DPI-unaware vs aware are indistinguishable at scale 1. Detected DPIs: {string.Join(", ", monitors.Select(m => m.Dpi.ToString()))}.");
            ctx.SkipCapability($"{scenario}: guest's actual monitor reads 96 DPI despite placement attempt");
            return null;
        }

        // Validate the GuineaPig --dpi mode actually took effect for the awareness class under test.
        ctx.Check(awareness == expectedAwareness,
            $"pig spawned with --dpi {dpiArg} reports awareness '{awareness}', expected '{expectedAwareness}' (GuineaPig --dpi did not take effect)");

        return pig;
    }

    /// <summary>
    /// capture-dpi-unaware-guest: a KNOWN DPI-unaware guest must be captured normally
    /// (no blanket refusal) and docked into a group when the ACTUAL test monitor is
    /// non-100%. On a machine with no non-100% monitor it self-skips with an explicit
    /// reason; a failed monitor-DPI probe FAILS.
    /// </summary>
    private static void CaptureDpiUnawareGuest(Ctx ctx, Options opt)
    {
        GuestInfo? pig = PrepareDpiScenario("capture-dpi-unaware-guest", ctx, "DUM", "unaware", "DPI_UNAWARE");
        if (pig == null) return; // SKIPPED
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(NativeMethods.IsWindow(container), "container opened while capturing DPI-unaware guest (capture accepted, not refused)");
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 5000), "DPI-unaware guest docked into the group (no geometry drift on acceptance)");
    }

    /// <summary>
    /// capture-dpi-system-guest: a SYSTEM-aware guest must also capture normally under the
    /// revised policy (control case; ensures the policy change did not regress aware guests).
    /// Gated identically on the ACTUAL test monitor's DPI.
    /// </summary>
    private static void CaptureDpiSystemGuest(Ctx ctx, Options opt)
    {
        GuestInfo? pig = PrepareDpiScenario("capture-dpi-system-guest", ctx, "DSY", "system", "OTHER_AWARE");
        if (pig == null) return; // SKIPPED
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        ctx.Check(NativeMethods.IsWindow(container), "container opened while capturing system-aware guest");
        ctx.Check(Util.WaitUntil(() => IsDocked(pig.Hwnd, host), 5000), "system-aware guest docked into the group");
    }
}
