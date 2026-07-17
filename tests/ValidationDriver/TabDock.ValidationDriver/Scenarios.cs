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

    /// <summary>
    /// True for guests that are the user's own pre-existing real application
    /// instance (e.g. Codex, ChatGPT Classic) rather than something this
    /// driver spawned. Cleanup must NEVER Process.Kill such a guest — only
    /// release/pop it out back to standalone, exactly as a real user would.
    /// </summary>
    public bool DoNotKill;

    /// <summary>
    /// Stable substring for tab lookups (FindTabText/ClickTabMenuItem), where
    /// it differs from the full window Title. Real browser titles are NOT
    /// safe to match verbatim: confirmed live, Edge inserts a zero-width space
    /// (U+200B) around its own branding ("Microsoft<U+200B> Edge"), and the
    /// time.is test page's title ticks a live clock every second — either one
    /// can silently break an exact/substring match a few seconds after
    /// capture. Defaults to Title (guinea pigs' "TDVAL-..." titles have
    /// neither problem); set explicitly for real-browser guests.
    /// </summary>
    public string TabMatchKey = string.Empty;

    public string EffectiveTabMatchKey => string.IsNullOrEmpty(TabMatchKey) ? Title : TabMatchKey;
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
    // Confirmed present on this dev machine (see docs/internal/TEST_PLAN.md section 4).
    private const string EdgeExe = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
    // NOT installed on this dev machine — case exists so the code path is
    // written and reviewable, but it cannot be run/verified here (see
    // docs/internal/TEST_PLAN.md section 4 and KNOWN_ISSUES.md).
    private const string FirefoxExe = "C:/Program Files/Mozilla Firefox/firefox.exe";
    private const string ContentHostClass = "TabDockContentHost";

    private static readonly Random Rng = new Random();

    public static readonly string[] AllOrder =
    {
        "rename", "popout", "closewin", "closewin-hide", "selfclose", "selfhide", "selfminhide",
        "tabswitch-hidesafety", "minrestore", "maximize-repro", "repeat-cycles", "crossfeature",
        "renderhealth", "hotkey-afterclose", "persist-kill", "dragreorder", "chrometabdrag",
        "closegroupprompt",
    };

    /// <summary>
    /// "realapp" is deliberately NOT in AllOrder/"all": it attaches to the user's
    /// own live app (Codex/ChatGPT Classic) rather than a disposable guest, so it
    /// must always be invoked explicitly by name with --guest codex|chatgptclassic,
    /// never swept in by a blanket "all" run.
    /// </summary>
    public static readonly string[] RealAppGuestKinds = { "codex", "chatgptclassic" };

    /// <summary>
    /// Real-browser scenarios (docs/internal/TEST_PLAN.md section 5) are also
    /// deliberately NOT in AllOrder/"all": each needs an explicit --guest
    /// {chrome-normal|edge-normal|firefox-normal} to mean anything, so a blanket
    /// "all" run must not silently launch real browsers with no guest chosen.
    /// </summary>
    public static readonly string[] BrowserOnlyScenarios =
    {
        "browser-lifecycle", "browser-tabswitch-hidesafety", "browser-dragreorder", "browser-soak",
        "renderhealth", "hotkey-afterclose", "persist-kill", "dragreorder",
        "contentinput", "chromeinput", "alttabinput",
        "keyboardinput", "keyboardinput-chrome", "keyboardinput-notepad", "keyboardinput-rapid-switch",
    };
    public static readonly string[] BrowserGuestKinds = { "chrome-normal", "edge-normal", "firefox-normal" };

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
            "chrometabdrag" => ChromeTabDrag,
            "realapp" => RealAppFillMaxHide,
            "closegroupprompt" => CloseGroupPrompt,
            "browser-lifecycle" => BrowserLifecycle,
            "browser-tabswitch-hidesafety" => BrowserTabSwitchHideSafety,
            "browser-dragreorder" => BrowserDragReorder,
            "browser-multi" => BrowserMulti,
            "browser-soak" => BrowserSoak,
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
            //    Guard against killing a shared-instance host process (e.g. wt.exe hands
            //    its window to an already-running WindowsTerminal.exe "monarch" process,
            //    which can be an ancestor of THIS driver's own shell) — see
            //    GuardedProc.IsAncestorOfCurrentProcess and its doc comment.
            foreach (GuestInfo g in ctx.Guests)
            {
                try
                {
                    if (g.DoNotKill)
                    {
                        GuardedProc.Log($"  Cleanup: guest PID {g.Pid} ('{g.Title}') is a protected real app (DoNotKill) — never killed. " +
                            "Its captured window was released/popped-out during the scenario body, not here.");
                        continue;
                    }
                    if (g.Proc != null && !g.Proc.HasExited)
                    {
                        if (GuardedProc.IsAncestorOfCurrentProcess(g.Proc.Id))
                        {
                            GuardedProc.Log($"  Cleanup: SAFETY: REFUSING to kill guest PID {g.Proc.Id} ('{g.Title}') — " +
                                "it is this driver's own process or an ancestor of it, not an isolated spawned child. " +
                                "Its captured window is closed via WM_CLOSE below instead.");
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

            // 3) A guest whose process kill was refused above (shared-instance host,
            //    e.g. Windows Terminal's monarch) is by now released back to standalone
            //    (via the "No" click on the close-group prompt above). Close its OWN
            //    window handle directly instead: for Windows Terminal this ends just
            //    that one window/pane's shell without touching the shared host process
            //    or any other window it hosts.
            foreach (GuestInfo g in ctx.Guests)
            {
                if (g.Proc != null && !g.Proc.HasExited && GuardedProc.IsAncestorOfCurrentProcess(g.Proc.Id)
                    && NativeMethods.IsWindow(g.Hwnd))
                {
                    GuardedProc.Log($"  Cleanup: WM_CLOSE -> guest window 0x{g.Hwnd.ToInt64():X} ('{g.Title}') (shared-host process left untouched).");
                    NativeMethods.PostMessage(g.Hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
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
                    $"--user-data-dir=\"{FreshProfileDir("TabDockChromeProfile")}\" --disable-gpu --no-first-run --no-default-browser-check https://time.is",
                    "Chrome_WidgetWin_1", useShellExecute: true);
            case "chrome-gpu":
                return SpawnClassGuest(ctx, ChromeExe,
                    $"--user-data-dir=\"{FreshProfileDir("TabDockChromeProfile")}\" --no-first-run --no-default-browser-check https://time.is",
                    "Chrome_WidgetWin_1", useShellExecute: true);
            case "chrome-normal":
                // Deliberately NOT --app=: normal browser chrome (tab strip, omnibox)
                // is required so the H5 fill-clamp can be exercised by dragging the
                // guest's own client-drawn tab strip (Chrome hit-tests it as
                // HTCAPTION). Isolated, FRESH --user-data-dir (new per invocation,
                // not reused) keeps this off the user's real profile/history AND
                // avoids Chrome's "Restore pages?" crash-recovery prompt, which a
                // reused profile accumulates after enough force-killed test runs
                // (reproduced live: a stale shared profile directory caused a
                // "Restore pages?" window instead of time.is, breaking the picker
                // lookup with 0 matches).
                return WithStableTabMatchKey(SpawnClassGuest(ctx, ChromeExe,
                    $"--user-data-dir=\"{FreshProfileDir("TabDockChromeProfileNormal")}\" --no-first-run --no-default-browser-check --disable-session-crashed-bubble https://time.is",
                    "Chrome_WidgetWin_1", useShellExecute: true), "Google Chrome");
            case "edge-normal":
                // Chromium-based: same window class, args shape, and fresh-profile
                // rationale as chrome-normal.
                return WithStableTabMatchKey(SpawnClassGuest(ctx, EdgeExe,
                    $"--user-data-dir=\"{FreshProfileDir("TabDockEdgeProfileNormal")}\" --no-first-run --no-default-browser-check --disable-session-crashed-bubble https://time.is",
                    "Chrome_WidgetWin_1", useShellExecute: true), "Microsoft");
            case "firefox-normal":
                // Gecko engine, different window class. NOT installed on this dev
                // machine (docs/internal/TEST_PLAN.md section 4) — this case is
                // written for review/future use but cannot be run/verified here.
                // "Mozilla Firefox" match key is UNVERIFIED (never executed).
                return WithStableTabMatchKey(SpawnClassGuest(ctx, FirefoxExe,
                    $"-profile \"{FreshProfileDir("TabDockFirefoxProfileNormal")}\" -no-remote https://time.is",
                    "MozillaWindowClass", useShellExecute: true), "Mozilla Firefox");
            case "codex":
                // Attaches to the user's own already-running Codex/ChatGPT app
                // (process name "ChatGPT", window class Chrome_WidgetWin_1, title
                // "ChatGPT") rather than spawning a new instance. DoNotKill=true:
                // this is a real app with a real session, never a disposable guest.
                return AttachExistingRealApp(ctx, "ChatGPT", "Chrome_WidgetWin_1", exactTitle: "ChatGPT");
            case "chatgptclassic":
                return AttachExistingRealApp(ctx, "ChatGPT Classic", null, exactTitle: "ChatGPT Classic");
            default:
                throw new ArgumentException($"Unknown --guest kind '{kind}' (expected pig|wt|chrome-nogpu|chrome-gpu|chrome-normal|edge-normal|firefox-normal|codex|chatgptclassic).");
        }
    }

    /// <summary>
    /// Attaches to a single already-running instance of a real, user-owned app by
    /// process name (never spawns or kills it). Refuses if zero or more than one
    /// matching visible-or-hidden top-level window is found — an ambiguous match
    /// on someone's live app is exactly the "wrong window" failure mode the
    /// project's safety rules exist to prevent. Reveals the window via ShowWindow
    /// if it is currently hidden in its tray state.
    /// </summary>
    private static GuestInfo AttachExistingRealApp(Ctx ctx, string processName, string? className, string exactTitle)
    {
        Process[] procs = Process.GetProcessesByName(processName);
        if (procs.Length == 0)
            throw new InvalidOperationException($"No running process named '{processName}' found — refusing to guess.");

        var candidates = new List<(IntPtr Hwnd, uint Pid)>();
        foreach (Process p in procs)
        {
            foreach (IntPtr h in Discover.GetTopLevelWindowsByPid((uint)p.Id, visibleOnly: false))
            {
                string title = NativeMethods.GetWindowTextString(h) ?? string.Empty;
                if (!string.Equals(title, exactTitle, StringComparison.Ordinal))
                    continue;
                if (className != null && !string.Equals(NativeMethods.GetClassNameString(h), className, StringComparison.OrdinalIgnoreCase))
                    continue;
                candidates.Add((h, (uint)p.Id));
            }
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException($"No window titled '{exactTitle}' found among '{processName}' processes — refusing to guess.");
        if (candidates.Count > 1)
            throw new InvalidOperationException($"{candidates.Count} windows titled '{exactTitle}' found among '{processName}' processes — ambiguous, refusing to touch any of them.");

        (IntPtr hwnd, uint pid) = candidates[0];
        GuardedProc.Log($"  Attaching to existing real app '{processName}' PID {pid} HWND 0x{hwnd.ToInt64():X} (never spawned, never killed by this driver).");

        if (!NativeMethods.IsWindowVisible(hwnd))
        {
            GuardedProc.Log($"  '{exactTitle}' window is currently hidden (tray state); revealing with ShowWindow(SW_SHOW).");
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            Thread.Sleep(500);
        }

        var g = new GuestInfo
        {
            Proc = Process.GetProcessById((int)pid),
            Pid = pid,
            Hwnd = hwnd,
            Title = exactTitle,
            IsPig = false,
            DoNotKill = true,
        };
        ctx.Guests.Add(g);
        return g;
    }

    /// <summary>
    /// A fresh, never-reused temp directory for a browser's --user-data-dir.
    /// Reusing one fixed profile directory across many runs accumulates
    /// "didn't shut down properly" state from this driver's own force-kills,
    /// which surfaces as Chrome/Edge's "Restore pages?" crash-recovery prompt
    /// on a later launch instead of the requested URL — reproduced live, it
    /// breaks the picker lookup (0 matches) for a window that isn't the guest
    /// the scenario expected. A new GUID-suffixed directory per invocation
    /// guarantees a clean profile every time.
    /// </summary>
    private static string FreshProfileDir(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
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
    /// Sets a stable, special-character-free, non-ticking substring as the
    /// guest's tab-lookup key. Real browser window titles are not safe to
    /// match verbatim for anything beyond the picker's one-shot row lookup
    /// (which happens fast, right after launch): confirmed live, Edge embeds
    /// a zero-width space (U+200B) in its own branding, and the time.is test
    /// page's title changes over time — either can desync an exact/substring
    /// tab-label match a few seconds later. Uses each browser's own brand
    /// suffix (not "Time.is", which every "-normal" guest shares — ambiguous
    /// the moment two of them are captured into one container at once, e.g.
    /// browser-multi) so guests stay uniquely distinguishable from each other.
    /// The key is taken from BEFORE any fragile trailing character (Edge's
    /// zero-width space sits right after "Microsoft").
    /// </summary>
    private static GuestInfo WithStableTabMatchKey(GuestInfo g, string key)
    {
        g.TabMatchKey = key;
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
    // 17. chrometabdrag (H4/H5 PR gate): drag a real captured Chrome window by
    //     its own client-drawn tab strip (Chrome hit-tests this as HTCAPTION,
    //     so the guest itself is moved relative to the host) and verify the
    //     fill-clamp snaps it back to the host client rect on release, with no
    //     residual host-background smear.
    // -------------------------------------------------------------------------
    private static void ChromeTabDrag(Ctx ctx, Options opt)
    {
        long capOff = TabDockLog.RecordLogLength();
        GuestInfo chrome = SpawnGuest(ctx, "chrome-normal");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, chrome);

        ctx.Check(TabDockLog.ContainsNewLine(capOff, "LAYOUT[capture]"), "TabDock log gained a LAYOUT[capture] line");
        ctx.Check(GuestMatchesHost(chrome.Hwnd, host, out string geoCap),
            $"guest rect == host client rect at capture ({geoCap})");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        // Baseline brightness before the drag (host is fully covered by Chrome's
        // bright default page background).
        int[]? preFrame = Pixels.CaptureHostScreenArea(host);
        double preBrightness = preFrame != null ? Pixels.ComputeAvgBrightness(preFrame) : -1;

        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        double scale = NativeMethods.GetDpiForWindow(container) / 96.0;
        // Chrome's tab strip is a ~36-40px (96 DPI) band at the top of its
        // client area; this x offset clears the first tab's own close button
        // and lands on a freshly opened single-tab window's tab.
        int startX = hostRect.left + (int)(150 * scale);
        int startY = hostRect.top + (int)(18 * scale);
        int endX = hostRect.left + (int)(280 * scale);
        int endY = hostRect.top + (int)(110 * scale);

        long dragOff = TabDockLog.RecordLogLength();
        Input.DragFromTo(startX, startY, endX, endY, 16);

        // Sampled immediately on release, before the MOVESIZEEND reclamp has had
        // time to settle — the narrowest window to catch a residual smear: if
        // the guest is still offset, the host's own #1E1E1E background brush
        // (the H4 fix) now paints the exposed strip and brightness drops.
        int[]? justAfterFrame = Pixels.CaptureHostScreenArea(host);
        double justAfterBrightness = justAfterFrame != null ? Pixels.ComputeAvgBrightness(justAfterFrame) : -1;

        Thread.Sleep(600); // let the MOVESIZEEND reclamp settle
        ctx.Check(TabDockLog.ContainsNewLine(dragOff, "LAYOUT[movesize]"),
            "TabDock log gained a LAYOUT[movesize] line on release");
        ctx.Check(GuestMatchesHost(chrome.Hwnd, host, out string geoAfter),
            $"guest rect snapped back to host client rect after drag ({geoAfter})");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
        ctx.Check(chrome.Proc != null && !chrome.Proc.HasExited, "chrome guest process alive after drag");

        GuardedProc.Log(
            $"  SMEAR CHECK: preDragBrightness={preBrightness:F2} justAfterReleaseBrightness={justAfterBrightness:F2} " +
            "(sampled over the host client rect; a low value while the guest is still offset would indicate " +
            "exposed #1E1E1E host background — i.e. smear).");
    }

    // -------------------------------------------------------------------------
    // 18. realapp (real Codex / ChatGPT Classic): fill + maximize/restore +
    //     hide-on-close, against the user's OWN already-running app instance.
    //     Never spawned, never killed by this driver (GuestInfo.DoNotKill) —
    //     "Close window" is expected to hide it back to tray (its normal
    //     X-click behavior), exactly like a real user closing the tab, not
    //     terminate it. Not part of AllOrder/"all" — must be invoked by name.
    // -------------------------------------------------------------------------
    private static void RealAppFillMaxHide(Ctx ctx, Options opt)
    {
        GuestInfo app = SpawnGuest(ctx, opt.Guest);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, app);

        // "Fill": click into the window and type a short, clearly-marked
        // throwaway line WITHOUT pressing Enter/Send. This exercises real
        // rendering/layout with real on-screen content without submitting
        // anything to a live account/session — deliberately conservative.
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        NativeMethods.RECT hostRect = Discover.GetClientScreenRect(host);
        Input.ClickAt(hostRect.left + hostRect.Width / 2, hostRect.top + hostRect.Height - 60);
        Thread.Sleep(300);
        Input.TypeText("TDVAL test fill - please ignore, not sent");
        Thread.Sleep(300);
        ctx.Check(GuestMatchesHost(app.Hwnd, host, out string geoFill), $"guest still fills host after fill ({geoFill})");

        // Maximize / restore cycle. Only one monitor is available in this
        // environment, so the checklist's "2nd monitor" clause cannot be
        // exercised here — noted, not silently skipped.
        ClickMaximizeButton(container);
        Thread.Sleep(1200);
        ctx.Check(GuestMatchesHost(app.Hwnd, host, out string geoMax), $"geometry OK after maximize ({geoMax})");
        ClickMaximizeButton(container);
        Thread.Sleep(1200);
        ctx.Check(GuestMatchesHost(app.Hwnd, host, out string geoRest), $"geometry OK after restore ({geoRest})");

        // Hide-on-close: "Close window" from the tab menu should hide the
        // real app back to tray (never terminate it) — its normal X-click
        // behavior, exercised through TabDock's teardown path instead.
        long off = TabDockLog.RecordLogLength();
        ClickTabMenuItem(ctx, container, app.Title, "Close window");
        ctx.Check(Util.WaitUntil(() => TabDockLog.ContainsNewLine(off, "hid itself")
                || TabDockLog.ContainsNewLine(off, "destroyed; removing its tab"), 8000),
            "TabDock log shows the tab was torn down (hide or destroy path)");
        Thread.Sleep(1500);
        ctx.Check(app.Proc != null && !app.Proc.HasExited, "real app process still alive after close (hidden, not terminated)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");

        GuardedProc.Log($"  Real app '{app.Title}' left in its normal hidden/tray state (never captured, never killed by cleanup).");
    }

    // -------------------------------------------------------------------------
    // 19. closegroupprompt: the container's Closing handler shows a Yes/No/
    //     Cancel MessageBox when it still has tabs (ContainerWindow.xaml.cs
    //     ContainerWindow_Closing). Cancel must leave the container and its
    //     tabs untouched; Yes must actually close (exit) every captured guest,
    //     not just release it — the one path no other scenario exercises,
    //     since cleanup's own MessageBox handling always clicks "No".
    // -------------------------------------------------------------------------
    private static void CloseGroupPrompt(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "CGA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "CGB", "--color", "blue");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB);

        // --- Cancel: container and both tabs must be completely unaffected. ---
        NativeMethods.PostMessage(container, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        IntPtr dlg1 = Discover.FindMessageBox(ctx.TabDockPid, "Close group");
        Util.WaitUntil(() => (dlg1 = Discover.FindMessageBox(ctx.TabDockPid, "Close group")) != IntPtr.Zero, 5000);
        ctx.Check(dlg1 != IntPtr.Zero, "Close-group prompt appeared on WM_CLOSE with tabs present");
        if (dlg1 != IntPtr.Zero)
        {
            IntPtr cancelBtn = Discover.FindChildWindowByText(dlg1, new[] { "Cancel" });
            ctx.Check(cancelBtn != IntPtr.Zero, "prompt has a Cancel button");
            if (cancelBtn != IntPtr.Zero)
            {
                Input.ForceForeground(dlg1);
                NativeMethods.GetWindowRect(cancelBtn, out NativeMethods.RECT rc);
                Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
            }
            Util.WaitUntil(() => !NativeMethods.IsWindow(dlg1), 3000);
        }
        Thread.Sleep(400);
        ctx.Check(NativeMethods.IsWindow(container), "Cancel: container still open");
        ctx.Check(TabCount(container) == 2, "Cancel: both tabs still present");
        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited,
            "Cancel: both pigs still alive");
        ctx.Check(((long)NativeMethods.GetWindowLongPtr(pigA.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0
                && ((long)NativeMethods.GetWindowLongPtr(pigB.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0,
            "Cancel: both pigs still captured (WS_CHILD)");

        // --- Yes: must actually close (exit) both captured guests. ---
        long off = TabDockLog.RecordLogLength();
        NativeMethods.PostMessage(container, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        IntPtr dlg2 = IntPtr.Zero;
        Util.WaitUntil(() => (dlg2 = Discover.FindMessageBox(ctx.TabDockPid, "Close group")) != IntPtr.Zero, 5000);
        ctx.Check(dlg2 != IntPtr.Zero, "Close-group prompt appeared again on second WM_CLOSE");
        if (dlg2 != IntPtr.Zero)
        {
            IntPtr yesBtn = Discover.FindChildWindowByText(dlg2, new[] { "&Yes", "Yes" });
            ctx.Check(yesBtn != IntPtr.Zero, "prompt has a Yes button");
            if (yesBtn != IntPtr.Zero)
            {
                Input.ForceForeground(dlg2);
                NativeMethods.GetWindowRect(yesBtn, out NativeMethods.RECT rc);
                Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
            }
        }
        ctx.Check(Util.WaitUntil(() => !NativeMethods.IsWindow(container), 5000), "Yes: container closed");
        ctx.Check(Util.WaitUntil(() => pigA.Proc!.HasExited, 5000), "Yes: pigA actually exited (not just released)");
        ctx.Check(Util.WaitUntil(() => pigB.Proc!.HasExited, 5000), "Yes: pigB actually exited (not just released)");
        ctx.Check(TabDockLog.CountNewLines(off, "EXCEPTION") == 0, "no EXCEPTION lines across the whole prompt sequence");
    }

    // -------------------------------------------------------------------------
    // 20. browser-lifecycle --guest {chrome-normal|edge-normal|firefox-normal}
    //     (docs/internal/TEST_PLAN.md 5.1): real reparent lifecycle (launch/
    //     attach/detach) plus the H4 hide->show smear check with a real browser.
    // -------------------------------------------------------------------------
    private static void BrowserLifecycle(Ctx ctx, Options opt)
    {
        GuestInfo browser = SpawnGuest(ctx, opt.Guest);
        long capOff = TabDockLog.RecordLogLength();
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, browser);
        ctx.Check(TabDockLog.ContainsNewLine(capOff, "LAYOUT[capture]"), "TabDock log gained a LAYOUT[capture] line");
        ctx.Check(GuestMatchesHost(browser.Hwnd, host, out string geoCap), $"guest rect == host client rect at capture ({geoCap})");

        (double bBefore, _) = SampleHost(host);
        ctx.Check(bBefore > 1.0, $"host bright with browser visible before hide ({bBefore:F2})");

        // Force a hide->show cycle of the guest within its own host by
        // minimizing/restoring the container itself (mirrors minrestore).
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the browser container to the foreground — refusing to click blind.");
        NativeMethods.ShowWindow(container, NativeMethods.SW_MINIMIZE);
        Thread.Sleep(600);
        NativeMethods.ShowWindow(container, NativeMethods.SW_RESTORE);
        Thread.Sleep(800);

        (double bAfter, _) = SampleHost(host);
        ctx.Check(bAfter > 1.0, $"host bright again after minimize->restore hide/show cycle, i.e. no H4 smear residue ({bAfter:F2})");
        ctx.Check(GuestMatchesHost(browser.Hwnd, host, out string geoAfter), $"guest still fills host after hide/show ({geoAfter})");

        List<string> drift = TabDockLog.FindDriftWithoutPrecedingMovesize(ctx.LogOffset);
        ctx.Check(drift.Count == 0, drift.Count == 0
            ? "no LAYOUT[drift] fired without a preceding LAYOUT[movesize]"
            : $"UNEXPECTED bare drift line(s): {string.Join(" | ", drift)}");

        ClickTabMenuItem(ctx, container, browser.EffectiveTabMatchKey, "Pop out");
        ctx.Check(Util.WaitUntil(() => IsReleased(browser), 5000), "browser released by Pop out");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // 21. browser-tabswitch-hidesafety --guest {chrome-normal|edge-normal|firefox-normal}
    //     (docs/internal/TEST_PLAN.md 5.2): the existing tabswitch-hidesafety
    //     gate, with one of the three tabs a real browser instead of all pigs.
    // -------------------------------------------------------------------------
    private static void BrowserTabSwitchHideSafety(Ctx ctx, Options opt)
    {
        GuestInfo browser = SpawnGuest(ctx, opt.Guest);
        GuestInfo pigB = SpawnPig(ctx, "BTB", "--color", "blue");
        GuestInfo pigG = SpawnPig(ctx, "BTG", "--color", "green");
        GuestInfo[] guests = { browser, pigB, pigG };
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, guests);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture (1 real browser + 2 pigs)");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        bool everyClickOk = true;
        for (int i = 0; i < 24; i++)
        {
            int idx = i % guests.Length;
            AutomationElement? tab = FindTabText(container, guests[idx].EffectiveTabMatchKey, out int count);
            if (tab == null || count != 1)
            {
                everyClickOk = false;
                ctx.Check(false, $"click {i + 1}/24: tab for '{guests[idx].Title}' found uniquely (count={count})");
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
        }
        if (everyClickOk)
            ctx.Check(true, "tab count stayed 3 after every one of the 24 clicks (real browser included)");

        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "hid itself") == 0, "ZERO 'hid itself' lines in TabDock log");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "destroyed") == 0, "ZERO 'destroyed' lines in TabDock log");
        ctx.Check(browser.Proc != null && !browser.Proc.HasExited
                && ((long)NativeMethods.GetWindowLongPtr(browser.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0,
            $"real browser '{browser.Title}' alive and still captured after 24 switches");
        List<string> drift = TabDockLog.FindDriftWithoutPrecedingMovesize(ctx.LogOffset);
        ctx.Check(drift.Count == 0, drift.Count == 0
            ? "no LAYOUT[drift] fired without a preceding LAYOUT[movesize]"
            : $"UNEXPECTED bare drift line(s): {string.Join(" | ", drift)}");
    }

    // -------------------------------------------------------------------------
    // 22. browser-dragreorder --guest {chrome-normal|edge-normal|firefox-normal}
    //     (docs/internal/TEST_PLAN.md 5.3): H2's TabDock-tab-strip drag-reorder,
    //     with a real browser as one of the two tabs (dragreorder uses pigs only).
    // -------------------------------------------------------------------------
    private static void BrowserDragReorder(Ctx ctx, Options opt)
    {
        GuestInfo browser = SpawnGuest(ctx, opt.Guest);
        GuestInfo pig = SpawnPig(ctx, "BDR", "--color", "red");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, browser, pig);
        ctx.Check(TabCount(container) == 2, "2 tabs after capture (1 real browser + 1 pig)");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        AutomationElement? tabBrowser = FindTabText(container, browser.EffectiveTabMatchKey, out int cA);
        AutomationElement? tabPig = FindTabText(container, pig.EffectiveTabMatchKey, out int cB);
        if (tabBrowser == null || cA != 1 || tabPig == null || cB != 1)
            throw new InvalidOperationException($"Tabs not found uniquely (browser={cA}, pig={cB}).");
        Rect rBrowser = Uia.GetElementRect(tabBrowser);
        Rect rPig = Uia.GetElementRect(tabPig);
        bool browserIsRight = rBrowser.X > rPig.X;
        GuestInfo movedGuest = browserIsRight ? browser : pig;
        Rect leftRect = browserIsRight ? rPig : rBrowser;
        (int sx, int sy) = Uia.Center(browserIsRight ? tabBrowser : tabPig);
        Input.DragFromTo(sx, sy, (int)(leftRect.X + 8), sy, 14);
        Thread.Sleep(600);

        ctx.Check(TabCount(container) == 2, "still 2 tabs after drag-reorder");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "Reordered tab") >= 1, "a reorder was applied (log)");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-reorder");
        ctx.Check(browser.Proc != null && !browser.Proc.HasExited && pig.Proc != null && !pig.Proc.HasExited,
            "both the real browser and the pig are alive after drag-reorder");

        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        Input.DragFromTo((int)(leftRect.X + leftRect.Width / 2), sy, rc.right + 150, rc.bottom + 150, 14);
        ctx.Check(Util.WaitUntil(() => IsReleased(movedGuest), 5000), $"moved guest '{movedGuest.Title}' released by drag-out");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines after drag-out");
        ctx.Check(movedGuest.Proc != null && !movedGuest.Proc.HasExited, "moved guest alive standalone");
    }

    // -------------------------------------------------------------------------
    // 23. browser-multi (docs/internal/TEST_PLAN.md 5.4): Chrome + Edge as two
    //     simultaneous tabs in one container (Firefox omitted — not installed
    //     on this dev machine; add "firefox-normal" to the guest list below if
    //     it becomes available, per TEST_PLAN.md section 4/6).
    // -------------------------------------------------------------------------
    private static void BrowserMulti(Ctx ctx, Options opt)
    {
        GuestInfo chrome = SpawnGuest(ctx, "chrome-normal");
        GuestInfo edge = SpawnGuest(ctx, "edge-normal");
        GuestInfo[] guests = { chrome, edge };
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, guests);
        ctx.Check(TabCount(container) == 2, "2 tabs after simultaneous multi-browser capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
        foreach (GuestInfo g in guests)
        {
            AutomationElement? tab = FindTabText(container, g.EffectiveTabMatchKey, out int count);
            ctx.Check(tab != null && count == 1, $"tab for '{g.Title}' found uniquely (count={count})");
            if (tab == null || count != 1)
                continue;
            (int tx, int ty) = Uia.Center(tab);
            Input.ClickAt(tx, ty);
            Thread.Sleep(400);
            ctx.Check(TabCount(container) == 2, $"tab count still 2 after switching to '{g.Title}'");
        }

        foreach (GuestInfo g in guests)
        {
            ctx.Check(g.Proc != null && !g.Proc.HasExited
                    && ((long)NativeMethods.GetWindowLongPtr(g.Hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0,
                $"'{g.Title}' alive and still captured after the multi-browser switch pass");
        }
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "unhealthy") == 0, "no false-positive render-health 'unhealthy' verdict for either browser");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // 24. browser-soak --guest {chrome-normal|edge-normal|firefox-normal} --cycles N
    //     (docs/internal/TEST_PLAN.md 5.6): a SCOPED PROXY for long-running
    //     stability — N tab-switch cycles (default 30, several minutes) with a
    //     periodic health check, not a true multi-hour/day soak. See
    //     KNOWN_ISSUES.md for the honest scope note.
    // -------------------------------------------------------------------------
    private static void BrowserSoak(Ctx ctx, Options opt)
    {
        int cycles = opt.Cycles ?? 30;
        GuestInfo browser = SpawnGuest(ctx, opt.Guest);
        GuestInfo pig = SpawnPig(ctx, "SOAK", "--pulse", "--color", "white");
        GuestInfo[] guests = { browser, pig };
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, guests);

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");

        for (int i = 0; i < cycles; i++)
        {
            GuestInfo target = guests[i % guests.Length];
            AutomationElement? tab = FindTabText(container, target.Title, out int count);
            if (tab == null || count != 1)
            {
                ctx.Check(false, $"cycle {i + 1}/{cycles}: tab for '{target.Title}' found uniquely (count={count})");
                break;
            }
            (int tx, int ty) = Uia.Center(tab);
            Input.ClickAt(tx, ty);
            Thread.Sleep(300);

            if (i % 5 == 4)
            {
                bool ok = TabCount(container) == 2
                    && browser.Proc != null && !browser.Proc.HasExited
                    && pig.Proc != null && !pig.Proc.HasExited
                    && TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0;
                ctx.Check(ok, $"health check at cycle {i + 1}/{cycles}: 2 tabs, both guests alive, no EXCEPTION lines");
                if (!ok)
                    break;
            }
        }

        ctx.Check(browser.Proc != null && !browser.Proc.HasExited, $"real browser '{browser.Title}' survived {cycles} switch cycles");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "unhealthy") == 0, "no false-positive render-health verdict across the soak run");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines across the whole soak run");
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
