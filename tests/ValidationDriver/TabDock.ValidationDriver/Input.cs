using System;
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
    public const ushort VK_TAB = 0x09;
    public const ushort VK_DELETE = 0x2E;
    public const ushort VK_A = 0x41;
    public const ushort VK_D = 0x44;
    public const ushort VK_G = 0x47;
    public const ushort VK_L = 0x4C;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_PRIOR = 0x21;
    public const ushort VK_NEXT = 0x22;

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
    /// Brings a window verifiably to the foreground before real-input targeting.
    /// The driver usually runs from a terminal/IDE that owns the foreground, so
    /// freshly spawned TabDock windows open BEHIND it and plain
    /// SetForegroundWindow is denied — real clicks at UIA-read coordinates would
    /// then land in whatever covers the target (observed: clicks landing in the
    /// IDE). A benign key-up via SendInput makes this process the last input
    /// source, which grants foreground rights; a TOPMOST pulse is the fallback.
    /// Callers must treat false as "do NOT click".
    /// </summary>
    public static bool ForceForeground(IntPtr hwnd)
    {
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;
        string scopeReason = string.Empty;
        WindowIdentity target = default;
        bool identityCaptured = false;
        bool targetValid = false;
        int identityAttempt = 0;
        // Foreground transitions can briefly make one of the independent
        // identity reads incomplete even though this HWND was already
        // registered for the run. Re-read the complete proof for a bounded
        // interval; this only retries evidence collection and never broadens
        // the scope or permits an unverified operation.
        for (; identityAttempt < 4 && !targetValid; identityAttempt++)
        {
            identityCaptured = Discover.TryCaptureIdentity(root, out target);
            targetValid = identityCaptured
                && IsScoped(target, out scopeReason);
            if (!targetValid && identityAttempt < 3)
                Thread.Sleep(3);
        }
        if (!targetValid)
        {
            string reason = identityCaptured
                ? (string.IsNullOrEmpty(scopeReason) ? "identity-scope-rejected" : scopeReason)
                : "identity-unavailable";
            GuardedProc.Log($"WARNING: refusing foreground operation for unverified HWND 0x{root.ToInt64():X}: {reason}.");
            IdentityDiagnostics.RecordPointFailure(_hasLastPoint ? _lastX : 0, _hasLastPoint ? _lastY : 0, root, reason);
            return false;
        }
        if (identityAttempt > 1)
            GuardedProc.Log($"  Foreground identity proof settled after bounded retry for HWND 0x{target.Hwnd.ToInt64():X}.");
        _activeTarget = target;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (!MatchesStableIdentity(target.Hwnd, target))
                return false;
            if (NativeMethods.GetForegroundWindow() == target.Hwnd)
                return true;

            // This isolated key-up is deliberately non-targeting and carries
            // no character or button transition. It grants this process the
            // foreground-change right on Windows versions that deny a direct
            // SetForegroundWindow call from a background test console.
            SendRawVk(VK_MENU, up: true);
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            Thread.Sleep(30);
            if (!MatchesStableIdentity(target.Hwnd, target))
                return false;
            NativeMethods.SetForegroundWindow(target.Hwnd);
            Thread.Sleep(150);
            if (NativeMethods.GetForegroundWindow() == target.Hwnd
                && MatchesStableIdentity(target.Hwnd, target))
                return true;

            // Fallback: pulse TOPMOST to rise above the covering window, then drop
            // back to the normal band and try again.
            if (!MatchesStableIdentity(target.Hwnd, target))
                return false;
            NativeMethods.SetWindowPos(target.Hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            if (!MatchesStableIdentity(target.Hwnd, target))
                return false;
            NativeMethods.SetWindowPos(target.Hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            if (!MatchesStableIdentity(target.Hwnd, target))
                return false;
            NativeMethods.SetForegroundWindow(target.Hwnd);
            Thread.Sleep(150);
        }
        bool ok = NativeMethods.GetForegroundWindow() == target.Hwnd
            && MatchesStableIdentity(target.Hwnd, target);
        if (!ok)
            GuardedProc.Log($"WARNING: could not bring 0x{target.Hwnd.ToInt64():X} to the foreground.");
        return ok;
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
            var fgSw = System.Diagnostics.Stopwatch.StartNew();
            while (foreground == IntPtr.Zero && fgSw.ElapsedMilliseconds < 600)
            {
                Thread.Sleep(50);
                foreground = NativeMethods.GetForegroundWindow();
            }
        }
        bool foregroundUnavailable = foreground == IntPtr.Zero;
        IntPtr root = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = foreground;
        string currentScopeReason = string.Empty;
        bool currentIdentityCaptured = Discover.TryCaptureIdentity(root, out WindowIdentity current);
        bool currentValid = currentIdentityCaptured
            && IsScoped(current, out currentScopeReason);
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

    private static bool VerifyPointTarget(int x, int y)
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
            GuardedProc.Log(root == IntPtr.Zero
                ? $"WARNING: refusing coordinate input at ({x},{y}); no window is under the point after bounded identity retries."
                : $"WARNING: refusing coordinate input at ({x},{y}); root 0x{root.ToInt64():X} is outside the test identity scope after bounded retries: {lastReason}.");
            IdentityDiagnostics.RecordPointFailure(x, y, _activeTarget.Value.Hwnd, lastReason);
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

        // ForceForeground intentionally waits for ordinary windows to settle,
        // but this product has a legitimate delayed guest reassert on container
        // activation. Reassert the exact container immediately before the
        // atomic key batch so the reassert timer cannot win the handoff between
        // the foreground check and SendInput.
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        SendRawVk(VK_MENU, up: true);
        NativeMethods.SetForegroundWindow(root);
        if (NativeMethods.GetForegroundWindow() != root
            || !MatchesStableIdentity(root, _activeTarget.Value)
            || !VerifyForegroundTarget())
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

    private static void SendButtonClick(uint downFlags, uint upFlags, int downDurationMs)
    {
        bool sentDown = false;
        try
        {
            SendMouse(downFlags, verifyPoint: true);
            sentDown = true;
            Thread.Sleep(downDurationMs);
        }
        finally
        {
            if (sentDown)
                // A button-up is global input cleanup. Requiring the original
                // window to remain alive here can strand a physical button-down
                // when the click itself closes or releases that window.
                SendMouse(upFlags, verifyPoint: false, allowUnverifiedCleanup: true);
        }
    }

    private static void SendMouse(uint flags, bool verifyPoint, bool allowUnverifiedCleanup = false)
    {
        if (verifyPoint && (!_hasLastPoint || !VerifyPointTarget(_lastX, _lastY)))
            throw new InvalidOperationException("Refusing mouse input because the live point failed identity verification.");
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

    private static void SendRawVk(ushort vk, bool up)
    {
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        input.u.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = vk,
            dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0,
        };
        SendRaw(input);
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
        if (sent != 1)
            GuardedProc.Log($"WARNING: SendInput failed: {NativeMethods.FormatLastError()}");
    }
}
