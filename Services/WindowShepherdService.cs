using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using TabDock.Models;

namespace TabDock.Services;

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
/// Because none of those mutations touch the guest's identity (style/parent/
/// owner), release is symmetric and simple: restore the placement snapshotted
/// at capture time, re-show it, and undo the DWM transition suppression. There
/// is no style/owner/parent surgery to get wrong, no permanently-downgraded DPI
/// awareness, and no compositor invalidation from reparenting — the guest
/// renders and receives input exactly as if it were never touched. This is
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
    private readonly LoggingService _log;

    // HWNDs for which a positioning-call failure has already been logged this
    // session. Failures (UIPI-blocked SetWindowPos on a guest that became
    // elevated mid-capture, dead HWND, ...) repeat on every drag tick, so only
    // the first failure per window is logged — the hot drag path stays at one
    // integer comparison per tick (PERF25-3 invariant, spec: elevation-guard).
    private readonly HashSet<long> _positioningFailuresLogged = new();

    /// <summary>
    /// Logs a failed positioning call with the native error, at most once per
    /// HWND per session. Must be called immediately after the failing call so
    /// <see cref="NativeMethods.FormatLastError"/> reads the right error.
    /// </summary>
    private void LogPositioningFailureOnce(IntPtr hwnd, string operation)
    {
        if (_positioningFailuresLogged.Add(hwnd.ToInt64()))
            _log.Log($"SHEPHERD[position-fail] {operation} failed for 0x{hwnd.ToInt64():X}: {NativeMethods.FormatLastError()} (subsequent failures for this window suppressed)");
    }

    private static readonly string JournalPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabDock", "hidden-windows.json");

    // In-memory journal state plus a debounce timer (AUDIT25-01). Both
    // JournalHide and JournalClear mutate this in memory instead of re-reading
    // hidden-windows.json from disk on every call. Only JournalClear's write is
    // debounced through this timer, mirroring GroupManager.RequestSave; JournalHide
    // writes synchronously (see its own doc comment for why). FlushJournal forces
    // an immediate, synchronous write of any pending debounced clear, for
    // App.xaml.cs exit/crash paths — it cannot help a hard force-kill (which
    // bypasses those paths entirely), which is exactly why JournalHide never
    // relies on it.
    private HiddenWindowJournalFile? _journalCache;
    private DispatcherTimer? _journalDebounce;

    public WindowShepherdService(LoggingService log)
    {
        _log = log;
    }

    /// <summary>
    /// Captures a top-level window without reparenting or restyling it.
    /// Returns null and an error message if capture is refused (e.g. UIPI /
    /// elevation mismatch, or the target is one of TabDock's own windows).
    /// </summary>
    public CapturedWindow? Capture(IntPtr hwnd, out string? error)
    {
        error = null;
        if (!NativeMethods.IsWindow(hwnd))
        {
            error = "The window no longer exists.";
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == NativeMethods.GetCurrentProcessId())
        {
            error = "Cannot capture a TabDock window.";
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

        var originalPlacement = new NativeMethods.WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        bool hasValidPlacement = NativeMethods.GetWindowPlacement(hwnd, out originalPlacement);
        if (!hasValidPlacement)
        {
            _log.Log($"GetWindowPlacement failed for 0x{hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
        }

        NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT bounds);

        var cw = new CapturedWindow
        {
            Hwnd = hwnd,
            ProcessId = pid,
            ExePath = NativeMethods.GetProcessImagePath(pid) ?? string.Empty,
            OriginalTitle = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty,
            OriginalPlacement = originalPlacement,
            HasValidPlacement = hasValidPlacement,
            OriginalBounds = bounds,
            WasMaximized = originalPlacement.showCmd == NativeMethods.SW_SHOWMAXIMIZED,
        };

        // DWM plays its own default fade transition whenever a top-level
        // window's visibility changes — with no reparenting to hide behind,
        // this is directly visible as a "fade" on every tab switch (Hide the
        // outgoing guest, Show the incoming one). Force it off for the whole
        // captured lifetime so hide/show is instantaneous; restored on release.
        SetTransitionsDisabled(hwnd, true);

        _log.Log($"Shepherd-captured 0x{hwnd.ToInt64():X} ({cw.OriginalTitle}) without reparenting; guest={NativeMethods.DescribeWindow(hwnd)}");
        return cw;
    }

    private static void SetTransitionsDisabled(IntPtr hwnd, bool disabled)
    {
        int value = disabled ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int));
    }

    /// <summary>
    /// Positions the guest to exactly cover <paramref name="screenRect"/> and
    /// places it immediately above <paramref name="containerHwnd"/> in
    /// z-order, then shows it. Restores the guest first if it is iconic or
    /// zoomed, since either state would otherwise fight the exact-fit resize.
    /// Clears the crash-recovery journal entry: an actively-shown window
    /// needs no rescue.
    /// </summary>
    public void PositionAndShow(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect)
    {
        if (!NativeMethods.IsWindow(window.Hwnd))
            return;

        if (NativeMethods.IsIconic(window.Hwnd) || NativeMethods.IsZoomed(window.Hwnd))
        {
            if (!NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_RESTORE))
                LogPositioningFailureOnce(window.Hwnd, "ShowWindow(SW_RESTORE)");
        }

        // SetWindowPos's hWndInsertAfter PRECEDES (sits above) hWnd in z-order,
        // so passing containerHwnd here would put the guest BEHIND its own
        // container. Bring the guest to the true top instead, then pin the
        // container immediately behind it so nothing else can slot between.
        if (!NativeMethods.SetWindowPos(
            window.Hwnd,
            NativeMethods.HWND_TOP,
            screenRect.left,
            screenRect.top,
            screenRect.Width,
            screenRect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
        {
            LogPositioningFailureOnce(window.Hwnd, "SetWindowPos(guest)");
        }

        PairZOrderBehind(containerHwnd, window.Hwnd);

        JournalClear(window.Hwnd);
        // Deliberately NOT DescribeWindow here: this is the hottest logging site
        // in the app (it runs on every LocationChanged/SizeChanged tick while a
        // container is dragged or resized) and DescribeWindow costs five extra
        // P/Invokes to report a rect that this line already carries — the one
        // this call is in the middle of applying, at that.
        _log.Log($"SHEPHERD[position] guest=0x{window.Hwnd.ToInt64():X} rect={screenRect.left},{screenRect.top},{screenRect.Width}x{screenRect.Height}");
    }

    /// <summary>
    /// Pins <paramref name="containerHwnd"/> immediately behind the guest in
    /// z-order so nothing else can slot between them. This is the single
    /// implementation of the z-order pin — <see cref="PositionAndShow"/> uses it
    /// for its own glue, and the container's foreground-pairing path
    /// (ContainerWindow.PairZOrderBehindGuest) delegates here too instead of
    /// repeating the same native call with the same flags.
    /// </summary>
    public void PairZOrderBehind(IntPtr containerHwnd, IntPtr guestHwnd)
    {
        if (!NativeMethods.SetWindowPos(
            containerHwnd,
            guestHwnd,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
        {
            LogPositioningFailureOnce(containerHwnd, "SetWindowPos(container)");
        }
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
    public void Hide(CapturedWindow window)
    {
        if (!NativeMethods.IsWindow(window.Hwnd))
            return;
        JournalHide(window);
        NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_HIDE);
        // ShowWindow's return reports prior visibility, not success — calling
        // Hide on an already-hidden window returns false benignly. Verify the
        // post-state instead: a window that is still visible after SW_HIDE is
        // a real (e.g. UIPI-blocked) failure.
        if (NativeMethods.IsWindowVisible(window.Hwnd))
            LogPositioningFailureOnce(window.Hwnd, "ShowWindow(SW_HIDE)");
        _log.Log($"SHEPHERD[hide] guest=0x{window.Hwnd.ToInt64():X}");
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
        if (!NativeMethods.IsWindow(window.Hwnd))
            return;

        PositionAndShow(window, containerHwnd, screenRect);
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
        bool fg = NativeMethods.SetForegroundWindow(window.Hwnd);
        if (!fg && NativeMethods.GetForegroundWindow() != window.Hwnd)
        {
            // Windows' focus-stealing guard can still reject this even though
            // the container just legitimately activated (the WM_ACTIVATE that
            // triggers this call). A benign key-up is the standard,
            // documented way to (re-)grant this process foreground-change
            // rights before retrying once.
            SendBenignKeyNudge();
            fg = NativeMethods.SetForegroundWindow(window.Hwnd);
        }
        _log.Log($"SHEPHERD[bring-to-front] guest=0x{window.Hwnd.ToInt64():X} fg={fg}");
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
    public void Release(CapturedWindow window, bool show = true)
    {
        if (!NativeMethods.IsWindow(window.Hwnd))
        {
            JournalClear(window.Hwnd);
            _log.Log($"Shepherd release: window 0x{window.Hwnd.ToInt64():X} already gone.");
            return;
        }

        if (!show)
        {
            // The guest hid itself (e.g. tray-style close). Do NOT journal it for
            // rescue: force-showing it on the next launch would undo the user's
            // intentional hide. Clear any existing journal entry instead — and
            // do it immediately (bypassing the debounce), BEFORE the window ends
            // up hidden: a stale "hidden" entry left on disk by a force-kill
            // mid-debounce would be indistinguishable from a real orphan and get
            // incorrectly un-hidden by RescueOrphanedWindows (unlike
            // PositionAndShow/Release(show:true)'s clears, where the window is
            // already genuinely visible and a stale entry is harmless).
            JournalClear(window.Hwnd, immediate: true);
            NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_HIDE);
            if (NativeMethods.IsWindowVisible(window.Hwnd))
                LogPositioningFailureOnce(window.Hwnd, "ShowWindow(SW_HIDE)");
            SetTransitionsDisabled(window.Hwnd, false);
            _log.Log($"Shepherd-released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) hidden (guest-initiated hide)");
            return;
        }

        if (!window.HasValidPlacement)
        {
            // Capture-time GetWindowPlacement failed, so OriginalPlacement is
            // zeroed — its showCmd (0 == SW_HIDE) would hide the released guest
            // forever with its journal entry already cleared. Restore the
            // capture-time bounds and show explicitly instead.
            if (!NativeMethods.SetWindowPos(
                window.Hwnd,
                IntPtr.Zero,
                window.OriginalBounds.left,
                window.OriginalBounds.top,
                window.OriginalBounds.Width,
                window.OriginalBounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW))
            {
                LogPositioningFailureOnce(window.Hwnd, "SetWindowPos(release-bounds)");
            }
            NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_SHOW);
            if (!NativeMethods.IsWindowVisible(window.Hwnd))
                LogPositioningFailureOnce(window.Hwnd, "ShowWindow(SW_SHOW)");
            if (!NativeMethods.SetForegroundWindow(window.Hwnd)
                && NativeMethods.GetForegroundWindow() != window.Hwnd)
            {
                LogPositioningFailureOnce(window.Hwnd, "SetForegroundWindow(release)");
            }
            JournalClear(window.Hwnd);
            SetTransitionsDisabled(window.Hwnd, false);
            _log.Log($"Shepherd-released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) via bounds fallback (no valid capture-time placement); guest={NativeMethods.DescribeWindow(window.Hwnd)}");
            return;
        }

        NativeMethods.WINDOWPLACEMENT placement = window.OriginalPlacement;
        placement.length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();

        if (!NativeMethods.SetWindowPlacement(window.Hwnd, ref placement))
        {
            _log.Log($"SetWindowPlacement failed for 0x{window.Hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
            if (!NativeMethods.SetWindowPos(
                window.Hwnd,
                IntPtr.Zero,
                window.OriginalBounds.left,
                window.OriginalBounds.top,
                window.OriginalBounds.Width,
                window.OriginalBounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW))
            {
                LogPositioningFailureOnce(window.Hwnd, "SetWindowPos(release-fallback)");
            }
        }

        NativeMethods.ShowWindow(window.Hwnd, (int)placement.showCmd);
        if (placement.showCmd != NativeMethods.SW_HIDE && !NativeMethods.IsWindowVisible(window.Hwnd))
            LogPositioningFailureOnce(window.Hwnd, "ShowWindow(release)");
        if (!NativeMethods.SetForegroundWindow(window.Hwnd)
            && NativeMethods.GetForegroundWindow() != window.Hwnd)
        {
            LogPositioningFailureOnce(window.Hwnd, "SetForegroundWindow(release)");
        }
        JournalClear(window.Hwnd);
        SetTransitionsDisabled(window.Hwnd, false);

        _log.Log($"Shepherd-released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) guest={NativeMethods.DescribeWindow(window.Hwnd)}");
    }

    #region Crash-recovery journal (docs/internal/deep-audit-2026-07-17.md, section 6.5)

    /// <summary>
    /// Journals a newly-hidden guest and writes it to disk immediately
    /// (NOT debounced). This is the one journal write that must land before
    /// an unpredictable future force-kill (Process.Kill()/Task Manager "End
    /// Task"/taskkill /F) — those bypass every App.xaml.cs handler outright
    /// (TerminateProcess allows no user-mode code to run afterward, so no
    /// FlushJournal() call anywhere could rescue a debounced write here).
    /// Still cheaper than the pre-AUDIT25-01 code: GetJournalCache() serves
    /// the in-memory copy instead of re-reading hidden-windows.json from disk
    /// on every call, so only the write half of the round trip remains.
    /// </summary>
    private void JournalHide(CapturedWindow window)
    {
        try
        {
            HiddenWindowJournalFile file = GetJournalCache();
            file.Entries.RemoveAll(e => e.Hwnd == window.Hwnd.ToInt64());
            file.Entries.Add(new HiddenWindowEntry
            {
                Hwnd = window.Hwnd.ToInt64(),
                Pid = window.ProcessId,
                ExePath = window.ExePath,
            });
            _journalDebounce?.Stop();
            SaveJournal(file);
        }
        catch (Exception ex)
        {
            _log.LogException("WindowShepherdService.JournalHide", ex);
        }
    }

    /// <summary>
    /// Clears a guest's journal entry and, by default, debounces the disk
    /// write (AUDIT25-01). Debouncing is safe ONLY when the guest ends up
    /// genuinely visible: RescueOrphanedWindows unconditionally re-shows every
    /// journaled entry regardless of current visibility, so a stale clear that
    /// a force-kill catches mid-debounce merely causes a harmless redundant
    /// ShowWindow on the next rescue.
    ///
    /// <paramref name="immediate"/> MUST be true for any call site where the
    /// guest ends up intentionally left hidden instead — e.g. Release's
    /// guest-initiated-hide (tray-style close) path. There, a stale on-disk
    /// "hidden" entry surviving a crash is indistinguishable from a real
    /// orphan awaiting rescue, so RescueOrphanedWindows would incorrectly
    /// un-hide a window the user deliberately hid. That is a real behavior
    /// change, not a harmless no-op, so it cannot be left to a debounce timer
    /// that a hard force-kill can catch mid-flight — same reasoning as
    /// JournalHide, just triggered from the clear side instead of the hide
    /// side.
    /// </summary>
    private void JournalClear(IntPtr hwnd, bool immediate = false)
    {
        try
        {
            HiddenWindowJournalFile file = GetJournalCache();
            // The journal is empty in the overwhelmingly common case (nothing is
            // hidden while a single-tab group is dragged around), and this runs
            // from PositionAndShow on every drag tick. Bail before RemoveAll so
            // that path allocates no predicate closure at all.
            if (file.Entries.Count == 0)
                return;

            int before = file.Entries.Count;
            file.Entries.RemoveAll(e => e.Hwnd == hwnd.ToInt64());
            if (file.Entries.Count == before)
                return;

            if (immediate)
            {
                _journalDebounce?.Stop();
                SaveJournal(file);
            }
            else
            {
                RequestJournalSave();
            }
        }
        catch (Exception ex)
        {
            _log.LogException("WindowShepherdService.JournalClear", ex);
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
    /// Debounced disk write (AUDIT25-01) used only by JournalClear: coalesces
    /// rapid clears (one per tab switch, as the newly-active tab's entry is
    /// removed) into a single write ~300ms after the last one, mirroring
    /// GroupManager.RequestSave. JournalHide never calls this — see its own
    /// doc comment for why the hide write must stay synchronous. Every
    /// designated App.xaml.cs exit/crash path also calls FlushJournal() so a
    /// pending clear lands promptly on a graceful exit rather than lingering
    /// (harmlessly) until the next rescue.
    /// </summary>
    private void RequestJournalSave()
    {
        if (_journalDebounce == null)
        {
            _journalDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _journalDebounce.Tick += (_, _) =>
            {
                _journalDebounce!.Stop();
                if (_journalCache != null)
                    SaveJournal(_journalCache);
            };
        }
        _journalDebounce.Stop();
        _journalDebounce.Start();
    }

    /// <summary>
    /// Stops any pending debounced write and saves the in-memory journal to disk
    /// immediately. Called from every App.xaml.cs exit/crash path so a pending
    /// clear is never lost to a debounce timer that never got to fire on a
    /// graceful exit or managed exception. Has nothing to do for a hard
    /// force-kill — JournalHide never leaves anything pending in the first
    /// place, by design. No-op if nothing has been journaled yet this session.
    /// </summary>
    public void FlushJournal()
    {
        try
        {
            _journalDebounce?.Stop();
            if (_journalCache != null)
                SaveJournal(_journalCache);
        }
        catch (Exception ex)
        {
            _log.LogException("WindowShepherdService.FlushJournal", ex);
        }
    }

    private static HiddenWindowJournalFile LoadJournal()
    {
        if (!File.Exists(JournalPath))
            return new HiddenWindowJournalFile();
        string json = File.ReadAllText(JournalPath);
        try
        {
            HiddenWindowJournalFile file = JsonSerializer.Deserialize(json, TabDockJsonContext.Default.HiddenWindowJournalFile)
                ?? new HiddenWindowJournalFile();
            // A syntactically valid journal with a null Entries array must not
            // wedge RescueOrphanedWindows into a permanent fail-and-retry loop.
            file.Entries ??= new List<HiddenWindowEntry>();
            return file;
        }
        catch (JsonException)
        {
            string corruptPath = GetUniqueJournalCorruptPath();
            File.Move(JournalPath, corruptPath);
            return new HiddenWindowJournalFile();
        }
    }

    private static string GetUniqueJournalCorruptPath()
    {
        string basePath = $"{JournalPath}.corrupt.{DateTime.Now:yyyyMMddHHmmssfff}";
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

    private static void SaveJournal(HiddenWindowJournalFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        string json = JsonSerializer.Serialize(file, TabDockJsonContext.Default.HiddenWindowJournalFile);
        string tempPath = JournalPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, JournalPath, overwrite: true);
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
    /// journal is unconditionally cleared afterward regardless of how many
    /// entries actually validated.
    /// </summary>
    public static void RescueOrphanedWindows(LoggingService log)
    {
        try
        {
            HiddenWindowJournalFile file = LoadJournal();
            if (file.Entries.Count == 0)
            {
                // An empty journal file (including one with a null Entries array
                // that LoadJournal normalized) must not be left behind to be
                // re-read on every launch.
                if (File.Exists(JournalPath))
                    File.Delete(JournalPath);
                return;
            }

            int rescued = 0;
            foreach (HiddenWindowEntry entry in file.Entries)
            {
                var hwnd = new IntPtr(entry.Hwnd);
                if (!NativeMethods.IsWindow(hwnd))
                    continue;

                NativeMethods.GetWindowThreadProcessId(hwnd, out uint currentPid);
                if (currentPid != entry.Pid)
                    continue;

                string? currentExe = NativeMethods.GetProcessImagePath(currentPid);
                if (!string.Equals(currentExe, entry.ExePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (NativeMethods.IsIconic(hwnd))
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                else
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                rescued++;
                log.Log($"SHEPHERD[rescue] restored hidden guest 0x{hwnd.ToInt64():X} (pid={entry.Pid}, exe={entry.ExePath}) after an unclean previous shutdown.");
            }

            if (File.Exists(JournalPath))
                File.Delete(JournalPath);
            if (rescued > 0)
                log.Log($"SHEPHERD[rescue] {rescued} previously-hidden window(s) restored.");
        }
        catch (Exception ex)
        {
            log.LogException("WindowShepherdService.RescueOrphanedWindows", ex);
        }
    }

    #endregion
}
