using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

/// <summary>Run options parsed from the command line.</summary>
internal sealed class Options
{
    public bool Yes;
    public int? Cycles;
    public string Guest = "pig";
}

/// <summary>A window under test: a guinea pig or a real app (wt/chrome) for maximize-repro.</summary>
internal sealed class GuestInfo
{
    public Process? Proc;
    public uint Pid;
    public IntPtr Hwnd;
    public string Title = string.Empty;
    public bool IsPig;
    /// <summary>For guests matched by file/title (e.g. Notepad), the token that must remain in the window title.</summary>
    public string? VerifyToken;
    /// <summary>For guests that create a temp file, the full path so cleanup can delete it.</summary>
    public string? VerifyFilePath;
}

/// <summary>Per-scenario state: the TabDock instance, spawned guests, containers, and assertion results.</summary>
internal sealed class Ctx
{
    public string Name = string.Empty;
    public Process TabDock = null!;
    public uint TabDockPid;
    public IntPtr MainHwnd;
    public long LogOffset;
    public readonly List<GuestInfo> Guests = new List<GuestInfo>();
    public readonly List<IntPtr> Containers = new List<IntPtr>();
    public bool Pass = true;

    public void Check(bool condition, string what)
    {
        GuardedProc.Log($"  {(condition ? "PASS" : "FAIL")}: {what}");
        Pass &= condition;
    }
}

internal static class Scenarios
{
    public const string TabDockExe = @"d:\Documents\tryPython\TabDock\bin\Debug\net8.0-windows\win-x64\TabDock.exe";
    public const string PigExe = @"d:\Documents\tryPython\TabDock\tests\ValidationDriver\TabDock.GuineaPig\bin\Debug\net8.0-windows\TabDock.GuineaPig.exe";
    private const string ChromeExe = "C:/Program Files/Google/Chrome/Application/chrome.exe";
    private const string ContentHostClass = "TabDockContentHost";

    private static readonly Random Rng = new Random();

    public static readonly string[] AllOrder =
    {
        "rename", "popout", "closewin", "closewin-hide", "selfclose", "selfhide", "selfminhide",
        "tabswitch-hidesafety", "minrestore", "maximize-repro", "repeat-cycles", "crossfeature",
        "renderhealth", "hotkey-afterclose", "persist-kill", "dragreorder",
        "contentinput", "chromeinput", "alttabinput",
        "keyboardinput", "keyboardinput-chrome", "keyboardinput-notepad", "keyboardinput-rapid-switch",
    };

    // -------------------------------------------------------------------------
    // Runner
    // -------------------------------------------------------------------------
    public static bool RunScenario(string name, Options opt)
    {
        Action<Ctx, Options>? body = name switch
        {
            "rename" => Rename,
            "popout" => PopOut,
            "closewin" => CloseWin,
            "closewin-hide" => CloseWinHide,
            "selfclose" => SelfClose,
            "selfhide" => SelfHide,
            "selfminhide" => SelfMinHide,
            "tabswitch-hidesafety" => TabSwitchHideSafety,
            "minrestore" => MinRestore,
            "maximize-repro" => MaximizeRepro,
            "repeat-cycles" => RepeatCycles,
            "crossfeature" => CrossFeature,
            "renderhealth" => RenderHealth,
            "hotkey-afterclose" => HotkeyAfterClose,
            "persist-kill" => PersistKill,
            "dragreorder" => DragReorder,
            "contentinput" => ContentInput,
            "chromeinput" => ChromeInput,
            "alttabinput" => AltTabInput,
            "keyboardinput" => KeyboardInput,
            "keyboardinput-chrome" => KeyboardInputChrome,
            "keyboardinput-notepad" => KeyboardInputNotepad,
            "keyboardinput-rapid-switch" => KeyboardInputRapidSwitch,
            _ => null,
        };
        if (body == null)
        {
            GuardedProc.Log($"Unknown scenario '{name}'. Known: {string.Join(", ", AllOrder)}");
            return false;
        }

        GuardedProc.Log($"=== SCENARIO {name} ===");
        Ctx? ctx = null;
        try
        {
            ctx = StartScenario(name);
            body(ctx, opt);
        }
        catch (OperationCanceledException)
        {
            if (ctx != null)
                ctx.Pass = false;
            GuardedProc.Log("  ABORTED: overall time budget exceeded or Ctrl+C.");
            throw;
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  ERROR: {ex.Message}");
            if (ctx != null)
                ctx.Pass = false;
        }
        finally
        {
            if (ctx != null)
                Cleanup(ctx);
            GuardedProc.Log($"SCENARIO {name}: {(ctx != null && ctx.Pass ? "PASS" : "FAIL")}");
        }
        return ctx != null && ctx.Pass;
    }

    // -------------------------------------------------------------------------
    // Common setup / teardown
    // -------------------------------------------------------------------------
    private static string StateJsonPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabDock", "state.json");

    /// <summary>Per-scenario snapshot of the user's state.json (null = file absent).</summary>
    private static string? s_savedStateJson;

    private static Ctx StartScenario(string name)
    {
        GuardedProc.ResetScenarioBudget();

        // Hermetic persisted state: snapshot the user's state.json and start this
        // scenario's TabDock with a clean slate. Restored empty containers from
        // groups accumulated by earlier sessions/runs otherwise cover the picker
        // and tab strip, so real-input clicks land on the wrong window. Cleanup
        // restores the snapshot after the scenario's TabDock has exited.
        try
        {
            s_savedStateJson = File.Exists(StateJsonPath) ? File.ReadAllText(StateJsonPath) : null;
            if (s_savedStateJson != null)
                File.Delete(StateJsonPath);
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  WARNING: could not snapshot/clear state.json: {ex.Message}");
            s_savedStateJson = null;
        }

        Process[] strays = Process.GetProcessesByName("TabDock");
        if (strays.Length > 0)
        {
            throw new InvalidOperationException(
                $"PREFLIGHT: a TabDock process is already running (PID {string.Join(", ", strays.Select(p => p.Id))}) " +
                "that this driver did not spawn. Close it and re-run — the driver requires a fresh instance.");
        }

        var ctx = new Ctx { Name = name };
        ctx.TabDock = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
        });
        ctx.TabDockPid = (uint)ctx.TabDock.Id;

        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        if (ctx.MainHwnd == IntPtr.Zero)
            throw new InvalidOperationException("TabDock MainWindow did not appear within 20s.");

        Thread.Sleep(1000); // settle
        ctx.LogOffset = TabDockLog.RecordLogLength();
        GuardedProc.Log($"  TabDock PID {ctx.TabDockPid}, MainWindow 0x{ctx.MainHwnd.ToInt64():X}.");
        return ctx;
    }

    private static void Cleanup(Ctx ctx)
    {
        GuardedProc.Log("  Cleanup: begin.");
        try
        {
            // 1) Kill tracked guests first so containers empty out and close without prompting.
            foreach (GuestInfo g in ctx.Guests)
            {
                try
                {
                    if (g.Proc != null && !g.Proc.HasExited)
                    {
                        if (!VerifyGuestForKill(g))
                        {
                            GuardedProc.Log($"  Cleanup: REFUSING to kill guest PID {g.Proc.Id} ('{g.Title}') — verification failed.");
                            continue;
                        }
                        GuardedProc.Log($"  Cleanup: killing guest PID {g.Proc.Id} ('{g.Title}').");
                        g.Proc.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    GuardedProc.Log($"  Cleanup: guest kill failed: {ex.Message}");
                }
                finally
                {
                    if (!string.IsNullOrEmpty(g.VerifyFilePath))
                    {
                        try
                        {
                            if (File.Exists(g.VerifyFilePath))
                                File.Delete(g.VerifyFilePath);
                        }
                        catch (Exception ex)
                        {
                            GuardedProc.Log($"  Cleanup: could not delete temp file '{g.VerifyFilePath}': {ex.Message}");
                        }
                    }
                }
            }

            if (ctx.TabDock != null && !ctx.TabDock.HasExited)
            {
                Thread.Sleep(500);

                // 2) Graceful close: containers, then the main window.
                var toClose = new HashSet<IntPtr>(ctx.Containers.Where(NativeMethods.IsWindow));
                foreach (IntPtr h in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true))
                {
                    string t = NativeMethods.GetWindowTextString(h) ?? string.Empty;
                    if (h != ctx.MainHwnd &&
                        (t.StartsWith("Group", StringComparison.Ordinal) || t.StartsWith("TDVAL-", StringComparison.Ordinal)))
                    {
                        toClose.Add(h);
                    }
                }
                foreach (IntPtr h in toClose)
                {
                    GuardedProc.Log($"  Cleanup: WM_CLOSE -> container 0x{h.ToInt64():X}.");
                    NativeMethods.PostMessage(h, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }

                Thread.Sleep(300);
                HandleCloseGroupMessageBox(ctx, 3000);

                if (NativeMethods.IsWindow(ctx.MainHwnd))
                {
                    GuardedProc.Log("  Cleanup: WM_CLOSE -> MainWindow.");
                    NativeMethods.PostMessage(ctx.MainHwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                HandleCloseGroupMessageBox(ctx, 2000);

                if (!ctx.TabDock.WaitForExit(5000))
                {
                    GuardedProc.Log("  Cleanup: !!! TabDock did NOT exit after WM_CLOSE — killing the tracked TabDock process as a last resort. !!!");
                }
            }
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  Cleanup: error: {ex.Message}");
        }
        finally
        {
            GuardedProc.CleanupTrackedProcesses();

            // Put the user's state.json back exactly as it was before the scenario
            // (after TabDock has exited, so its exit-save cannot overwrite it again).
            try
            {
                if (s_savedStateJson != null)
                    File.WriteAllText(StateJsonPath, s_savedStateJson);
                else if (File.Exists(StateJsonPath))
                    File.Delete(StateJsonPath);
            }
            catch (Exception ex)
            {
                GuardedProc.Log($"  WARNING: could not restore state.json: {ex.Message}");
            }

            GuardedProc.Log("  Cleanup: done.");
        }
    }

    /// <summary>
    /// If a Win32 "Close group" MessageBox (#32770 owned by the TabDock pid) is up,
    /// real-click its "No" button so shutdown does not hang on a modal prompt.
    /// </summary>
    private static void HandleCloseGroupMessageBox(Ctx ctx, int budgetMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < budgetMs)
        {
            IntPtr dlg = Discover.FindMessageBox(ctx.TabDockPid, "Close group");
            if (dlg == IntPtr.Zero)
                dlg = Discover.FindMessageBox(ctx.TabDockPid, null);
            if (dlg == IntPtr.Zero)
            {
                Thread.Sleep(200);
                continue;
            }

            string title = NativeMethods.GetWindowTextString(dlg) ?? string.Empty;
            GuardedProc.Log($"  Cleanup: MessageBox '{title}' detected (0x{dlg.ToInt64():X}); clicking 'No'.");
            IntPtr noBtn = Discover.FindChildWindowByText(dlg, new[] { "&No", "No" });
            if (noBtn != IntPtr.Zero)
            {
                Input.ForceForeground(dlg);
                NativeMethods.GetWindowRect(noBtn, out NativeMethods.RECT rc);
                Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
            }
            else
            {
                GuardedProc.Log("  Cleanup: 'No' button not found; sending WM_CLOSE to the dialog.");
                NativeMethods.PostMessage(dlg, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            Thread.Sleep(500);
        }
    }

    // -------------------------------------------------------------------------
    // Guest spawning + capture flow
    // -------------------------------------------------------------------------
    private static GuestInfo SpawnPig(Ctx ctx, string tag, params string[] extraFlags)
    {
        string title = $"TDVAL-{tag}-{Rng.Next(0x10000):X4}";
        string args = $"--title \"{title}\"" + (extraFlags.Length > 0 ? " " + string.Join(" ", extraFlags) : string.Empty);
        Process p = GuardedProc.SpawnGuarded(new ProcessStartInfo(PigExe, args) { UseShellExecute = false });
        var g = new GuestInfo { Proc = p, Pid = (uint)p.Id, Title = title, IsPig = true };
        g.Hwnd = Discover.WaitForTopLevelWindow(g.Pid, t => t == title, 15000);
        if (g.Hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"Pig window '{title}' did not appear within 15s.");
        ctx.Guests.Add(g);
        GuardedProc.Log($"  Pig '{title}' PID {g.Pid} HWND 0x{g.Hwnd.ToInt64():X}.");
        return g;
    }

    private static GuestInfo SpawnGuest(Ctx ctx, string kind)
    {
        switch (kind)
        {
            case "pig":
                return SpawnPig(ctx, "MAX", "--pulse", "--color", "white");
            case "wt":
                // Ported from the CaptureReleaseTest Windows Terminal scenario.
                // No ';' in the command: wt.exe treats unescaped ';' as its own
                // subcommand separator, which silently breaks the loop and leaves
                // a static terminal (variance 0). A sleepless Get-Date loop keeps
                // the content scrolling for the live-render variance check.
                return SpawnClassGuest(ctx, "wt.exe",
                    "powershell.exe -NoExit -Command \"while (1) { Get-Date }\"",
                    "CASCADIA_HOSTING_WINDOW_CLASS", useShellExecute: false);
            case "chrome-nogpu":
                // Ported from the CaptureReleaseTest Chrome scenario (live-content page: https://time.is).
                return SpawnClassGuest(ctx, ChromeExe,
                    $"--user-data-dir=\"{Path.Combine(Path.GetTempPath(), "TabDockChromeProfile")}\" --disable-gpu --app=https://time.is",
                    "Chrome_WidgetWin_1", useShellExecute: true);
            case "chrome-gpu":
                return SpawnClassGuest(ctx, ChromeExe,
                    $"--user-data-dir=\"{Path.Combine(Path.GetTempPath(), "TabDockChromeProfile")}\" --app=https://time.is",
                    "Chrome_WidgetWin_1", useShellExecute: true);
            default:
                throw new ArgumentException($"Unknown --guest kind '{kind}' (expected pig|wt|chrome-nogpu|chrome-gpu).");
        }
    }

    private static GuestInfo SpawnClassGuest(Ctx ctx, string exe, string args, string className, bool useShellExecute)
    {
        HashSet<IntPtr> existing = Discover.FindWindowsByClass(className);
        Process launcher = GuardedProc.SpawnGuarded(new ProcessStartInfo(exe, args) { UseShellExecute = useShellExecute });

        IntPtr hwnd = IntPtr.Zero;
        Util.WaitUntil(() => (hwnd = Discover.FindNewWindowByClass(className, existing)) != IntPtr.Zero, 20000, 150);
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"No new {className} window appeared for guest '{exe}'.");

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        Process owner = launcher;
        if (pid != 0 && pid != (uint)launcher.Id)
        {
            // wt.exe / chrome.exe launchers can hand the window to another process; track it for cleanup.
            try
            {
                owner = Process.GetProcessById((int)pid);
                GuardedProc.Track(owner);
            }
            catch (Exception ex)
            {
                GuardedProc.Log($"  WARNING: could not open window owner PID {pid}: {ex.Message}");
            }
        }

        Thread.Sleep(2000); // let it render initial content
        var g = new GuestInfo
        {
            Proc = owner,
            Pid = pid,
            Hwnd = hwnd,
            Title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty,
            IsPig = false,
        };
        if (string.IsNullOrEmpty(g.Title))
            throw new InvalidOperationException("Guest window has no title; cannot match a picker row safely.");
        ctx.Guests.Add(g);
        GuardedProc.Log($"  Guest '{g.Title}' PID {g.Pid} HWND 0x{g.Hwnd.ToInt64():X}.");
        return g;
    }

    /// <summary>
    /// Spawns Notepad on a unique temp file and verifies the window that opens for that
    /// file by its title. Windows 11 Notepad is single-instance and may open the file in
    /// an existing user process; if that happens, the scenario still proceeds against the
    /// verified window, but cleanup kills only the launcher process we spawned — never an
    /// existing user Notepad process.
    /// </summary>
    private static GuestInfo SpawnNotepad(Ctx ctx)
    {
        string tempFile = Path.GetTempFileName();
        string fileName = Path.GetFileName(tempFile);
        string args = $"\"{tempFile}\"";

        Process launcher = GuardedProc.SpawnGuarded(new ProcessStartInfo("notepad.exe", args) { UseShellExecute = true });

        IntPtr hwnd = IntPtr.Zero;
        bool found = Util.WaitUntil(() =>
        {
            // Search all top-level Notepad windows, including existing ones, because
            // Windows 11 may open the temp file as a new tab in an already-running
            // Notepad instance rather than creating a new process/window.
            foreach (IntPtr h in Discover.FindWindowsByClass("Notepad"))
            {
                string title = NativeMethods.GetWindowTextString(h) ?? string.Empty;
                if (title.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hwnd = h;
                    return true;
                }
            }
            return false;
        }, 20000, 150);

        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"No Notepad window for file '{fileName}' appeared; aborting to avoid capturing an unrelated Notepad.");
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        bool isOurProcess = pid == (uint)launcher.Id;
        if (!isOurProcess)
        {
            GuardedProc.Log($"  WARNING: Notepad reused existing process PID {pid} for file '{fileName}'; cleanup will kill only the launcher PID {launcher.Id}.");
        }

        Thread.Sleep(1000);
        var g = new GuestInfo
        {
            // Always attach the launcher process for cleanup; it is the only PID this
            // scenario spawned. If Notepad reused an existing process, that process is
            // intentionally not tracked so it survives cleanup.
            Proc = launcher,
            Pid = pid,
            Hwnd = hwnd,
            Title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty,
            IsPig = false,
            VerifyToken = fileName,
            VerifyFilePath = tempFile,
        };
        ctx.Guests.Add(g);
        GuardedProc.Log($"  Notepad guest '{g.Title}' PID {g.Pid} HWND 0x{g.Hwnd.ToInt64():X} file='{fileName}' isOurProcess={isOurProcess}.");
        return g;
    }

    /// <summary>
    /// Opens the capture picker with the real Ctrl+Alt+G hotkey, real-clicks the row for each
    /// guest (aborting if a row is missing or ambiguous), real-clicks "Group these", and waits
    /// for the newly created container (EnumWindows diff so pre-existing/restored containers
    /// are never confused with the new one).
    /// </summary>
    private static (IntPtr Container, IntPtr Host) CaptureIntoGroup(Ctx ctx, params GuestInfo[] guests)
    {
        var before = new HashSet<IntPtr>(Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true));

        // Foreground handling is arranging (not validating); the hotkey is real input.
        Input.ForceForeground(ctx.MainHwnd);
        Thread.Sleep(400);
        Input.SendHotkeyCtrlAltG();

        // Find the picker via Win32 first: the managed UIA client's desktop-children
        // snapshot can be stale for freshly created windows (observed: the picker was
        // visible per EnumWindows but absent from RootElement children), so bridge
        // into UIA from the HWND instead of searching the desktop tree.
        IntPtr pickerHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 10000);
        if (pickerHwnd == IntPtr.Zero)
            throw new InvalidOperationException("'Capture windows' picker did not appear within 10s.");
        AutomationElement? picker = Uia.FromHwnd(pickerHwnd);
        if (picker == null)
            throw new InvalidOperationException("Picker HWND found but UIA FromHandle failed.");
        // The picker can open BEHIND the terminal/IDE that spawned this driver
        // (no foreground-activation rights). Never click blind: real clicks at
        // picker coordinates would land in whatever covers it.
        if (!Input.ForceForeground(pickerHwnd))
            throw new InvalidOperationException("Could not bring the capture picker to the foreground — refusing to click blind.");
        Thread.Sleep(600); // let the list populate
        Thread.Sleep(1000); // extra settle for multi-guest rows before clicking

        foreach (GuestInfo g in guests)
        {
            // The picker enumerates windows ONCE when it opens (Refresh() in its
            // constructor). A guinea-pig spawned moments earlier can miss that
            // one-shot enumeration, so if the row is absent we real-click the
            // picker's "Refresh" button to re-run EnumWindows, then re-search.
            // The WPF list virtualizes off-screen rows, so a window appended below
            // the fold has no realized UIA peer. Real-scroll the list downward to
            // realize rows, and click Refresh to re-enumerate if a young window
            // missed the picker's one-shot enumeration.
            AutomationElement? row = null;
            var rowSw = System.Diagnostics.Stopwatch.StartNew();
            InvalidOperationException? lastMiss = null;
            int scrolls = 0;
            while (row == null && rowSw.ElapsedMilliseconds < 12000)
            {
                try { row = FindPickerRow(picker, g.Title); }
                catch (InvalidOperationException ex)
                {
                    lastMiss = ex;
                    AutomationElement? list = Uia.FindFirstOfType(picker, ControlType.List);
                    if (list != null && scrolls < 8)
                    {
                        Rect lr = Uia.GetElementRect(list);
                        Input.ScrollWheel((int)(lr.X + lr.Width / 2), (int)(lr.Y + lr.Height / 2), -2);
                        scrolls++;
                    }
                    else
                    {
                        // Exhausted scrolling: re-enumerate via Refresh, reset scroll.
                        AutomationElement? refreshBtn = Uia.FindDescendantByName(picker, ControlType.Button, "Refresh", null, out int rc);
                        if (refreshBtn != null && rc == 1)
                        {
                            (int fx, int fy) = Uia.Center(refreshBtn);
                            Input.ClickAt(fx, fy);
                        }
                        scrolls = 0;
                    }
                    Thread.Sleep(300);
                }
            }
            if (row == null)
                throw lastMiss ?? new InvalidOperationException($"Picker row for '{g.Title}' not found.");

            // Real-click the checkbox and verify it toggled on; the CheckBox's
            // CanExecute gate on "Group these" depends on it. Use the row center
            // (the whole WPF CheckBox content is clickable) rather than the glyph
            // edge, which can miss on high-DPI or differently-templated rows.
            GuardedProc.Log($"  CaptureIntoGroup: toggling row for '{g.Title}' (controlType={row.Current.ControlType.ProgrammaticName}, rect={Uia.GetElementRect(row)}).");

            // Find the inner Text label so we can click on the CheckBox content
            // itself. Clicking directly on the text reliably toggles the parent
            // CheckBox; clicking the stretched CheckBox rect can land on ListBoxItem
            // padding or other non-toggleable space.
            AutomationElement? textEl = Uia.FindDescendantByName(picker, ControlType.Text, null, g.Title, out int textCount);
            if (textEl == null || textCount != 1)
                throw new InvalidOperationException($"Picker text label for '{g.Title}' not found uniquely (count={textCount}) — cannot toggle safely.");

            bool toggledOn = false;
            for (int attempt = 0; attempt < 3 && !toggledOn; attempt++)
            {
                // Vary the click point: start on the text label, then try the
                // CheckBox glyph area (left edge), then the CheckBox center.
                Rect r = Uia.GetElementRect(row);
                (int cx, int cy) = attempt switch
                {
                    0 => Uia.Center(textEl),
                    1 => ((int)(r.X + 5), (int)(r.Y + r.Height / 2)),
                    _ => Uia.Center(row),
                };
                GuardedProc.Log($"  CaptureIntoGroup: click attempt {attempt + 1} at ({cx},{cy}).");
                Input.ClickAt(cx, cy);
                Thread.Sleep(350);
                var ts = Uia.GetToggleState(row);
                GuardedProc.Log($"  CaptureIntoGroup: toggle state after attempt {attempt + 1} = {ts?.ToString() ?? "<null>"}.");
                toggledOn = ts == System.Windows.Automation.ToggleState.On;
            }
            if (!toggledOn)
            {
                // Fallback: programmatically toggle via UIA. Real-mouse clicks can
                // miss on high-DPI or differently-templated rows, but the toggle
                // pattern targets the element exactly and lets the scenario proceed.
                try
                {
                    if (row.TryGetCurrentPattern(TogglePattern.Pattern, out object pattern))
                    {
                        ((TogglePattern)pattern).Toggle();
                        Thread.Sleep(200);
                        var ts = Uia.GetToggleState(row);
                        GuardedProc.Log($"  CaptureIntoGroup: toggle pattern fallback state = {ts?.ToString() ?? "<null>"}.");
                        toggledOn = ts == System.Windows.Automation.ToggleState.On;
                    }
                }
                catch (Exception ex)
                {
                    GuardedProc.Log($"  CaptureIntoGroup: toggle pattern fallback threw: {ex.Message}");
                }
            }
            if (!toggledOn)
                throw new InvalidOperationException($"Picker row for '{g.Title}' did not toggle on after real clicks or toggle pattern fallback.");
            Thread.Sleep(200);
        }

        AutomationElement? groupBtn = Uia.FindDescendantByName(picker, ControlType.Button, "Group these", null, out int btnCount);
        if (groupBtn == null || btnCount != 1)
            throw new InvalidOperationException($"'Group these' button not found uniquely (count={btnCount}).");
        (int bx, int by) = Uia.Center(groupBtn);
        IntPtr wfp = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = bx, y = by });
        IntPtr wfpRoot = NativeMethods.GetAncestor(wfp, NativeMethods.GA_ROOT);
        GuardedProc.Log($"  Clicking 'Group these' at ({bx},{by}); windowFromPoint root=0x{wfpRoot.ToInt64():X} picker=0x{pickerHwnd.ToInt64():X} fg=0x{NativeMethods.GetForegroundWindow().ToInt64():X}.");
        Input.ClickAt(bx, by);
        if (!Util.WaitUntil(() => !NativeMethods.IsWindow(pickerHwnd), 3000))
            GuardedProc.Log("  WARNING: picker still open 3s after 'Group these' click.");

        IntPtr container = IntPtr.Zero;
        Util.WaitUntil(() =>
        {
            foreach (IntPtr h in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true))
            {
                if (before.Contains(h))
                    continue;
                string t = NativeMethods.GetWindowTextString(h) ?? string.Empty;
                if (t.StartsWith("Group", StringComparison.Ordinal))
                {
                    container = h;
                    return true;
                }
            }
            return false;
        }, 10000, 150);
        if (container == IntPtr.Zero)
            throw new InvalidOperationException("New container window did not appear within 10s.");

        IntPtr host = IntPtr.Zero;
        Util.WaitUntil(() => (host = Discover.FindChildByClass(container, ContentHostClass)) != IntPtr.Zero, 5000, 150);
        if (host == IntPtr.Zero)
            throw new InvalidOperationException($"{ContentHostClass} child not found in container.");

        foreach (GuestInfo g in guests)
        {
            bool captured = Util.WaitUntil(
                () => ((long)NativeMethods.GetWindowLongPtr(g.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0,
                5000);
            if (!captured)
                throw new InvalidOperationException($"Guest '{g.Title}' was not captured (WS_CHILD never set).");
        }

        Thread.Sleep(800); // settle
        ctx.Containers.Add(container);
        GuardedProc.Log($"  Captured {guests.Length} guest(s) into container 0x{container.ToInt64():X} (host 0x{host.ToInt64():X}).");
        return (container, host);
    }

    /// <summary>
    /// Returns the CheckBox row for a guest, matched via its inner Text label (the
    /// CheckBox's own UIA Name is empty for image+text content). Refuses to return
    /// an ambiguous match so an unverified row is never clicked.
    /// </summary>
    private static AutomationElement FindPickerRow(AutomationElement picker, string title)
    {
        // Direct CheckBox-name match first (in case a future template sets it).
        AutomationElement? el = Uia.FindDescendantByName(picker, ControlType.CheckBox, null, title, out int count);
        if (el != null && count == 1)
            return el;
        if (count > 1)
            throw new InvalidOperationException($"Picker row for '{title}' is ambiguous ({count} CheckBox matches) — refusing to click an unverified row.");

        // Fall back to the inner Text label, then walk up to its ancestor CheckBox.
        AutomationElement? text = Uia.FindDescendantByName(picker, ControlType.Text, null, title, out count);
        if (text == null || count != 1)
            throw new InvalidOperationException($"Picker row for '{title}' not found or ambiguous ({count} Text matches) — refusing to click an unverified row.");

        AutomationElement? box = Uia.NearestAncestorOfType(text, ControlType.CheckBox);
        if (box == null)
            throw new InvalidOperationException($"Picker row for '{title}' found as Text but no ancestor CheckBox — refusing to click.");
        return box;
    }

    // -------------------------------------------------------------------------
    // Tab / container UI helpers (UIA read + real mouse only)
    // -------------------------------------------------------------------------
    private static AutomationElement? GetTabList(IntPtr container)
    {
        AutomationElement? root = Uia.FromHwnd(container);
        return root == null ? null : Uia.FindFirstOfType(root, ControlType.List);
    }

    /// <summary>Tab count of a container; -1 when the container/list is gone.</summary>
    private static int TabCount(IntPtr container)
    {
        AutomationElement? list = GetTabList(container);
        return list == null ? -1 : Uia.CountChildrenOfType(list, ControlType.ListItem);
    }

    private static AutomationElement? FindTabText(IntPtr container, string guestTitle, out int count)
    {
        count = 0;
        AutomationElement? list = GetTabList(container);
        if (list == null)
            return null;
        return Uia.FindDescendantByName(list, ControlType.Text, null, guestTitle, out count);
    }

    /// <summary>Right-clicks a tab (by guest title) and real-clicks the named context-menu item.</summary>
    private static void ClickTabMenuItem(Ctx ctx, IntPtr container, string guestTitle, string menuItemName)
    {
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        AutomationElement? tab = FindTabText(container, guestTitle, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab for '{guestTitle}' not found uniquely (count={count}).");

        (int tx, int ty) = Uia.Center(tab);
        Input.RightClickAt(tx, ty);

        AutomationElement? mi = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, menuItemName, 5000);
        if (mi == null)
            throw new InvalidOperationException($"Context menu item '{menuItemName}' did not appear within 5s.");
        Thread.Sleep(150);
        (int mx, int my) = Uia.Center(mi);
        Input.ClickAt(mx, my);
        Thread.Sleep(300);
    }

    /// <summary>Real-clicks the container's maximize caption button (2nd of 46px-wide buttons from the right, DPI-scaled).</summary>
    private static void ClickMaximizeButton(IntPtr container)
    {
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        double scale = NativeMethods.GetDpiForWindow(container) / 96.0;
        int x = rc.right - (int)(1.5 * 46 * scale);
        int y = rc.top + (int)(16 * scale);
        GuardedProc.Log($"  Clicking maximize button at ({x},{y}) (container {Util.FormatRect(rc)}, dpiScale {scale:F2}).");
        Input.ClickAt(x, y);
    }

    /// <summary>Two host frames 1.5s apart: brightness of the second, avg inter-frame diff (variance).</summary>
    private static (double Brightness, double Variance) SampleHost(IntPtr host)
    {
        // Pixel sampling reads the screen; the container must actually be on top.
        Input.ForceForegroundRoot(host);
        int[]? f0 = Pixels.CaptureHostScreenArea(host);
        Thread.Sleep(1500);
        int[]? f1 = Pixels.CaptureHostScreenArea(host);
        if (f0 == null || f1 == null)
            return (-1, -1);
        return (Pixels.ComputeAvgBrightness(f1), Pixels.ComputeAvgFrameDiff(f0, f1));
    }

    private static bool GuestMatchesHost(IntPtr guest, IntPtr host, out string description)
    {
        NativeMethods.GetWindowRect(guest, out NativeMethods.RECT rcG);
        NativeMethods.RECT rcH = Discover.GetClientScreenRect(host);
        description = $"guest={Util.FormatRect(rcG)} hostClient={Util.FormatRect(rcH)}";
        return Util.RectNear(rcG, rcH, 4);
    }

    private static void DumpGeometry(Ctx ctx, IntPtr container, IntPtr host, GuestInfo guest, string phase)
    {
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rcC);
        NativeMethods.RECT rcH = Discover.GetClientScreenRect(host);
        NativeMethods.GetWindowRect(guest.Hwnd, out NativeMethods.RECT rcG);
        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(
            NativeMethods.MonitorFromWindow(container, NativeMethods.MONITOR_DEFAULTTONEAREST), ref mi);
        GuardedProc.Log(
            $"  GEOMETRY[{phase}] container={Util.FormatRect(rcC)} hostClient={Util.FormatRect(rcH)} " +
            $"guest={Util.FormatRect(rcG)} monitorWork={Util.FormatRect(mi.rcWork)} zoomed={NativeMethods.IsZoomed(container)}");
    }

    private static bool IsReleased(GuestInfo g)
    {
        return ((long)NativeMethods.GetWindowLongPtr(g.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) == 0
            && NativeMethods.GetParent(g.Hwnd) == IntPtr.Zero;
    }

    /// <summary>
    /// Verifies a guest process is still the one this scenario spawned before cleanup
    /// kills it. For Notepad, the window title must still contain the unique temp
    /// filename and the process name must be Notepad. For pigs, the process name must
    /// match the GuineaPig executable.
    /// </summary>
    private static bool VerifyGuestForKill(GuestInfo g)
    {
        try
        {
            if (g.Proc == null || g.Proc.HasExited)
                return false;

            string processName;
            try { processName = g.Proc.ProcessName; }
            catch { processName = string.Empty; }

            if (g.IsPig)
            {
                if (!processName.Equals("TabDock.GuineaPig", StringComparison.OrdinalIgnoreCase))
                {
                    GuardedProc.Log($"  VerifyGuestForKill: refusing pig PID {g.Proc.Id} — process name is '{processName}'.");
                    return false;
                }
                return true;
            }

            if (!string.IsNullOrEmpty(g.VerifyToken))
            {
                if (!processName.Equals("Notepad", StringComparison.OrdinalIgnoreCase))
                {
                    GuardedProc.Log($"  VerifyGuestForKill: refusing Notepad PID {g.Proc.Id} — process name is '{processName}'.");
                    return false;
                }
                string? currentTitle = NativeMethods.IsWindow(g.Hwnd)
                    ? NativeMethods.GetWindowTextString(g.Hwnd)
                    : null;
                if (currentTitle == null ||
                    !currentTitle.Contains(g.VerifyToken, StringComparison.OrdinalIgnoreCase))
                {
                    GuardedProc.Log($"  VerifyGuestForKill: refusing Notepad PID {g.Proc.Id} — title '{currentTitle ?? "<null>"}' does not contain '{g.VerifyToken}'.");
                    return false;
                }
                return true;
            }

            // Chrome/WT and other SpawnClassGuest guests: spawn-time verification is the
            // guard; cleanup relies on the tracked Process object not having been replaced.
            return true;
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  VerifyGuestForKill: exception for PID {g.Proc?.Id}: {ex.Message}");
            return false;
        }
    }

    private static bool StateJsonContains(string substring)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabDock", "state.json");
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            return sr.ReadToEnd().IndexOf(substring, StringComparison.Ordinal) >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Shared assertions for "guest hid itself on close → TabDock releases it hidden and drops the tab".</summary>
    private static void AssertHiddenRelease(Ctx ctx, GuestInfo pig, IntPtr container, long logOffset)
    {
        ctx.Check(PigLog.WaitForPigLine(pig.Pid, "WM_CLOSE", 5000), "pig log contains WM_CLOSE");
        Thread.Sleep(3000);
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process still alive after 3s (hide, not exit)");
        ctx.Check(TabDockLog.WaitForLogLine(logOffset, "hid itself (tray-style close)", 5000),
            "TabDock log gained 'hid itself (tray-style close)'");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 5000),
            "tab removed (container empty or closed)");
        ctx.Check(!NativeMethods.IsWindowVisible(pig.Hwnd), "pig window is hidden (IsWindowVisible == false)");
        ctx.Check(((long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) == 0,
            "WS_CHILD cleared (released, hidden)");
        ctx.Check(NativeMethods.GetParent(pig.Hwnd) == IntPtr.Zero, "GetParent(pig) == 0");
    }

    // -------------------------------------------------------------------------
    // 1. rename
    // -------------------------------------------------------------------------
    private static void Rename(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "REN", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        AutomationElement containerEl = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int count);
        ctx.Check(caption != null && count == 1, $"caption title TextBlock 'Group' found uniquely (count={count})");
        if (caption == null || count != 1)
            return;

        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rcBefore);
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        (int cx, int cy) = Uia.Center(caption);
        Input.DoubleClickAt(cx, cy);
        Thread.Sleep(300);
        Input.TypeText("TDVAL-Renamed");
        Input.SendKey(Input.VK_RETURN);

        ctx.Check(!NativeMethods.IsZoomed(container), "double-click did not maximize the container");
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rcAfter);
        ctx.Check(Util.RectNear(rcBefore, rcAfter, 0),
            $"container rect unchanged (before {Util.FormatRect(rcBefore)}, after {Util.FormatRect(rcAfter)})");
        ctx.Check(Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-Renamed", 2000),
            "container window text became 'TDVAL-Renamed' within 2s");
        ctx.Check(Util.WaitUntil(() => StateJsonContains("TDVAL-Renamed"), 3000),
            "state.json contains 'TDVAL-Renamed' without exiting TabDock");
    }

    // -------------------------------------------------------------------------
    // 2. popout
    // -------------------------------------------------------------------------
    private static void PopOut(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "POP", "--color", "green");
        IntPtr parentBefore = NativeMethods.GetParent(pig.Hwnd);
        long styleBefore = (long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_STYLE);
        GuardedProc.Log($"  Pre-capture: parent=0x{parentBefore.ToInt64():X} style=0x{styleBefore:X}.");

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        ClickTabMenuItem(ctx, container, pig.Title, "Pop out");

        ctx.Check(Util.WaitUntil(
                () => NativeMethods.GetParent(pig.Hwnd) == parentBefore
                    && ((long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) == 0,
                3000),
            "pig released within 3s (parent restored, WS_CHILD cleared)");
        long styleAfter = (long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_STYLE);
        ctx.Check((styleAfter & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION,
            $"WS_CAPTION restored (style=0x{styleAfter:X})");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process alive");
        ctx.Check(!PigLog.ContainsLine(pig.Pid, "WM_CLOSE"), "pig log has NO WM_CLOSE");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 3000),
            "tab gone (container empty or closed)");
    }

    // -------------------------------------------------------------------------
    // 3. closewin
    // -------------------------------------------------------------------------
    private static void CloseWin(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "CW", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pig.Title, "Close window");

        // Poll every 100ms until the HWND dies: the pig must never flash as a visible top-level window.
        bool becameTopLevelVisible = false;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000 && NativeMethods.IsWindow(pig.Hwnd))
        {
            long style = (long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_STYLE);
            if ((style & NativeMethods.WS_CHILD) == 0
                && NativeMethods.GetParent(pig.Hwnd) == IntPtr.Zero
                && NativeMethods.IsWindowVisible(pig.Hwnd))
            {
                becameTopLevelVisible = true;
            }
            Thread.Sleep(100);
        }
        ctx.Check(!becameTopLevelVisible, "pig NEVER became a visible top-level window while closing (stayed a child)");

        ctx.Check(PigLog.WaitForPigLine(pig.Pid, "WM_CLOSE", 3000), "pig log contains WM_CLOSE");
        ctx.Check(Util.WaitUntil(() => pig.Proc!.HasExited, 5000), "pig process exited within 5s");
        // A closing window may hide-then-destroy (default WinForms sequence) or
        // destroy directly; both drive the same teardown. Accept either.
        ctx.Check(Util.WaitUntil(() => TabDockLog.ContainsNewLine(off, "destroyed; removing its tab")
                || TabDockLog.ContainsNewLine(off, "hid itself"), 5000),
            "TabDock log shows the tab was torn down (destroy or hide path)");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 5000),
            "container closed (it was the only tab)");
    }

    // -------------------------------------------------------------------------
    // 4. closewin-hide
    // -------------------------------------------------------------------------
    private static void CloseWinHide(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "CWH", "--hide-on-close", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pig.Title, "Close window");
        AssertHiddenRelease(ctx, pig, container, off);
    }

    // -------------------------------------------------------------------------
    // 5. selfclose
    // -------------------------------------------------------------------------
    private static void SelfClose(Ctx ctx, Options opt)
    {
        // 7s (not 4s) so the timer cannot fire while the real-input capture flow (~5s) is still running.
        GuestInfo pig = SpawnPig(ctx, "SC", "--self-close-after", "7", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        long off = TabDockLog.RecordLogLength();

        Thread.Sleep(8000);

        ctx.Check(TabDockLog.ContainsNewLine(off, "destroyed; removing its tab")
                || TabDockLog.ContainsNewLine(off, "hid itself"),
            "TabDock log shows the tab was torn down (destroy or hide path)");
        ctx.Check(pig.Proc != null && pig.Proc.HasExited, "pig process exited by itself");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 3000),
            "container closed (last tab)");
        // Note: a persisted group restored from the user's state.json opens its own
        // empty "Group" container at startup, so we verify THIS scenario's container
        // is gone (above) rather than asserting no "Group" window exists at all.
    }

    // -------------------------------------------------------------------------
    // 6. selfhide
    // -------------------------------------------------------------------------
    private static void SelfHide(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "SH", "--hide-on-close", "--close-button", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        long off = TabDockLog.RecordLogLength();
        AutomationElement pigEl = Uia.FromHwnd(pig.Hwnd)
            ?? throw new InvalidOperationException("Pig UIA element unavailable.");
        AutomationElement? closeBtn = Uia.FindDescendantByName(pigEl, ControlType.Button, "X-CLOSE", null, out int count);
        if (closeBtn == null || count != 1)
            throw new InvalidOperationException($"X-CLOSE button not found uniquely in pig (count={count}).");

        if (!Input.ForceForegroundRoot(pig.Hwnd))
            throw new InvalidOperationException("Could not bring the captured pig to the foreground — refusing to click blind.");
        (int bx, int by) = Uia.Center(closeBtn);
        Input.ClickAt(bx, by);
        AssertHiddenRelease(ctx, pig, container, off);
    }

    // -------------------------------------------------------------------------
    // 7. selfminhide
    // -------------------------------------------------------------------------
    private static void SelfMinHide(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "SMH", "--minimize-then-hide-on-close", "--close-button", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        long off = TabDockLog.RecordLogLength();
        AutomationElement pigEl = Uia.FromHwnd(pig.Hwnd)
            ?? throw new InvalidOperationException("Pig UIA element unavailable.");
        AutomationElement? closeBtn = Uia.FindDescendantByName(pigEl, ControlType.Button, "X-CLOSE", null, out int count);
        if (closeBtn == null || count != 1)
            throw new InvalidOperationException($"X-CLOSE button not found uniquely in pig (count={count}).");

        if (!Input.ForceForegroundRoot(pig.Hwnd))
            throw new InvalidOperationException("Could not bring the captured pig to the foreground — refusing to click blind.");
        (int bx, int by) = Uia.Center(closeBtn);
        Input.ClickAt(bx, by);

        ctx.Check(TabDockLog.WaitForLogLine(off, "hid itself", 6000), "TabDock log gained 'hid itself'");
        Thread.Sleep(2500); // give any restore loop time to manifest
        int restores = TabDockLog.CountNewLines(off, "minimized; restoring");
        ctx.Check(restores <= 1, $"no restore loop (got {restores} 'minimized; restoring' line(s), max 1 allowed)");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 5000),
            "tab removed (container empty or closed)");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig process alive");
        ctx.Check(!NativeMethods.IsWindowVisible(pig.Hwnd), "pig hidden");
        ctx.Check(IsReleased(pig), "pig released (WS_CHILD cleared, no parent)");
    }

    // -------------------------------------------------------------------------
    // 8. tabswitch-hidesafety (CRITICAL)
    // -------------------------------------------------------------------------
    private static void TabSwitchHideSafety(Ctx ctx, Options opt)
    {
        GuestInfo[] pigs =
        {
            SpawnPig(ctx, "RED", "--color", "red"),
            SpawnPig(ctx, "BLUE", "--color", "blue"),
            SpawnPig(ctx, "GREEN", "--color", "green"),
        };
        char[] channelByIdx = { 'r', 'b', 'g' };
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigs);

        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        bool everyClickOk = true;
        int lastIdx = -1;
        for (int i = 0; i < 24; i++)
        {
            int idx = i % 3;
            AutomationElement? tab = FindTabText(container, pigs[idx].Title, out int count);
            if (tab == null || count != 1)
            {
                everyClickOk = false;
                ctx.Check(false, $"click {i + 1}/24: tab for '{pigs[idx].Title}' found uniquely (count={count})");
                break;
            }
            (int tx, int ty) = Uia.Center(tab);
            Input.ClickAt(tx, ty);
            Thread.Sleep(250);

            int tabs = TabCount(container);
            if (tabs != 3)
            {
                everyClickOk = false;
                ctx.Check(false, $"click {i + 1}/24: tab count still 3 (got {tabs})");
                break;
            }
            lastIdx = idx;
        }
        if (everyClickOk)
            ctx.Check(true, "tab count stayed 3 after every one of the 24 clicks");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "hid itself") == 0, "ZERO 'hid itself' lines in TabDock log");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "destroyed") == 0, "ZERO 'destroyed' lines in TabDock log");

        for (int i = 0; i < pigs.Length; i++)
        {
            bool alive = pigs[i].Proc != null && !pigs[i].Proc!.HasExited;
            bool child = ((long)NativeMethods.GetWindowLongPtr(pigs[i].Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0;
            ctx.Check(alive && child, $"pig '{pigs[i].Title}' alive and still captured (WS_CHILD)");
        }

        if (lastIdx >= 0)
        {
            Thread.Sleep(400);
            Input.ForceForegroundRoot(host);
            int[]? frame = Pixels.CaptureHostScreenArea(host);
            char dominant = frame != null ? Pixels.DominantChannel(frame) : '?';
            ctx.Check(frame != null && dominant == channelByIdx[lastIdx],
                $"host dominant channel '{dominant}' matches last-clicked pig color channel '{channelByIdx[lastIdx]}'");
        }

        if (ctx.Pass)
            GuardedProc.Log("  HIDE-SAFETY: no tab was removed by tab-switch-induced hide");
    }

    // -------------------------------------------------------------------------
    // 9. minrestore
    // -------------------------------------------------------------------------
    private static void MinRestore(Ctx ctx, Options opt)
    {
        // 7s (not 3s) so the timer cannot fire while the real-input capture flow (~5s) is still running.
        GuestInfo pig = SpawnPig(ctx, "MR", "--color", "white", "--self-minimize-after", "7");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);
        long off = TabDockLog.RecordLogLength();

        Thread.Sleep(8000);

        ctx.Check(TabDockLog.ContainsNewLine(off, "minimized; restoring it inside its tab."),
            "TabDock log gained 'minimized; restoring it inside its tab.'");
        ctx.Check(!NativeMethods.IsIconic(pig.Hwnd), "pig not iconic after restore");
        ctx.Check(GuestMatchesHost(pig.Hwnd, host, out string geo), $"pig rect equals host client area within 4px ({geo})");
        Input.ForceForegroundRoot(host);
        int[]? frame = Pixels.CaptureHostScreenArea(host);
        double brightness = frame != null ? Pixels.ComputeAvgBrightness(frame) : -1;
        ctx.Check(brightness > 1.0, $"host brightness > 1.0 ({brightness:F2})");
    }

    // -------------------------------------------------------------------------
    // 10. maximize-repro (also the diagnostic: completes all cycles and dumps everything even on FAIL)
    // -------------------------------------------------------------------------
    private static void MaximizeRepro(Ctx ctx, Options opt)
    {
        int cycles = opt.Cycles ?? 3;
        GuestInfo guest = SpawnGuest(ctx, opt.Guest);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, guest);

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            GuardedProc.Log($"  --- maximize-repro cycle {cycle}/{cycles} (guest={opt.Guest}) ---");
            long cycOff = TabDockLog.RecordLogLength();

            (double bBase, double vBase) = SampleHost(host);
            GuardedProc.Log($"  cycle {cycle}: baseline brightness={bBase:F2} variance={vBase:F4}");

            ClickMaximizeButton(container);
            Thread.Sleep(1500);
            (double bMax, double vMax) = SampleHost(host);
            DumpGeometry(ctx, container, host, guest, $"cycle{cycle} after-maximize");
            bool geoMaxOk = GuestMatchesHost(guest.Hwnd, host, out string geoMax);
            GuardedProc.Log($"  cycle {cycle}: brightnessAfterMax={bMax:F2} varianceAfterMax={vMax:F4}");

            // Restore: recompute the button position from the NEW (maximized) rect.
            ClickMaximizeButton(container);
            Thread.Sleep(1500);
            (double bRest, double vRest) = SampleHost(host);
            DumpGeometry(ctx, container, host, guest, $"cycle{cycle} after-restore");
            bool geoRestOk = GuestMatchesHost(guest.Hwnd, host, out string geoRest);
            GuardedProc.Log($"  cycle {cycle}: brightnessAfterRestore={bRest:F2} varianceAfterRestore={vRest:F4}");

            string newLines = TabDockLog.DumpNewLines(cycOff);
            GuardedProc.Log($"  cycle {cycle}: new TabDock log lines (MAXCLICK/STATE/LAYOUT instrumentation):");
            Console.WriteLine(newLines.Length > 0 ? newLines : "  (none)");
            Console.Out.Flush();

            ctx.Check(bMax > 1.0, $"cycle {cycle}: brightness > 1.0 after maximize ({bMax:F2})");
            ctx.Check(vMax > 0.005, $"cycle {cycle}: variance > 0.005 after maximize ({vMax:F4})");
            ctx.Check(geoMaxOk, $"cycle {cycle}: guest rect equals host client rect within 4px after maximize ({geoMax})");
            ctx.Check(bRest > 1.0, $"cycle {cycle}: brightness > 1.0 after restore ({bRest:F2})");
            ctx.Check(vRest > 0.005, $"cycle {cycle}: variance > 0.005 after restore ({vRest:F4})");
            ctx.Check(geoRestOk, $"cycle {cycle}: guest rect equals host client rect within 4px after restore ({geoRest})");
        }
    }

    // -------------------------------------------------------------------------
    // 11. repeat-cycles
    // -------------------------------------------------------------------------
    private static void RepeatCycles(Ctx ctx, Options opt)
    {
        int cycles = opt.Cycles ?? 5;
        GuestInfo pig = SpawnPig(ctx, "CYC", "--color", "blue");

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            GuardedProc.Log($"  --- repeat-cycles {cycle}/{cycles} ---");
            long cycOff = TabDockLog.RecordLogLength();

            (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

            ClickMaximizeButton(container);
            Thread.Sleep(1200);
            ctx.Check(GuestMatchesHost(pig.Hwnd, host, out string geoMax),
                $"cycle {cycle}: geometry OK after maximize ({geoMax})");

            ClickMaximizeButton(container);
            Thread.Sleep(1200);
            ctx.Check(GuestMatchesHost(pig.Hwnd, host, out string geoRest),
                $"cycle {cycle}: geometry OK after restore ({geoRest})");

            ClickTabMenuItem(ctx, container, pig.Title, "Pop out");
            ctx.Check(Util.WaitUntil(() => IsReleased(pig), 5000), $"cycle {cycle}: pig released by Pop out");
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 5000),
                $"cycle {cycle}: container closed/empty after Pop out");
            ctx.Check(TabDockLog.CountNewLines(cycOff, "EXCEPTION") == 0,
                $"cycle {cycle}: no EXCEPTION lines in TabDock log");

            Thread.Sleep(800); // let the released pig settle before recapturing
        }

        // A persisted group restored from state.json keeps its own empty "Group"
        // container open, so assert no orphaned TDVAL guest windows rather than
        // "no Group window at all".
        ctx.Check(NoOrphanPigWindows(ctx), "final tab state correct: no orphan TDVAL guest windows");
        ctx.Check(pig.Proc != null && !pig.Proc.HasExited, "pig survived all cycles");
    }

    // -------------------------------------------------------------------------
    // 12. crossfeature
    // -------------------------------------------------------------------------
    private static void CrossFeature(Ctx ctx, Options opt)
    {
        GuestInfo pig1 = SpawnPig(ctx, "XF1", "--pulse", "--color", "white");
        GuestInfo pig2 = SpawnPig(ctx, "XF2", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig1, pig2);

        // Step 1: rename (scenario-1 steps).
        AutomationElement containerEl = Uia.FromHwnd(container)
            ?? throw new InvalidOperationException("Container UIA element unavailable.");
        AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int count);
        ctx.Check(caption != null && count == 1, $"step rename: caption 'Group' found uniquely (count={count})");
        if (caption != null && count == 1)
        {
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
            (int cx, int cy) = Uia.Center(caption);
            bool renamed = false;
            for (int attempt = 0; attempt < 3 && !renamed; attempt++)
            {
                Input.DoubleClickAt(cx, cy);
                Thread.Sleep(300);
                Input.TypeText("TDVAL-Renamed");
                Input.SendKey(Input.VK_RETURN);
                renamed = Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-Renamed", 2000);
            }
            ctx.Check(renamed, "step rename: container title became 'TDVAL-Renamed'");
        }

        // Step 2: Close window on tab 2 (scenario-3 steps).
        long off2 = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, pig2.Title, "Close window");
        ctx.Check(PigLog.WaitForPigLine(pig2.Pid, "WM_CLOSE", 3000), "step closewin: pig2 log contains WM_CLOSE");
        ctx.Check(Util.WaitUntil(() => pig2.Proc!.HasExited, 5000), "step closewin: pig2 exited within 5s");
        ctx.Check(Util.WaitUntil(() => TabDockLog.ContainsNewLine(off2, "destroyed; removing its tab")
                || TabDockLog.ContainsNewLine(off2, "hid itself"), 5000),
            "step closewin: tab torn down (destroy or hide path)");
        ctx.Check(Util.WaitUntil(() => TabCount(container) == 1, 3000), "step closewin: one tab remains");

        // Step 3: maximize + restore (scenario-10 single cycle).
        ClickMaximizeButton(container);
        Thread.Sleep(1500);
        (double bMax, double vMax) = SampleHost(host);
        ctx.Check(bMax > 1.0, $"step maximize: brightness > 1.0 ({bMax:F2})");
        ctx.Check(vMax > 0.005, $"step maximize: variance > 0.005 ({vMax:F4})");
        ctx.Check(GuestMatchesHost(pig1.Hwnd, host, out string geoMax), $"step maximize: geometry OK ({geoMax})");

        ClickMaximizeButton(container);
        Thread.Sleep(1500);
        (double bRest, double vRest) = SampleHost(host);
        ctx.Check(bRest > 1.0, $"step restore: brightness > 1.0 ({bRest:F2})");
        ctx.Check(vRest > 0.005, $"step restore: variance > 0.005 ({vRest:F4})");
        ctx.Check(GuestMatchesHost(pig1.Hwnd, host, out string geoRest), $"step restore: geometry OK ({geoRest})");

        // Step 4: pop out the remaining pig — the container should end up empty/closed.
        ClickTabMenuItem(ctx, container, pig1.Title, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleased(pig1), 5000), "step popout: pig1 released");
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container) || TabCount(container) == 0, 5000),
            "step popout: container empty/closed");

        // Final checks.
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(NoOrphanPigWindows(ctx), "no orphan TDVAL windows on the desktop");
        ctx.Check(NativeMethods.IsWindow(ctx.MainHwnd), "TabDock MainWindow still alive/responsive");
    }

    // -------------------------------------------------------------------------
    // 13. renderhealth (H1+M4): a genuinely black-painting guest must be detected
    //     and auto-released; a normal guest must not be (no false positive).
    // -------------------------------------------------------------------------
    private static void RenderHealth(Ctx ctx, Options opt)
    {
        // Negative control first: a white pig must stay captured.
        GuestInfo white = SpawnPig(ctx, "RHW", "--color", "white");
        (IntPtr c1, IntPtr h1) = CaptureIntoGroup(ctx, white);
        Thread.Sleep(3000); // the health check runs ~800ms after capture; wide margin
        ctx.Check(((long)NativeMethods.GetWindowLongPtr(white.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0,
            "white pig still captured 3s after capture (no false-positive release)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "unhealthy") == 0,
            "no 'unhealthy' verdict logged for the white pig");

        // A guest whose whole client area genuinely paints RGB black must be
        // flagged unhealthy and released back to standalone with the notice.
        long off = TabDockLog.RecordLogLength();
        GuestInfo black = SpawnPig(ctx, "RHB", "--color", "black");
        (IntPtr c2, IntPtr h2) = CaptureIntoGroup(ctx, black);

        ctx.Check(TabDockLog.WaitForLogLine(off, "unhealthy", 10000),
            "TabDock log gained an 'unhealthy' verdict for the black pig");
        ctx.Check(Util.WaitUntil(() => IsReleased(black), 10000),
            "black pig auto-released back to standalone (WS_CHILD cleared, no parent)");
        ctx.Check(ClickMessageBoxButton(ctx, "Window could not be tabbed", new[] { "OK", "&OK" }, 8000),
            "release-notification MessageBox appeared (clicked OK)");
        ctx.Check(Util.WaitUntil(() => NativeMethods.IsWindowVisible(black.Hwnd), 3000),
            "black pig visible as a standalone window after release");
        ctx.Check(black.Proc != null && !black.Proc.HasExited, "black pig process alive");
        ctx.Check(!ctx.TabDock.HasExited, "TabDock alive");
    }

    // -------------------------------------------------------------------------
    // 14. hotkey-afterclose (H3): close the launcher with a group still open,
    //     then the global hotkey AND the container '+' button must still open
    //     the picker instead of crashing or doing nothing.
    // -------------------------------------------------------------------------
    private static void HotkeyAfterClose(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "HK", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        // Close the launcher with a real click on its caption close button.
        if (!Input.ForceForeground(ctx.MainHwnd))
            throw new InvalidOperationException("Could not bring the launcher to the foreground — refusing to click blind.");
        NativeMethods.GetWindowRect(ctx.MainHwnd, out NativeMethods.RECT rc);
        double scale = NativeMethods.GetDpiForWindow(ctx.MainHwnd) / 96.0;
        Input.ClickAt(rc.right - (int)(23 * scale), rc.top + (int)(16 * scale));
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindowVisible(ctx.MainHwnd), 3000),
            "launcher closed after real X click");
        Thread.Sleep(500);
        ctx.Check(!ctx.TabDock.HasExited, "TabDock still alive (open container keeps the app running)");

        int cycles = opt.Cycles ?? 3;
        for (int i = 1; i <= cycles && !ctx.TabDock.HasExited; i++)
        {
            long off = TabDockLog.RecordLogLength();
            Input.SendHotkeyCtrlAltG();
            IntPtr picker = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 6000);
            bool hotkeySeen = TabDockLog.ContainsNewLine(off, "hotkey Ctrl+Alt+G pressed");
            ctx.Check(picker != IntPtr.Zero,
                $"cycle {i}: picker appeared after hotkey with launcher closed (hotkey log line seen={hotkeySeen})");
            if (picker == IntPtr.Zero)
                break;
            Thread.Sleep(300);
            Input.ForceForeground(picker);
            Input.SendKey(Input.VK_ESCAPE); // picker Cancel is IsCancel=True
            ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(picker), 3000),
                $"cycle {i}: picker dismissed with Esc");
            ctx.Check(!ctx.TabDock.HasExited, $"cycle {i}: TabDock alive after hotkey cycle");
            Thread.Sleep(300);
        }

        // The container's '+' (add window) button funnels through the same
        // ShowCapturePicker path and must also survive the launcher being closed.
        if (!ctx.TabDock.HasExited && NativeMethods.IsWindow(container))
        {
            AutomationElement? containerEl = Uia.FromHwnd(container);
            AutomationElement? addBtn = containerEl == null
                ? null
                : Uia.FindDescendantByName(containerEl, ControlType.Button, "", null, out _);
            ctx.Check(addBtn != null, "container '+' button located via UIA");
            if (addBtn != null)
            {
                if (!Input.ForceForeground(container))
                    throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
                (int ax, int ay) = Uia.Center(addBtn);
                Input.ClickAt(ax, ay);
                IntPtr picker2 = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 6000);
                ctx.Check(picker2 != IntPtr.Zero, "picker appeared from container '+' with launcher closed");
                if (picker2 != IntPtr.Zero)
                {
                    Thread.Sleep(300);
                    Input.ForceForeground(picker2);
                    Input.SendKey(Input.VK_ESCAPE);
                    Util.WaitUntil(() => !NativeMethods.IsWindow(picker2), 3000);
                }
            }
        }

        Thread.Sleep(500);
        ctx.Check(!ctx.TabDock.HasExited, "TabDock alive at scenario end (no dispatcher crash)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        if (!ctx.TabDock.HasExited)
        {
            ctx.Check(((long)NativeMethods.GetWindowLongPtr(pig.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0,
                "pig still captured at scenario end");
        }
    }

    // -------------------------------------------------------------------------
    // 15. persist-kill (M5): capture + rename must reach state.json without a
    //     clean exit; a force-kill must not lose it; relaunch restores the group;
    //     a later save with the group still empty must not wipe tab metadata.
    //     (StartScenario/Cleanup snapshot and restore the user's state.json.)
    // -------------------------------------------------------------------------
    private static void PersistKill(Ctx ctx, Options opt)
    {
        {
            GuestInfo pig = SpawnPig(ctx, "PK", "--color", "blue");
            (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

            // 1) Debounced save: the capture must reach state.json with no rename/exit.
            ctx.Check(Util.WaitUntil(() => StateJsonContains(pig.Title), 5000),
                "state.json contains the captured tab's title within 5s of capture (debounced save)");

            // 2) Rename so the restored group is positively identifiable after relaunch.
            AutomationElement containerEl = Uia.FromHwnd(container)
                ?? throw new InvalidOperationException("Container UIA element unavailable.");
            AutomationElement? caption = Uia.FindDescendantByName(containerEl, ControlType.Text, "Group", null, out int count);
            if (caption == null || count != 1)
                throw new InvalidOperationException($"Container caption 'Group' not found uniquely (count={count}).");
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
            (int cx, int cy) = Uia.Center(caption);
            bool renamed = false;
            for (int attempt = 0; attempt < 3 && !renamed; attempt++)
            {
                Input.DoubleClickAt(cx, cy);
                Thread.Sleep(300);
                Input.TypeText("TDVAL-PKGRP");
                Input.SendKey(Input.VK_RETURN);
                renamed = Util.WaitUntil(() => NativeMethods.GetWindowTextString(container) == "TDVAL-PKGRP", 2000);
            }
            ctx.Check(renamed, "group renamed to TDVAL-PKGRP");
            ctx.Check(Util.WaitUntil(() => StateJsonContains("TDVAL-PKGRP"), 3000),
                "state.json contains the group rename");

            // 3) Force-kill TabDock: no shutdown handler runs; the file must already be durable.
            GuardedProc.Log("  Force-killing TabDock (Process.Kill, no graceful shutdown).");
            ctx.TabDock.Kill();
            ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 5000), "TabDock force-killed");
            Thread.Sleep(1000);
            ctx.Check(StateJsonContains("TDVAL-PKGRP") && StateJsonContains(pig.Title),
                "state.json survived the force-kill with group name and tab metadata");
            // The captured pig HWND dies with the host window tree (documented limitation) — not asserted.

            // 4) Relaunch: the group must come back as a named (empty) container shell.
            Process td2 = GuardedProc.SpawnGuarded(new ProcessStartInfo(TabDockExe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(TabDockExe)!,
            });
            ctx.TabDock = td2;
            ctx.TabDockPid = (uint)td2.Id;
            ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
            ctx.Check(ctx.MainHwnd != IntPtr.Zero, "TabDock relaunched (MainWindow up)");
            IntPtr restored = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TDVAL-PKGRP", 10000);
            ctx.Check(restored != IntPtr.Zero, "restored container 'TDVAL-PKGRP' opened after relaunch");
            if (restored != IntPtr.Zero)
                ctx.Containers.Add(restored);

            // 5) A clean-exit save with the group still empty must NOT wipe the
            //    persisted tab metadata (layout intent).
            Thread.Sleep(1000);
            foreach (IntPtr h in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true))
                NativeMethods.PostMessage(h, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            ctx.Check(Util.WaitUntil(() => ctx.TabDock.HasExited, 8000), "relaunched TabDock exited cleanly");
            ctx.Check(StateJsonContains("TDVAL-PKGRP"), "group name survived the clean-exit save");
            ctx.Check(StateJsonContains(pig.Title),
                "persisted tab metadata survived a save with the group empty (not wiped)");
        }
    }

    // -------------------------------------------------------------------------
    // 16. dragreorder: real-mouse drag-reorder within the strip (no crash, tabs
    //     intact) and drag-out of the container (pop-out release).
    // -------------------------------------------------------------------------
    private static void DragReorder(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "DRA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "DRB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        // Reorder: drag the RIGHTMOST tab into the left half of the LEFTMOST tab
        // (capture order follows picker Z-order, so tab order is not guaranteed;
        // GetDropIndex uses item midpoints, so the target must be left of the
        // leftmost tab's midpoint to produce a different drop index).
        AutomationElement? tabA = FindTabText(container, pigA.Title, out int cA);
        AutomationElement? tabB = FindTabText(container, pigB.Title, out int cB);
        if (tabA == null || cA != 1 || tabB == null || cB != 1)
            throw new InvalidOperationException($"Tabs not found uniquely (A={cA}, B={cB}).");
        Rect rA = Uia.GetElementRect(tabA);
        Rect rB = Uia.GetElementRect(tabB);
        bool aIsRight = rA.X > rB.X;
        GuestInfo movedPig = aIsRight ? pigA : pigB;
        Rect leftRect = aIsRight ? rB : rA;
        (int sx, int sy) = Uia.Center(aIsRight ? tabA : tabB);
        Input.DragFromTo(sx, sy, (int)(leftRect.X + 8), sy, 14);
        Thread.Sleep(600);

        ctx.Check(TabCount(container) == 2, "still 2 tabs after drag-reorder");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "Reordered tab") >= 1, "a reorder was applied (log)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-reorder");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited,
            "both pigs alive after drag-reorder");

        // Drag-out: drag the just-moved tab well outside the container -> pop-out
        // release. Deliberately reuses the leftmost slot's screen position rather
        // than re-finding the tab via UIA: after a reorder the WPF automation
        // peers for re-inserted items go stale (observed: FindTabText count=0 for
        // several seconds while the tab was demonstrably alive).
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        Input.DragFromTo((int)(leftRect.X + leftRect.Width / 2), sy, rc.right + 150, rc.bottom + 150, 14);

        ctx.Check(Util.WaitUntil(() => IsReleased(movedPig), 5000), $"moved pig '{movedPig.Title}' released by drag-out");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-out");
        ctx.Check(movedPig.Proc != null && !movedPig.Proc.HasExited, "moved pig alive standalone");
    }

    // -------------------------------------------------------------------------
    // 17. contentinput (Test A): non-Chromium content-area input gate.
    //     Clicks the center counter button of a captured GuineaPig and verifies
    //     the guest actually receives the input.
    // -------------------------------------------------------------------------
    private static void ContentInput(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "CI", "--color", "blue", "--click-counter-button");
        Thread.Sleep(2000); // extra settle time for the button-hosted pig before picker enumeration
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  ContentInput: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured guest to the foreground — refusing to click blind.");

        Input.ClickAt(cx, cy);
        bool clicked = PigLog.WaitForPigLine(pig.Pid, "BUTTON_CLICK count=1", 2000);
        ctx.Check(clicked, "GuineaPig content-area button received the click (BUTTON_CLICK count=1)");

        // Also exercise drag: start well outside the button and drag across the
        // content area; the count must not increment from a drag that never
        // presses the button.
        Input.DragFromTo(cx - 150, cy, cx + 150, cy, 12);
        Thread.Sleep(200);
        bool noExtraClick = !PigLog.ContainsLine(pig.Pid, "BUTTON_CLICK count=2");
        ctx.Check(noExtraClick, "drag across the content area did not produce a second button click (guest saw mouse motion)");
    }

    // -------------------------------------------------------------------------
    // 18. chromeinput (Test B): Chromium input recovery after activation fix.
    // -------------------------------------------------------------------------
    private static void ChromeInput(Ctx ctx, Options opt)
    {
        string htmlPath = CreateChromeInputTestPage();
        GuestInfo chrome = SpawnClassGuest(ctx, ChromeExe,
            $"--user-data-dir=\"{Path.Combine(Path.GetTempPath(), "TabDockChromeProfile")}\" --disable-gpu --app=\"{htmlPath}\"",
            "Chrome_WidgetWin_1", useShellExecute: true);

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, chrome);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  ChromeInput: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured Chrome guest to the foreground — refusing to click blind.");

        // The page starts white; the centered button turns the background green.
        Input.ClickAt(cx, cy);
        Thread.Sleep(1000);

        int[]? frame = Pixels.CaptureHostScreenArea(host);
        char dominant = frame != null ? Pixels.DominantChannel(frame) : '?';
        GuardedProc.Log($"  ChromeInput: after click dominant channel='{dominant}'.");
        ctx.Check(dominant == 'g', $"Chrome page turned green after click (dominant channel='{dominant}')");
    }

    private static string CreateChromeInputTestPage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TabDock-Validation");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "chrome-input-test.html");
        File.WriteAllText(path, @"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><style>
body { margin: 0; width: 100vw; height: 100vh; background: white; display: flex; align-items: center; justify-content: center; }
button { padding: 24px 48px; font-size: 24px; }
</style></head>
<body>
<button id='btn'>Click me</button>
<script>
document.getElementById('btn').addEventListener('click', function() {
    document.body.style.backgroundColor = '#00aa00';
});
</script>
</body>
</html>");
        return path;
    }

    // -------------------------------------------------------------------------
    // 19. alttabinput (Test D): container reactivation after alt-tab away/back.
    // -------------------------------------------------------------------------
    private static void AltTabInput(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "AT", "--color", "blue", "--click-counter-button");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured guest to the foreground — refusing to click blind.");

        // Baseline click: establish the guest is responsive.
        Input.ClickAt(cx, cy);
        ctx.Check(PigLog.WaitForPigLine(pig.Pid, "BUTTON_CLICK count=1", 2000),
            "baseline click received (count=1)");

        // Switch focus away from the container to the driver's own console window.
        IntPtr driverHwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (driverHwnd == IntPtr.Zero)
        {
            // Fallback: spawn a Notepad to receive focus. Use the safe helper so we
            // never capture or kill an existing user Notepad.
            GuestInfo notepad = SpawnNotepad(ctx);
            driverHwnd = notepad.Hwnd;
        }

        Input.ForceForegroundRoot(driverHwnd);
        Thread.Sleep(800);

        // Switch focus back to the container (simulates alt-tab back).
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container back to the foreground.");
        Thread.Sleep(500);

        // Click the guest again; the WM_ACTIVATE-forwarding path should have re-activated it.
        Input.ClickAt(cx, cy);
        ctx.Check(PigLog.WaitForPigLine(pig.Pid, "BUTTON_CLICK count=2", 2000),
            "click after alt-tab-back received (count=2)");
    }

    // -------------------------------------------------------------------------
    // 20. keyboardinput (H8 baseline): real keyboard typing must land in a
    //     captured non-Chromium guest's editable control.
    // -------------------------------------------------------------------------
    private static void KeyboardInput(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "KI", "--color", "blue", "--text-box");
        Thread.Sleep(2000); // let the text-box control realize before picker enumeration
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  KeyboardInput: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured guest to the foreground — refusing to type blind.");

        Input.ClickAt(cx, cy);
        Thread.Sleep(300);

        const string typed = "H8TEST";
        Input.TypeText(typed);
        bool received = PigLog.WaitForPigLine(pig.Pid, $"TEXTBOX text='{typed}'", 3000);
        ctx.Check(received, $"GuineaPig text box received typed string '{typed}'");
    }

    // -------------------------------------------------------------------------
    // 21. keyboardinput-chrome (H8 baseline): real keyboard typing must land
    //     in a captured Chrome guest's <input> field.
    // -------------------------------------------------------------------------
    private static void KeyboardInputChrome(Ctx ctx, Options opt)
    {
        string htmlPath = CreateChromeKeyboardTestPage();
        GuestInfo chrome = SpawnClassGuest(ctx, ChromeExe,
            $"--user-data-dir=\"{Path.Combine(Path.GetTempPath(), "TabDockChromeProfile")}\" --disable-gpu --app=\"{htmlPath}\"",
            "Chrome_WidgetWin_1", useShellExecute: true);

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, chrome);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  KeyboardInputChrome: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured Chrome guest to the foreground — refusing to type blind.");

        Input.ClickAt(cx, cy);
        Thread.Sleep(300);

        const string typed = "H8TEST";
        Input.TypeText(typed);

        string titlePrefix = $"TYPED:{typed}";
        bool titleChanged = Util.WaitUntil(() =>
            (NativeMethods.GetWindowTextString(chrome.Hwnd) ?? string.Empty).StartsWith(titlePrefix, StringComparison.Ordinal),
            3000);
        ctx.Check(titleChanged, $"Chrome page title reflects typed string '{typed}' (title prefix '{titlePrefix}')");
    }

    // -------------------------------------------------------------------------
    // 22. keyboardinput-notepad (H8 isolation): real keyboard typing must land
    //     in a captured Notepad edit/document control. This isolates whether the
    //     non-Chromium failure is specific to the WinForms guinea pig or general.
    // -------------------------------------------------------------------------
    private static void KeyboardInputNotepad(Ctx ctx, Options opt)
    {
        GuestInfo notepad = SpawnNotepad(ctx);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, notepad);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  KeyboardInputNotepad: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured Notepad guest to the foreground — refusing to type blind.");

        Input.ClickAt(cx, cy);
        Thread.Sleep(300);

        const string typed = "H8TEST";
        Input.TypeText(typed);

        // Read the Notepad edit/document control via UIA ValuePattern.
        string? value = null;
        int editCount = 0;
        bool readOk = Util.WaitUntil(() =>
        {
            AutomationElement? root = Uia.FromHwnd(notepad.Hwnd);
            if (root == null)
                return false;
            AutomationElement? edit = Uia.FindEditOrDocument(root, out editCount);
            if (edit == null)
                return false;
            value = Uia.GetValue(edit);
            return value != null;
        }, 3000, 150);

        GuardedProc.Log($"  KeyboardInputNotepad: editCount={editCount}, readOk={readOk}, value='{value ?? "<null>"}'.");
        ctx.Check(readOk, "Notepad edit/document control value read via UIA");
        ctx.Check(value != null && value.Contains(typed, StringComparison.Ordinal),
            $"Notepad edit control contains typed string '{typed}' (value='{value ?? "<null>"}')");
    }

    private static string CreateChromeKeyboardTestPage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TabDock-Validation");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "chrome-keyboard-test.html");
        File.WriteAllText(path, @"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><title>Chrome Keyboard Test</title><style>
body { margin: 0; width: 100vw; height: 100vh; background: white; display: flex; align-items: center; justify-content: center; }
input { padding: 16px 24px; font-size: 24px; width: 60vw; }
</style></head>
<body>
<input id='txt' autofocus>
<script>
var input = document.getElementById('txt');
function claimFocus() { input.focus(); }
window.addEventListener('load', claimFocus);
document.body.addEventListener('click', claimFocus);
input.addEventListener('input', function() {
    document.title = 'TYPED:' + input.value;
});
</script>
</body>
</html>");
        return path;
    }

    // -------------------------------------------------------------------------
    // 23. keyboardinput-rapid-switch (H8 stress): keyboard input must land after
    //     switching between two captured guests. Exercises the attach/detach
    //     lifecycle so a leak or stale attachment cannot hide behind a single tab.
    // -------------------------------------------------------------------------
    private static void KeyboardInputRapidSwitch(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "KIRS", "--color", "blue", "--text-box");
        GuestInfo notepad = SpawnNotepad(ctx);
        Thread.Sleep(3000); // let guests realize before picker enumeration
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig, notepad);
        Thread.Sleep(1500); // let the container's UIA tab tree settle before switching

        void TypeIntoHost(string text)
        {
            NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
            int cx = hostClient.left + hostClient.Width / 2;
            int cy = hostClient.top + hostClient.Height / 2;
            if (!Input.ForceForegroundRoot(host))
                throw new InvalidOperationException("Could not bring the captured guest to the foreground — refusing to type blind.");
            Input.ClickAt(cx, cy);
            Thread.Sleep(300);
            Input.TypeText(text);
        }

        void SwitchToTab(string title)
        {
            // Find the selectable ListBoxItem directly; walking up from the inner
            // Text element is fragile when virtualization has not materialized the
            // ancestor. Fall back to text+ancestor only if the ListItem search fails.
            AutomationElement? item = null;
            string lastError = "not searched";
            for (int attempt = 0; attempt < 10 && item == null; attempt++)
            {
                try
                {
                    AutomationElement? list = GetTabList(container);
                    if (list != null)
                    {
                        item = Uia.FindDescendantByName(list, ControlType.ListItem, null, title, out int listCount);
                        if (item == null)
                            lastError = $"ListItem not found (count={listCount})";
                    }
                    else
                    {
                        lastError = "tab list not found";
                    }

                    if (item == null)
                    {
                        AutomationElement? text = FindTabText(container, title, out int textCount);
                        if (text != null && textCount == 1)
                            item = Uia.NearestAncestorOfType(text, ControlType.ListItem);
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
                if (item == null)
                    Thread.Sleep(200);
            }
            if (item == null)
                throw new InvalidOperationException($"Tab '{title}' ListBoxItem not found ({lastError}).");

            Uia.Realize(item);

            // Prefer the UIA selection pattern; it avoids coordinate/virtualization
            // fragility and reliably switches tabs. We still verify IsSelected.
            bool selected = false;
            for (int attempt = 0; attempt < 3 && !selected; attempt++)
            {
                if (!Input.ForceForeground(container))
                    throw new InvalidOperationException("Could not bring the container to the foreground — refusing to select blind.");

                if (attempt == 0)
                    Uia.Select(item);
                else
                {
                    (int tx, int ty) = Uia.Center(item);
                    Input.ClickAt(tx, ty);
                }
                Thread.Sleep(350);
                selected = Uia.IsSelected(item) == true;
            }
            if (!selected)
                throw new InvalidOperationException($"Tab '{title}' did not become selected.");
        }

        string? ReadNotepadValue()
        {
            string? value = null;
            Util.WaitUntil(() =>
            {
                AutomationElement? root = Uia.FromHwnd(notepad.Hwnd);
                if (root == null)
                    return false;
                AutomationElement? edit = Uia.FindEditOrDocument(root, out _);
                if (edit == null)
                    return false;
                value = Uia.GetValue(edit);
                return value != null;
            }, 3000, 150);
            return value;
        }

        // Start on Notepad.
        SwitchToTab(notepad.Title);
        TypeIntoHost("NOTEPAD-A");
        string? valueAfterA = ReadNotepadValue();
        ctx.Check(valueAfterA != null && valueAfterA.Contains("NOTEPAD-A", StringComparison.Ordinal),
            $"Notepad contains 'NOTEPAD-A' after initial type (value='{valueAfterA ?? "<null>"}')");

        // Switch to the pig tab and type there.
        SwitchToTab(pig.Title);
        TypeIntoHost("PIG-B");
        bool pigReceived = PigLog.WaitForPigLine(pig.Pid, "TEXTBOX text='PIG-B'", 3000);
        ctx.Check(pigReceived, "GuineaPig text box received 'PIG-B' after switch");

        // Switch back to Notepad and type again.
        SwitchToTab(notepad.Title);
        TypeIntoHost("NOTEPAD-C");
        string? valueAfterC = ReadNotepadValue();
        ctx.Check(valueAfterC != null && valueAfterC.Contains("NOTEPAD-C", StringComparison.Ordinal),
            $"Notepad contains 'NOTEPAD-C' after switch-back (value='{valueAfterC ?? "<null>"}')");
    }

    /// <summary>Waits for a MessageBox owned by the TabDock pid and real-clicks the named button.</summary>
    private static bool ClickMessageBoxButton(Ctx ctx, string? titleContains, string[] buttonTexts, int budgetMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < budgetMs)
        {
            Util.ThrowIfCancelled();
            IntPtr dlg = Discover.FindMessageBox(ctx.TabDockPid, titleContains);
            if (dlg != IntPtr.Zero)
            {
                IntPtr btn = Discover.FindChildWindowByText(dlg, buttonTexts);
                if (btn != IntPtr.Zero)
                {
                    Input.ForceForeground(dlg);
                    NativeMethods.GetWindowRect(btn, out NativeMethods.RECT rc);
                    Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
                    return true;
                }
            }
            Thread.Sleep(200);
        }
        return false;
    }

    /// <summary>Every visible top-level TDVAL-* window must belong to a guest this scenario spawned (TabDock's own renamed container excluded).</summary>
    private static bool NoOrphanPigWindows(Ctx ctx)
    {
        var knownPids = new HashSet<uint>(ctx.Guests.Select(g => g.Pid));
        bool ok = true;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;
            string title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
            if (!title.StartsWith("TDVAL-", StringComparison.Ordinal))
                return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ctx.TabDockPid || knownPids.Contains(pid))
                return true;
            GuardedProc.Log($"  Orphan window '{title}' (PID {pid}, HWND 0x{hwnd.ToInt64():X}).");
            ok = false;
            return true;
        }, IntPtr.Zero);
        return ok;
    }
}
