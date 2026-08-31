using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace TabDock.ValidationDriver;

/// <summary>
/// Real user input, exclusively via SetCursorPos + SendInput. Nothing in this class
/// posts synthetic messages to specific windows — everything goes through the OS input
/// queue exactly as a human's mouse/keyboard would.
/// </summary>
internal static class Input
{
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_UP = 0x26;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_DELETE = 0x2E;
    public const ushort VK_A = 0x41;
    public const ushort VK_D = 0x44;
    public const ushort VK_G = 0x47;
    public const ushort VK_L = 0x4C;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_PRIOR = 0x21;
    public const ushort VK_NEXT = 0x22;
    public const ushort VK_F11 = 0x7A;

    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const int WHEEL_DELTA = 120;

    // The driver injects real input at screen coordinates, so the coordinate
    // itself is not an identity. Keep a per-run allow-list of the windows the
    // driver discovered/spawned and verify the live root window immediately
    // before every click/scroll and foreground-dependent key event. This
    // makes a stale/recycled HWND fail closed instead of sending input to the
    // user's foreground application.
    private static WindowIdentity? _activeTarget;
    private static int _lastX;
    private static int _lastY;
    private static bool _hasLastPoint;
    private static bool _leftButtonHeld;
    private static DesktopQualificationLease? _desktopLease;

    public static void SetDesktopLease(DesktopQualificationLease? lease)
        => _desktopLease = lease;

    public static void ResetIdentityScope(string? scenario = null)
    {
        TestRunProvenance.BeginScenario(scenario ?? TestRunProvenance.CurrentScenario);
        _activeTarget = null;
        _hasLastPoint = false;
        _leftButtonHeld = false;
    }

    public static void RegisterIdentity(WindowIdentity identity, string role = "DiscoveredWindow")
    {
        if (!TestRunProvenance.TryRegisterWindow(identity, role, out string reason))
            throw new InvalidOperationException($"Refusing to register HWND 0x{identity.Hwnd.ToInt64():X}: {reason}.");
        _desktopLease?.RegisterTarget(identity, role);
    }

    public static void RegisterDiscoveredWindow(IntPtr hwnd, string role)
    {
        if (!Discover.TryCaptureIdentity(hwnd, out WindowIdentity identity))
            throw new InvalidOperationException($"Refusing to register HWND 0x{hwnd.ToInt64():X}: identity unavailable.");
        RegisterIdentity(identity, role);
    }

    /// <summary>Real mouse-wheel scroll at (x,y). Positive notches scroll up, negative scroll down.</summary>
    public static void ScrollWheel(int x, int y, int notches)
    {
        MoveTo(x, y);
        Thread.Sleep(30);
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
        input.u.mi = new NativeMethods.MOUSEINPUT
        {
            dwFlags = MOUSEEVENTF_WHEEL,
            mouseData = unchecked((uint)(notches * WHEEL_DELTA)),
        };
        Send(input, verifyPoint: true, allowUnverifiedCleanup: false);
        Thread.Sleep(120);
    }

    /// <summary>
    /// Qualifies a target for real input. SetForegroundWindow is only an
    /// arrangement attempt; if it does not establish foreground, the shared
    /// primitive may perform one click on the target's separately proven frame
    /// activation point. It never uses AttachThreadInput or a TOPMOST pulse.
    /// Callers must treat false as "do NOT click".
    /// </summary>
    public static bool ForceForeground(IntPtr hwnd)
    {
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;

        if (!Discover.TryCaptureIdentity(root, out WindowIdentity target))
        {
            GuardedProc.Log($"WARNING: refusing foreground qualification for HWND 0x{root.ToInt64():X}: identity-unavailable.");
            IdentityDiagnostics.RecordPointFailure(_hasLastPoint ? _lastX : 0, _hasLastPoint ? _lastY : 0, root, "identity-unavailable");
            return false;
        }

        var qualification = new ForegroundQualification(new NativeForegroundQualificationRuntime());
        ForegroundQualificationResult result = qualification.Qualify(target);
        GuardedProc.Log(result.IsValid
            ? $"  Foreground qualification succeeded for 0x{target.Hwnd.ToInt64():X}: {result.Kind} ({result.Reason})."
            : $"WARNING: foreground qualification refused for 0x{target.Hwnd.ToInt64():X}: {result.Reason}.");
        if (result.IsValid)
            _activeTarget = target;
        return result.IsValid;
    }

    /// <summary>ForceForeground on the top-level root of (possibly child) <paramref name="hwnd"/>.</summary>
    public static bool ForceForegroundRoot(IntPtr hwnd)
    {
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        return ForceForeground(root == IntPtr.Zero ? hwnd : root);
    }

    private static bool IsScoped(WindowIdentity current, out string reason)
    {
        bool result = TestRunProvenance.TryValidateWindow(current, out reason);
        return result;
    }

    private sealed class NativeForegroundQualificationRuntime : IForegroundQualificationRuntime
    {
        public bool LeaseIsActive => _desktopLease != null && _desktopLease.IsValid;

        public bool IsTargetCurrent(WindowIdentity expected)
            => MatchesStableIdentity(expected.Hwnd, expected);

        public bool TryArrangeForeground(WindowIdentity expected)
        {
            if (!MatchesStableIdentity(expected.Hwnd, expected))
                return false;

            _desktopLease?.RecordInteraction(
                "foreground-arrangement-attempt",
                TestRunProvenance.WindowRole(expected.Hwnd),
                expected.Hwnd,
                new Dictionary<string, string>
                {
                    ["method"] = "SetForegroundWindow;SetWindowPos(HWND_TOP)",
                });
            bool arranged = NativeMethods.SetForegroundWindow(expected.Hwnd);
            Thread.Sleep(150);
            bool raised = false;
            if (!IsTargetForeground(expected))
            {
                // Raise only the known test-owned window in ordinary,
                // non-activating z-order. This is not TOPMOST and does not
                // touch, minimize, or otherwise manipulate a foreign window.
                raised = NativeMethods.SetWindowPos(
                    expected.Hwnd,
                    NativeMethods.HWND_TOP,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE
                        | NativeMethods.SWP_NOSIZE
                        | NativeMethods.SWP_NOACTIVATE);
                GuardedProc.Log($"  Foreground qualification ordinary z-order raise returned={raised} error={Marshal.GetLastWin32Error()} foreground=0x{NativeMethods.GetForegroundWindow().ToInt64():X}.");
                if (!IsTargetForeground(expected)
                    && !TryGetSafeActivationPoint(expected, out _))
                {
                    bool movedToExposedMonitor = TryMoveToExposedMonitor(expected);
                    raised |= movedToExposedMonitor;
                }
            }
            _desktopLease?.RecordInteraction(
                "foreground-arrangement-result",
                TestRunProvenance.WindowRole(expected.Hwnd),
                expected.Hwnd,
                new Dictionary<string, string>
                {
                    ["returned"] = arranged.ToString(),
                    ["raised"] = raised.ToString(),
                    ["foreground"] = IsTargetForeground(expected).ToString(),
                });
            return arranged || raised;
        }
        private bool TryMoveToExposedMonitor(WindowIdentity expected)
        {
            if (!NativeMethods.GetWindowRect(expected.Hwnd, out NativeMethods.RECT current))
                return false;

            int width = Math.Max(100, current.Width);
            int height = Math.Max(100, current.Height);
            bool exposed = false;
            try
            {
                NativeMethods.EnumDisplayMonitors(
                    IntPtr.Zero,
                    IntPtr.Zero,
                    (IntPtr monitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
                    {
                        if (exposed)
                            return false;

                        var info = new NativeMethods.MONITORINFO
                        {
                            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>(),
                        };
                        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                            return true;

                        int monitorWidth = Math.Max(100, info.rcWork.Width - 80);
                        int monitorHeight = Math.Max(100, info.rcWork.Height - 80);
                        int targetWidth = Math.Min(width, monitorWidth);
                        int targetHeight = Math.Min(height, monitorHeight);
                        int x = info.rcWork.left + 40;
                        int y = info.rcWork.top + 40;
                        bool moved = NativeMethods.SetWindowPos(
                            expected.Hwnd,
                            NativeMethods.HWND_TOP,
                            x,
                            y,
                            targetWidth,
                            targetHeight,
                            NativeMethods.SWP_NOACTIVATE);
                        GuardedProc.Log($"  Foreground qualification monitor arrangement monitor={monitor} moved={moved} rect={x},{y},{targetWidth},{targetHeight}.");
                        if (!moved)
                            return true;

                        Thread.Sleep(100);
                        exposed = TryGetSafeActivationPoint(expected, out _);
                        return !exposed;
                    },
                    IntPtr.Zero);
            }
            catch (Exception ex)
            {
                GuardedProc.Log($"  Foreground qualification monitor arrangement failed: {ex.GetType().Name}.");
                return false;
            }

            return exposed;
        }

        public bool IsTargetForeground(WindowIdentity expected)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            IntPtr root = foreground == IntPtr.Zero
                ? IntPtr.Zero
                : NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
            if (root == IntPtr.Zero)
                root = foreground;
            return root == expected.Hwnd && MatchesStableIdentity(expected.Hwnd, expected);
        }

        public bool TryGetSafeActivationPoint(
            WindowIdentity expected,
            out ForegroundActivationPoint point)
        {
            point = default;
            if (!MatchesStableIdentity(expected.Hwnd, expected)
                || !NativeMethods.IsWindowVisible(expected.Hwnd)
                || !NativeMethods.IsWindowEnabled(expected.Hwnd)
                || !NativeMethods.GetWindowRect(expected.Hwnd, out NativeMethods.RECT rect)
                || rect.Width < 4
                || rect.Height < 4)
            {
                return false;
            }

            uint style = unchecked((uint)NativeMethods.GetWindowLongPtr(
                expected.Hwnd,
                NativeMethods.GWL_STYLE).ToInt64());
            bool hasFrame = (style & (NativeMethods.WS_CAPTION
                | NativeMethods.WS_THICKFRAME
                | NativeMethods.WS_BORDER)) != 0;
            if (!hasFrame)
            {
                GuardedProc.Log($"  Foreground qualification: target 0x{expected.Hwnd.ToInt64():X} has no native frame style 0x{style:X}; no safe activation point.");
                return false;
            }
            // Probe only fixed points on the native frame. A covered top edge
            // does not justify clicking through it; another frame edge may be
            // exposed and is equally non-actionable.
            var candidates = new[]
            {
                new NativeMethods.POINT { x = rect.left + rect.Width / 2, y = rect.top + 1 },
                new NativeMethods.POINT { x = rect.left + rect.Width / 2, y = rect.bottom - 2 },
                new NativeMethods.POINT { x = rect.left + 1, y = rect.top + rect.Height / 2 },
                new NativeMethods.POINT { x = rect.right - 2, y = rect.top + rect.Height / 2 },
            };
            foreach (NativeMethods.POINT candidate in candidates)
            {
                IntPtr atPoint = NativeMethods.WindowFromPoint(candidate);
                IntPtr root = atPoint == IntPtr.Zero
                    ? IntPtr.Zero
                    : NativeMethods.GetAncestor(atPoint, NativeMethods.GA_ROOT);
                if (root == IntPtr.Zero)
                    root = atPoint;
                if (root == expected.Hwnd)
                {
                    point = new ForegroundActivationPoint(candidate.x, candidate.y);
                    GuardedProc.Log($"  Foreground qualification found safe frame activation point ({candidate.x},{candidate.y}) for 0x{expected.Hwnd.ToInt64():X}.");
                    return true;
                }

                string coverage = Discover.TryCaptureIdentity(root, out WindowIdentity covered)
                    ? $" pid={covered.ProcessId} title={covered.Title} exstyle=0x{NativeMethods.GetWindowLongPtr(root, NativeMethods.GWL_EXSTYLE).ToInt64():X}"
                    : string.Empty;
                GuardedProc.Log($"  Foreground qualification frame candidate ({candidate.x},{candidate.y}) resolves to 0x{root.ToInt64():X}, expected 0x{expected.Hwnd.ToInt64():X}.{coverage}");
            }

            GuardedProc.Log($"  Foreground qualification: no exposed frame point for 0x{expected.Hwnd.ToInt64():X}; rect={rect.left},{rect.top},{rect.right},{rect.bottom} style=0x{style:X}.");
            return false;
        }

        public bool VerifyActivationPoint(
            WindowIdentity expected,
            ForegroundActivationPoint point)
            => VerifyPointTargetForExpected(expected, point.X, point.Y);

        public bool ClickActivationPoint(
            WindowIdentity expected,
            ForegroundActivationPoint point)
        {
            _activeTarget = expected;
            return ClickAtVerified(expected, point.X, point.Y);
        }

        public bool VerifyForegroundAfterActivation(WindowIdentity expected)
        {
            bool exactForeground = IsTargetForeground(expected);
            DesktopLeaseCheckpoint checkpoint = _desktopLease?.Checkpoint(
                "foreground-after-activation",
                expected,
                requireForeground: true)
                ?? default;
            return exactForeground && checkpoint.IsValid;
        }
    }

    private static bool MatchesStableIdentity(IntPtr hwnd, WindowIdentity expected)
    {
        return Discover.TryCaptureIdentity(hwnd, out WindowIdentity current)
            && current.ProcessId == expected.ProcessId
            && current.WindowThreadId == expected.WindowThreadId
            && string.Equals(current.ClassName, expected.ClassName, StringComparison.Ordinal)
            && string.Equals(current.ExePath, expected.ExePath, StringComparison.OrdinalIgnoreCase)
            && current.ProcessStartTimeUtcTicks == expected.ProcessStartTimeUtcTicks
            && IsScoped(current, out _);
    }

    private static bool VerifyForegroundTarget(bool allowRegisteredDragTarget = false)
    {
        if (!_activeTarget.HasValue)
        {
            GuardedProc.Log("WARNING: refusing keyboard input before a verified foreground target was established.");
            IdentityDiagnostics.RecordPointFailure(_hasLastPoint ? _lastX : 0, _hasLastPoint ? _lastY : 0, IntPtr.Zero, "no-verified-foreground-target");
            return false;
        }

        // GetForegroundWindow() legitimately returns NULL for brief windows
        // during focus transitions (documented Win32 behavior, observed live in
        // split-move after entering split). Retry the read before concluding
        // the desktop has no foreground window.
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            ScenarioWait.Until(
                () => (foreground = NativeMethods.GetForegroundWindow()) != IntPtr.Zero,
                600,
                50,
                describe: () => foreground == IntPtr.Zero ? "foreground-unavailable" : $"foreground=0x{foreground.ToInt64():X}");
        }
        bool foregroundUnavailable = foreground == IntPtr.Zero;
        IntPtr root = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = foreground;
        string currentScopeReason = string.Empty;
        bool currentIdentityCaptured = Discover.TryCaptureIdentity(root, out WindowIdentity current);
        bool currentValid = currentIdentityCaptured
            && IsScoped(current, out currentScopeReason);
        if (currentValid
            && _desktopLease != null
            && !_desktopLease.Checkpoint("foreground-before-input", current, requireForeground: true).IsValid)
        {
            GuardedProc.Log("WARNING: refusing keyboard input because the desktop lease rejected the foreground continuity proof.");
            return false;
        }
        WindowIdentity previousTarget = _activeTarget.Value;
        bool previousTargetLive = MatchesStableIdentity(previousTarget.Hwnd, previousTarget);
        bool targetChanged = currentValid && current.Hwnd != _activeTarget.Value.Hwnd;
        bool sameProcessIdentity = currentValid
            && current.ProcessId == previousTarget.ProcessId
            && string.Equals(current.ExePath, previousTarget.ExePath, StringComparison.OrdinalIgnoreCase)
            && current.ProcessStartTimeUtcTicks == previousTarget.ProcessStartTimeUtcTicks;
        // A WPF menu, picker, or dialog can legitimately disappear after it
        // supplied the prior target. If that prior HWND is no longer live, the
        // independently validated current foreground root may become the new
        // target, but only inside the already-registered test process scope.
        // A modal dialog or edit surface created by the same verified process
        // is also a safe transition while the owner HWND remains live. A live
        // target in a different process still requires exact matching.
        bool registeredTargetTransition = currentValid
            && (allowRegisteredDragTarget || TestRunProvenance.IsProcessInScope(current.ProcessId));
        if (!currentValid || (previousTargetLive && targetChanged && !sameProcessIdentity
            && !registeredTargetTransition))
        {
            string actual = Discover.TryCaptureIdentity(root, out WindowIdentity observed)
                ? TestRunProvenance.SafeIdentity(observed)
                : "identity-unavailable";
            WindowIdentity expected = _activeTarget.Value;
            string reason = currentValid
                ? "foreground-target-transition-not-proven"
                : foregroundUnavailable
                    ? "foreground-window-unavailable"
                : (currentIdentityCaptured
                    ? (string.IsNullOrEmpty(currentScopeReason) ? "identity-scope-rejected" : currentScopeReason)
                    : "identity-unavailable");
            GuardedProc.Log($"WARNING: refusing keyboard input; foreground 0x{root.ToInt64():X} is not the verified target (expected={TestRunProvenance.SafeIdentity(expected)}; actual={actual}; reason={reason}).");
            IdentityDiagnostics.RecordPointFailure(_hasLastPoint ? _lastX : 0, _hasLastPoint ? _lastY : 0, expected.Hwnd, reason);
            return false;
        }
        if (!previousTargetLive || targetChanged)
            GuardedProc.Log($"  Input identity transition: previous keyboard target 0x{previousTarget.Hwnd.ToInt64():X} -> current foreground root 0x{current.Hwnd.ToInt64():X}; registered process identity and scope validation passed.");
        _activeTarget = current;
        return true;
    }

    private static bool VerifyPointTarget(int x, int y, WindowIdentity? expectedTarget = null)
    {
        if (!_activeTarget.HasValue)
        {
            GuardedProc.Log("WARNING: refusing coordinate input before a verified target was established.");
            IdentityDiagnostics.RecordPointFailure(x, y, IntPtr.Zero, "no-verified-target");
            return false;
        }

        // WindowFromPoint and the process identity APIs are separate native
        // observations. A WPF popup closing, a guest activation, or a brief
        // shell transition can make one sample resolve to an incomplete or
        // unregistered HWND even though the same point is immediately back on
        // the already-proven test window. Re-sample a bounded number of times;
        // input is still refused unless a complete current identity passes the
        // run provenance guard. This is a retry of evidence collection, not a
        // whitelist or an identity bypass.
        IntPtr root = IntPtr.Zero;
        string scopeReason = string.Empty;
        WindowIdentity current = default;
        bool currentValid = false;
        bool identityCaptured = false;
        string lastReason = "identity-unavailable";
        for (int attempt = 0; attempt < 4 && !currentValid; attempt++)
        {
            IntPtr atPoint = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
            root = NativeMethods.GetAncestor(atPoint, NativeMethods.GA_ROOT);
            if (root == IntPtr.Zero)
            {
                lastReason = "no-window-under-point";
            }
            else
            {
                identityCaptured = Discover.TryCaptureIdentity(root, out current);
                currentValid = identityCaptured && IsScoped(current, out scopeReason);
                lastReason = identityCaptured
                    ? (string.IsNullOrEmpty(scopeReason) ? "identity-scope-rejected" : scopeReason)
                    : "identity-unavailable";
            }

            if (!currentValid && attempt < 3)
                Thread.Sleep(3);
        }
        if (!currentValid)
        {
            _desktopLease?.Checkpoint("point-before-input", x: x, y: y);
            GuardedProc.Log(root == IntPtr.Zero
                ? $"WARNING: refusing coordinate input at ({x},{y}); no window is under the point after bounded identity retries."
                : $"WARNING: refusing coordinate input at ({x},{y}); root 0x{root.ToInt64():X} is outside the test identity scope after bounded retries: {lastReason}.");
            IdentityDiagnostics.RecordPointFailure(x, y, _activeTarget.Value.Hwnd, lastReason);
            return false;
        }

        if (expectedTarget.HasValue && !SameStableIdentity(current, expectedTarget.Value))
        {
            _desktopLease?.Checkpoint(
                "point-before-input",
                expectedTarget.Value,
                x,
                y);
            GuardedProc.Log($"WARNING: refusing coordinate input at ({x},{y}); root 0x{root.ToInt64():X} is not the exact expected target 0x{expectedTarget.Value.Hwnd.ToInt64():X}.");
            IdentityDiagnostics.RecordPointFailure(x, y, expectedTarget.Value.Hwnd, "point-target-mismatch");
            return false;
        }

        if (_desktopLease != null
            && !_desktopLease.Checkpoint("point-before-input", current, x, y).IsValid)
        {
            GuardedProc.Log($"WARNING: refusing coordinate input at ({x},{y}) because the desktop lease rejected the target continuity proof.");
            return false;
        }

        // The previous target may be a transient WPF context-menu popup that
        // legitimately disappeared after the last action. A dead previous
        // target is not evidence that the independently discovered point root
        // is unsafe: the current root has already passed the registered
        // process-start, PID, executable, class, and HWND checks above. Keep
        // the scope gate, but allow this safe identity transition.
        if (!MatchesStableIdentity(_activeTarget.Value.Hwnd, _activeTarget.Value))
            GuardedProc.Log($"  Input identity transition: previous target 0x{_activeTarget.Value.Hwnd.ToInt64():X} is no longer live; current point root 0x{current.Hwnd.ToInt64():X} passed independent scope validation.");

        // The point may be a visible Shepherd guest over a TabDock container;
        // a real click activates that guest. Promote the verified point root
        // so subsequent keyboard input is checked against the window that the
        // click actually targeted.
        _activeTarget = current;
        return true;
    }

    private static bool SameStableIdentity(WindowIdentity actual, WindowIdentity expected)
        => actual.Hwnd == expected.Hwnd
            && actual.ProcessId == expected.ProcessId
            && actual.WindowThreadId == expected.WindowThreadId
            && actual.ProcessStartTimeUtcTicks == expected.ProcessStartTimeUtcTicks
            && string.Equals(actual.ClassName, expected.ClassName, StringComparison.Ordinal)
            && string.Equals(actual.ExePath, expected.ExePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Verifies that a point is currently owned by one exact target root.</summary>
    public static bool VerifyClickPoint(IntPtr hwnd, int x, int y)
    {
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;
        return Discover.TryCaptureIdentity(root, out WindowIdentity expected)
            && expected.Hwnd == root
            && VerifyPointTarget(x, y, expected);
    }

    private static bool ClickAtVerified(WindowIdentity expected, int x, int y)
    {
        if (!VerifyPointTargetForExpected(expected, x, y))
            return false;

        _activeTarget = expected;
        _lastX = x;
        _lastY = y;
        _hasLastPoint = true;
        _desktopLease?.RecordInteraction(
            "expected-input-target",
            TestRunProvenance.WindowRole(expected.Hwnd),
            expected.Hwnd,
            new Dictionary<string, string>
            {
                ["kind"] = "foreground-activation-point",
                ["x"] = x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["y"] = y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        if (!NativeMethods.SetCursorPos(x, y))
            return false;
        SendMouse(
            NativeMethods.MOUSEEVENTF_MOVE,
            verifyPoint: true,
            expectedPointTarget: expected);
        Thread.Sleep(40);
        SendButtonClick(
            NativeMethods.MOUSEEVENTF_LEFTDOWN,
            NativeMethods.MOUSEEVENTF_LEFTUP,
            40,
            expected);
        Thread.Sleep(60);
        return true;
    }

    private static bool VerifyPointTargetForExpected(
        WindowIdentity expected,
        int x,
        int y)
    {
        _activeTarget = expected;
        return VerifyPointTarget(x, y, expected);
    }

    private static NativeMethods.POINT _savedCursor;
    private static bool _cursorSaved;

    /// <summary>Records the cursor position at run start so it can be restored at run end.</summary>
    public static void SaveCursor()
    {
        _cursorSaved = NativeMethods.GetCursorPos(out _savedCursor);
    }

    public static void RestoreCursor()
    {
        if (_cursorSaved)
            NativeMethods.SetCursorPos(_savedCursor.x, _savedCursor.y);
    }

    public static void MoveTo(int x, int y)
    {
        if (!VerifyPointTarget(x, y))
            throw new InvalidOperationException($"Refusing real input at ({x},{y}) because the live target failed identity verification.");
        if (_activeTarget.HasValue)
        {
            _desktopLease?.RecordInteraction(
                "expected-input-target",
                TestRunProvenance.WindowRole(_activeTarget.Value.Hwnd),
                _activeTarget.Value.Hwnd,
                new Dictionary<string, string>
                {
                    ["kind"] = "point",
                    ["x"] = x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["y"] = y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
        }
        _lastX = x;
        _lastY = y;
        _hasLastPoint = true;
        NativeMethods.SetCursorPos(x, y);
        // Zero-delta nudge so apps see a genuine WM_MOUSEMOVE from the input queue.
        SendMouse(NativeMethods.MOUSEEVENTF_MOVE, verifyPoint: true);
        Thread.Sleep(30);
    }

    public static void ClickAt(int x, int y)
    {
        MoveTo(x, y);
        Thread.Sleep(40);
        SendButtonClick(NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP, 40);
        Thread.Sleep(60);
    }

    /// <summary>
    /// Two clicks well inside the default double-click time. An initial click
    /// activates/gives focus to the target window first, then the actual
    /// double-click is delivered with tight, fixed-position timing so WPF
    /// reports ClickCount==2 reliably from synthetic input.
    /// </summary>
    public static void DoubleClickAt(int x, int y)
    {
        // Pre-activate: a first standalone click so the window is foreground and
        // the following pair isn't consumed by activation.
        MoveTo(x, y);
        Thread.Sleep(40);
        SendButtonClick(NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP, 30);
        Thread.Sleep(250);

        // The double-click pair, same pixel, tight gaps.
        SendButtonClick(NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP, 20);
        Thread.Sleep(40);
        SendButtonClick(NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP, 20);
        Thread.Sleep(60);
    }

    public static void RightClickAt(int x, int y)
    {
        MoveTo(x, y);
        Thread.Sleep(40);
        SendButtonClick(NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP, 40);
        Thread.Sleep(60);
    }

    public static void MiddleClickAt(int x, int y)
    {
        MoveTo(x, y);
        GuardedProc.Log($"  middle-click at ({x},{y}) windowFromPoint=0x{NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y }).ToInt64():X}");
        Thread.Sleep(40);
        SendButtonClick(NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP, 40);
        Thread.Sleep(60);
    }

    /// <summary>Press at (x1,y1), interpolate at least 8 move steps (15 ms apart), release at (x2,y2).</summary>
    public static void DragFromTo(int x1, int y1, int x2, int y2, int steps = 10)
    {
        if (steps < 8)
            steps = 8;

        MoveTo(x1, y1);
        Thread.Sleep(60);
        bool sentDown = false;
        try
        {
            SendMouse(NativeMethods.MOUSEEVENTF_LEFTDOWN, verifyPoint: true);
            sentDown = true;
            _leftButtonHeld = true;
            for (int i = 1; i <= steps; i++)
            {
                int x = x1 + (x2 - x1) * i / steps;
                int y = y1 + (y2 - y1) * i / steps;
                _lastX = x;
                _lastY = y;
                NativeMethods.SetCursorPos(x, y);
                SendMouse(NativeMethods.MOUSEEVENTF_MOVE, verifyPoint: false);
                Thread.Sleep(15);
            }
        }
        finally
        {
            if (sentDown)
                SendMouse(NativeMethods.MOUSEEVENTF_LEFTUP, verifyPoint: false, allowUnverifiedCleanup: true);
            _leftButtonHeld = false;
        }
        Thread.Sleep(60);
    }

    /// <summary>
    /// Presses the left mouse button down at (x,y) and returns WITHOUT releasing it —
    /// only for scenarios that need a real OS-level mouse-button-down state to persist
    /// across an external event (e.g. force-killing TabDock while a tab-strip drag is
    /// theoretically still in progress). Every caller MUST eventually call
    /// <see cref="ReleaseLeftButtonHeld"/> (ideally in a finally block): an unreleased
    /// real button-down state would corrupt every subsequent click in this run.
    /// </summary>
    public static void PressLeftButtonHeld(int x, int y)
    {
        MoveTo(x, y);
        Thread.Sleep(40);
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTDOWN, verifyPoint: true);
        _leftButtonHeld = true;
        Thread.Sleep(40);
    }

    /// <summary>Moves the cursor while the left button is already held down (see <see cref="PressLeftButtonHeld"/>), without a fresh down/up pair.</summary>
    public static void MoveWhileHeld(int x, int y)
    {
        if (!VerifyForegroundTarget())
            throw new InvalidOperationException("Refusing a drag move because the verified drag target is no longer foreground.");
        _lastX = x;
        _lastY = y;
        NativeMethods.SetCursorPos(x, y);
        SendMouse(NativeMethods.MOUSEEVENTF_MOVE, verifyPoint: false);
        Thread.Sleep(15);
    }

    /// <summary>Releases a left-button-down state started by <see cref="PressLeftButtonHeld"/>.</summary>
    public static void ReleaseLeftButtonHeld()
    {
        // Always release a physically held button, even if the target vanished
        // during the test. This is cleanup of global input state, not a new
        // window action; failing to do so would contaminate the user's desktop.
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTUP, verifyPoint: false, allowUnverifiedCleanup: true);
        _leftButtonHeld = false;
        Thread.Sleep(40);
    }

    /// <summary>
    /// One continuous drag through multiple waypoints: press at (xs[0], ys[0]),
    /// interpolate stepsPerSegment moves per segment (15 ms apart), release at
    /// the last waypoint. Unlike chaining DragFromTo calls (which release and
    /// re-press between segments, splitting the gesture into several independent
    /// native move loops), this composes PressLeftButtonHeld/MoveWhileHeld/
    /// ReleaseLeftButtonHeld into ONE modal move loop with many intermediate
    /// WM_WINDOWPOSCHANGED events — the shape needed to exercise drag
    /// finalization after a multi-segment trajectory.
    /// </summary>
    public static void DragPolyline(int[] xs, int[] ys, int stepsPerSegment = 8)
    {
        if (xs == null || ys == null || xs.Length != ys.Length || xs.Length < 2)
            throw new ArgumentException("DragPolyline needs at least two matching waypoints.");
        if (stepsPerSegment < 2)
            stepsPerSegment = 2;

        PressLeftButtonHeld(xs[0], ys[0]);
        try
        {
            for (int s = 1; s < xs.Length; s++)
            {
                for (int i = 1; i <= stepsPerSegment; i++)
                {
                    int x = xs[s - 1] + (xs[s] - xs[s - 1]) * i / stepsPerSegment;
                    int y = ys[s - 1] + (ys[s] - ys[s - 1]) * i / stepsPerSegment;
                    MoveWhileHeld(x, y);
                }
            }
        }
        finally
        {
            ReleaseLeftButtonHeld();
        }
    }

    /// <summary>Types text as KEYEVENTF_UNICODE down/up pairs, one character at a time.</summary>
    public static void TypeText(string text)
    {
        foreach (char ch in text)
        {
            bool sentDown = false;
            try
            {
                SendUnicode(ch, up: false);
                sentDown = true;
            }
            finally
            {
                if (sentDown)
                    SendUnicode(ch, up: true, allowUnverifiedCleanup: true);
            }
            Thread.Sleep(15);
        }
    }

    public static void SendKey(ushort vk)
    {
        bool sentDown = false;
        try
        {
            SendVk(vk, up: false);
            sentDown = true;
        }
        finally
        {
            if (sentDown)
                SendVk(vk, up: true, allowUnverifiedCleanup: true);
        }
        Thread.Sleep(30);
    }

    public static void SendKeyDown(ushort vk)
    {
        SendVk(vk, up: false);
        Thread.Sleep(20);
    }

    public static void SendKeyUp(ushort vk)
    {
        SendVk(vk, up: true, allowUnverifiedCleanup: true);
        Thread.Sleep(20);
    }

    /// <summary>
    /// Sends a real Windows-logo plus arrow chord to one exact captured root.
    /// The chord is atomic after the foreground/lease proof so a guest cannot
    /// be replaced by a recycled HWND or a foreground transition between the
    /// modifier and arrow events.
    /// </summary>
    public static bool SendWinArrowTo(IntPtr hwnd, bool up)
    {
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;
        if (!Discover.TryCaptureIdentity(root, out WindowIdentity expected)
            || !ForceForeground(root)
            || !VerifyForegroundTarget()
            || !IsExactForeground(expected))
        {
            GuardedProc.Log($"WARNING: refusing Win+{(up ? "Up" : "Down")} because exact foreground could not be proven for 0x{root.ToInt64():X}.");
            return false;
        }

        ushort arrow = up ? VK_UP : VK_DOWN;
        var inputs = new[]
        {
            KeyboardInput(VK_LWIN, up: false),
            KeyboardInput(arrow, up: false),
            KeyboardInput(arrow, up: true),
            KeyboardInput(VK_LWIN, up: true),
        };
        uint sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());
        _desktopLease?.RecordInteraction(
            "input-dispatch-batch",
            TestRunProvenance.WindowRole(root),
            root,
            new Dictionary<string, string>
            {
                ["inputType"] = "keyboard",
                ["chord"] = $"Win+{(up ? "Up" : "Down")}",
                ["sent"] = sent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        if (sent != inputs.Length)
        {
            // Do not leave a modifier physically down after a partial native
            // dispatch. This is global input cleanup, not a second action.
            if (sent > 0)
            {
                var cleanup = new[]
                {
                    KeyboardInput(arrow, up: true),
                    KeyboardInput(VK_LWIN, up: true),
                };
                NativeMethods.SendInput(
                    (uint)cleanup.Length,
                    cleanup,
                    Marshal.SizeOf<NativeMethods.INPUT>());
            }
            GuardedProc.Log($"WARNING: Win+{(up ? "Up" : "Down")} SendInput batch failed: sent={sent}/{inputs.Length}; {NativeMethods.FormatLastError()}");
            return false;
        }
        Thread.Sleep(80);
        return true;
    }

    /// <summary>
    /// Sends a real Windows-logo plus Shift plus arrow monitor-transfer chord
    /// to one exact captured root. The full chord is dispatched atomically only
    /// after the foreground and desktop-lease proof, so a guest HWND cannot be
    /// replaced between modifier and arrow events.
    /// </summary>
    public static bool SendWinShiftArrowTo(IntPtr hwnd, bool right)
    {
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;
        if (!Discover.TryCaptureIdentity(root, out WindowIdentity expected)
            || !ForceForeground(root)
            || !VerifyForegroundTarget()
            || !IsExactForeground(expected))
        {
            GuardedProc.Log($"WARNING: refusing Win+Shift+{(right ? "Right" : "Left")} because exact foreground could not be proven for 0x{root.ToInt64():X}.");
            return false;
        }

        ushort arrow = right ? VK_RIGHT : VK_LEFT;
        var inputs = new[]
        {
            KeyboardInput(VK_LWIN, up: false),
            KeyboardInput(VK_SHIFT, up: false),
            KeyboardInput(arrow, up: false),
            KeyboardInput(arrow, up: true),
            KeyboardInput(VK_SHIFT, up: true),
            KeyboardInput(VK_LWIN, up: true),
        };
        uint sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());
        _desktopLease?.RecordInteraction(
            "input-dispatch-batch",
            TestRunProvenance.WindowRole(root),
            root,
            new Dictionary<string, string>
            {
                ["inputType"] = "keyboard",
                ["chord"] = $"Win+Shift+{(right ? "Right" : "Left")}",
                ["sent"] = sent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        if (sent != inputs.Length)
        {
            // Release every modifier/key that could have been pressed before
            // the partial dispatch. This cleanup is not a second user action.
            var cleanup = new[]
            {
                KeyboardInput(arrow, up: true),
                KeyboardInput(VK_SHIFT, up: true),
                KeyboardInput(VK_LWIN, up: true),
            };
            NativeMethods.SendInput(
                (uint)cleanup.Length,
                cleanup,
                Marshal.SizeOf<NativeMethods.INPUT>());
            GuardedProc.Log($"WARNING: Win+Shift+{(right ? "Right" : "Left")} SendInput batch failed: sent={sent}/{inputs.Length}; {NativeMethods.FormatLastError()}");
            return false;
        }
        Thread.Sleep(80);
        return true;
    }

    public static bool SendWinShiftRightTo(IntPtr hwnd) => SendWinShiftArrowTo(hwnd, right: true);

    public static bool SendWinShiftLeftTo(IntPtr hwnd) => SendWinShiftArrowTo(hwnd, right: false);

    public static bool SendWinUpTo(IntPtr hwnd) => SendWinArrowTo(hwnd, up: true);

    public static bool SendWinDownTo(IntPtr hwnd) => SendWinArrowTo(hwnd, up: false);

    private static bool IsExactForeground(WindowIdentity expected)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        IntPtr root = foreground == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = foreground;
        return root == expected.Hwnd
            && MatchesStableIdentity(expected.Hwnd, expected);
    }


    /// <summary>
    /// Sends one real key press to an exact top-level root after a fresh
    /// foreground and desktop-lease checkpoint. Used for browser F11, where
    /// the browser—not TabDock—must receive the native shortcut.
    /// </summary>
    public static bool SendKeyTo(IntPtr hwnd, ushort vk, string label)
    {
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;
        if (!Discover.TryCaptureIdentity(root, out WindowIdentity expected)
            || !ForceForeground(root)
            || !VerifyForegroundTarget()
            || !IsExactForeground(expected))
        {
            GuardedProc.Log($"WARNING: refusing {label} because exact foreground could not be proven for 0x{root.ToInt64():X}.");
            return false;
        }

        var inputs = new[]
        {
            KeyboardInput(vk, up: false),
            KeyboardInput(vk, up: true),
        };
        uint sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());
        _desktopLease?.RecordInteraction(
            "input-dispatch-batch",
            TestRunProvenance.WindowRole(root),
            root,
            new Dictionary<string, string>
            {
                ["inputType"] = "keyboard",
                ["key"] = label,
                ["sent"] = sent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        if (sent != inputs.Length)
        {
            if (sent > 0)
                NativeMethods.SendInput(1, new[] { KeyboardInput(vk, up: true) }, Marshal.SizeOf<NativeMethods.INPUT>());
            GuardedProc.Log($"WARNING: {label} SendInput batch failed: sent={sent}/{inputs.Length}; {NativeMethods.FormatLastError()}");
            return false;
        }
        Thread.Sleep(80);
        return true;
    }

    public static bool SendF11To(IntPtr hwnd) => SendKeyTo(hwnd, VK_F11, "F11");
    /// <summary>
    /// Sends Ctrl+Tab as one real-input batch after proving that the exact
    /// requested top-level window is foreground. Split activation deliberately
    /// reasserts the focused guest shortly after container activation; sending
    /// the modifier and key as separate calls can therefore race that benign
    /// reassert and deliver Ctrl+Tab to the guest instead of TabDock. The batch
    /// keeps the safety gate strict while removing that harness-only gap.
    /// </summary>
    public static bool SendCtrlTabTo(IntPtr hwnd)
    {
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;
        if (!ForceForeground(root)
            || !_activeTarget.HasValue
            || _activeTarget.Value.Hwnd != root
            || !MatchesStableIdentity(root, _activeTarget.Value))
        {
            GuardedProc.Log($"WARNING: refusing Ctrl+Tab because exact container foreground could not be proven for 0x{root.ToInt64():X}.");
            return false;
        }

        // The shared qualification primitive is the only foreground
        // arrangement path. Re-read the exact target immediately before the
        // atomic key batch; a delayed guest reassert is a refusal, not a reason
        // to nudge the input queue or pulse window z-order.
        if (!VerifyForegroundTarget())
        {
            GuardedProc.Log($"WARNING: refusing Ctrl+Tab because exact container foreground was lost at dispatch for 0x{root.ToInt64():X}.");
            return false;
        }

        var inputs = new[]
        {
            KeyboardInput(VK_CONTROL, up: false),
            KeyboardInput(VK_TAB, up: false),
            KeyboardInput(VK_TAB, up: true),
            KeyboardInput(VK_CONTROL, up: true),
        };
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            GuardedProc.Log($"WARNING: Ctrl+Tab SendInput batch failed: sent={sent}/{inputs.Length}; {NativeMethods.FormatLastError()}");
            return false;
        }
        Thread.Sleep(30);
        return true;
    }

    /// <summary>Ctrl+L: the standard browser shortcut to focus and select-all the address/search bar.</summary>
    public static void SendCtrlL()
    {
        bool ctrlDown = false;
        bool lDown = false;
        try
        {
            SendVk(VK_CONTROL, up: false);
            ctrlDown = true;
            Thread.Sleep(20);
            SendVk(VK_L, up: false);
            lDown = true;
            Thread.Sleep(20);
        }
        finally
        {
            if (lDown)
                SendVk(VK_L, up: true, allowUnverifiedCleanup: true);
            if (ctrlDown)
                SendVk(VK_CONTROL, up: true, allowUnverifiedCleanup: true);
        }
        Thread.Sleep(50);
    }

    public static void SendHotkeyCtrlAltG()
    {
        bool ctrlDown = false;
        bool altDown = false;
        bool gDown = false;
        try
        {
            SendVk(VK_CONTROL, up: false);
            ctrlDown = true;
            Thread.Sleep(20);
            SendVk(VK_MENU, up: false);
            altDown = true;
            Thread.Sleep(20);
            SendVk(VK_G, up: false);
            gDown = true;
            Thread.Sleep(20);
        }
        finally
        {
            if (gDown)
                SendVk(VK_G, up: true, allowUnverifiedCleanup: true);
            if (altDown)
                SendVk(VK_MENU, up: true, allowUnverifiedCleanup: true);
            if (ctrlDown)
                SendVk(VK_CONTROL, up: true, allowUnverifiedCleanup: true);
        }
        Thread.Sleep(50);
    }

    /// <summary>
    /// Sends one of TabDock's global Ctrl+Alt+PageUp/PageDown shortcuts after
    /// proving the requested top-level guest/container is foreground. This is
    /// real OS input; it never posts a synthetic WM_HOTKEY or bypasses the
    /// driver's identity scope.
    /// </summary>
    public static bool SendHotkeyCtrlAltPageTo(IntPtr target, bool previous)
    {
        if (!ForceForegroundRoot(target) || !VerifyForegroundTarget())
            return false;

        ushort page = previous ? VK_PRIOR : VK_NEXT;
        var inputs = new[]
        {
            KeyboardInput(VK_CONTROL, up: false),
            KeyboardInput(VK_MENU, up: false),
            KeyboardInput(page, up: false),
            KeyboardInput(page, up: true),
            KeyboardInput(VK_MENU, up: true),
            KeyboardInput(VK_CONTROL, up: true),
        };
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            GuardedProc.Log($"WARNING: Ctrl+Alt+Page input batch failed: sent={sent}/{inputs.Length}; {NativeMethods.FormatLastError()}");
            return false;
        }
        Thread.Sleep(80);
        return true;
    }

    /// <summary>Ctrl+Alt+Shift+D: request TabDock's diagnostic support bundle.</summary>
    public static void SendHotkeyCtrlAltShiftD()
    {
        bool ctrlDown = false;
        bool altDown = false;
        bool shiftDown = false;
        bool dDown = false;
        try
        {
            SendVk(VK_CONTROL, up: false);
            ctrlDown = true;
            Thread.Sleep(15);
            SendVk(VK_MENU, up: false);
            altDown = true;
            Thread.Sleep(15);
            SendVk(VK_SHIFT, up: false);
            shiftDown = true;
            Thread.Sleep(15);
            SendVk(VK_D, up: false);
            dDown = true;
            Thread.Sleep(20);
        }
        finally
        {
            if (dDown)
                SendVk(VK_D, up: true, allowUnverifiedCleanup: true);
            if (shiftDown)
                SendVk(VK_SHIFT, up: true, allowUnverifiedCleanup: true);
            if (altDown)
                SendVk(VK_MENU, up: true, allowUnverifiedCleanup: true);
            if (ctrlDown)
                SendVk(VK_CONTROL, up: true, allowUnverifiedCleanup: true);
        }
        Thread.Sleep(80);
    }

    private static void SendButtonClick(
        uint downFlags,
        uint upFlags,
        int downDurationMs,
        WindowIdentity? expectedPointTarget = null)
    {
        bool sentDown = false;
        try
        {
            SendMouse(
                downFlags,
                verifyPoint: true,
                expectedPointTarget: expectedPointTarget);
            sentDown = true;
            Thread.Sleep(downDurationMs);
        }
        finally
        {
            if (sentDown)
                // A button-up is global input cleanup. Requiring the original
                // window to remain alive here can strand a physical button-down
                // when the click itself closes or releases that window.
                SendMouse(
                    upFlags,
                    verifyPoint: false,
                    allowUnverifiedCleanup: true);
        }
    }

    private static void SendMouse(
        uint flags,
        bool verifyPoint,
        bool allowUnverifiedCleanup = false,
        WindowIdentity? expectedPointTarget = null)
    {
        if (verifyPoint && (!_hasLastPoint
            || !VerifyPointTarget(_lastX, _lastY, expectedPointTarget)))
        {
            throw new InvalidOperationException("Refusing mouse input because the live point failed identity verification.");
        }
        if (!verifyPoint && !VerifyForegroundTarget(_leftButtonHeld) && !allowUnverifiedCleanup)
            throw new InvalidOperationException("Refusing mouse input because the verified target is no longer foreground.");
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
        input.u.mi = new NativeMethods.MOUSEINPUT { dwFlags = flags };
        SendRaw(input);
    }


    private static void SendVk(ushort vk, bool up, bool allowUnverifiedCleanup = false)
    {
        NativeMethods.INPUT input = KeyboardInput(vk, up);
        Send(input, verifyPoint: false, allowUnverifiedCleanup);
    }

    private static NativeMethods.INPUT KeyboardInput(ushort vk, bool up)
    {
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        input.u.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = vk,
            dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0,
        };
        return input;
    }

    private static void SendUnicode(char ch, bool up, bool allowUnverifiedCleanup = false)
    {
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        input.u.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = 0,
            wScan = ch,
            dwFlags = NativeMethods.KEYEVENTF_UNICODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0),
        };
        Send(input, verifyPoint: false, allowUnverifiedCleanup);
    }


    private static void Send(NativeMethods.INPUT input, bool verifyPoint, bool allowUnverifiedCleanup)
    {
        bool verified = verifyPoint
            ? _hasLastPoint && VerifyPointTarget(_lastX, _lastY)
            : VerifyForegroundTarget();
        if (!verified && !allowUnverifiedCleanup)
            throw new InvalidOperationException("Refusing input because the live foreground target failed identity verification.");
        SendRaw(input);
    }

    private static void SendRaw(NativeMethods.INPUT input)
    {
        uint sent = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        if (_desktopLease != null)
        {
            IntPtr hwnd = _activeTarget?.Hwnd ?? IntPtr.Zero;
            _desktopLease.RecordInteraction(
                "input-dispatch",
                hwnd == IntPtr.Zero ? "Unknown" : TestRunProvenance.WindowRole(hwnd),
                hwnd,
                new Dictionary<string, string>
                {
                    ["inputType"] = input.type == NativeMethods.INPUT_MOUSE ? "mouse" : "keyboard",
                    ["sent"] = sent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
        }
        if (sent != 1)
            GuardedProc.Log($"WARNING: SendInput failed: {NativeMethods.FormatLastError()}");
    }
}
