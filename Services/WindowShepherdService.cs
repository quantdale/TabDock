using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// TabDock's only capture backend (docs/internal/deep-audit-2026-07-17.md,
/// section 6). A shepherded guest remains an unmodified top-level window for
/// its entire captured lifetime: no SetParent, no style/ex-style mutation, no
/// DPI-message forwarding, no cross-thread input attachment. Instead, the
/// guest is positioned directly over the container's content area and brought
/// to the true top of the z-order (SetWindowPos with hwndInsertAfter =
/// HWND_TOP — passing the container itself here would place the guest
/// *behind* it, since hwndInsertAfter precedes hWnd in z-order), then the
/// container is immediately pinned right behind the guest so nothing else can
/// slot between them. Hidden with ShowWindow(SW_HIDE) when it is not the
/// active tab.
///
/// Because nothing about the guest is mutated, release is symmetric and
/// simple: restore the placement snapshotted at capture time. There is no
/// style/owner/parent surgery to get wrong, no permanently-downgraded DPI
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

    private static readonly string JournalPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabDock", "hidden-windows.json");

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

        if (NativeMethods.IsProcessElevated(pid, out bool targetElevated) && targetElevated)
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
        if (!NativeMethods.GetWindowPlacement(hwnd, out originalPlacement))
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
            OriginalBounds = bounds,
            WasMaximized = originalPlacement.showCmd == NativeMethods.SW_SHOWMAXIMIZED,
        };

        _log.Log($"Shepherd-captured 0x{hwnd.ToInt64():X} ({cw.OriginalTitle}) without reparenting; guest={NativeMethods.DescribeWindow(hwnd)}");
        return cw;
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
            NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_RESTORE);

        // SetWindowPos's hWndInsertAfter PRECEDES (sits above) hWnd in z-order,
        // so passing containerHwnd here would put the guest BEHIND its own
        // container. Bring the guest to the true top instead, then pin the
        // container immediately behind it so nothing else can slot between.
        NativeMethods.SetWindowPos(
            window.Hwnd,
            NativeMethods.HWND_TOP,
            screenRect.left,
            screenRect.top,
            screenRect.Width,
            screenRect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        NativeMethods.SetWindowPos(
            containerHwnd,
            window.Hwnd,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        JournalClear(window.Hwnd);
        _log.Log($"SHEPHERD[position] guest={NativeMethods.DescribeWindow(window.Hwnd)} rect={screenRect.left},{screenRect.top},{screenRect.Width}x{screenRect.Height}");
    }

    /// <summary>
    /// Hides an inactive shepherded guest. Safe to call on a window that is
    /// already hidden or has been destroyed. Journals the guest so a
    /// force-killed TabDock (which no longer destroys the guest — it was
    /// never reparented — but also can't run its normal release-on-exit path)
    /// doesn't leave it invisibly hidden forever; see
    /// <see cref="RescueOrphanedWindows"/>.
    /// </summary>
    public void Hide(CapturedWindow window)
    {
        if (!NativeMethods.IsWindow(window.Hwnd))
            return;
        NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_HIDE);
        JournalHide(window);
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
    /// nothing about the guest was mutated while docked (no style, no parent,
    /// no owner), this only needs to restore the placement snapshotted at
    /// capture — there is no style/owner/parent surgery to undo. When
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
            NativeMethods.ShowWindow(window.Hwnd, NativeMethods.SW_HIDE);
            JournalHide(window);
            _log.Log($"Shepherd-released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) hidden (guest-initiated hide)");
            return;
        }

        NativeMethods.WINDOWPLACEMENT placement = window.OriginalPlacement;
        placement.length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();

        if (!NativeMethods.SetWindowPlacement(window.Hwnd, ref placement))
        {
            _log.Log($"SetWindowPlacement failed for 0x{window.Hwnd.ToInt64():X}: {NativeMethods.FormatLastError()}");
            NativeMethods.SetWindowPos(
                window.Hwnd,
                NativeMethods.HWND_TOP,
                window.OriginalBounds.left,
                window.OriginalBounds.top,
                window.OriginalBounds.Width,
                window.OriginalBounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        }

        NativeMethods.ShowWindow(window.Hwnd, (int)placement.showCmd);
        NativeMethods.SetForegroundWindow(window.Hwnd);
        JournalClear(window.Hwnd);

        _log.Log($"Shepherd-released 0x{window.Hwnd.ToInt64():X} ({window.OriginalTitle}) guest={NativeMethods.DescribeWindow(window.Hwnd)}");
    }

    #region Crash-recovery journal (docs/internal/deep-audit-2026-07-17.md, section 6.5)

    private void JournalHide(CapturedWindow window)
    {
        try
        {
            HiddenWindowJournalFile file = LoadJournal();
            file.Entries.RemoveAll(e => e.Hwnd == window.Hwnd.ToInt64());
            file.Entries.Add(new HiddenWindowEntry
            {
                Hwnd = window.Hwnd.ToInt64(),
                Pid = window.ProcessId,
                ExePath = window.ExePath,
            });
            SaveJournal(file);
        }
        catch (Exception ex)
        {
            _log.LogException("WindowShepherdService.JournalHide", ex);
        }
    }

    private void JournalClear(IntPtr hwnd)
    {
        try
        {
            HiddenWindowJournalFile file = LoadJournal();
            int before = file.Entries.Count;
            file.Entries.RemoveAll(e => e.Hwnd == hwnd.ToInt64());
            if (file.Entries.Count != before)
                SaveJournal(file);
        }
        catch (Exception ex)
        {
            _log.LogException("WindowShepherdService.JournalClear", ex);
        }
    }

    private static HiddenWindowJournalFile LoadJournal()
    {
        if (!File.Exists(JournalPath))
            return new HiddenWindowJournalFile();
        string json = File.ReadAllText(JournalPath);
        return JsonSerializer.Deserialize(json, TabDockJsonContext.Default.HiddenWindowJournalFile)
            ?? new HiddenWindowJournalFile();
    }

    private static void SaveJournal(HiddenWindowJournalFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        string json = JsonSerializer.Serialize(file, TabDockJsonContext.Default.HiddenWindowJournalFile);
        File.WriteAllText(JournalPath, json);
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
                return;

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
