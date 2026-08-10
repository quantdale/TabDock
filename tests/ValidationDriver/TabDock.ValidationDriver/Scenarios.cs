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

internal static partial class Scenarios
{
    // Resolved relative to the driver assembly's own location (walk up to the
    // repo root, identified by TabDock.sln) so the driver runs on any machine,
    // not just the original dev box (previously hardcoded d:\Documents\... paths).
    public static readonly string TabDockExe = Path.Combine(RepoRoot, "bin", "Debug", "net8.0-windows", "win-x64", "TabDock.exe");
    public static readonly string PigExe = Path.Combine(RepoRoot, "tests", "ValidationDriver", "TabDock.GuineaPig", "bin", "Debug", "net8.0-windows", "TabDock.GuineaPig.exe");
    private static readonly string ChromeExe = FindExe(new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
    }, "chrome.exe");
    private static readonly string EdgeExe = FindExe(new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
    }, "msedge.exe");
    // Firefox is not exercised on the dev machine (see docs/internal/TEST_PLAN.md
    // section 4 and KNOWN_ISSUES.md) — the case exists so the code path is
    // written and reviewable, but it cannot be run/verified there.
    private static readonly string FirefoxExe = FindExe(new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe"),
    }, "firefox.exe");
    private const string ContentHostClass = "TabDockContentHost";

    /// <summary>
    /// Locates the repo root by walking up from the driver assembly location
    /// until the directory containing TabDock.sln is found.
    /// </summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
                dir = dir.Parent;
            if (dir == null)
                throw new InvalidOperationException($"Could not locate the TabDock repo root (TabDock.sln) above '{AppContext.BaseDirectory}'.");
            return dir.FullName;
        }
    }

    /// <summary>
    /// Picks the first well-known install path that exists; otherwise falls back
    /// to whatever a PATH lookup resolves, and finally to the bare executable
    /// name (letting Process.Start's own search produce a clear error).
    /// </summary>
    private static string FindExe(string[] candidatePaths, string exeName)
    {
        foreach (string candidate in candidatePaths)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (dir.Length == 0)
                    continue;
                string candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return exeName;
    }

    private static readonly Random Rng = new Random();

    public static readonly string[] AllOrder =
    {
        "rename", "popout", "closewin", "closewin-hide", "selfclose", "selfhide", "selfminhide",
        "tabswitch-hidesafety", "minrestore", "maximize-repro", "repeat-cycles", "crossfeature",
        "hotkey-afterclose", "persist-kill", "dragreorder", "chrometabdrag",
        "closegroupprompt", "exitpopulated",
        // expand-e2e-coverage additions: each guards an H-series bug that had no
        // automated coverage before this change. All are pig-only/hermetic (the
        // launcher-hint one is a pure UIA read) and join `all` per the spec.
        "container-minimize-retains-tabs", "hotkey-hold-single-picker", "popout-inactive-keeps-active",
        "double-capture-refused", "persist-active-tab-index", "restored-group-survives-member-reclose",
        "selfminimize-timer-vs-teardown", "launcher-empty-state-hint",
        // Vertical left/right split-screen coverage. All pig-only/hermetic (no
        // --guest needed) and join `all`; bodies live in Scenarios.Split.cs.
        "split-single-disabled", "split-two-auto", "split-select-partner", "split-exit",
        "split-resize", "split-move", "split-minrestore", "split-reorder",
        "split-popout-left", "split-popout-right", "split-selfclose", "split-native-move-reassert",
        "split-native-resize-reassert", "split-contextmenu-render-stability", "split-closebutton-left",
        "split-closebutton-right", "split-click-third",
        "split-directclick", "split-repeat-cycles", "contextmenu-render-stability",
        "chrome-click-render-stability", "tab-closebutton-popout", "tab-middleclick-popout",
        "capture-inline-ui", "group-create-inline",
        "three-app-torture",
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
    // Previously also listed hotkey-afterclose/persist-kill/dragreorder
    // and every contentinput/chromeinput/alttabinput/keyboardinput* scenario, none
    // of which read opt.Guest at all (they spawn a hardcoded pig/Chrome/Notepad
    // guest directly) — that mislabeling made Program.cs demand a bogus
    // --guest {chrome-normal|edge-normal|firefox-normal} to run them at all, which
    // in turn made "all" (which includes hotkey-afterclose/persist-kill/
    // dragreorder via AllOrder) fail its own argument validation before spawning
    // anything. Confirmed by running `all` and hitting this exact Usage() error.
    public static readonly string[] BrowserOnlyScenarios =
    {
        "browser-lifecycle", "browser-tabswitch-hidesafety", "browser-dragreorder", "browser-soak",
    };
    public static readonly string[] BrowserGuestKinds = { "chrome-normal", "edge-normal", "firefox-normal" };

    /// <summary>
    /// Scenarios that read `RunScenario`'s switch fine but were left off of
    /// every allowlist in `Program.cs`'s CLI validation when
    /// `contentinput`/`chromeinput`/`alttabinput`/`keyboardinput*` were pulled
    /// out of the mislabeled `BrowserOnlyScenarios` (see KNOWN_ISSUES.md
    /// H-NEW2) — with neither list matching, `Program.cs`'s `known` check
    /// rejected every one of them, making them uninvokable from the CLI at
    /// all. None take --guest and none belong in AllOrder/"all" (each spawns
    /// its own hardcoded pig/Chrome/Edge/Notepad guest; folding them into
    /// "all" would slow every run down for coverage the browser-* scenarios
    /// already give via an explicit --guest).
    /// </summary>
    public static readonly string[] StandaloneExtraScenarios =
    {
        "contentinput", "chromeinput", "alttabinput",
        "keyboardinput", "keyboardinput-chrome", "keyboardinput-notepad", "keyboardinput-rapid-switch",
        "keyboardinput-chrome-altswitch", "keyboardinput-edge-altswitch", "keyboardinput-chrome-omnibox-altswitch",
        "realworkflow-altswitch", "directclick-foreground-pairing", "dragout-by-titlebar",
        "crashkill-rescue", "crashkill-rapidswitch-rescue", "crashkill-selfhide-not-rescued", "realapp-multi-render",
        "instant-tabswitch", "reattach-thenclick-othertab", "reattach-repeated-cycles",
        "picker-owner-is-requesting-container", "picker-owner-falls-back-when-container-closed",
        "rename-edge-cases", "multi-group-independent-interaction", "dragreorder-then-immediate-popout",
        "keyboard-only-tab-navigation", "crashkill-during-active-drag", "dwm-transitions-disabled-on-capture",
        "dragprobe",
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
            "hotkey-afterclose" => HotkeyAfterClose,
            "persist-kill" => PersistKill,
            "dragreorder" => DragReorder,
            "dragprobe" => DragProbe,
            "chrometabdrag" => ChromeTabDrag,
            "realapp" => RealAppFillMaxHide,
            "closegroupprompt" => CloseGroupPrompt,
            "exitpopulated" => ExitPopulated,
            "container-minimize-retains-tabs" => ContainerMinimizeRetainsTabs,
            "hotkey-hold-single-picker" => HotkeyHoldSinglePicker,
            "popout-inactive-keeps-active" => PopOutInactiveKeepsActive,
            "double-capture-refused" => DoubleCaptureRefused,
            "persist-active-tab-index" => PersistActiveTabIndex,
            "restored-group-survives-member-reclose" => RestoredGroupSurvivesMemberReclose,
            "selfminimize-timer-vs-teardown" => SelfMinimizeTimerVsTeardown,
            "launcher-empty-state-hint" => LauncherEmptyStateHint,
            "split-single-disabled" => SplitSingleDisabled,
            "split-two-auto" => SplitTwoAuto,
            "split-select-partner" => SplitSelectPartner,
            "split-exit" => SplitExit,
            "split-resize" => SplitResize,
            "split-move" => SplitMove,
            "split-minrestore" => SplitMinRestore,
            "split-reorder" => SplitReorder,
            "split-popout-left" => SplitPopoutLeft,
            "split-popout-right" => SplitPopoutRight,
            "split-selfclose" => SplitSelfClose,
            "split-native-move-reassert" => SplitNativeMoveReassert,
            "split-native-resize-reassert" => SplitNativeResizeReassert,
            "split-contextmenu-render-stability" => SplitContextMenuRenderStability,
            "split-closebutton-left" => SplitCloseButtonLeft,
            "split-closebutton-right" => SplitCloseButtonRight,
            "split-click-third" => SplitClickThird,
            "split-directclick" => SplitDirectClick,
            "split-repeat-cycles" => SplitRepeatCycles,
            "contextmenu-render-stability" => ContextMenuRenderStability,
            "chrome-click-render-stability" => ChromeClickRenderStability,
            "tab-closebutton-popout" => TabCloseButtonPopout,
            "tab-middleclick-popout" => TabMiddleClickPopout,
            "capture-inline-ui" => CaptureInlineUi,
            "group-create-inline" => GroupCreateInline,
            "three-app-torture" => ThreeAppTorture,
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
            "keyboardinput-chrome-altswitch" => KeyboardInputChromeAltSwitch,
            "keyboardinput-edge-altswitch" => KeyboardInputEdgeAltSwitch,
            "keyboardinput-chrome-omnibox-altswitch" => KeyboardInputChromeOmniboxAltSwitch,
            "realworkflow-altswitch" => RealWorkflowAltSwitch,
            "directclick-foreground-pairing" => DirectClickForegroundPairing,
            "dragout-by-titlebar" => DragOutByTitlebar,
            "crashkill-rescue" => CrashKillRescue,
            "crashkill-rapidswitch-rescue" => CrashKillRapidSwitchRescue,
            "crashkill-selfhide-not-rescued" => CrashKillSelfHideNotRescued,
            "realapp-multi-render" => RealAppMultiRender,
            "instant-tabswitch" => InstantTabSwitch,
            "reattach-thenclick-othertab" => ReattachThenClickOtherTab,
            "reattach-repeated-cycles" => ReattachRepeatedCycles,
            "picker-owner-is-requesting-container" => PickerOwnerIsRequestingContainer,
            "picker-owner-falls-back-when-container-closed" => PickerOwnerFallsBackWhenContainerClosed,
            "rename-edge-cases" => RenameEdgeCases,
            "multi-group-independent-interaction" => MultiGroupIndependentInteraction,
            "dragreorder-then-immediate-popout" => DragReorderThenImmediatePopOut,
            "keyboard-only-tab-navigation" => KeyboardOnlyTabNavigation,
            "crashkill-during-active-drag" => CrashKillDuringActiveDrag,
            "dwm-transitions-disabled-on-capture" => DwmTransitionsDisabledOnCapture,
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
                            continue;
                        }
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
                () => IsDocked(g.Hwnd, host) || IsReleasedAndHidden(g.Hwnd),
                5000);
            if (!captured)
                throw new InvalidOperationException($"Guest '{g.Title}' was not captured (neither docked over host nor hidden).");
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
    /// <summary>
    /// Best-effort foreground acquisition before a blind click. Tries
    /// <see cref="Input.ForceForeground"/> first; if that fails (observed
    /// deterministically right after a "Pop out" release, where
    /// WindowShepherdService.Release explicitly foregrounds the just-released
    /// guest and Windows' foreground-lock heuristic then blocks THIS
    /// background process from immediately reclaiming it via
    /// SetForegroundWindow), fall back to confirming the intended click point
    /// is not obscured by another window. A real click there lands correctly
    /// and grants the target window foreground as a side effect purely via
    /// normal click-to-activate — exactly what a human user gets for free
    /// without ever calling SetForegroundWindow — so it is safe to proceed.
    /// </summary>
    private static bool EnsureClickable(IntPtr target, int x, int y)
    {
        if (Input.ForceForeground(target))
            return true;

        IntPtr atPoint = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
        IntPtr rootAtPoint = NativeMethods.GetAncestor(atPoint, NativeMethods.GA_ROOT);
        bool clickable = rootAtPoint == target;
        GuardedProc.Log(clickable
            ? $"  EnsureClickable: ForceForeground failed for 0x{target.ToInt64():X}, but ({x},{y}) resolves to it directly (no obscuring window) — proceeding with a real click, as a human user would."
            : $"  EnsureClickable: ForceForeground failed for 0x{target.ToInt64():X} and ({x},{y}) resolves to 0x{rootAtPoint.ToInt64():X} instead — refusing to click blind.");
        return clickable;
    }

    /// <summary>
    /// Verifies a real click at (x, y) will land on <paramref name="target"/>:
    /// WindowFromPoint at the point must resolve (via GA_ROOT) to the target.
    /// Returns Zero when clickable; otherwise logs which window actually sits
    /// at the point and returns its root HWND. Unlike
    /// <see cref="EnsureClickable"/>, this runs unconditionally — even a
    /// successful ForceForeground does not un-cover the point, and the
    /// covering window swallows both the click and any typed text.
    /// </summary>
    private static IntPtr FindObstructingWindow(IntPtr target, int x, int y)
    {
        IntPtr atPoint = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
        IntPtr rootAtPoint = NativeMethods.GetAncestor(atPoint, NativeMethods.GA_ROOT);
        if (rootAtPoint == target)
            return IntPtr.Zero;
        string windowText = rootAtPoint == IntPtr.Zero ? "(none)" : NativeMethods.GetWindowTextString(rootAtPoint) ?? "(untitled)";
        GuardedProc.Log($"  clickability: click point ({x},{y}) resolves to 0x{rootAtPoint.ToInt64():X} '{windowText}' instead of 0x{target.ToInt64():X} — target obscured, skipping this attempt.");
        return rootAtPoint;
    }

    private static void ClickTabMenuItem(Ctx ctx, IntPtr container, string guestTitle, string menuItemName)
    {
        AutomationElement? tab = FindTabText(container, guestTitle, out int count);
        if (tab == null || count != 1)
            throw new InvalidOperationException($"Tab for '{guestTitle}' not found uniquely (count={count}).");

        (int tx, int ty) = Uia.Center(tab);
        if (!EnsureClickable(container, tx, ty))
            throw new InvalidOperationException("Could not bring the container to the foreground and the tab is obscured — refusing to click blind.");
        Input.RightClickAt(tx, ty);

        AutomationElement? mi = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, menuItemName, 5000);
        if (mi == null)
            throw new InvalidOperationException($"Context menu item '{menuItemName}' did not appear within 5s.");

        // A menu item found in a popup that is mid-close or not yet laid out
        // reports an empty bounding rect (Rect.Empty → int.MinValue centers).
        // Clicking that would send a real mouse click to (0,0). Wait briefly
        // for a genuine, clickable rect before proceeding.
        System.Windows.Rect itemRect = Uia.GetElementRect(mi);
        var rectSw = Stopwatch.StartNew();
        while ((itemRect.IsEmpty || itemRect.Width <= 0 || itemRect.Height <= 0) && rectSw.ElapsedMilliseconds < 2000)
        {
            Thread.Sleep(100);
            mi = Uia.FindMenuItemOnDesktop(ctx.TabDockPid, menuItemName, 3000);
            if (mi == null)
                break;
            itemRect = Uia.GetElementRect(mi);
        }
        if (itemRect.IsEmpty || itemRect.Width <= 0 || itemRect.Height <= 0)
            throw new InvalidOperationException($"Context menu item '{menuItemName}' was found but never displayed with a real bounding rect.");
        if (mi == null)
            throw new InvalidOperationException($"Context menu item '{menuItemName}' disappeared after its bounding rect was read.");

        Thread.Sleep(150);
        (int mx, int my) = Uia.Center(mi);
        Input.ClickAt(mx, my);
        Thread.Sleep(300);
    }

    /// <summary>Real-clicks the container's maximize caption button (2nd of 46px-wide buttons from the right, DPI-scaled).</summary>
    private static void ClickMaximizeButton(IntPtr container)
    {
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        double scale = NativeMethods.GetDpiForWindow(container) / 96.0;
        int x = rc.right - (int)(1.5 * 46 * scale);
        int y = rc.top + (int)(16 * scale);
        if (!EnsureClickable(container, x, y))
            throw new InvalidOperationException("Could not bring the container to the foreground and its maximize button is obscured — refusing to click blind.");
        GuardedProc.Log($"  Clicking maximize button at ({x},{y}) (container {Util.FormatRect(rc)}, dpiScale {scale:F2}).");
        Input.ClickAt(x, y);
    }

    /// <summary>
    /// Screen-coordinate center of a header caption icon-button, counting the row of
    /// 46px-wide CaptionButtonStyle buttons (Views/ContainerWindow.xaml) from the
    /// right: 0=Close, 1=Maximize, 2=Minimize, 3=Add window. Mirrors
    /// ClickMaximizeButton's own DPI-scaled math (index 1) for the buttons that
    /// helper does not cover.
    /// </summary>
    private static (int X, int Y) CaptionButtonCenterFromRight(IntPtr container, int indexFromRight)
    {
        NativeMethods.GetWindowRect(container, out NativeMethods.RECT rc);
        double scale = NativeMethods.GetDpiForWindow(container) / 96.0;
        int x = rc.right - (int)((indexFromRight + 0.5) * 46 * scale);
        int y = rc.top + (int)(16 * scale);
        return (x, y);
    }

    /// <summary>
    /// Real-clicks the container's minimize caption button (3rd of 46px-wide
    /// buttons from the right, DPI-scaled) — same pixel-offset technique as
    /// ClickMaximizeButton, which this container's plain WPF Button (no
    /// AutomationProperties.Name set, only a ToolTip) does not reliably expose
    /// a distinguishable UIA Name for.
    /// </summary>
    private static void ClickMinimizeButton(IntPtr container)
    {
        (int x, int y) = CaptionButtonCenterFromRight(container, 2);
        if (!EnsureClickable(container, x, y))
            throw new InvalidOperationException("Could not bring the container to the foreground and its minimize button is obscured — refusing to click blind.");
        GuardedProc.Log($"  Clicking minimize button at ({x},{y}).");
        Input.ClickAt(x, y);
    }

    /// <summary>
    /// Real-clicks the container's "+" (add window to group) caption button.
    /// Tries a UIA Name match first ("Add window to group" is the button's
    /// ToolTip in Views/ContainerWindow.xaml); WPF's ButtonBaseAutomationPeer
    /// does not promote ToolTipService.ToolTip into the automation Name (only
    /// HelpText), so in practice this UIA lookup is expected to miss and fall
    /// through to the same DPI-scaled pixel-offset technique ClickMaximizeButton
    /// already uses for the rest of this button row (4th button from the right,
    /// after Minimize/Maximize/Close) — kept as the first attempt anyway since
    /// a future template change could add an explicit AutomationProperties.Name.
    /// </summary>
    private static void ClickAddWindowButton(IntPtr container)
    {
        // Coordinates are resolvable via UIA/GetWindowRect without needing the
        // container to be foreground yet, so compute them first and let
        // EnsureClickable fall back to a point-obscured check if a plain
        // ForceForeground fails.
        AutomationElement? containerEl = Uia.FromHwnd(container);
        int count = 0;
        AutomationElement? addBtn = containerEl == null
            ? null
            : Uia.FindDescendantByName(containerEl, ControlType.Button, "Add window to group", null, out count);
        int x, y;
        if (addBtn != null && count == 1)
        {
            (x, y) = Uia.Center(addBtn);
        }
        else
        {
            (x, y) = CaptionButtonCenterFromRight(container, 3);
            GuardedProc.Log($"  ClickAddWindowButton: UIA Name lookup found {count} match(es) for 'Add window to group'; falling back to the pixel-offset caption-button position ({x},{y}).");
        }

        if (!EnsureClickable(container, x, y))
            throw new InvalidOperationException("Could not bring the container to the foreground and its 'Add window' button is obscured — refusing to click blind.");
        Input.ClickAt(x, y);
    }

    /// <summary>
    /// Re-captures already-known guest(s) back into an EXISTING container's group
    /// via that container's own "+" add-window button — which auto-preselects the
    /// SAME group in the picker's "Add to" ComboBox (App.ShowCapturePicker's
    /// preselectedGroup path sets pickerVm.SelectedGroupOption before the picker
    /// is shown) — rather than always landing in a fresh "&lt;New group&gt;" the way
    /// CaptureIntoGroup's hotkey-opened picker does (CaptureIntoGroup never
    /// touches the ComboBox, so CapturePickerViewModel.Refresh's default
    /// SelectedGroupOption, index 0 = "&lt;New group&gt;", is whatever it opens with).
    /// Verifies no second container is created as a side effect of the reattach.
    /// </summary>
    private static void CaptureIntoExistingGroupViaAddButton(Ctx ctx, IntPtr existingContainer, IntPtr host, params GuestInfo[] guests)
    {
        var before = new HashSet<IntPtr>(Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true));

        ClickAddWindowButton(existingContainer);
        IntPtr pickerHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 10000);
        if (pickerHwnd == IntPtr.Zero)
            throw new InvalidOperationException("'Capture windows' picker did not appear within 10s from the container's '+' button.");
        AutomationElement? picker = Uia.FromHwnd(pickerHwnd);
        if (picker == null)
            throw new InvalidOperationException("Picker HWND found but UIA FromHandle failed.");
        if (!Input.ForceForeground(pickerHwnd))
            throw new InvalidOperationException("Could not bring the capture picker to the foreground — refusing to click blind.");
        Thread.Sleep(600);
        Thread.Sleep(1000);

        foreach (GuestInfo g in guests)
        {
            // Same robust row-find loop as CaptureIntoGroup (scroll/refresh retry
            // for a virtualized or not-yet-enumerated row) — duplicated rather
            // than shared because CaptureIntoGroup must not be modified.
            AutomationElement? row = null;
            var rowSw = Stopwatch.StartNew();
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

            AutomationElement? textEl = Uia.FindDescendantByName(picker, ControlType.Text, null, g.Title, out int textCount);
            if (textEl == null || textCount != 1)
                throw new InvalidOperationException($"Picker text label for '{g.Title}' not found uniquely (count={textCount}) — cannot toggle safely.");

            bool toggledOn = false;
            for (int attempt = 0; attempt < 3 && !toggledOn; attempt++)
            {
                Rect r = Uia.GetElementRect(row);
                (int cx, int cy) = attempt switch
                {
                    0 => Uia.Center(textEl),
                    1 => ((int)(r.X + 5), (int)(r.Y + r.Height / 2)),
                    _ => Uia.Center(row),
                };
                Input.ClickAt(cx, cy);
                Thread.Sleep(350);
                toggledOn = Uia.GetToggleState(row) == System.Windows.Automation.ToggleState.On;
            }
            if (!toggledOn)
            {
                try
                {
                    if (row.TryGetCurrentPattern(TogglePattern.Pattern, out object pattern))
                    {
                        ((TogglePattern)pattern).Toggle();
                        Thread.Sleep(200);
                        toggledOn = Uia.GetToggleState(row) == System.Windows.Automation.ToggleState.On;
                    }
                }
                catch (Exception ex)
                {
                    GuardedProc.Log($"  CaptureIntoExistingGroupViaAddButton: toggle pattern fallback threw: {ex.Message}");
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
        Input.ClickAt(bx, by);
        Util.WaitUntil(() => !NativeMethods.IsWindow(pickerHwnd), 5000);

        var after = new HashSet<IntPtr>(Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true));
        List<IntPtr> newWindows = after.Except(before).ToList();
        ctx.Check(newWindows.Count == 0,
            $"reattach via the container's '+' button created no NEW top-level window (targeted the existing group, not a fresh one) — {newWindows.Count} unexpected new window(s)");

        foreach (GuestInfo g in guests)
        {
            bool captured = Util.WaitUntil(() => IsDocked(g.Hwnd, host) || IsReleasedAndHidden(g.Hwnd), 5000);
            ctx.Check(captured, $"'{g.Title}' reattached into the existing container (docked over host or hidden inactive tab)");
        }
        Thread.Sleep(500);
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

    /// <summary>True if the guest is a real, visible top-level window positioned exactly over the host's content area — the Shepherd "docked, active tab" state. Never WS_CHILD; this is the only reliable signal.</summary>
    private static bool IsDocked(IntPtr guest, IntPtr host)
    {
        return NativeMethods.IsWindow(guest) && NativeMethods.IsWindowVisible(guest)
            && GuestMatchesHost(guest, host, out _);
    }

    /// <summary>True if the guest is visible but NOT docked over the host — i.e. released back to its own placement (or never captured).</summary>
    private static bool IsReleasedAndShown(IntPtr guest, IntPtr host)
    {
        return NativeMethods.IsWindow(guest) && NativeMethods.IsWindowVisible(guest) && !IsDocked(guest, host);
    }

    /// <summary>True if the guest still exists but is hidden — either an inactive captured tab, or released-while-hidden (guest-initiated hide-on-close).</summary>
    private static bool IsReleasedAndHidden(IntPtr guest)
    {
        return NativeMethods.IsWindow(guest) && !NativeMethods.IsWindowVisible(guest);
    }

    /// <summary>
    /// Walks GW_HWNDNEXT from <paramref name="hwnd"/>, skipping invisible
    /// windows — Windows inserts invisible per-thread IME helper windows
    /// (MSCTFIME UI, Default IME) into the z-order next to whatever window a
    /// thread just touched, unrelated to any real z-order pairing under test.
    /// </summary>
    private static IntPtr NextVisibleWindow(IntPtr hwnd)
    {
        IntPtr cur = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT);
        while (cur != IntPtr.Zero && !NativeMethods.IsWindowVisible(cur))
            cur = NativeMethods.GetWindow(cur, NativeMethods.GW_HWNDNEXT);
        return cur;
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

    private static bool IsReleased(GuestInfo g, IntPtr host)
    {
        return IsReleasedAndShown(g.Hwnd, host);
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
        ctx.Check(IsReleasedAndHidden(pig.Hwnd), "pig released and hidden (guest-initiated hide)");
    }

    /// <summary>
    /// Real-clicks a GUEST's own native title-bar minimize button (the standard
    /// Windows caption chrome a shepherded guest keeps), distinct from
    /// ClickMinimizeButton which targets the container's custom WPF chrome. Finds
    /// the button via UIA by its localized name first (standard caption buttons
    /// are exposed as Button elements named "Minimize"); falls back to the same
    /// DPI-scaled 46px caption-button pixel math the container helpers use
    /// (minimize = 3rd button from the right).
    /// </summary>
    private static void ClickNativeMinimizeButton(IntPtr guest)
    {
        int x, y;
        AutomationElement? el = Uia.FromHwnd(guest);
        int count = 0;
        AutomationElement? minBtn = el == null
            ? null
            : Uia.FindDescendantByName(el, ControlType.Button, "Minimize", null, out count);
        if (minBtn != null && count == 1)
        {
            (x, y) = Uia.Center(minBtn);
            GuardedProc.Log($"  ClickNativeMinimizeButton: UIA 'Minimize' button at ({x},{y}).");
        }
        else
        {
            NativeMethods.GetWindowRect(guest, out NativeMethods.RECT rc);
            double scale = NativeMethods.GetDpiForWindow(guest) / 96.0;
            x = rc.right - (int)(2.5 * 46 * scale);
            y = rc.top + (int)(16 * scale);
            GuardedProc.Log($"  ClickNativeMinimizeButton: UIA 'Minimize' not found (count={count}); pixel offset ({x},{y}).");
        }
        if (!EnsureClickable(guest, x, y))
            throw new InvalidOperationException("Could not bring the guest to the foreground and its minimize button is obscured — refusing to click blind.");
        Input.ClickAt(x, y);
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

    /// <summary>
    /// Counts open container/group windows for this TabDock instance (title
    /// starts with "Group" or "TDVAL-", the same prefix convention Cleanup()
    /// uses), excluding the main launcher window. Used to positively assert
    /// "exactly one container is open" after an action that must target an
    /// EXISTING group rather than accidentally creating a new one.
    /// </summary>
    private static int CountOpenContainers(Ctx ctx)
    {
        int n = 0;
        foreach (IntPtr h in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true))
        {
            if (h == ctx.MainHwnd)
                continue;
            string t = NativeMethods.GetWindowTextString(h) ?? string.Empty;
            if (t.StartsWith("Group", StringComparison.Ordinal) || t.StartsWith("TDVAL-", StringComparison.Ordinal))
                n++;
        }
        return n;
    }
}
