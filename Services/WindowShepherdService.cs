using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>Result of the release transaction owned by WindowShepherdService.</summary>
public enum WindowReleaseOutcome
{
    Released,
    TargetGoneOrRecycled,
    RecoveryPending,
}

/// <summary>Result of a journal-safe hide presentation transaction.</summary>
public enum WindowHideOutcome
{
    Hidden,
    TargetGoneOrRecycled,
    RecoveryPending,
}

internal enum RecoveryMutationOutcome
{
    Restored,
    TargetGoneOrRecycled,
    RecoveryPending,
}

/// <summary>
/// Native seam for the release transaction. Production delegates to USER32/
/// DWM; deterministic diagnostics inject it so identity failures can prove
/// that no native mutation was attempted.
/// </summary>
internal interface IWindowReleaseNativeApi
{
    bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement);
    bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    bool ShowWindow(IntPtr hwnd, int command);
    bool IsWindowVisible(IntPtr hwnd);
    bool SetForegroundWindow(IntPtr hwnd);
    IntPtr GetForegroundWindow();
    int SetTransitionsDisabled(IntPtr hwnd, int value);
    string DescribeWindow(IntPtr hwnd);
}

/// <summary>
/// Native seam for the capture journal-to-token boundary. Keeping SetProp and
/// the first DWM write injectable lets deterministic tests prove that a target
/// which changes after durable journaling is never tagged or mutated.
/// </summary>
internal interface IWindowCaptureNativeApi
{
    IntPtr GetCaptureIdentityToken(IntPtr hwnd);
    IntPtr GetPendingRecoveryToken(IntPtr hwnd);
    bool SetCaptureIdentityToken(IntPtr hwnd, IntPtr token);
    int SetTransitionsDisabled(IntPtr hwnd, int value);
}

internal sealed class NativeWindowReleaseNativeApi : IWindowReleaseNativeApi
{
    public static NativeWindowReleaseNativeApi Instance { get; } = new();

    private NativeWindowReleaseNativeApi() { }

    public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement)
        => NativeMethods.SetWindowPlacement(hwnd, ref placement);

    public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
        => NativeMethods.SetWindowPos(hwnd, insertAfter, x, y, width, height, flags);

    public bool ShowWindow(IntPtr hwnd, int command)
        => NativeMethods.ShowWindow(hwnd, command);

    public bool IsWindowVisible(IntPtr hwnd)
        => NativeMethods.IsWindowVisible(hwnd);

    public bool SetForegroundWindow(IntPtr hwnd)
        => NativeMethods.SetForegroundWindow(hwnd);

    public IntPtr GetForegroundWindow()
        => NativeMethods.GetForegroundWindow();

    public int SetTransitionsDisabled(IntPtr hwnd, int value)
        => NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED,
            ref value,
            sizeof(int));

    public string DescribeWindow(IntPtr hwnd)
        => NativeMethods.DescribeWindow(hwnd);
}

internal sealed class NativeWindowCaptureNativeApi : IWindowCaptureNativeApi
{
    public static NativeWindowCaptureNativeApi Instance { get; } = new();

    private NativeWindowCaptureNativeApi() { }

    public IntPtr GetCaptureIdentityToken(IntPtr hwnd)
        => NativeMethods.GetProp(hwnd, NativeWindowIdentityApi.CaptureIdentityPropertyName);

    public IntPtr GetPendingRecoveryToken(IntPtr hwnd)
        => NativeMethods.GetProp(hwnd, PendingRecoveryService.TemporaryRecoveryPropertyName);

    public bool SetCaptureIdentityToken(IntPtr hwnd, IntPtr token)
        => NativeMethods.SetProp(hwnd, NativeWindowIdentityApi.CaptureIdentityPropertyName, token);

    public int SetTransitionsDisabled(IntPtr hwnd, int value)
        => NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED,
            ref value,
            sizeof(int));
}

/// <summary>
/// TabDock's only capture backend (docs/internal/deep-audit-2026-07-17.md,
/// section 6). A shepherded guest is never restyled, reparented, or re-owned
/// for its entire captured lifetime: no SetParent, no style/ex-style mutation,
/// no owner change, no DPI-message forwarding, no cross-thread input
/// attachment. The only mutations are reversible presentation state —
/// placement, z-order, visibility, and DWM transition suppression
/// (DWMWA_TRANSITIONS_FORCEDISABLED, set at capture, restored on release).
/// Instead, the guest is positioned directly over the container's content area
/// and brought to the true top of the z-order (SetWindowPos with hwndInsertAfter
/// = HWND_TOP — passing the container itself here would place the guest
/// *behind* it, since hwndInsertAfter precedes hWnd in z-order), then the
/// container is immediately pinned right behind the guest so nothing else can
/// slot between them. Hidden with ShowWindow(SW_HIDE) when it is not the
/// active tab.
///
/// Because none of those presentation mutations touch the guest's hierarchy,
/// style, or owner, release is symmetric and simple: restore the placement
/// snapshotted at capture time, re-show it, undo the DWM transition suppression,
/// and remove the reversible identity token. There is no style/owner/parent
/// surgery to get wrong, no permanently-downgraded DPI awareness, and no
/// compositor invalidation from reparenting — the guest renders and receives
/// input exactly as if it were never touched. This is
/// what eliminates the keyboard-input bug class the project used to have:
/// there is no attach/detach state machine, no synthetic WM_ACTIVATE, no
/// shared input queue for anything to race on. See the audit doc's root
/// cause analysis (RC1-RC3) for the full history of the backend this
/// replaced (Services/WindowCaptureService.cs, deleted).
///
/// A guest keeps its own real, visible title bar while docked (the audit's
/// §6.4 notes this as a v1 cosmetic tradeoff, deliberately not addressed by
/// reversibly stripping WS_CAPTION — that reintroduces the exact
/// style-mutation risk this backend exists to avoid). Dragging it by that
/// title bar and z-order pairing on external foreground changes are handled
/// by ContainerWindow's NoteGuestMoveSize/PairZOrderBehindGuest.
/// </summary>
public sealed class WindowShepherdService
{
    private static long _nextCaptureIdentityToken;
    private readonly LoggingService _log;

    // HWNDs for which a positioning-call failure has already been logged this
    // session. Failures (UIPI-blocked SetWindowPos on a guest that became
    // elevated mid-capture, dead HWND, ...) repeat on every drag tick, so only
    // the first failure per window is logged — the hot drag path stays at one
    // integer comparison per tick (PERF25-3 invariant, spec: elevation-guard).
    private readonly HashSet<long> _positioningFailuresLogged = new();

    // A captured HWND can be destroyed and recycled before a queued WinEvent
    // or layout callback reaches the UI thread. Keep identity failures quiet
    // after their first report; the hot positioning paths may otherwise log
    // once per layout tick while the stale member is being removed.
    private readonly HashSet<long> _identityFailuresLogged = new();

    // WM_GETMINMAXINFO is a synchronous cross-process probe. It is only
    // requested from the container's dirty constraint refresh, never from the
    // per-frame glue path, but a guest can still stop pumping messages between
    // refreshes. Keep the wait bounded and retain the last successful value for
    // this CapturedWindow object so a transient timeout cannot make the shell
    // forget a known-safe minimum or freeze for 500 ms per guest.
    internal const uint MinTrackProbeTimeoutMilliseconds = 100;
    private readonly Dictionary<CapturedWindow, (int Width, int Height)> _minTrackCache = new();
    private readonly IWindowIdentityNativeApi _identityApi;
    private readonly IWindowReleaseNativeApi _releaseApi;
    private readonly IWindowCaptureNativeApi _captureApi;
    private readonly IMonitorDpiProbe _monitorDpiProbe;
    private readonly Action<string>? _testSequencingHook;
    internal IPresentationBudgetSink? BudgetSink { get; set; }

    // The native HWND value is not a generation number. Keep the live
    // CapturedWindow object bound to each value so a delayed callback for an
    // old object cannot operate on a same-process re-capture of the same HWND.
    // This map is UI-thread owned, like the captured-member index in
    // GroupManager, and is consulted on both identity tiers.
    private readonly WindowIdentityBinding _capturedByHwnd = new();

    /// <summary>
    /// Logs a failed positioning call with the native error, at most once per
    /// HWND per session. Must be called immediately after the failing call so
    /// <see cref="NativeMethods.FormatLastError"/> reads the right error.
    /// </summary>
    private void LogPositioningFailureOnce(IntPtr hwnd, string operation)
    {
        string error = NativeMethods.FormatLastError();
        if (_positioningFailuresLogged.Add(hwnd.ToInt64()))
            _log.Log($"SHEPHERD[position-fail] {operation} failed for 0x{hwnd.ToInt64():X}: {error} (subsequent failures for this window suppressed)");
        DiagnosticRuntime.Record("repair.native-failure", guest: hwnd, action: operation, result: "failed",
            data: new Dictionary<string, string> { ["error"] = error });
    }

    private readonly string _journalPath;
    private bool _journalStorageChecked;
    private bool _journalStorageAvailable;
    private bool _journalLoadFailed;

    // In-memory journal state. Mutations are written synchronously: this is a
    // capture-session safety journal, not a performance-oriented layout cache.
    // A hard force-kill bypasses every exit handler, so no debounce can be part
    // of the correctness boundary.
    private HiddenWindowJournalFile? _journalCache;

    // A normal capture durably commits its complete recovery entry before the
    // first presentation mutation. Rewriting and fsync'ing that identical entry
    // before every tab-hide made tab switching perform forced disk I/O on the UI
    // thread. Track capture generations whose rescue entry is already durable so
    // ordinary hides can reuse it. An intentional-hide marker removes the token
    // from this set because a later retained capture must re-commit rescue intent.
    private readonly HashSet<long> _durablyJournaledCaptureTokens = new();

    public WindowShepherdService(LoggingService log, string? journalPath = null)
        : this(log, journalPath, identityApi: null, monitorDpiProbe: null, releaseApi: null, captureApi: null, testSequencingHook: null)
    {
    }

    internal WindowShepherdService(
        LoggingService log,
        string? journalPath,
        IWindowIdentityNativeApi? identityApi,
        IMonitorDpiProbe? monitorDpiProbe,
        IWindowReleaseNativeApi? releaseApi,
        IWindowCaptureNativeApi? captureApi = null,
        Action<string>? testSequencingHook = null)
    {
        _log = log;
        _identityApi = identityApi ?? NativeWindowIdentityApi.Instance;
        _releaseApi = releaseApi ?? NativeWindowReleaseNativeApi.Instance;
        _captureApi = captureApi ?? NativeWindowCaptureNativeApi.Instance;
        _monitorDpiProbe = monitorDpiProbe ?? NativeMonitorDpiProbe.Instance;
        _testSequencingHook = testSequencingHook;
        _journalPath = string.IsNullOrWhiteSpace(journalPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabDock", "hidden-windows.json")
            : Path.GetFullPath(journalPath);
    }

    private void TestSequence(string stage)
        => _testSequencingHook?.Invoke(stage);

    /// <summary>
    /// Completes the durable capture transaction. The strong pre-token check is
    /// intentionally separate from the cheap token check: the former closes
    /// the journal-I/O gap before SetProp, while the latter closes the token
    /// installation gap before the first DWM mutation.
    /// </summary>
    private bool TryCompleteCaptureAfterJournal(
        CapturedWindow window,
        IntPtr expectedToken,
        out WindowIdentityResult failure,
        out string reason,
        out bool tokenInstalled)
    {
        failure = WindowIdentityResult.Unverifiable;
        reason = string.Empty;
        tokenInstalled = false;

        if (!_capturedByHwnd.IsCurrent(window))
        {
            failure = WindowIdentityResult.Mismatch;
            reason = "captured object is no longer the current HWND binding";
            return false;
        }

        failure = WindowIdentityGate.EvaluateBeforeCaptureToken(
            window,
            _identityApi,
            verifyExecutable: true,
            verifyProcessInstance: true,
            out reason);
        if (failure != WindowIdentityResult.Match)
            return false;

        // Never overwrite a property belonging to a different capture.
        if (_captureApi.GetCaptureIdentityToken(window.Hwnd) != IntPtr.Zero)
        {
            failure = WindowIdentityResult.Mismatch;
            reason = "a capture token appeared before token installation";
            return false;
        }
        if (_captureApi.GetPendingRecoveryToken(window.Hwnd) != IntPtr.Zero)
        {
            failure = WindowIdentityResult.Mismatch;
            reason = "a pending-recovery token appeared before capture token installation";
            return false;
        }

        if (!_captureApi.SetCaptureIdentityToken(window.Hwnd, expectedToken))
        {
            failure = WindowIdentityResult.Unverifiable;
            reason = "SetProp could not install the capture token";
            return false;
        }
        tokenInstalled = _captureApi.GetCaptureIdentityToken(window.Hwnd) == expectedToken;
        if (!tokenInstalled)
        {
            failure = WindowIdentityResult.Mismatch;
            reason = "the installed capture token could not be verified";
            return false;
        }

        // This is intentionally the last managed work before the first DWM
        // mutation. It is a cheap generation check, not a second heavy probe.
        TestSequence("capture-before-dwm");
        failure = EvaluateCurrentCapturedWindow(
            window,
            "capture-before-dwm",
            verifyExecutable: false,
            verifyProcessInstance: false);
        reason = failure == WindowIdentityResult.Match
            ? "capture generation matched before DWM mutation"
            : "capture generation changed before DWM mutation";
        if (failure != WindowIdentityResult.Match)
            return false;

        _captureApi.SetTransitionsDisabled(window.Hwnd, 1);
        return true;
    }

    internal bool CompleteCaptureAfterJournalForTesting(
        CapturedWindow window,
        out WindowIdentityResult failure,
        out bool tokenInstalled)
    {
        if (!JournalCapture(window))
        {
            failure = WindowIdentityResult.Unverifiable;
            tokenInstalled = false;
            return false;
        }

        return TryCompleteCaptureAfterJournal(
            window,
            new IntPtr(window.WindowIdentityToken),
            out failure,
            out _,
            out tokenInstalled);
    }

    /// <summary>
    /// True when the journal directory can be created and a small durable
    /// probe file can be committed. Capture uses this gate before changing any
    /// guest state; a best-effort log or state file is not enough to make a
    /// capture safe after TerminateProcess.
    /// </summary>
    public bool RecoveryJournalStorageAvailable => EnsureJournalStorage();

    private bool EnsureJournalStorage()
    {
        if (_journalStorageChecked)
            return _journalStorageAvailable;

        _journalStorageChecked = true;
        try
        {
            if (Directory.Exists(_journalPath))
                throw new IOException("The recovery journal path is a directory.");
            string directory = Path.GetDirectoryName(_journalPath)!;
            Directory.CreateDirectory(directory);
            string probePath = _journalPath + ".storage-probe";
            WriteDurableText(probePath, "TabDock recovery journal probe");
            File.Delete(probePath);
            _journalStorageAvailable = true;
        }
        catch (Exception ex)
        {
            _journalStorageAvailable = false;
            _log.LogException("SHEPHERD[journal-storage] unavailable", ex);
        }
        return _journalStorageAvailable;
    }

    /// <summary>
    /// Captures a top-level window without reparenting or restyling it.
    /// Returns null and an error message if capture is refused (e.g. UIPI /
    /// elevation mismatch, or the target is one of TabDock's own windows).
    /// </summary>
    public CapturedWindow? Capture(IntPtr hwnd, out string? error)
    {
        error = null;
        if (!EnsureJournalStorage())
        {
            error = "Capture is disabled because TabDock cannot durably write its guest recovery journal.";
            _log.Log($"SHEPHERD[capture-blocked] HWND 0x{hwnd.ToInt64():X}: durable recovery journal unavailable.");
            return null;
        }
        if (!NativeMethods.IsWindow(hwnd))
        {
            error = "The window no longer exists.";
            return null;
        }
        if (_captureApi.GetPendingRecoveryToken(hwnd) != IntPtr.Zero)
        {
            error = "Cannot capture a window carrying a pending-recovery transaction token.";
            _log.Log($"SHEPHERD[capture-blocked] HWND 0x{hwnd.ToInt64():X}: pending-recovery token is present.");
            return null;
        }

        WindowProcessIdentity initialIdentity = _identityApi.GetProcessIdentity(hwnd);
        uint pid = initialIdentity.ProcessId;
        if (pid == 0 || initialIdentity.ThreadId == 0)
        {
            error = "Could not determine the window's owning process/thread identity.";
            return null;
        }
        if (pid == NativeMethods.GetCurrentProcessId())
        {
            error = "Cannot capture a TabDock window.";
            return null;
        }

        string? initialClass = _identityApi.GetClassName(hwnd);
        string initialTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
        if (string.IsNullOrEmpty(initialClass))
        {
            error = "Could not verify the window's class identity.";
            return null;
        }

        bool checkOk = NativeMethods.IsProcessElevated(pid, out bool targetElevated, out string? elevError);
        if (!checkOk)
        {
            // The token query failed (e.g. OpenProcess/OpenProcessToken denied
            // by a hardened token DACL) — elevation is indeterminate. Fail
            // closed rather than fail open: capturing a possibly-elevated
            // window would leave every subsequent UIPI-blocked positioning
            // call silently failing with the guest floating unpositioned.
            NativeMethods.IsCurrentProcessElevated(out bool selfElevated);
            if (!selfElevated)
            {
                error = "Cannot verify the window's elevation status. Run TabDock as administrator or choose another window.";
                _log.Log($"Shepherd capture blocked: elevation check indeterminate for 0x{hwnd.ToInt64():X} PID {pid}: {elevError}");
                return null;
            }
            _log.Log($"Shepherd capture: elevation check indeterminate for 0x{hwnd.ToInt64():X} PID {pid}, proceeding because TabDock is elevated: {elevError}");
        }
        else if (targetElevated)
        {
            NativeMethods.IsCurrentProcessElevated(out bool selfElevated);
            if (!selfElevated)
            {
                error = "Cannot capture an elevated window. Run TabDock as administrator or choose a non-elevated window.";
                _log.Log($"Shepherd capture blocked: elevated target 0x{hwnd.ToInt64():X} PID {pid}");
                return null;
            }
        }

        // DPI-unaware guests run in a DWM-virtualized 96-DPI coordinate space:
        // their CONTENT is bitmap-stretched by DWM to the monitor's physical
        // size, so they appear blurry (exactly as they look standing alone on
        // that monitor — not a TabDock geometry defect, and not something
        // capture worsens). Crucially, placement is decided by the CALLER, not
        // the target: TabDock is PerMonitorV2, so its SetWindowPos/GetWindowRect
        // calls are never DPI-virtualized and operate in PHYSICAL screen pixels
        // against ANY target HWND's OUTER rect. A PMv2 SetWindowPos with a
        // physical pane rectangle therefore pins an unaware guest's outer frame
        // to that exact physical rect, and GetWindowRect reads it back exactly —
        // no mis-placement, no drift. Per-monitor-aware and system-aware guests
        // are unaffected (system-aware matches on single-DPI systems;
        // per-monitor-aware tracks the container on every monitor).
        //
        // Refusal is therefore reserved for a probe that genuinely FAILS or
        // returns an UNKNOWN context: admitting a guest we could not classify
        // could silently admit an unverifiable coordinate space. A KNOWN
        // DPI_UNAWARE guest is captured normally. The one place the guest's own
        // logical 96-DPI space leaks into TabDock's physical contract is the
        // native minimum-track size; GetEffectiveMinTrackSize converts it
        // centrally at the sole authoritative coordinate boundary.
        try
        {
            IntPtr guestContext = NativeMethods.GetWindowDpiAwarenessContext(hwnd);
            if (guestContext == IntPtr.Zero)
            {
                // PROBE FAILED / UNKNOWN — not a known awareness class.
                error = "Could not verify the window's DPI awareness. TabDock could not confirm the window can be positioned reliably; try another window or run TabDock as administrator. (DPI probe failed)";
                _log.Log($"Shepherd capture blocked: DPI-awareness context could not be read for 0x{hwnd.ToInt64():X} (dpi::probe-failed)");
                return null;
            }

            int guestAwareness = NativeMethods.GetAwarenessFromDpiAwarenessContext(guestContext);
            if (!DpiCapturePolicy.IsKnownAwareness(guestAwareness))
            {
                error = "Could not classify the window's DPI awareness. TabDock refused capture because the coordinate context is unknown.";
                _log.Log($"Shepherd capture blocked: DPI-awareness context was unknown for 0x{hwnd.ToInt64():X} (dpi::probe-unknown)");
                return null;
            }
            bool dpiUnaware = guestAwareness == DpiCapturePolicy.DpiAwarenessUnaware;

            // Scale classification must use the monitor that actually carries
            // the TARGET, not GetDpiForSystem (which is the PRIMARY monitor and
            // misclassifies targets on differently-scaled secondary monitors).
            // GetDpiForWindow(hwnd) on an unaware guest returns 96 by definition,
            // so the contract-correct monitor probe uses a hidden PMv2 helper
            // HWND associated with this monitor. Read it even for aware guests
            // so the capture diagnostic can report the real scale context.
            IntPtr targetMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (targetMonitor == IntPtr.Zero)
            {
                error = "Could not determine the target monitor for capture; try another window.";
                _log.Log($"Shepherd capture blocked: MonitorFromWindow returned null for 0x{hwnd.ToInt64():X} (dpi::probe-failed)");
                return null;
            }
            uint targetDpi = GetMonitorEffectiveDpi(targetMonitor);
            if (!DpiCapturePolicy.HasKnownAwarenessAndMonitorDpi(guestAwareness, targetDpi))
            {
                error = "Could not determine the display scaling on the target monitor; try another window.";
                _log.Log($"Shepherd capture blocked: monitor DPI could not be read for 0x{hwnd.ToInt64():X} (dpi::probe-failed)");
                return null;
            }

            if (dpiUnaware)
            {
                // KNOWN DPI_UNAWARE: capture normally. Outer-rect shepherding is
                // physical-pixel exact regardless of the guest's awareness; the
                // guest is DWM-stretched (blurry) exactly as it is standalone.
                // GetEffectiveMinTrackSize keeps the size-constraint hardening
                // correct for the guest's logical 96-DPI min-track space.
                _log.Log($"Shepherd capture: DPI-unaware target 0x{hwnd.ToInt64():X} accepted at target monitor DPI {targetDpi} (dpi::unaware-accepted; content DWM-scaled, geometry physical-exact)");
            }
        }
        catch (Exception ex)
        {
            // PROBE FAILED / UNKNOWN (thrown). A failed probe must not silently
            // admit a virtualized guest — geometry is only reliable when the
            // awareness probe succeeds.
            error = "Could not verify the window's DPI awareness. TabDock could not confirm the window can be positioned reliably; try another window or run TabDock as administrator. (DPI probe failed)";
            _log.LogException("Shepherd capture: DPI-awareness probe failed (dpi::probe-failed)", ex);
            return null;
        }

        var originalPlacement = new NativeMethods.WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        bool hasValidPlacement = NativeMethods.GetWindowPlacement(hwnd, ref originalPlacement);
        if (!hasValidPlacement)
        {
            _log.Log($"GetWindowPlacement failed for 0x{hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
        }

        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT bounds))
        {
            error = "Could not read the window's screen bounds.";
            _log.Log($"Shepherd capture blocked: GetWindowRect failed for 0x{hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
            return null;
        }

        string? exePath = _identityApi.GetProcessImagePath(pid);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            error = "Could not verify the window's owning executable.";
            _log.Log($"Shepherd capture blocked: executable identity could not be read for 0x{hwnd.ToInt64():X} PID {pid}: {NativeMethods.FormatLastError()}");
            return null;
        }

        // The picker and the capture call race with normal window teardown.
        // Recheck the identity after all metadata probes and before changing
        // DWM state, so a recycled/dead HWND is not admitted as a member.
        if (!NativeMethods.IsWindow(hwnd))
        {
            error = "The window closed while it was being captured.";
            return null;
        }
        WindowProcessIdentity currentIdentity = _identityApi.GetProcessIdentity(hwnd);
        uint currentPid = currentIdentity.ProcessId;
        if (currentPid != pid || currentIdentity.ThreadId != initialIdentity.ThreadId)
        {
            error = "The window changed owners while it was being captured.";
            _log.Log($"Shepherd capture blocked: HWND 0x{hwnd.ToInt64():X} changed process/thread identity.");
            return null;
        }

        string? currentExePath = _identityApi.GetProcessImagePath(currentPid);
        string? finalClass = _identityApi.GetClassName(hwnd);
        string finalTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentExePath)
            || !string.Equals(currentExePath, exePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(finalClass, initialClass, StringComparison.Ordinal)
            || !string.Equals(finalTitle, initialTitle, StringComparison.Ordinal))
        {
            error = "The window identity changed while it was being captured.";
            _log.Log($"Shepherd capture blocked: HWND 0x{hwnd.ToInt64():X} failed final identity verification (pid={currentPid}, class/title changed or executable changed).");
            return null;
        }

        long processStartTimeUtcTicks = _identityApi.GetProcessStartTimeUtcTicks(pid);
        if (processStartTimeUtcTicks == 0)
        {
            error = "Could not verify the target process instance.";
            _log.Log($"Shepherd capture blocked: process-start identity could not be read for HWND 0x{hwnd.ToInt64():X} PID {pid}.");
            return null;
        }

        if (_capturedByHwnd.ContainsHwnd(hwnd))
        {
            error = "The window is already bound to a captured member.";
            _log.Log($"Shepherd capture blocked: HWND 0x{hwnd.ToInt64():X} already has a live captured identity.");
            return null;
        }

        long identityToken = Interlocked.Increment(ref _nextCaptureIdentityToken);
        if (identityToken == 0)
        {
            error = "Could not allocate a window identity token.";
            return null;
        }
        IntPtr identityTokenValue = new(identityToken);
        if (!WindowIdentityGate.IsCaptureTokenAvailable(hwnd, _identityApi))
        {
            error = "Could not establish a same-window identity token.";
            _log.Log($"Shepherd capture blocked: HWND 0x{hwnd.ToInt64():X} already carries a capture identity token.");
            return null;
        }

        var cw = new CapturedWindow
        {
            Hwnd = hwnd,
            ProcessId = pid,
            WindowThreadId = initialIdentity.ThreadId,
            WindowIdentityToken = identityToken,
            ProcessStartTimeUtcTicks = processStartTimeUtcTicks,
            ExePath = exePath,
            OriginalClassName = finalClass ?? string.Empty,
            OriginalTitle = finalTitle,
            OriginalPlacement = originalPlacement,
            HasValidPlacement = hasValidPlacement,
            OriginalBounds = bounds,
            WasMaximized = originalPlacement.showCmd == NativeMethods.SW_SHOWMAXIMIZED,
            OriginallyVisible = NativeMethods.IsWindowVisible(hwnd),
        };

        _capturedByHwnd.Bind(cw);

        if (NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED,
                out bool transitionsDisabled,
                sizeof(int)) == 0)
        {
            cw.HasOriginalTransitionsState = true;
            cw.OriginalTransitionsDisabled = transitionsDisabled;
        }

        // This is the capture-session journal entry, not merely a hidden-tab
        // record. It must be durable before DWM suppression or any positioning
        // call changes the guest's presentation state.
        if (!JournalCapture(cw))
        {
            UnregisterCapturedIdentity(cw);
            error = "Capture is disabled because TabDock could not commit the guest recovery journal.";
            _log.Log($"SHEPHERD[capture-blocked] HWND 0x{hwnd.ToInt64():X}: recovery journal commit failed.");
            return null;
        }

        // JournalCapture is intentionally still before every presentation
        // mutation. The helper performs the strong pre-token revalidation,
        // generation-token installation, and cheap token check immediately
        // before the first DWM mutation.
        if (!TryCompleteCaptureAfterJournal(
                cw,
                identityTokenValue,
                out WindowIdentityResult captureFailure,
                out string captureFailureReason,
                out bool tokenInstalled))
        {
            if (tokenInstalled
                && _captureApi.GetCaptureIdentityToken(hwnd) == identityTokenValue
                && !_identityApi.RemoveCaptureIdentityToken(hwnd, identityTokenValue))
            {
                _log.Log($"SHEPHERD[identity-token] token cleanup failed after capture-boundary rejection for 0x{hwnd.ToInt64():X}; future capture remains fail-closed.");
            }

            bool journalCleared = captureFailure != WindowIdentityResult.Unverifiable && JournalClear(cw);
            UnregisterCapturedIdentity(cw);
            if (captureFailure != WindowIdentityResult.Unverifiable && !journalCleared)
                _log.Log($"SHEPHERD[capture-blocked] HWND 0x{hwnd.ToInt64():X}: capture identity failed but journal cleanup was not proven.");
            error = captureFailure == WindowIdentityResult.Mismatch
                ? "The window changed identity before capture presentation could begin."
                : "The window identity could not be verified before capture presentation could begin.";
            _log.Log($"Shepherd capture blocked: HWND 0x{hwnd.ToInt64():X} capture-boundary result={captureFailure} reason={captureFailureReason}.");
            return null;
        }

        _log.Log($"Shepherd-captured 0x{hwnd.ToInt64():X} ({cw.OriginalTitle}) without reparenting; guest={NativeMethods.DescribeWindow(hwnd)}");
        return cw;
    }

    private void UnregisterCapturedIdentity(CapturedWindow window)
    {
        _capturedByHwnd.Unbind(window);
    }

    internal void BindCapturedWindowForTesting(CapturedWindow window)
    {
        _capturedByHwnd.Bind(window);
    }

    private bool RemoveCaptureIdentityToken(CapturedWindow window)
    {
        if (window.WindowIdentityToken == 0)
            return false;
        bool removed = _identityApi.RemoveCaptureIdentityToken(window.Hwnd, new IntPtr(window.WindowIdentityToken));
        if (!removed)
        {
            _log.Log($"SHEPHERD[identity-token] could not remove the capture token from 0x{window.Hwnd.ToInt64():X}; future capture of this HWND remains fail-closed.");
        }
        return removed;
    }

    private bool RestoreOriginalTransitions(CapturedWindow window)
    {
        int value = window.HasOriginalTransitionsState && window.OriginalTransitionsDisabled ? 1 : 0;
        try
        {
            return _releaseApi.SetTransitionsDisabled(window.Hwnd, value) == 0;
        }
        catch (Exception ex)
        {
            _log.LogException($"SHEPHERD[release-transitions] 0x{window.Hwnd.ToInt64():X}", ex);
            return false;
        }
    }

    /// <summary>
    /// Verifies that a live HWND still represents the captured window before a
    /// shepherd mutation. The hot tier checks the HWND/PID/thread/class tuple
    /// and the live CapturedWindow binding. Slow/destructive paths additionally
    /// verify executable path and process-start identity. The title is
    /// deliberately not part of the stable identity because many guests
    /// legitimately change it while captured.
    /// </summary>
    private WindowIdentityResult EvaluateCurrentCapturedWindow(
        CapturedWindow window,
        string operation,
        bool verifyExecutable,
        bool verifyProcessInstance)
    {
        WindowIdentityResult result;
        string reason;
        if (!_capturedByHwnd.IsCurrent(window))
        {
            result = WindowIdentityResult.Mismatch;
            reason = "captured object is no longer the current HWND binding";
        }
        else
        {
            result = WindowIdentityGate.Evaluate(
                window,
                _identityApi,
                verifyExecutable,
                verifyProcessInstance,
                out reason);
        }

        if (result != WindowIdentityResult.Match && _identityFailuresLogged.Add(window.Hwnd.ToInt64()))
        {
            _log.Log($"SHEPHERD[identity-blocked] {operation} refused for 0x{window.Hwnd.ToInt64():X}: result={result} reason={reason}.");
        }

        return result;
    }

    private bool IsCurrentCapturedWindow(
        CapturedWindow window,
        string operation,
        bool verifyExecutable,
        bool verifyProcessInstance)
        => EvaluateCurrentCapturedWindow(window, operation, verifyExecutable, verifyProcessInstance)
            == WindowIdentityResult.Match;

    private bool IsCurrentMutationGeneration(CapturedWindow window, string operation)
    {
        TestSequence(operation + ".before");
        return EvaluateCurrentCapturedWindow(
            window,
            operation,
            verifyExecutable: false,
            verifyProcessInstance: false) == WindowIdentityResult.Match;
    }

    internal WindowIdentityResult EvaluateCurrentCapturedWindow(
        CapturedWindow window,
        bool verifyExecutable,
        bool verifyProcessInstance)
        => EvaluateCurrentCapturedWindow(window, "diagnostic", verifyExecutable, verifyProcessInstance);

    private void LogIdentityReleaseOutcome(
        CapturedWindow window,
        WindowIdentityResult identityResult,
        string reason)
    {
        _log.Log($"SHEPHERD[release-decision] guest=0x{window.Hwnd.ToInt64():X} identity={identityResult} reason={reason}.");
    }

    /// <summary>
    /// Public identity gate for non-shepherd callers that need to send a
    /// narrowly-scoped native message to a captured guest. The full check is
    /// intentional here: these callers are destructive-message paths, not the
    /// per-frame positioning hot path.
    /// </summary>
    public bool IsCurrentCapturedWindow(CapturedWindow window)
        => EvaluateCurrentCapturedWindow(window, "external", verifyExecutable: true, verifyProcessInstance: true)
            == WindowIdentityResult.Match;

    /// <summary>
    /// Captures the independent native identity needed by the close-group Yes
    /// transaction before release removes the live capture binding and token.
    /// </summary>
    internal bool TryCreateReleasedWindowCloseTarget(
        CapturedWindow window,
        out ReleasedWindowCloseTarget target,
        out WindowIdentityResult result,
        out string reason)
    {
        target = default;
        result = EvaluateCurrentCapturedWindow(
            window,
            "close-group-snapshot",
            verifyExecutable: true,
            verifyProcessInstance: true);
        reason = result == WindowIdentityResult.Match
            ? "captured target identity matched before release"
            : "captured target identity could not be proven before release";
        if (result != WindowIdentityResult.Match)
            return false;

        target = ReleasedWindowCloseTarget.FromCaptured(window);
        return true;
    }

    /// <summary>
    /// Revalidates a released target without consulting the live Shepherd
    /// registry. Only an exact match is safe for WM_CLOSE.
    /// </summary>
    internal ReleasedWindowCloseTargetResult VerifyReleasedWindowCloseTarget(
        ReleasedWindowCloseTarget target,
        out string reason)
        => WindowIdentityGate.VerifyReleasedCloseTarget(target, _identityApi, out reason);

    /// <summary>
    /// Restores an iconic/zoomed captured guest only after the strong slow-path
    /// identity gate. The BOOL returned by ShowWindow reports the previous
    /// visibility state, so the required post-state is visible, non-iconic,
    /// and non-zoomed. A benign false return therefore cannot consume the
    /// native positioning-failure suppression slot.
    /// </summary>
    public bool RestoreMinimized(CapturedWindow window)
    {
        if (!IsCurrentCapturedWindow(window, "restore-minimized", verifyExecutable: true, verifyProcessInstance: true))
            return false;
        return RestoreForMutation(window, "restore-minimized", identityAlreadyVerified: true);
    }

    private bool RestoreForMutation(
        CapturedWindow window,
        string operation,
        bool identityAlreadyVerified = false)
    {
        if (!identityAlreadyVerified
            && !IsCurrentCapturedWindow(window, operation, verifyExecutable: true, verifyProcessInstance: true))
        {
            return false;
        }

        if (!IsCurrentMutationGeneration(window, operation + "-boundary"))
            return false;

        bool previouslyVisible = _releaseApi.ShowWindow(window.Hwnd, NativeMethods.SW_RESTORE);
        if (EvaluateCurrentCapturedWindow(
                window,
                operation + "-post-restore",
                verifyExecutable: false,
                verifyProcessInstance: false) != WindowIdentityResult.Match)
        {
            return false;
        }
        bool restored = ShowWindowSemantics.RestoreSucceeded(
            previouslyVisible,
            visibleAfter: _releaseApi.IsWindowVisible(window.Hwnd),
            iconicAfter: NativeMethods.IsIconic(window.Hwnd),
            zoomedAfter: NativeMethods.IsZoomed(window.Hwnd));
        if (!restored)
            LogPositioningFailureOnce(window.Hwnd, $"ShowWindow(SW_RESTORE) [{operation}]");
        return restored;
    }

    /// <summary>
    /// Positions the guest to exactly cover <paramref name="screenRect"/> and
    /// places it immediately above <paramref name="containerHwnd"/> in
    /// z-order, then shows it. Restores the guest first if it is iconic or
    /// zoomed, since either state would otherwise fight the exact-fit resize.
    /// The capture-session journal remains until Release completes; an active
    /// guest can still be left at TabDock-controlled presentation state by a
    /// hard kill.
    /// </summary>
    public void PositionAndShow(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect)
        => PositionAndShowCore(window, containerHwnd, screenRect, verifyProcessInstance: false);

    private void PositionAndShowCore(
        CapturedWindow window,
        IntPtr containerHwnd,
        NativeMethods.RECT screenRect,
        bool verifyProcessInstance)
    {
        RuntimeTelemetry.Instance.RecordSetWindowPos();
        if (!NativeMethods.IsWindow(containerHwnd)
            || !IsCurrentCapturedWindow(window, "position", verifyExecutable: false, verifyProcessInstance: verifyProcessInstance))
            return;

        if ((NativeMethods.IsIconic(window.Hwnd) || NativeMethods.IsZoomed(window.Hwnd))
            && !RestoreForMutation(window, "position"))
        {
            return;
        }

        if (!IsCurrentMutationGeneration(window, "position-before-set-window-pos"))
            return;

        // SetWindowPos's hWndInsertAfter PRECEDES (sits above) hWnd in z-order,
        // so passing containerHwnd here would put the guest BEHIND its own
        // container. Bring the guest to the true top instead, then pin the
        // container immediately behind it so nothing else can slot between.
        bool positioned = NativeMethods.SetWindowPos(
            window.Hwnd,
            NativeMethods.HWND_TOP,
            screenRect.left,
            screenRect.top,
            screenRect.Width,
            screenRect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        if (!positioned)
        {
            LogPositioningFailureOnce(window.Hwnd, "SetWindowPos(guest)");
        }
        else
        {
            BudgetSink?.RecordPositionAndShow(window.Hwnd);
        }

        if (!IsCurrentCapturedWindow(window, "position-before-z-order", verifyExecutable: false, verifyProcessInstance: false))
            return;
        PairZOrderBehindCore(containerHwnd, window.Hwnd, window);

        // Deliberately NOT DescribeWindow here: this is the hottest logging site
        // in the app (it runs on every LocationChanged/SizeChanged tick while a
        // container is dragged or resized) and DescribeWindow costs five extra
        // P/Invokes to report a rect that this line already carries — the one
        // this call is in the middle of applying, at that.
        _log.Log($"SHEPHERD[position] guest=0x{window.Hwnd.ToInt64():X} rect={screenRect.left},{screenRect.top},{screenRect.Width}x{screenRect.Height}");
    }

    /// <summary>
    /// Positions a guest to exactly cover <paramref name="screenRect"/> and
    /// inserts it into the z-order immediately BELOW
    /// <paramref name="insertAfter"/> (SetWindowPos places the window below its
    /// hWndInsertAfter). Split-screen building block: two guests are visible at
    /// once, so the caller establishes their relative order via
    /// <paramref name="insertAfter"/>; pass <see cref="NativeMethods.HWND_TOP"/>
    /// to raise a guest to the top. Restores the guest first if iconic or
    /// zoomed, since either state would fight the exact-fit resize. Clears the
    /// capture-session journal entry: an actively-shown window can still need
    /// full-state rescue after a hard kill.
    /// </summary>
    public void PositionGuest(CapturedWindow window, NativeMethods.RECT screenRect, IntPtr insertAfter)
    {
        if (!IsCurrentCapturedWindow(window, "position-split", verifyExecutable: false, verifyProcessInstance: false))
            return;

        if (NativeMethods.IsIconic(window.Hwnd) || NativeMethods.IsZoomed(window.Hwnd))
            if (!RestoreForMutation(window, "position-split"))
                return;

        if (!IsCurrentMutationGeneration(window, "position-split-before-set-window-pos"))
            return;

        if (!NativeMethods.SetWindowPos(
            window.Hwnd,
            insertAfter,
            screenRect.left,
            screenRect.top,
            screenRect.Width,
            screenRect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
        {
            LogPositioningFailureOnce(window.Hwnd, "SetWindowPos(guest-split)");
        }

        // Deliberately NOT DescribeWindow here (same hot-path reason as
        // PositionAndShow): split layout runs on every move/resize tick.
        _log.Log($"SHEPHERD[position] guest=0x{window.Hwnd.ToInt64():X} rect={screenRect.left},{screenRect.top},{screenRect.Width}x{screenRect.Height}");
    }

    /// <summary>Queries a captured guest's effective native minimum track size (the size it refuses to shrink below) via a bounded cross-process WM_GETMINMAXINFO probe. Returns the min width/height in physical pixels, plus whether the probe was available. Callers invoke this only on dirty constraint transitions; a timeout uses the last successful value for the same captured object.</summary>
    public (int MinWidth, int MinHeight, bool Available) GetEffectiveMinTrackSize(CapturedWindow window)
    {
        if (!NativeMethods.IsWindow(window.Hwnd)
            || !IsCurrentCapturedWindow(window, "min-track", verifyExecutable: true, verifyProcessInstance: true))
        {
            _minTrackCache.Remove(window);
            return (0, 0, false);
        }
        var mmi = new NativeMethods.MINMAXINFO();
        IntPtr lParam = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MINMAXINFO>());
        try
        {
            // WM_GETMINMAXINFO normally arrives from USER32 with an already
            // initialized MINMAXINFO buffer. This is an out-of-band probe,
            // so USER32 does not initialize the memory for us; an
            // AllocHGlobal buffer contains indeterminate bytes. Seed the
            // complete structure before sending it cross-process, otherwise
            // a guest that leaves one field untouched can report arbitrary
            // values (observed as a 65,535px minimum height during maximize).
            InitializeMinTrackProbeBuffer(lParam);
            IntPtr result = IntPtr.Zero;
            IntPtr handle = NativeMethods.SendMessageTimeout(window.Hwnd, NativeMethods.WM_GETMINMAXINFO, IntPtr.Zero, lParam, NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_NORMAL, MinTrackProbeTimeoutMilliseconds, out result);
            if (handle == IntPtr.Zero)
            {
                if (_minTrackCache.TryGetValue(window, out var cached))
                {
                    _log.Log($"SHEPHERD[sizemin] guest=0x{window.Hwnd.ToInt64():X} probe timed out/failed; retaining cached minimum {cached.Width}x{cached.Height}.");
                    return (cached.Width, cached.Height, true);
                }
                _log.Log($"SHEPHERD[sizemin] guest=0x{window.Hwnd.ToInt64():X} probe timed out/failed; no cached minimum, using unconstrained fallback.");
                return (0, 0, false); // send failed / timed out / UIPI-blocked
            }
            mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            int minW = Math.Max(0, mmi.ptMinTrackSize.x);
            int minH = Math.Max(0, mmi.ptMinTrackSize.y);
            minW = ToPhysicalScaleForGuest(window.Hwnd, minW);
            minH = ToPhysicalScaleForGuest(window.Hwnd, minH);
            _minTrackCache[window] = (minW, minH);
            return (minW, minH, true);
        }
        catch (Exception ex)
        {
            _log.LogException("SHEPHERD[sizemin] probe failed", ex);
            if (_minTrackCache.TryGetValue(window, out var cached))
                return (cached.Width, cached.Height, true);
            return (0, 0, false);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(lParam);
        }
    }

    /// <summary>
    /// Initializes the native buffer used by the synthetic WM_GETMINMAXINFO
    /// probe. A real system message supplies this initialized buffer; the
    /// cross-process SendMessageTimeout seam must do so explicitly.
    /// </summary>
    internal static void InitializeMinTrackProbeBuffer(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
            throw new ArgumentNullException(nameof(lParam));
        Marshal.StructureToPtr(new NativeMethods.MINMAXINFO(), lParam, fDeleteOld: false);
    }

    /// <summary>
    /// Returns the effective physical DPI of a monitor handle, or 0 when the
    /// contract-correct PMv2 helper probe fails. Used by both the capture gate
    /// and the min-track conversion so the scale source is one authoritative
    /// helper and never runs on the per-frame glue path.
    /// </summary>
    private uint GetMonitorEffectiveDpi(IntPtr monitor)
        => _monitorDpiProbe.GetEffectiveDpi(monitor);

    /// <summary>
    /// Converts a single native minimum-track dimension a guest reported via
    /// WM_GETMINMAXINFO into the PHYSICAL-pixel space TabDock's size-constraint
    /// contract lives in. WM_GETMINMAXINFO is answered by the TARGET's own
    /// window proc, so a DPI-unaware guest fills <c>ptMinTrackSize</c> in ITS
    /// logical 96-DPI space; Windows DWM-scales that logical size by the
    /// monitor's effective DPI to get the real physical minimum the guest
    /// enforces. This is the single authoritative logical→physical boundary for
    /// guest min-track geometry — no multiplicative scaling is scattered across
    /// the codebase. Awareness-aware guests report in a space already consistent
    /// with the physical contract, and at 100% (any guest) the factor is 1, so
    /// this is a strict no-op except for an unaware guest on a scaled monitor.
    /// </summary>
    private int ToPhysicalScaleForGuest(IntPtr guestHwnd, int value)
    {
        if (value <= 0)
            return value;
        try
        {
            IntPtr ctx = NativeMethods.GetWindowDpiAwarenessContext(guestHwnd);
            if (ctx == IntPtr.Zero)
                return value;
            int awareness = NativeMethods.GetAwarenessFromDpiAwarenessContext(ctx);
            if (!DpiCapturePolicy.IsKnownAwareness(awareness)
                || awareness != DpiCapturePolicy.DpiAwarenessUnaware)
                return value; // aware guest: value already in the physical contract.
            IntPtr monitor = NativeMethods.MonitorFromWindow(guestHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            uint dpi = _monitorDpiProbe.GetEffectiveDpi(monitor);
            // SplitGeometry owns the (pure, deterministic) logical->physical math;
            // here we only decide whether the guest is unaware and feed it the
            // target monitor's effective DPI.
            return DpiCapturePolicy.ShouldScaleUnawareMinimum(awareness, dpi)
                ? SplitGeometry.ScaleUnawareLogicalToPhysical(value, dpi)
                : value;
        }
        catch (Exception)
        {
            return value;
        }
    }

    /// <summary>
    /// Positions both split guests and re-pins the container in a single
    /// compositor transaction (BeginDeferWindowPos / DeferWindowPos /
    /// EndDeferWindowPos) instead of three separate SetWindowPos calls. The
    /// atomic batch removes the visible pane separation that occurred between
    /// the individual writes (the top pane moved while the bottom pane was
    /// still at its old position). Falls back to per-guest PositionGuest +
    /// PairZOrderBehind if the deferred handle cannot be created. The container
    /// is inserted below the bottom (partner) guest, preserving the local
    /// top -> partner -> container z-order invariant.
    /// </summary>
    public void PositionGuestsDeferred(CapturedWindow top, NativeMethods.RECT topRect, CapturedWindow bottom, NativeMethods.RECT bottomRect, IntPtr containerHwnd)
    {
        RuntimeTelemetry.Instance.RecordSetWindowPos();
        if (!IsCurrentCapturedWindow(top, "position-split", verifyExecutable: false, verifyProcessInstance: false)
            || !IsCurrentCapturedWindow(bottom, "position-split", verifyExecutable: false, verifyProcessInstance: false)
            || !NativeMethods.IsWindow(containerHwnd))
            return;

        if (NativeMethods.IsIconic(top.Hwnd) || NativeMethods.IsZoomed(top.Hwnd))
            if (!RestoreForMutation(top, "position-split"))
                return;
        if (NativeMethods.IsIconic(bottom.Hwnd) || NativeMethods.IsZoomed(bottom.Hwnd))
            if (!RestoreForMutation(bottom, "position-split"))
                return;

        // The restore calls above are native operations and can block while a
        // guest tears down. Revalidate both cheap generations immediately
        // before committing the deferred batch; no process-start probe occurs
        // on this hot path.
        if (!IsCurrentMutationGeneration(top, "position-split-before-deferred-batch")
            || !IsCurrentMutationGeneration(bottom, "position-split-before-deferred-batch"))
            return;

        const uint guestFlags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
        const uint containerFlags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
        DeferredWindowPositionResult result = DeferredWindowPositionBatch.Apply(
            NativeDeferredWindowPositionApi.Instance,
            new[]
            {
                new DeferredWindowPositionEntry(top.Hwnd, NativeMethods.HWND_TOP,
                    topRect.left, topRect.top, topRect.Width, topRect.Height, guestFlags),
                new DeferredWindowPositionEntry(bottom.Hwnd, top.Hwnd,
                    bottomRect.left, bottomRect.top, bottomRect.Width, bottomRect.Height, guestFlags),
                new DeferredWindowPositionEntry(containerHwnd, bottom.Hwnd,
                    0, 0, 0, 0, containerFlags),
            },
            beforeDefer: index => index switch
            {
                0 => IsCurrentMutationGeneration(top, "position-split-before-top-defer"),
                1 => IsCurrentMutationGeneration(bottom, "position-split-before-bottom-defer"),
                _ => NativeMethods.IsWindow(containerHwnd),
            });

        if (result != DeferredWindowPositionResult.Applied)
        {
            // A generation failure is deliberately not sent through the
            // fallback: End closed the valid HDWP and may have committed only
            // earlier, independently validated entries. Fallback would erase
            // the safety boundary by touching the known-stale guest again.
            if (result == DeferredWindowPositionResult.ValidationFailed)
            {
                LogPositioningFailureOnce(top.Hwnd, "DeferWindowPos(batch:validation)");
                return;
            }
            // A failed Defer is abandoned before EndDeferWindowPos per the
            // Win32 contract. The existing fallback remains generation-gated
            // and preserves recovery when the native API itself fails.
            if (result != DeferredWindowPositionResult.BeginFailed)
                LogPositioningFailureOnce(top.Hwnd, $"DeferWindowPos(batch:{result})");
            FallbackPosition(top, topRect, bottom, bottomRect, containerHwnd);
            return;
        }

        BudgetSink?.RecordDeferBatch();
        BudgetSink?.RecordPositionAndShow(top.Hwnd);
        BudgetSink?.RecordPositionAndShow(bottom.Hwnd);
        _log.Log($"SHEPHERD[position] guest=0x{top.Hwnd.ToInt64():X} rect={topRect.left},{topRect.top},{topRect.Width}x{topRect.Height}");
        _log.Log($"SHEPHERD[position] guest=0x{bottom.Hwnd.ToInt64():X} rect={bottomRect.left},{bottomRect.top},{bottomRect.Width}x{bottomRect.Height}");
    }

    /// <summary>
    /// Per-guest fallback for <see cref="PositionGuestsDeferred"/> when the
    /// deferred batch cannot be created or fails: same rects, same z-order
    /// semantics (top above bottom above container), just not atomic.
    /// </summary>
    private void FallbackPosition(CapturedWindow top, NativeMethods.RECT topRect, CapturedWindow bottom, NativeMethods.RECT bottomRect, IntPtr containerHwnd)
    {
        PositionGuest(top, topRect, NativeMethods.HWND_TOP);
        PositionGuest(bottom, bottomRect, top.Hwnd);
        PairZOrderBehind(containerHwnd, bottom);
    }

    /// <summary>
    /// Raises a TabDock container for a short-lived piece of TabDock-owned UI
    /// (for example a context menu or an owned capture dialog). Guests remain
    /// visible; this only changes which surface is on top while the UI is open.
    /// The caller must reconcile the guest stack when that UI closes.
    /// </summary>
    public void RaiseContainerForChrome(IntPtr containerHwnd, bool useTopmostBand = false)
    {
        if (!NativeMethods.IsWindow(containerHwnd))
            return;

        IntPtr insertAfter = useTopmostBand ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP;
        bool raised = NativeMethods.SetWindowPos(
            containerHwnd,
            insertAfter,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        DiagnosticRuntime.Record("repair.container-z-order", containerHwnd,
            action: useTopmostBand ? "raise-topmost-band" : "raise-normal-band",
            result: raised ? "success" : "failed");
        if (!raised)
        {
            LogPositioningFailureOnce(containerHwnd, "SetWindowPos(container-chrome)");
        }
    }

    /// <summary>
    /// Returns a container raised into the topmost band for an owned modal to
    /// the normal z-order band. The caller then performs the ordinary guest
    /// positioning pass, which puts the guest above the container again.
    /// </summary>
    public void RestoreContainerFromChrome(IntPtr containerHwnd)
    {
        if (!NativeMethods.IsWindow(containerHwnd))
            return;

        if (!NativeMethods.SetWindowPos(
            containerHwnd,
            NativeMethods.HWND_NOTOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
        {
            LogPositioningFailureOnce(containerHwnd, "SetWindowPos(container-not-topmost)");
        }
    }

    /// <summary>
    /// Pins <paramref name="containerHwnd"/> immediately behind the guest in
    /// z-order so nothing else can slot between them. This is the single
    /// implementation of the z-order pin — <see cref="PositionAndShow"/> uses it
    /// for its own glue, and the container's foreground-pairing path
    /// (ContainerWindow.PairZOrderBehindGuest) delegates here too instead of
    /// repeating the same native call with the same flags.
    /// </summary>
    public void PairZOrderBehind(IntPtr containerHwnd, CapturedWindow guest)
    {
        RuntimeTelemetry.Instance.RecordSetWindowPos();
        if (!IsCurrentCapturedWindow(guest, "z-order", verifyExecutable: false, verifyProcessInstance: false))
            return;

        PairZOrderBehindCore(containerHwnd, guest.Hwnd, guest);
    }

    private void PairZOrderBehindCore(IntPtr containerHwnd, IntPtr guestHwnd, CapturedWindow? capturedGuest = null)
    {
        if (!NativeMethods.IsWindow(containerHwnd) || !NativeMethods.IsWindow(guestHwnd))
            return;

        // Both the foreground and desktop-reorder WinEvent paths converge here.
        // A repair itself can generate another reorder event, so avoid issuing
        // a second native mutation once the local pairing invariant already
        // holds — the container sits BELOW the guest. The invariant check is
        // an upward walk (skipping invisible helper windows), not a strict
        // adjacency probe: a WS_EX_TOPMOST guest lives in a different z-order
        // band (taskbar etc. sit between it and the container, so "immediately
        // below" is unachievable even though the guest IS above the container),
        // and hidden IME helpers are inserted next to any touched window.
        // Both cases must not trigger a pin that can never succeed (and would
        // otherwise repeat on every relayout pass). This keeps the event-driven
        // repair bounded without weakening the local guest/container invariant.
        if (IsPairingSatisfied(containerHwnd, guestHwnd))
            return;

        if (capturedGuest != null
            && !IsCurrentMutationGeneration(capturedGuest, "z-order-before-pair"))
            return;

        bool ok = NativeMethods.SetWindowPos(
            containerHwnd,
            guestHwnd,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        DiagnosticRuntime.Record("repair.pair-z-order", containerHwnd, guestHwnd,
            action: "SetWindowPos(container-behind-guest)", result: ok ? "success" : "failed",
            data: new Dictionary<string, string>
            {
                ["observedBeforePairing"] = "container-not-below-guest",
                ["observedAfterPairing"] = IsContainerBelowGuest(containerHwnd, guestHwnd).ToString(),
            });
        if (ok) BudgetSink?.RecordPairZOrder();
        else LogPositioningFailureOnce(containerHwnd, "SetWindowPos(container)");
    }

    /// <summary>
    /// True when <paramref name="containerHwnd"/> sits BELOW
    /// <paramref name="guestHwnd"/> in the z-order — the local
    /// guest-above-container pairing invariant. Walks GW_HWNDPREV (upward) from
    /// the container, skipping invisible helper windows (IME etc.), until the
    /// guest is reached (healthy) or the walk ends (the container is above the
    /// guest and a pin is needed). Correct where a strict-adjacency probe is
    /// not: topmost guests live in a separate z-order band, so "immediately
    /// below" is impossible even though the guest IS above the container, and
    /// hidden intermediates must never trigger repairs.
    /// </summary>
    public bool IsContainerBelowGuest(IntPtr containerHwnd, IntPtr guestHwnd)
    {
        IntPtr cur = NativeMethods.GetWindow(containerHwnd, NativeMethods.GW_HWNDPREV);
        while (cur != IntPtr.Zero)
        {
            if (cur == guestHwnd)
                return true;
            cur = NativeMethods.GetWindow(cur, NativeMethods.GW_HWNDPREV);
        }
        return false;
    }

    /// <summary>
    /// Tests the stronger local pairing contract for ordinary windows: after
    /// invisible helper windows are ignored, the guest must be the first
    /// visible window above the container. A topmost guest is the exception:
    /// USER32 keeps it in a separate z-order band, so strict adjacency across
    /// the band is not representable even though the guest is correctly above
    /// the container.
    /// </summary>
    private bool IsPairingSatisfied(IntPtr containerHwnd, IntPtr guestHwnd)
    {
        if ((NativeMethods.GetWindowLongPtr(guestHwnd, NativeMethods.GWL_EXSTYLE).ToInt64()
                & NativeMethods.WS_EX_TOPMOST) != 0)
        {
            return IsContainerBelowGuest(containerHwnd, guestHwnd);
        }

        IntPtr previous = NativeMethods.GetWindow(containerHwnd, NativeMethods.GW_HWNDPREV);
        while (previous != IntPtr.Zero && !NativeMethods.IsWindowVisible(previous))
            previous = NativeMethods.GetWindow(previous, NativeMethods.GW_HWNDPREV);

        return previous == guestHwnd;
    }

    /// <summary>
    /// Hides an inactive shepherded guest. Safe to call on a window that is
    /// already hidden or has been destroyed. Journals the guest BEFORE hiding
    /// it — a force-kill landing between the two would otherwise leave the
    /// guest hidden on screen with no journal entry, exactly the orphan the
    /// journal exists to rescue. The reversed order is safe:
    /// <see cref="RescueOrphanedWindows"/> re-showing an already-visible
    /// window is a documented harmless no-op. See
    /// <see cref="RescueOrphanedWindows"/>.
    /// </summary>
    public WindowHideOutcome Hide(CapturedWindow window)
    {
        WindowIdentityResult identityResult = EvaluateCurrentCapturedWindow(
            window,
            "hide",
            verifyExecutable: true,
            verifyProcessInstance: true);
        if (identityResult == WindowIdentityResult.Mismatch)
        {
            bool journalCleared = JournalClear(window);
            _log.Log($"SHEPHERD[hide-decision] guest=0x{window.Hwnd.ToInt64():X} identity mismatch; journalCleared={journalCleared}.");
            return WindowHideOutcome.TargetGoneOrRecycled;
        }
        if (identityResult == WindowIdentityResult.Unverifiable)
        {
            _log.Log($"SHEPHERD[hide-pending] guest=0x{window.Hwnd.ToInt64():X}: identity could not be verified; guest remains represented and journaled.");
            return WindowHideOutcome.RecoveryPending;
        }
        if (!JournalHide(window))
        {
            // A hard termination bypasses every in-process shutdown handler.
            // Never create a newly-hidden guest unless its recovery record is
            // known to be durable.
            _log.Log($"SHEPHERD[hide-blocked] guest=0x{window.Hwnd.ToInt64():X}: hidden-window journal could not be committed; leaving guest visible.");
            return WindowHideOutcome.RecoveryPending;
        }
        // JournalHide is durable-before-dangerous-mutation, but its synchronous
        // disk write is still a meaningful HWND-reuse window. Revalidate the
        // cheap generation immediately before SW_HIDE.
        if (!IsCurrentMutationGeneration(window, "hide-after-journal"))
        {
            WindowIdentityResult boundary = EvaluateCurrentCapturedWindow(
                window,
                "hide-after-journal-outcome",
                verifyExecutable: false,
                verifyProcessInstance: false);
            if (boundary == WindowIdentityResult.Mismatch)
                JournalClear(window);
            return boundary == WindowIdentityResult.Mismatch
                ? WindowHideOutcome.TargetGoneOrRecycled
                : WindowHideOutcome.RecoveryPending;
        }

        bool previouslyVisible = _releaseApi.ShowWindow(window.Hwnd, NativeMethods.SW_HIDE);
        RuntimeTelemetry.Instance.RecordShowWindow();
        WindowIdentityResult postHideIdentity = EvaluateCurrentCapturedWindow(
            window,
            "hide-post-native",
            verifyExecutable: false,
            verifyProcessInstance: false);
        if (postHideIdentity != WindowIdentityResult.Match)
        {
            if (postHideIdentity == WindowIdentityResult.Mismatch)
                JournalClear(window);
            return postHideIdentity == WindowIdentityResult.Mismatch
                ? WindowHideOutcome.TargetGoneOrRecycled
                : WindowHideOutcome.RecoveryPending;
        }
        // ShowWindow's return reports prior visibility, not success — calling
        // Hide on an already-hidden window returns false benignly. Verify the
        // post-state instead: a window that is still visible after SW_HIDE is
        // a real (e.g. UIPI-blocked) failure.
        if (!ShowWindowSemantics.VisibilitySucceeded(
                previouslyVisible,
                visibleAfter: _releaseApi.IsWindowVisible(window.Hwnd),
                expectedVisible: false))
        {
            LogPositioningFailureOnce(window.Hwnd, "ShowWindow(SW_HIDE)");
            DiagnosticRuntime.Record("repair.visibility", guest: window.Hwnd, action: "ShowWindow(SW_HIDE)",
                result: "failed");
            return WindowHideOutcome.RecoveryPending;
        }
        DiagnosticRuntime.Record("repair.visibility", guest: window.Hwnd, action: "ShowWindow(SW_HIDE)",
            result: "success");
        BudgetSink?.RecordHide(window.Hwnd);
        _log.Log($"SHEPHERD[hide] guest=0x{window.Hwnd.ToInt64():X}");
        return WindowHideOutcome.Hidden;
    }

    /// <summary>
    /// Re-asserts the guest's overlay position/z-order and gives it real
    /// foreground activation. Called when the container itself becomes the
    /// foreground window (e.g. alt-tab back, click on caption) so the guest
    /// is both visually and input-wise "in front" again. No thread-input
    /// attachment is needed: TabDock's process is genuinely the foreground
    /// process at the moment this runs, so SetForegroundWindow is legal here.
    /// </summary>
    public void BringToFront(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect)
    {
        if (!IsCurrentCapturedWindow(window, "bring-to-front", verifyExecutable: true, verifyProcessInstance: true))
            return;

        PositionAndShowCore(window, containerHwnd, screenRect, verifyProcessInstance: true);
        if (NativeMethods.GetForegroundWindow() == window.Hwnd)
        {
            // Already foreground — most commonly the container received this
            // WM_ACTIVATE as a side effect of the user clicking directly into
            // one of the guest's own child controls (which legitimately
            // activates the guest first). Calling SetForegroundWindow again
            // here is not just redundant: it can interrupt that click's own
            // mouse-capture/click-tracking mid-gesture (observed: a WinForms
            // button's Click event silently failed to fire when this ran
            // between its mouse-down and mouse-up).
            return;
        }
        if (!IsCurrentMutationGeneration(window, "bring-to-front-before-foreground"))
            return;
        bool fg = NativeMethods.SetForegroundWindow(window.Hwnd);
        if (!fg && NativeMethods.GetForegroundWindow() != window.Hwnd)
        {
            // Windows' focus-stealing guard can still reject this even though
            // the container just legitimately activated (the WM_ACTIVATE that
            // triggers this call). A benign key-up is the standard,
            // documented way to (re-)grant this process foreground-change
            // rights before retrying once.
            SendBenignKeyNudge();
            if (!IsCurrentMutationGeneration(window, "bring-to-front-before-foreground-retry"))
                return;
            fg = NativeMethods.SetForegroundWindow(window.Hwnd);
        }
        DiagnosticRuntime.Record("repair.foreground", containerHwnd, window.Hwnd,
            foreground: NativeMethods.GetForegroundWindow(), action: "SetForegroundWindow",
            result: fg && NativeMethods.GetForegroundWindow() == window.Hwnd ? "success" : "refused-or-changed");
        _log.Log($"SHEPHERD[bring-to-front] guest=0x{window.Hwnd.ToInt64():X} fg={fg}");
    }

    /// <summary>
    /// Gives a guest real foreground activation WITHOUT repositioning it or
    /// re-pinning the container. Used by split mode after the container has
    /// already laid out both panes and pinned itself below both: only one
    /// member should be foreground, and re-running PositionAndShow here (as
    /// BringToFront does) would disturb the pair's established z-order. Mirrors
    /// BringToFront's SetForegroundWindow + benign-key-nudge retry.
    /// </summary>
    public void SetForeground(CapturedWindow window)
    {
        RuntimeTelemetry.Instance.RecordSetForeground();
        if (!IsCurrentCapturedWindow(window, "foreground", verifyExecutable: true, verifyProcessInstance: true))
            return;
        if (NativeMethods.GetForegroundWindow() == window.Hwnd)
            return;
        if (!IsCurrentMutationGeneration(window, "foreground-before-set"))
            return;
        bool fg = NativeMethods.SetForegroundWindow(window.Hwnd);
        if (!fg && NativeMethods.GetForegroundWindow() != window.Hwnd)
        {
            SendBenignKeyNudge();
            if (!IsCurrentMutationGeneration(window, "foreground-before-set-retry"))
                return;
            fg = NativeMethods.SetForegroundWindow(window.Hwnd);
        }
        DiagnosticRuntime.Record("repair.foreground", guest: window.Hwnd,
            foreground: NativeMethods.GetForegroundWindow(), action: "SetForegroundWindow",
            result: fg && NativeMethods.GetForegroundWindow() == window.Hwnd ? "success" : "refused-or-changed");
        if (fg) BudgetSink?.RecordSetForeground(window.Hwnd);
        _log.Log($"SHEPHERD[split-foreground] guest=0x{window.Hwnd.ToInt64():X} fg={fg}");
    }

    private static void SendBenignKeyNudge()
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)NativeMethods.VK_MENU,
                    dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                },
            },
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    /// <summary>
    /// Releases a shepherded guest back to its original placement. Because
    /// nothing about the guest's identity was mutated while docked (no style,
    /// no parent, no owner — only reversible placement, z-order, visibility,
    /// and DWM transition-suppression changes), this only needs to restore the
    /// placement snapshotted at capture and undo the transition suppression —
    /// there is no style/owner/parent surgery to undo. When
    /// <paramref name="show"/> is false the window is left hidden
    /// (guest-initiated hide / tray-style close) and journaled the same as
    /// <see cref="Hide"/>.
    /// </summary>
    public WindowReleaseOutcome Release(CapturedWindow window, bool show = true)
    {
        WindowIdentityResult identityResult = EvaluateCurrentCapturedWindow(
            window,
            "release",
            verifyExecutable: true,
            verifyProcessInstance: true);
        if (identityResult != WindowIdentityResult.Match)
        {
            _minTrackCache.Remove(window);
            if (identityResult == WindowIdentityResult.Mismatch)
            {
                // Positive stale/recycled evidence proves the old captured
                // object cannot be safely mutated. JournalClear is scoped to
                // the full old identity tuple, so it cannot erase a newer
                // same-HWND record.
                bool journalCleared = JournalClear(window);
                LogIdentityReleaseOutcome(window, identityResult, journalCleared
                    ? "positive mismatch; old journal evidence cleared"
                    : "positive mismatch; old journal evidence retained after clear failure");
                UnregisterCapturedIdentity(window);
                return WindowReleaseOutcome.TargetGoneOrRecycled;
            }

            LogIdentityReleaseOutcome(window, identityResult, "required evidence unavailable; native release and journal clear skipped");
            return WindowReleaseOutcome.RecoveryPending;
        }

        try
        {
            WindowReleaseOutcome outcome = show
                ? ReleaseVisible(window)
                : ReleaseIntentionalHide(window);
            if (outcome != WindowReleaseOutcome.RecoveryPending)
                UnregisterCapturedIdentity(window);
            return outcome;
        }
        catch (Exception ex)
        {
            _log.LogException($"SHEPHERD[release] 0x{window.Hwnd.ToInt64():X}; recovery remains pending", ex);
            return WindowReleaseOutcome.RecoveryPending;
        }
    }

    private bool TryReleaseMutationBoundary(
        CapturedWindow window,
        string operation,
        out WindowIdentityResult result)
    {
        TestSequence(operation + ".before");
        result = EvaluateCurrentCapturedWindow(
            window,
            operation,
            verifyExecutable: false,
            verifyProcessInstance: false);
        return result == WindowIdentityResult.Match;
    }

    private WindowReleaseOutcome ReleaseBoundaryFailure(
        CapturedWindow window,
        WindowIdentityResult result,
        string operation)
    {
        if (result == WindowIdentityResult.Mismatch)
        {
            bool journalCleared = JournalClear(window);
            LogIdentityReleaseOutcome(window, result, journalCleared
                ? $"{operation}: positive mismatch; old journal evidence cleared"
                : $"{operation}: positive mismatch; old journal evidence retained after clear failure");
            return WindowReleaseOutcome.TargetGoneOrRecycled;
        }

        LogIdentityReleaseOutcome(window, result, $"{operation}: required generation evidence unavailable");
        return WindowReleaseOutcome.RecoveryPending;
    }

    private WindowReleaseOutcome ReleaseIntentionalHide(CapturedWindow window)
    {
        // The marker is committed before the intentional hide. If it cannot
        // be committed, restore visible presentation and retain ownership.
        if (!JournalMarkIntentionalHide(window))
        {
            if (!TryReleaseMutationBoundary(window, "release-intentional-hide-marker-failure-before-show", out WindowIdentityResult markerShowIdentity))
                return ReleaseBoundaryFailure(window, markerShowIdentity, "marker-failure-show");
            bool visible = ShowWindowVerified(window, NativeMethods.SW_SHOW, expectedVisible: true,
                "ShowWindow(SW_SHOW) after journal-marker failure");
            if (!TryReleaseMutationBoundary(window, "release-intentional-hide-marker-failure-before-transitions", out WindowIdentityResult markerTransitionIdentity))
                return ReleaseBoundaryFailure(window, markerTransitionIdentity, "marker-failure-transitions");
            bool transitions = RestoreOriginalTransitions(window);
            _log.Log($"SHEPHERD[release-blocked] guest=0x{window.Hwnd.ToInt64():X}: marker commit failed; visible={visible}, transitions={transitions}.");
            _minTrackCache.Remove(window);
            return WindowReleaseOutcome.RecoveryPending;
        }

        if (!TryReleaseMutationBoundary(window, "release-intentional-hide-before-hide", out WindowIdentityResult hideIdentity))
            return ReleaseBoundaryFailure(window, hideIdentity, "intentional-hide");
        bool hidden = ShowWindowVerified(window, NativeMethods.SW_HIDE, expectedVisible: false, "ShowWindow(SW_HIDE)");
        if (hidden
            && !TryReleaseMutationBoundary(window, "release-intentional-hide-before-transitions", out WindowIdentityResult postHideIdentity))
        {
            return ReleaseBoundaryFailure(window, postHideIdentity, "intentional-hide-transitions");
        }
        bool transitionsRestored = hidden && RestoreOriginalTransitions(window);
        if (hidden && transitionsRestored)
        {
            if (!TryReleaseMutationBoundary(window, "release-intentional-hide-before-token-removal", out WindowIdentityResult tokenIdentity))
                return ReleaseBoundaryFailure(window, tokenIdentity, "intentional-hide-token");
            if (!RemoveCaptureIdentityToken(window))
            {
                WindowIdentityResult afterTokenFailure = EvaluateCurrentCapturedWindow(
                    window,
                    "release-intentional-hide-token-failure",
                    verifyExecutable: false,
                    verifyProcessInstance: false);
                return afterTokenFailure == WindowIdentityResult.Mismatch
                    ? ReleaseBoundaryFailure(window, afterTokenFailure, "intentional-hide-token-failure")
                    : WindowReleaseOutcome.RecoveryPending;
            }
            if (JournalClear(window))
            {
                _log.Log($"Shepherd-released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) hidden (guest-initiated hide)");
                _minTrackCache.Remove(window);
                return WindowReleaseOutcome.Released;
            }
        }

        // Never leave an ambiguous hidden guest after finalization failed.
        if (!TryReleaseMutationBoundary(window, "release-intentional-hide-finalization-before-show", out WindowIdentityResult finalizationShowIdentity))
            return ReleaseBoundaryFailure(window, finalizationShowIdentity, "intentional-hide-finalization-show");
        bool visibleAfterFailure = ShowWindowVerified(
            window,
            NativeMethods.SW_SHOW,
            expectedVisible: true,
            "ShowWindow(SW_SHOW) after journal finalization failure");
        if (!TryReleaseMutationBoundary(window, "release-intentional-hide-finalization-before-transitions", out WindowIdentityResult finalizationTransitionIdentity))
            return ReleaseBoundaryFailure(window, finalizationTransitionIdentity, "intentional-hide-finalization-transitions");
        bool transitionsAfterFailure = RestoreOriginalTransitions(window);
        _log.Log($"SHEPHERD[release-pending] guest=0x{window.Hwnd.ToInt64():X}: intentional-hide finalization failed; visible={visibleAfterFailure}, transitions={transitionsAfterFailure}; journal retained.");
        _minTrackCache.Remove(window);
        return WindowReleaseOutcome.RecoveryPending;
    }

    private WindowReleaseOutcome ReleaseVisible(CapturedWindow window)
    {
        bool placementRestored;
        if (!window.HasValidPlacement)
        {
            if (!TryReleaseMutationBoundary(window, "release-before-bounds", out WindowIdentityResult boundsIdentity))
                return ReleaseBoundaryFailure(window, boundsIdentity, "release-bounds");
            placementRestored = _releaseApi.SetWindowPos(
                window.Hwnd,
                IntPtr.Zero,
                window.OriginalBounds.left,
                window.OriginalBounds.top,
                window.OriginalBounds.Width,
                window.OriginalBounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            if (!placementRestored)
                LogPositioningFailureOnce(window.Hwnd, "SetWindowPos(release-bounds)");
        }
        else
        {
            NativeMethods.WINDOWPLACEMENT placement = window.OriginalPlacement;
            placement.length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();
            if (!TryReleaseMutationBoundary(window, "release-before-placement", out WindowIdentityResult placementIdentity))
                return ReleaseBoundaryFailure(window, placementIdentity, "release-placement");
            placementRestored = _releaseApi.SetWindowPlacement(window.Hwnd, ref placement);
            if (!placementRestored)
            {
                _log.Log($"SetWindowPlacement failed for 0x{window.Hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
                if (!TryReleaseMutationBoundary(window, "release-before-placement-fallback", out WindowIdentityResult fallbackIdentity))
                    return ReleaseBoundaryFailure(window, fallbackIdentity, "release-placement-fallback");
                placementRestored = _releaseApi.SetWindowPos(
                    window.Hwnd,
                    IntPtr.Zero,
                    window.OriginalBounds.left,
                    window.OriginalBounds.top,
                    window.OriginalBounds.Width,
                    window.OriginalBounds.Height,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                if (!placementRestored)
                    LogPositioningFailureOnce(window.Hwnd, "SetWindowPos(release-fallback)");
            }
        }

        int showCommand = window.OriginallyVisible
            ? (window.OriginalPlacement.showCmd == 0 ? NativeMethods.SW_SHOW : (int)window.OriginalPlacement.showCmd)
            : NativeMethods.SW_HIDE;
        if (!TryReleaseMutationBoundary(window, "release-before-visibility", out WindowIdentityResult visibilityIdentity))
            return ReleaseBoundaryFailure(window, visibilityIdentity, "release-visibility");
        bool visibilityRestored = ShowWindowVerified(
            window,
            showCommand,
            window.OriginallyVisible,
            "ShowWindow(release)");
        if (!visibilityRestored)
        {
            _minTrackCache.Remove(window);
            return WindowReleaseOutcome.RecoveryPending;
        }

        if (window.OriginallyVisible)
        {
            if (!TryReleaseMutationBoundary(window, "release-before-foreground", out WindowIdentityResult foregroundIdentity))
                return ReleaseBoundaryFailure(window, foregroundIdentity, "release-foreground");
            bool foregroundSet = _releaseApi.SetForegroundWindow(window.Hwnd);
            if (!foregroundSet && _releaseApi.GetForegroundWindow() != window.Hwnd)
                LogPositioningFailureOnce(window.Hwnd, "SetForegroundWindow(release)");
            if (!TryReleaseMutationBoundary(window, "release-after-foreground-before-transitions", out WindowIdentityResult postForegroundIdentity))
                return ReleaseBoundaryFailure(window, postForegroundIdentity, "release-transitions");
        }

        if (!TryReleaseMutationBoundary(window, "release-before-transitions", out WindowIdentityResult transitionsIdentity))
            return ReleaseBoundaryFailure(window, transitionsIdentity, "release-transitions");
        bool transitionsRestored = RestoreOriginalTransitions(window);
        bool releaseComplete = placementRestored && visibilityRestored && transitionsRestored;
        if (releaseComplete)
        {
            if (!TryReleaseMutationBoundary(window, "release-before-token-removal", out WindowIdentityResult tokenIdentity))
                return ReleaseBoundaryFailure(window, tokenIdentity, "release-token");
            if (!RemoveCaptureIdentityToken(window))
            {
                WindowIdentityResult afterTokenFailure = EvaluateCurrentCapturedWindow(
                    window,
                    "release-token-failure",
                    verifyExecutable: false,
                    verifyProcessInstance: false);
                return afterTokenFailure == WindowIdentityResult.Mismatch
                    ? ReleaseBoundaryFailure(window, afterTokenFailure, "release-token-failure")
                    : WindowReleaseOutcome.RecoveryPending;
            }
            if (JournalClear(window))
            {
                _log.Log($"Shepherd-released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) guest={_releaseApi.DescribeWindow(window.Hwnd)}");
                _minTrackCache.Remove(window);
                return WindowReleaseOutcome.Released;
            }
        }

        _log.Log($"SHEPHERD[release-pending] guest=0x{window.Hwnd.ToInt64():X}: retained recovery journal after incomplete placement/finalization release.");
        _minTrackCache.Remove(window);
        return WindowReleaseOutcome.RecoveryPending;
    }

    private bool ShowWindowVerified(CapturedWindow window, int command, bool expectedVisible, string operation)
    {
        bool previouslyVisible = _releaseApi.ShowWindow(window.Hwnd, command);
        bool visible = _releaseApi.IsWindowVisible(window.Hwnd);
        bool succeeded = ShowWindowSemantics.VisibilitySucceeded(previouslyVisible, visible, expectedVisible);
        if (!succeeded)
            LogPositioningFailureOnce(window.Hwnd, operation);
        return succeeded;
    }

    #region Crash-recovery journal (docs/internal/deep-audit-2026-07-17.md, section 6.5)

    /// <summary>
    /// Writes the complete capture-session entry before the first dangerous
    /// guest mutation. This synchronous commit is the hard-kill safety
    /// boundary: a terminating process cannot run an exit handler later.
    /// </summary>
    private bool JournalCapture(CapturedWindow window)
    {
        bool committed = UpsertJournalEntry(window, doNotRescue: false, "JournalCapture");
        if (committed)
            _durablyJournaledCaptureTokens.Add(window.WindowIdentityToken);
        return committed;
    }

    /// <summary>
    /// Ensures rescue intent is durable before a TabDock-driven hide.
    ///
    /// Capture already commits the complete capture-session recovery entry
    /// synchronously before any presentation mutation. For that overwhelmingly
    /// common case, rewriting the identical JSON with WriteThrough + Flush(true)
    /// on every tab switch is redundant and blocks the WPF input turn. Only
    /// captures that do not currently have a known-durable rescue entry pay the
    /// synchronous journal write here.
    /// </summary>
    private bool JournalHide(CapturedWindow window)
    {
        if (_durablyJournaledCaptureTokens.Contains(window.WindowIdentityToken))
        {
            TestSequence("JournalHide.already-durable");
            return true;
        }

        bool committed = UpsertJournalEntry(window, doNotRescue: false, "JournalHide");
        if (committed)
            _durablyJournaledCaptureTokens.Add(window.WindowIdentityToken);
        return committed;
    }

    private bool JournalMarkIntentionalHide(CapturedWindow window)
    {
        bool committed = UpsertJournalEntry(window, doNotRescue: true, "JournalIntentionalHide");
        if (committed)
            _durablyJournaledCaptureTokens.Remove(window.WindowIdentityToken);
        return committed;
    }

    private bool UpsertJournalEntry(CapturedWindow window, bool doNotRescue, string operation)
    {
        try
        {
            if (_journalLoadFailed || !EnsureJournalStorage())
                return false;

            HiddenWindowJournalFile file = GetJournalCache();
            HiddenWindowEntry entry = ToJournalEntry(window);
            entry.DoNotRescue = doNotRescue;
            file.Entries.RemoveAll(e => IsSameJournalIdentity(e, entry));
            file.Entries.Add(entry);
            file.Version = HiddenWindowJournalFile.CurrentVersion;
            SaveJournal(file);
            RuntimeTelemetry.Instance.RecordJournalCommit();
            TestSequence(operation + ".committed");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogException($"WindowShepherdService.{operation}", ex);
            return false;
        }
    }

    private static HiddenWindowEntry ToJournalEntry(CapturedWindow window)
    {
        NativeMethods.WINDOWPLACEMENT placement = window.OriginalPlacement;
        return new HiddenWindowEntry
        {
            Hwnd = window.Hwnd.ToInt64(),
            Pid = window.ProcessId,
            WindowThreadId = window.WindowThreadId,
            WindowIdentityToken = window.WindowIdentityToken,
            ExePath = window.ExePath,
            ClassName = window.OriginalClassName,
            ProcessStartTimeUtcTicks = window.ProcessStartTimeUtcTicks,
            OriginallyVisible = window.OriginallyVisible,
            HasOriginalPlacement = window.HasValidPlacement,
            OriginalPlacementFlags = placement.flags,
            OriginalShowCommand = unchecked((int)placement.showCmd),
            OriginalMinPositionX = placement.ptMinPosition.x,
            OriginalMinPositionY = placement.ptMinPosition.y,
            OriginalMaxPositionX = placement.ptMaxPosition.x,
            OriginalMaxPositionY = placement.ptMaxPosition.y,
            OriginalNormalLeft = placement.rcNormalPosition.left,
            OriginalNormalTop = placement.rcNormalPosition.top,
            OriginalNormalRight = placement.rcNormalPosition.right,
            OriginalNormalBottom = placement.rcNormalPosition.bottom,
            HasOriginalTransitionsState = window.HasOriginalTransitionsState,
            OriginalTransitionsDisabled = window.OriginalTransitionsDisabled,
        };
    }

    private static bool IsSameJournalIdentity(HiddenWindowEntry left, HiddenWindowEntry right)
        => left.Hwnd == right.Hwnd
            && left.Pid == right.Pid
            && left.WindowThreadId == right.WindowThreadId
            && left.ProcessStartTimeUtcTicks == right.ProcessStartTimeUtcTicks
            && left.WindowIdentityToken == right.WindowIdentityToken
            && string.Equals(left.ExePath, right.ExePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ClassName, right.ClassName, StringComparison.Ordinal);

    /// <summary>Clears a guest's capture-session journal entry synchronously.</summary>
    private bool JournalClear(CapturedWindow window)
    {
        List<HiddenWindowEntry> removed = new List<HiddenWindowEntry>();
        try
        {
            HiddenWindowJournalFile file = GetJournalCache();
            // The journal is empty in the overwhelmingly common case (nothing is
            // hidden while a single-tab group is dragged around), and this runs
            // from a normal release path. Bail before scanning so the common
            // no-entry case does not allocate or inspect any identity fields.
            if (file.Entries.Count == 0)
                return true;

            HiddenWindowEntry expected = ToJournalEntry(window);
            for (int i = file.Entries.Count - 1; i >= 0; i--)
            {
                HiddenWindowEntry entry = file.Entries[i];
                if (IsSameJournalIdentity(entry, expected))
                {
                    removed.Add(entry);
                    file.Entries.RemoveAt(i);
                }
            }
            if (removed.Count == 0)
                return true;

            SaveJournal(file);
            _durablyJournaledCaptureTokens.Remove(window.WindowIdentityToken);
            return true;
        }
        catch (Exception ex)
        {
            // An immediate clear is part of the visibility transition. Keep
            // the in-memory entry when its durable write fails so a later
            // retry can still repair the same journal rather than silently
            // forgetting what disk still contains.
            if (removed.Count > 0 && _journalCache != null)
                _journalCache.Entries.AddRange(removed);
            _log.LogException("WindowShepherdService.JournalClear", ex);
            return false;
        }
    }

    private HiddenWindowJournalFile GetJournalCache()
    {
        // Loaded once per process lifetime, on first mutation (not eagerly at
        // construction): RescueOrphanedWindows runs before any Hide/Clear call
        // and unconditionally deletes hidden-windows.json after consuming it, so
        // loading here first would just re-read entries that rescue already
        // consumed. All subsequent mutations act on this in-memory copy only.
        return _journalCache ??= LoadJournal();
    }

    /// <summary>
    /// Saves the in-memory journal to disk immediately. It is retained as an
    /// explicit exit/crash-path hook even though every mutation is already
    /// synchronous, so future changes cannot accidentally reintroduce a
    /// delayed safety write without updating the lifecycle contract.
    /// </summary>
    public void FlushJournal()
    {
        try
        {
            if (_journalCache != null)
                SaveJournal(_journalCache);
        }
        catch (Exception ex)
        {
            _log.LogException("WindowShepherdService.FlushJournal", ex);
        }
    }

    private HiddenWindowJournalFile LoadJournal()
    {
        HiddenWindowJournalFile file = LoadJournal(_journalPath, _log, out _journalLoadFailed, out _);
        if (!_journalLoadFailed
            && (file.Version < HiddenWindowJournalFile.CurrentVersion
                || file.Entries.Any(entry => !HasCurrentJournalIdentity(entry))))
        {
            _journalLoadFailed = true;
            _log.Log($"SHEPHERD[journal] legacy/incomplete recovery evidence remains at '{DiagnosticEnvironmentService.RedactPath(_journalPath)}'; refusing to rewrite it.");
        }
        return file;
    }

    private static HiddenWindowJournalFile LoadJournal(
        string path,
        LoggingService log,
        out bool failed,
        out byte[]? rawBytes)
    {
        failed = false;
        rawBytes = null;
        if (!File.Exists(path))
            return new HiddenWindowJournalFile();

        string json;
        try
        {
            rawBytes = File.ReadAllBytes(path);
            json = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(rawBytes);
            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json[1..];
        }
        catch (Exception ex)
        {
            failed = true;
            log.LogException("WindowShepherdService.LoadJournal read", ex);
            return new HiddenWindowJournalFile();
        }
        try
        {
            int sourceVersion = 1;
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        if (string.Equals(property.Name, "Version", StringComparison.OrdinalIgnoreCase)
                            && property.Value.TryGetInt32(out int parsedVersion))
                        {
                            sourceVersion = parsedVersion;
                            break;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // The source-generation deserialize below produces the
                // canonical corruption/quarantine path.
            }

            HiddenWindowJournalFile file = JsonSerializer.Deserialize(json, TabDockJsonContext.Default.HiddenWindowJournalFile)
                ?? new HiddenWindowJournalFile();
            // A syntactically valid journal with a null Entries array must not
            // wedge rescue into a permanent fail-and-retry loop.
            file.Entries ??= new List<HiddenWindowEntry>();
            file.Version = sourceVersion;
            if (sourceVersion > HiddenWindowJournalFile.CurrentVersion)
            {
                failed = true;
                log.Log($"SHEPHERD[journal] unsupported future journal version {sourceVersion}; preserving '{DiagnosticEnvironmentService.RedactPath(path)}'.");
                return file;
            }
            // Version 1 entries contain only HWND/PID/executable. Version 2
            // adds the full presentation/process-start record but predates the
            // thread/token generation proof. They remain explicitly legacy;
            // startup rescue classifies them as pending manual recovery rather
            // than pretending they are equivalent to v3.
            if (sourceVersion < HiddenWindowJournalFile.CurrentVersion)
            {
                foreach (HiddenWindowEntry entry in file.Entries)
                {
                    // v1 recorded only entries that were hidden by TabDock;
                    // their pre-capture state was necessarily visible.
                    entry.OriginallyVisible = true;
                    entry.OriginalShowCommand = NativeMethods.SW_SHOW;
                }
            }
            return file;
        }
        catch (Exception ex)
        {
            // Malformed JSON and schema/type-shape failures are both corrupt
            // journal evidence. Quarantine them before any later capture can
            // overwrite the only recovery record.
            try
            {
                string corruptPath = GetUniqueJournalCorruptPath(path);
                File.Move(path, corruptPath);
                log.Log($"Quarantined corrupt recovery journal to {DiagnosticEnvironmentService.RedactPath(corruptPath)}");
                return new HiddenWindowJournalFile();
            }
            catch (Exception quarantineEx)
            {
                failed = true;
                log.LogException("WindowShepherdService.LoadJournal quarantine", quarantineEx);
                log.LogException("WindowShepherdService.LoadJournal JSON", ex);
                return new HiddenWindowJournalFile();
            }
        }
    }

    private static string GetUniqueJournalCorruptPath(string path)
    {
        string basePath = $"{path}.corrupt.{DateTime.Now:yyyyMMddHHmmssfff}";
        if (!File.Exists(basePath))
            return basePath;

        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{basePath}.{i:D3}";
            if (!File.Exists(candidate))
                return candidate;
        }

        return $"{basePath}.{Guid.NewGuid():N}";
    }

    private static bool HasCurrentJournalIdentity(HiddenWindowEntry entry)
        => entry.WindowThreadId != 0
            && entry.WindowIdentityToken != 0
            && entry.ProcessStartTimeUtcTicks != 0
            && !string.IsNullOrWhiteSpace(entry.ExePath)
            && !string.IsNullOrWhiteSpace(entry.ClassName);

    private static string GetUniqueJournalPendingPath(string path)
    {
        string basePath = path + ".pending";
        if (!File.Exists(basePath))
            return basePath;

        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{basePath}.{i:D3}";
            if (!File.Exists(candidate))
                return candidate;
        }

        return $"{basePath}.{Guid.NewGuid():N}";
    }

    private static bool PreservePendingJournal(
        string path,
        byte[]? rawBytes,
        int version,
        LoggingService log,
        string reason)
    {
        if (rawBytes == null || rawBytes.Length == 0)
            return false;

        string pendingPath = GetUniqueJournalPendingPath(path);
        try
        {
            WriteDurableBytes(pendingPath, rawBytes);
            File.Delete(path);
            log.Log($"SHEPHERD[journal-pending] preserved recovery journal schema v{version} at '{DiagnosticEnvironmentService.RedactPath(pendingPath)}'; automatic rescue skipped ({reason}). Supervised manual recovery is required before retiring this evidence.");
            return true;
        }
        catch (Exception ex)
        {
            log.LogException("WindowShepherdService.PreservePendingJournal", ex);
            try
            {
                if (File.Exists(pendingPath))
                    File.Delete(pendingPath);
            }
            catch { }
            return false;
        }
    }

    private static WindowIdentityResult EvaluateRecoveryIdentity(
        HiddenWindowEntry entry,
        IntPtr hwnd,
        IRecoveryNativeApi api,
        out string reason)
    {
        try
        {
            if (!api.IsWindow(hwnd))
            {
                reason = "HWND no longer exists";
                return WindowIdentityResult.Mismatch;
            }

            uint currentPid = api.GetProcessId(hwnd);
            if (currentPid == 0)
            {
                reason = "live PID could not be read";
                return WindowIdentityResult.Unverifiable;
            }
            if (currentPid != entry.Pid)
            {
                reason = "PID differs";
                return WindowIdentityResult.Mismatch;
            }

            uint currentThread = api.GetWindowThreadId(hwnd);
            if (currentThread == 0)
            {
                reason = "GUI thread identity could not be read";
                return WindowIdentityResult.Unverifiable;
            }
            if (currentThread != entry.WindowThreadId)
            {
                reason = "GUI thread identity differs";
                return WindowIdentityResult.Mismatch;
            }

            string? currentExe = api.GetProcessImagePath(currentPid);
            if (string.IsNullOrWhiteSpace(currentExe))
            {
                reason = "executable identity could not be read";
                return WindowIdentityResult.Unverifiable;
            }
            if (!string.Equals(currentExe, entry.ExePath, StringComparison.OrdinalIgnoreCase))
            {
                reason = "executable identity differs";
                return WindowIdentityResult.Mismatch;
            }

            string? currentClass = api.GetClassName(hwnd);
            if (string.IsNullOrWhiteSpace(currentClass))
            {
                reason = "window class identity could not be read";
                return WindowIdentityResult.Unverifiable;
            }
            if (!string.Equals(currentClass, entry.ClassName, StringComparison.Ordinal))
            {
                reason = "window class differs";
                return WindowIdentityResult.Mismatch;
            }

            long currentStart = api.GetProcessStartTimeUtcTicks(currentPid);
            if (currentStart == 0)
            {
                reason = "process-start identity could not be read";
                return WindowIdentityResult.Unverifiable;
            }
            if (currentStart != entry.ProcessStartTimeUtcTicks)
            {
                reason = "process-start identity differs";
                return WindowIdentityResult.Mismatch;
            }

            if (api.GetCaptureIdentityToken(hwnd) != new IntPtr(entry.WindowIdentityToken))
            {
                reason = "HWND generation token differs";
                return WindowIdentityResult.Mismatch;
            }

            reason = "all recovery identity evidence matched";
            return WindowIdentityResult.Match;
        }
        catch (Exception ex)
        {
            reason = $"recovery identity probe threw {ex.GetType().Name}";
            return WindowIdentityResult.Unverifiable;
        }
    }

    /// <summary>
    /// Cheap post-strong-check rescue gate. It intentionally omits executable
    /// and process-start probes because the full recovery identity was already
    /// proven at transaction entry; its purpose is to detect HWND generation
    /// change immediately before the next native write.
    /// </summary>
    private static WindowIdentityResult EvaluateRecoveryGeneration(
        HiddenWindowEntry entry,
        IntPtr hwnd,
        IRecoveryNativeApi api,
        out string reason)
    {
        try
        {
            if (!api.IsWindow(hwnd))
            {
                reason = "HWND no longer exists";
                return WindowIdentityResult.Mismatch;
            }

            uint currentPid = api.GetProcessId(hwnd);
            if (currentPid == 0)
            {
                reason = "live PID could not be read";
                return WindowIdentityResult.Unverifiable;
            }
            if (currentPid != entry.Pid)
            {
                reason = "PID differs";
                return WindowIdentityResult.Mismatch;
            }

            uint currentThread = api.GetWindowThreadId(hwnd);
            if (currentThread == 0)
            {
                reason = "GUI thread identity could not be read";
                return WindowIdentityResult.Unverifiable;
            }
            if (currentThread != entry.WindowThreadId)
            {
                reason = "GUI thread identity differs";
                return WindowIdentityResult.Mismatch;
            }

            string? currentClass = api.GetClassName(hwnd);
            if (string.IsNullOrWhiteSpace(currentClass))
            {
                reason = "window class identity could not be read";
                return WindowIdentityResult.Unverifiable;
            }
            if (!string.Equals(currentClass, entry.ClassName, StringComparison.Ordinal))
            {
                reason = "window class differs";
                return WindowIdentityResult.Mismatch;
            }

            if (api.GetCaptureIdentityToken(hwnd) != new IntPtr(entry.WindowIdentityToken))
            {
                reason = "HWND generation token differs";
                return WindowIdentityResult.Mismatch;
            }

            reason = "cheap recovery generation matched";
            return WindowIdentityResult.Match;
        }
        catch (Exception ex)
        {
            reason = $"recovery generation probe threw {ex.GetType().Name}";
            return WindowIdentityResult.Unverifiable;
        }
    }

    private void SaveJournal(HiddenWindowJournalFile file)
    {
        if (_journalLoadFailed)
            throw new IOException("Recovery journal was unreadable or an unsupported future version; refusing to overwrite it.");
        file.Version = HiddenWindowJournalFile.CurrentVersion;
        SaveJournal(_journalPath, file);
    }

    private static void SaveJournal(string path, HiddenWindowJournalFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        file.Version = HiddenWindowJournalFile.CurrentVersion;
        string json = JsonSerializer.Serialize(file, TabDockJsonContext.Default.HiddenWindowJournalFile);
        string tempPath = path + ".tmp";
        WriteDurableText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void WriteDurableText(string path, string contents)
    {
        WriteDurableBytes(path, Encoding.UTF8.GetBytes(contents));
    }

    private static void WriteDurableBytes(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Called once at startup, before any groups are opened. A force-killed
    /// TabDock never reaches its normal exit/emergency-release path, so a
    /// guest that was hidden (an inactive tab) at the moment of the kill has
    /// no way to reappear on its own — unlike the old Reparent backend, the
    /// guest process itself survives (it was never reparented), it's just
    /// invisible. Restore anything the journal remembers, cross-checked
    /// against the window's current owning PID and exe path so a recycled
    /// HWND value pointing at an unrelated window is never touched. This is a
    /// same-session recovery aid only (HWNDs don't survive reboots, matching
    /// the existing "layout intent only" persistence philosophy) — the
    /// identity-valid entry is cleared only after the guest is verified visible;
    /// entries that could not be shown or verified remain for a later retry.
    /// Positive stale/recycled identities are discarded because their old
    /// object is conclusively gone. Tokenless legacy entries are preserved in
    /// a `.pending` sidecar for supervised manual recovery and are never
    /// auto-mutated.
    /// </summary>
    public static void RescueOrphanedWindows(LoggingService log, string? journalPath = null)
        => RescueOrphanedWindows(log, journalPath, NativeRecoveryNativeApi.Instance);

    internal static void RescueOrphanedWindows(LoggingService log, string? journalPath, IRecoveryNativeApi api)
    {
        string path = string.IsNullOrWhiteSpace(journalPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabDock", "hidden-windows.json")
            : Path.GetFullPath(journalPath);
        try
        {
            HiddenWindowJournalFile file = LoadJournal(path, log, out bool loadFailed, out byte[]? rawBytes);
            if (loadFailed)
                return;
            if (file.Version < HiddenWindowJournalFile.CurrentVersion)
            {
                if (!PreservePendingJournal(
                        path,
                        rawBytes,
                        file.Version,
                        log,
                        "legacy tokenless HWND generation cannot be proven"))
                {
                    log.Log("SHEPHERD[journal-pending] could not quarantine legacy evidence; leaving the original journal untouched.");
                }
                return;
            }
            if (file.Entries.Any(entry => !HasCurrentJournalIdentity(entry)))
            {
                if (!PreservePendingJournal(
                        path,
                        rawBytes,
                        file.Version,
                        log,
                        "v3 entry is missing required identity evidence"))
                {
                    log.Log("SHEPHERD[journal-pending] could not quarantine incomplete v3 evidence; leaving the original journal untouched.");
                }
                return;
            }
            if (file.Entries.Count == 0)
            {
                // An empty journal file (including one with a null Entries array
                // that LoadJournal normalized) must not be left behind to be
                // re-read on every launch.
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            int rescued = 0;
            var retry = new List<HiddenWindowEntry>();
            foreach (HiddenWindowEntry entry in file.Entries)
            {
                var hwnd = new IntPtr(entry.Hwnd);
                WindowIdentityResult identity = EvaluateRecoveryIdentity(entry, hwnd, api, out string identityReason);
                if (identity == WindowIdentityResult.Mismatch)
                {
                    log.Log($"SHEPHERD[rescue] discarded conclusively stale/recycled entry 0x{hwnd.ToInt64():X}: {identityReason}.");
                    continue;
                }
                if (identity == WindowIdentityResult.Unverifiable)
                {
                    retry.Add(entry);
                    log.Log($"SHEPHERD[rescue-retry] retained 0x{hwnd.ToInt64():X}: identity unverifiable ({identityReason}).");
                    continue;
                }

                if (entry.DoNotRescue)
                {
                    RecoveryMutationOutcome outcome = RestoreJournaledIntentionalHide(hwnd, entry, log, api);
                    if (outcome == RecoveryMutationOutcome.Restored)
                    {
                        rescued++;
                        log.Log($"SHEPHERD[rescue] consumed intentional-hide marker for 0x{hwnd.ToInt64():X} without showing the guest.");
                    }
                    else if (outcome == RecoveryMutationOutcome.RecoveryPending)
                    {
                        retry.Add(entry);
                        log.Log($"SHEPHERD[rescue-retry] could not restore DWM state for intentional-hide marker 0x{hwnd.ToInt64():X}; retaining journal entry.");
                    }
                }
                else
                {
                    RecoveryMutationOutcome outcome = RestoreJournaledPresentation(hwnd, entry, log, api);
                    if (outcome == RecoveryMutationOutcome.Restored)
                    {
                        rescued++;
                        log.Log($"SHEPHERD[rescue] restored guest 0x{hwnd.ToInt64():X} (pid={entry.Pid}, exe={DiagnosticEnvironmentService.RedactPath(entry.ExePath)}) after an unclean previous shutdown.");
                    }
                    else if (outcome == RecoveryMutationOutcome.RecoveryPending)
                    {
                        retry.Add(entry);
                        log.Log($"SHEPHERD[rescue-retry] could not restore guest 0x{hwnd.ToInt64():X}; retaining journal entry.");
                    }
                }
            }

            if (retry.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            else
            {
                // Keep the existing file intact if this rewrite fails; the
                // next startup can retry from the complete original journal.
                SaveJournal(path, new HiddenWindowJournalFile { Entries = retry });
            }
            if (rescued > 0)
                log.Log($"SHEPHERD[rescue] {rescued} previously-hidden window(s) restored.");
        }
        catch (Exception ex)
        {
            log.LogException("WindowShepherdService.RescueOrphanedWindows", ex);
        }
    }

    private static RecoveryMutationOutcome EvaluateRecoveryBoundary(
        IntPtr hwnd,
        HiddenWindowEntry entry,
        IRecoveryNativeApi api,
        LoggingService log,
        string operation)
    {
        WindowIdentityResult result = EvaluateRecoveryGeneration(entry, hwnd, api, out string reason);
        if (result != WindowIdentityResult.Match)
            log.Log($"SHEPHERD[rescue-boundary] {operation} refused for 0x{hwnd.ToInt64():X}: result={result} reason={reason}.");
        return result switch
        {
            WindowIdentityResult.Match => RecoveryMutationOutcome.Restored,
            WindowIdentityResult.Mismatch => RecoveryMutationOutcome.TargetGoneOrRecycled,
            _ => RecoveryMutationOutcome.RecoveryPending,
        };
    }

    private static RecoveryMutationOutcome RestoreJournaledIntentionalHide(
        IntPtr hwnd,
        HiddenWindowEntry entry,
        LoggingService log,
        IRecoveryNativeApi api)
    {
        RecoveryMutationOutcome boundary = EvaluateRecoveryBoundary(hwnd, entry, api, log, "intentional-hide-before-transitions");
        if (boundary != RecoveryMutationOutcome.Restored)
            return boundary;
        if (!RestoreJournaledTransitions(hwnd, entry, log, api))
            return RecoveryMutationOutcome.RecoveryPending;

        boundary = EvaluateRecoveryBoundary(hwnd, entry, api, log, "intentional-hide-before-token-removal");
        if (boundary != RecoveryMutationOutcome.Restored)
            return boundary;
        if (ClearJournaledIdentityToken(hwnd, entry, api))
            return RecoveryMutationOutcome.Restored;

        return EvaluateRecoveryBoundary(hwnd, entry, api, log, "intentional-hide-token-removal-failure")
            == RecoveryMutationOutcome.TargetGoneOrRecycled
            ? RecoveryMutationOutcome.TargetGoneOrRecycled
            : RecoveryMutationOutcome.RecoveryPending;
    }

    private static RecoveryMutationOutcome RestoreJournaledPresentation(
        IntPtr hwnd,
        HiddenWindowEntry entry,
        LoggingService log,
        IRecoveryNativeApi api)
    {
        bool placementOk = true;
        if (entry.HasOriginalPlacement)
        {
            var placement = new NativeMethods.WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
                flags = entry.OriginalPlacementFlags,
                showCmd = unchecked((uint)entry.OriginalShowCommand),
                ptMinPosition = new NativeMethods.POINT { x = entry.OriginalMinPositionX, y = entry.OriginalMinPositionY },
                ptMaxPosition = new NativeMethods.POINT { x = entry.OriginalMaxPositionX, y = entry.OriginalMaxPositionY },
                rcNormalPosition = new NativeMethods.RECT
                {
                    left = entry.OriginalNormalLeft,
                    top = entry.OriginalNormalTop,
                    right = entry.OriginalNormalRight,
                    bottom = entry.OriginalNormalBottom,
                },
            };
            RecoveryMutationOutcome boundary = EvaluateRecoveryBoundary(hwnd, entry, api, log, "presentation-before-placement");
            if (boundary != RecoveryMutationOutcome.Restored)
                return boundary;
            placementOk = api.SetWindowPlacement(hwnd, ref placement);
        }
        else if (entry.OriginalNormalRight > entry.OriginalNormalLeft
            && entry.OriginalNormalBottom > entry.OriginalNormalTop)
        {
            RecoveryMutationOutcome boundary = EvaluateRecoveryBoundary(hwnd, entry, api, log, "presentation-before-bounds");
            if (boundary != RecoveryMutationOutcome.Restored)
                return boundary;
            placementOk = api.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                entry.OriginalNormalLeft,
                entry.OriginalNormalTop,
                entry.OriginalNormalRight - entry.OriginalNormalLeft,
                entry.OriginalNormalBottom - entry.OriginalNormalTop,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        if (!placementOk)
        {
            log.Log($"SHEPHERD[rescue] placement restore failed for 0x{hwnd.ToInt64():X}: native restore failed");
            return RecoveryMutationOutcome.RecoveryPending;
        }

        int showCommand = entry.OriginallyVisible
            ? (entry.OriginalShowCommand == 0 ? NativeMethods.SW_SHOW : entry.OriginalShowCommand)
            : NativeMethods.SW_HIDE;
        RecoveryMutationOutcome beforeShow = EvaluateRecoveryBoundary(hwnd, entry, api, log, "presentation-before-visibility");
        if (beforeShow != RecoveryMutationOutcome.Restored)
            return beforeShow;
        api.ShowWindow(hwnd, showCommand);
        RecoveryMutationOutcome afterShow = EvaluateRecoveryBoundary(hwnd, entry, api, log, "presentation-after-visibility");
        if (afterShow != RecoveryMutationOutcome.Restored)
            return afterShow;
        bool visibilityOk = api.IsWindowVisible(hwnd) == entry.OriginallyVisible;
        if (!visibilityOk)
        {
            log.Log($"SHEPHERD[rescue] visibility restore failed for 0x{hwnd.ToInt64():X}: resulting visibility did not match the journal.");
            return RecoveryMutationOutcome.RecoveryPending;
        }

        RecoveryMutationOutcome beforeTransitions = EvaluateRecoveryBoundary(hwnd, entry, api, log, "presentation-before-transitions");
        if (beforeTransitions != RecoveryMutationOutcome.Restored)
            return beforeTransitions;
        if (!RestoreJournaledTransitions(hwnd, entry, log, api))
            return RecoveryMutationOutcome.RecoveryPending;

        RecoveryMutationOutcome beforeToken = EvaluateRecoveryBoundary(hwnd, entry, api, log, "presentation-before-token-removal");
        if (beforeToken != RecoveryMutationOutcome.Restored)
            return beforeToken;
        if (ClearJournaledIdentityToken(hwnd, entry, api))
            return RecoveryMutationOutcome.Restored;

        return EvaluateRecoveryBoundary(hwnd, entry, api, log, "presentation-token-removal-failure")
            == RecoveryMutationOutcome.TargetGoneOrRecycled
            ? RecoveryMutationOutcome.TargetGoneOrRecycled
            : RecoveryMutationOutcome.RecoveryPending;
    }

    private static bool ClearJournaledIdentityToken(
        IntPtr hwnd,
        HiddenWindowEntry entry,
        IRecoveryNativeApi api)
        => entry.WindowIdentityToken != 0
            && api.RemoveCaptureIdentityToken(hwnd, new IntPtr(entry.WindowIdentityToken));

    private static bool RestoreJournaledTransitions(IntPtr hwnd, HiddenWindowEntry entry, LoggingService log, IRecoveryNativeApi api)
    {
        int transitionValue = entry.HasOriginalTransitionsState && entry.OriginalTransitionsDisabled ? 1 : 0;
        int transitionHr = api.SetTransitionsDisabled(hwnd, transitionValue);
        if (transitionHr != 0)
        {
            log.Log($"SHEPHERD[rescue] DWM transition restore returned HRESULT 0x{transitionHr:X8} for 0x{hwnd.ToInt64():X}.");
            return false;
        }
        return true;
    }

    #endregion
}

/// <summary>
/// Native seam for crash-rescue qualification. Production uses the adapter
/// below; the deterministic self-test injects identity reuse and one-entry
/// restore failures without touching arbitrary desktop windows.
/// </summary>
internal interface IRecoveryNativeApi
{
    bool IsWindow(IntPtr hwnd);
    uint GetProcessId(IntPtr hwnd);
    uint GetWindowThreadId(IntPtr hwnd);
    string? GetProcessImagePath(uint pid);
    string? GetClassName(IntPtr hwnd);
    long GetProcessStartTimeUtcTicks(uint pid);
    IntPtr GetCaptureIdentityToken(IntPtr hwnd);
    bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken);
    bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement);
    bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    bool ShowWindow(IntPtr hwnd, int command);
    bool IsWindowVisible(IntPtr hwnd);
    int SetTransitionsDisabled(IntPtr hwnd, int value);
}

internal sealed class NativeRecoveryNativeApi : IRecoveryNativeApi
{
    public static NativeRecoveryNativeApi Instance { get; } = new();

    private NativeRecoveryNativeApi() { }

    public bool IsWindow(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);

    public uint GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    public uint GetWindowThreadId(IntPtr hwnd)
        => NativeMethods.GetWindowThreadProcessId(hwnd, out _);

    public string? GetProcessImagePath(uint pid) => NativeMethods.GetProcessImagePath(pid);

    public string? GetClassName(IntPtr hwnd) => NativeMethods.GetClassNameString(hwnd);

    public long GetProcessStartTimeUtcTicks(uint pid)
        => NativeMethods.GetProcessStartTimeUtcTicks(pid);

    public IntPtr GetCaptureIdentityToken(IntPtr hwnd)
        => NativeMethods.GetProp(hwnd, NativeWindowIdentityApi.CaptureIdentityPropertyName);

    public bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken)
    {
        if (expectedToken == IntPtr.Zero
            || GetCaptureIdentityToken(hwnd) != expectedToken)
            return false;
        return NativeMethods.RemoveProp(hwnd, NativeWindowIdentityApi.CaptureIdentityPropertyName) == expectedToken;
    }

    public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement)
        => NativeMethods.SetWindowPlacement(hwnd, ref placement);

    public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
        => NativeMethods.SetWindowPos(hwnd, insertAfter, x, y, width, height, flags);

    public bool ShowWindow(IntPtr hwnd, int command) => NativeMethods.ShowWindow(hwnd, command);

    public bool IsWindowVisible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);

    public int SetTransitionsDisabled(IntPtr hwnd, int value)
        => NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int));
}
