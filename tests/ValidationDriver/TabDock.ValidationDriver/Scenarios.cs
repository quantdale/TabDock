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
    public string Configuration = "Debug";
    public string Rid = "auto";
    public string? TabDockPath;
    public string? GuineaPigPath;
    public string? Shard;
}

/// <summary>A window under test: a guinea pig or a real app (wt/chrome) for maximize-repro.</summary>
internal sealed class GuestInfo
{
    public Process? Proc;
    public uint Pid;
    public IntPtr Hwnd;
    public string Title = string.Empty;
    public WindowIdentity? Identity;
    public bool IsPig;
    public string Role = "ControlledGuest";

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

internal enum QualificationStatus
{
    Pass,
    Fail,
    Skip,
    Blocked,
}

internal sealed record AssertionEvidence(string Name, bool Passed);

/// <summary>Per-scenario state: the TabDock instance, spawned guests, containers, and assertion results.</summary>
internal sealed class Ctx
{
    public string Name = string.Empty;
    public Process TabDock = null!;
    public uint TabDockPid;
    public IntPtr MainHwnd;
    public string MainClassName = string.Empty;
    public WindowIdentity? MainIdentity;
    public long LogOffset;
    public readonly List<GuestInfo> Guests = new List<GuestInfo>();
    public readonly List<IntPtr> Containers = new List<IntPtr>();
    public readonly List<WindowIdentity> ContainerIdentities = new List<WindowIdentity>();
    public bool Pass = true;
    public QualificationStatus Status { get; private set; } = QualificationStatus.Pass;
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public readonly List<AssertionEvidence> Assertions = new();
    public readonly List<string> FailureReasons = new();
    public string? ExpectedState { get; set; }
    public string? ObservedState { get; set; }
    // Captured immediately before cleanup so the result artifact describes
    // the state that was qualified, not the deliberately torn-down state.
    public object[]? LiveVisibleHwndSet { get; set; }
    public object[]? LiveGuestRectangles { get; set; }
    public object[]? LivePaneRectangles { get; set; }
    public object[]? LiveClientRenderingEvidence { get; set; }
    public string? LiveForegroundHwnd { get; set; }
    public string[]? LiveSplitRelationshipMembers { get; set; }
    public bool? LiveSplitPairPresented { get; set; }
    public string? LiveActiveGuest { get; set; }

    public void Check(bool condition, string what)
    {
        GuardedProc.Log($"  {(condition ? "PASS" : "FAIL")}: {what}");
        Assertions.Add(new AssertionEvidence(what, condition));
        if (!condition)
        {
            Pass = false;
            // A failed assertion is authoritative regardless of earlier skips:
            // a scenario that skipped a prerequisite and then failed a real
            // check must report FAIL, never remain green SKIP.
            Status = QualificationStatus.Fail;
            FailureReasons.Add(what);
        }
    }

    public void Skip(string reason)
    {
        GuardedProc.Log($"  SKIP: {reason}");
        // Skips never mask an already-decided failure.
        if (Status != QualificationStatus.Fail)
            Status = QualificationStatus.Skip;
        Pass = Pass && Status != QualificationStatus.Fail;
        FailureReasons.Add(reason);
    }

    public void Block(string reason)
    {
        GuardedProc.Log($"  BLOCKED: {reason}");
        Status = QualificationStatus.Blocked;
        Pass = false;
        FailureReasons.Add(reason);
    }
}

internal static partial class Scenarios
{
    // Resolved relative to the driver assembly's own location (walk up to the
    // repo root, identified by TabDock.sln) so the driver runs on any machine,
    // not just the original dev box (previously hardcoded d:\Documents\... paths).
    // Program.ConfigureArtifacts replaces these with the requested Debug/Release
    // and RID-aware paths before any scenario can spawn a process.
    public static string TabDockExe { get; private set; } = string.Empty;
    public static string PigExe { get; private set; } = string.Empty;
    public static string SelectedConfiguration { get; private set; } = "Debug";
    public static string SelectedRid { get; private set; } = "auto";
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
    private static readonly string BraveExe = FindExe(new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
    }, "brave.exe");
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

    private static bool IsExecutableAvailable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return false;
        if (Path.IsPathRooted(executable))
            return File.Exists(executable);
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return false;
        return pathEnv
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, executable)));
    }

    private static readonly Random Rng = new Random();

    // Keep each real-input process comfortably below the fixed 10-minute
    // GuardedProc budget. The original split category grew to 30 scenarios and
    // hit that budget before its final cases; these explicit groups preserve
    // logical coverage without increasing any safety timeout or spawn cap.
    public static readonly string[] SplitCoreScenarios =
    {
        "split-single-disabled", "split-two-auto", "split-select-partner", "split-exit",
        "split-resize", "split-move", "split-minrestore", "split-reorder",
        "split-popout-left", "split-popout-right",
    };

    public static readonly string[] SplitRenderScenarios =
    {
        "split-selfclose", "split-native-move-reassert", "split-native-resize-reassert",
        "split-contextmenu-render-stability", "split-closebutton-left", "split-closebutton-right",
        "split-click-third", "split-third-tab-hover-persists", "split-third-tab-click-persists",
        "split-four-tab-nonmember-switching",
        "split-three-app-client-settle",
        "split-diagnostic-snapshot",
        "split-dormant-member-removal",
        "split-drag-release-render-stability", "drag-release-render-stability",
        "split-guest-does-not-overflow-pane", "split-narrow-container-constraints",
    };

    public static readonly string[] SplitFocusScenarios =
    {
        "split-directclick", "split-repeat-cycles", "split-composite",
        "split-three-tab-partner-popout", "split-focus-bidirectional",
        "split-partner-permutation", "split-maximize-restore-no-overlap",
    };

    public static readonly string[] OrchestratedShardNames =
    {
        "core-lifecycle", "capture-group", "split-core", "split-render", "split-focus", "drag-z-order",
        "crash-recovery", "keyboard-input", "dpi-multi-monitor", "startup", "diagnostics",
    };

    public static readonly string[] ExplicitOnlyShardNames =
    {
        "browser", "real-app",
    };

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
        "split-third-tab-hover-persists", "split-third-tab-click-persists",
        "split-four-tab-nonmember-switching",
        "split-three-app-client-settle",
        "split-diagnostic-snapshot",
        "split-dormant-member-removal",
        "split-drag-release-render-stability", "drag-release-render-stability",
        "split-directclick", "split-repeat-cycles", "contextmenu-render-stability",
        "chrome-click-render-stability", "tab-closebutton-popout", "tab-middleclick-popout",
        "capture-inline-ui", "group-create-inline",
        "three-app-torture",
        "group-dropdown-stability", "add-window-toggle", "group-rename-menu",
        "group-delete-populated", "split-composite",
        // Split-symmetry / window-state hardening (goal §4-§14): regression
        // scenarios for the partner-member pop-out defect, bidirectional
        // member focus, initiator/partner permutation, and maximize/restore
        // pane partitioning.
        "split-three-tab-partner-popout", "split-focus-bidirectional",
        "split-partner-permutation", "split-maximize-restore-no-overlap",
        // Post-audit containment (guest native-minimum) scenarios.
        "split-guest-does-not-overflow-pane", "split-narrow-container-constraints",
        "single-guest-does-not-overflow-content",
        "hung-guest-mintrack",
        "crashkill-maximized-recovery", "crashkill-minimized-recovery",
        "crashkill-split-rescue",
        "startup-local-stack-above-unrelated-when-guest-present",
        // R22 qualification torture harness (Scenarios.Torture.cs): hermetic
        // pig-only soaks; categorized in GetScenarioShard's torture- block.
        "torture-tabswitch-rapid", "torture-tabswitch-random", "torture-split-member-destroy",
        "torture-closegroup-same-process", "torture-minrestore-soak", "torture-crash-restart-soak",
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
        "browser-split-persistent-render",
    };
    public static readonly string[] BrowserGuestKinds = { "chrome-normal", "edge-normal", "brave-normal", "firefox-normal" };

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
"capture-dpi-unaware-guest",
"capture-dpi-system-guest",
        "split-comparison-observe",
        "startup-group-not-hidden-behind-existing-window",
        "startup-does-not-steal-foreground-after-external-activation",
    };

    /// <summary>
    /// Selects the application and GuineaPig artifacts for one driver process.
    /// The default is deterministic discovery of the RID-specific output, with
    /// a no-RID fallback for projects (such as GuineaPig) that are normally
    /// built without a runtime identifier. Explicit paths always win.
    /// </summary>
    public static void ConfigureArtifacts(string configuration, string rid, string? tabDockPath, string? guineaPigPath)
    {
        SelectedConfiguration = configuration;
        SelectedRid = rid;
        TabDockExe = ResolveArtifact(
            tabDockPath,
            "TabDock",
            Path.Combine("bin", configuration, "net8.0-windows"),
            "TabDock.exe",
            rid);
        PigExe = ResolveArtifact(
            guineaPigPath,
            "GuineaPig",
            Path.Combine("tests", "ValidationDriver", "TabDock.GuineaPig", "bin", configuration, "net8.0-windows"),
            "TabDock.GuineaPig.exe",
            rid);
    }

    private static string ResolveArtifact(string? explicitPath, string label, string relativeDirectory, string fileName, string rid)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var candidates = new List<string>();
        if (!string.Equals(rid, "none", StringComparison.OrdinalIgnoreCase))
        {
            string selectedRid = string.Equals(rid, "auto", StringComparison.OrdinalIgnoreCase) ? "win-x64" : rid;
            candidates.Add(Path.Combine(RepoRoot, relativeDirectory, selectedRid, fileName));
        }
        candidates.Add(Path.Combine(RepoRoot, relativeDirectory, fileName));

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        // Return the most likely path so the caller's diagnostic names the
        // requested configuration/RID rather than hiding the missing artifact.
        return candidates[0];
    }

    /// <summary>
    /// Returns the stable category for every registered scenario. A new
    /// scenario must be assigned here; ValidateShardCoverage is called at
    /// startup and fails closed if registration and categorization diverge.
    /// </summary>
    public static string? GetScenarioShard(string name)
    {
        if (name == "realapp" || name == "realapp-multi-render")
            return "real-app";
        if (name == "browser-multi" || Array.IndexOf(BrowserOnlyScenarios, name) >= 0)
            return "browser";
        // R22 qualification torture harness (Scenarios.Torture.cs). Shard
        // mapping follows each soak's dominant cost driver: tab-switch soaks
        // are click/input churn (keyboard-input), member-destroy drives the
        // split relationship lifecycle (split-core), the close-group prompt
        // exercises multi-capture grouping (capture-group), the min/restore
        // soak is split presentation focus (split-focus), and the crash/restart
        // soak is rescue-journal recovery (crash-recovery).
        if (name.StartsWith("torture-", StringComparison.Ordinal))
        {
            return name switch
            {
                "torture-tabswitch-rapid" or "torture-tabswitch-random" => "keyboard-input",
                "torture-split-member-destroy" => "split-core",
                "torture-closegroup-same-process" => "capture-group",
                "torture-minrestore-soak" => "split-focus",
                "torture-crash-restart-soak" => "crash-recovery",
                _ => null,
            };
        }
        if (name.StartsWith("crashkill-", StringComparison.Ordinal))
            return "crash-recovery";
        if (Array.IndexOf(SplitCoreScenarios, name) >= 0)
            return "split-core";
        if (Array.IndexOf(SplitRenderScenarios, name) >= 0)
            return "split-render";
        if (Array.IndexOf(SplitFocusScenarios, name) >= 0)
            return "split-focus";
        if (name == "split-comparison-observe")
            return "split-render";
        if (name.StartsWith("split-", StringComparison.Ordinal) || name == "split")
            return null;
        if (name.Contains("drag", StringComparison.Ordinal)
            || name.Contains("contextmenu", StringComparison.Ordinal)
            || name.Contains("chrome-click", StringComparison.Ordinal)
            || name.Contains("directclick", StringComparison.Ordinal)
            || name == "tab-middleclick-popout")
            return "drag-z-order";
        if (name.Contains("keyboard", StringComparison.Ordinal)
            || name.Contains("input", StringComparison.Ordinal)
            || name.Contains("alttab", StringComparison.Ordinal)
            || name.Contains("altswitch", StringComparison.Ordinal)
            || name == "realworkflow-altswitch")
            return "keyboard-input";
        if (name.StartsWith("startup-", StringComparison.Ordinal))
            return "startup";
        if (name.Contains("dpi", StringComparison.Ordinal)
            || name.Contains("mintrack", StringComparison.Ordinal)
            || name == "maximize-repro"
            || name == "minrestore"
            || name == "container-minimize-retains-tabs"
            || name == "selfminimize-timer-vs-teardown")
            return "dpi-multi-monitor";
        if (name.Contains("picker", StringComparison.Ordinal)
            || name.Contains("dwm", StringComparison.Ordinal)
            || name == "capture-inline-ui"
            || name == "launcher-empty-state-hint"
            || name == "add-window-toggle")
            return "diagnostics";
        if (name.Contains("capture", StringComparison.Ordinal)
            || name.Contains("group", StringComparison.Ordinal)
            || name.Contains("persist", StringComparison.Ordinal)
            || name.Contains("reattach", StringComparison.Ordinal)
            || name == "instant-tabswitch"
            || name == "double-capture-refused"
            || name == "three-app-torture"
            || name == "tab-closebutton-popout")
            return "capture-group";

        // These are intentionally the small, ordinary lifecycle set. Keeping
        // this explicit makes a future registration fail validation instead of
        // silently disappearing into an arbitrary shard.
        switch (name)
        {
            case "rename":
            case "rename-edge-cases":
            case "popout":
            case "closewin":
            case "closewin-hide":
            case "selfclose":
            case "selfhide":
            case "selfminhide":
            case "tabswitch-hidesafety":
            case "repeat-cycles":
            case "crossfeature":
            case "hotkey-afterclose":
            case "hotkey-hold-single-picker":
            case "closegroupprompt":
            case "exitpopulated":
            case "popout-inactive-keeps-active":
            case "multi-group-independent-interaction":
            case "single-guest-does-not-overflow-content":
                return "core-lifecycle";
            default:
                return null;
        }
    }

    public static void ValidateShardCoverage()
    {
        var registered = AllOrder
            .Concat(BrowserOnlyScenarios)
            .Concat(StandaloneExtraScenarios)
            .Concat(new[] { "realapp", "browser-multi" })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var unknown = registered
            .Where(name => GetScenarioShard(name) == null)
            .ToArray();
        if (unknown.Length != 0)
            throw new InvalidOperationException("Uncategorized ValidationDriver scenario(s): " + string.Join(", ", unknown));

        foreach (string shard in OrchestratedShardNames.Concat(ExplicitOnlyShardNames))
        {
            if (GetShardScenarios(shard).Count == 0)
                throw new InvalidOperationException($"ValidationDriver shard '{shard}' has no scenarios.");
        }
    }

    public static IReadOnlyList<string> GetShardScenarios(string shard)
    {
        if (string.Equals(shard, "browser", StringComparison.Ordinal))
            return BrowserOnlyScenarios.Concat(new[] { "browser-multi" }).ToArray();
        if (string.Equals(shard, "real-app", StringComparison.Ordinal))
            return new[] { "realapp", "realapp-multi-render" };

        return AllOrder
            .Concat(StandaloneExtraScenarios)
            .Where(name => string.Equals(GetScenarioShard(name), shard, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

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
            "split-third-tab-hover-persists" => SplitThirdTabHoverPersists,
            "split-third-tab-click-persists" => SplitThirdTabClickPersists,
            "split-four-tab-nonmember-switching" => SplitFourTabNonmemberSwitching,
            "split-three-app-client-settle" => SplitThreeAppClientSettle,
            "split-diagnostic-snapshot" => SplitDiagnosticSnapshot,
            "split-dormant-member-removal" => SplitDormantMemberRemoval,
            "split-comparison-observe" => SplitComparisonObserve,
            "split-drag-release-render-stability" => SplitDragReleaseRenderStability,
            "drag-release-render-stability" => DragReleaseRenderStability,
            "split-directclick" => SplitDirectClick,
            "split-repeat-cycles" => SplitRepeatCycles,
            "contextmenu-render-stability" => ContextMenuRenderStability,
            "chrome-click-render-stability" => ChromeClickRenderStability,
            "tab-closebutton-popout" => TabCloseButtonPopout,
            "tab-middleclick-popout" => TabMiddleClickPopout,
            "capture-inline-ui" => CaptureInlineUi,
            "group-create-inline" => GroupCreateInline,
            "group-dropdown-stability" => GroupDropdownStability,
            "add-window-toggle" => AddWindowToggle,
            "group-rename-menu" => GroupRenameMenu,
            "group-delete-populated" => GroupDeletePopulated,
            "split-composite" => SplitComposite,
            "split-three-tab-partner-popout" => SplitThreeTabPartnerPopout,
            "split-focus-bidirectional" => SplitFocusBidirectional,
            "split-partner-permutation" => SplitPartnerPermutation,
            "split-maximize-restore-no-overlap" => SplitMaximizeRestoreNoOverlap,
            "split-guest-does-not-overflow-pane" => SplitGuestDoesNotOverflowPane,
            "split-narrow-container-constraints" => SplitNarrowContainerConstraints,
            "single-guest-does-not-overflow-content" => SingleGuestDoesNotOverflowContent,
            "hung-guest-mintrack" => HungGuestMinTrack,
            "crashkill-maximized-recovery" => CrashKillMaximizedRecovery,
            "crashkill-minimized-recovery" => CrashKillMinimizedRecovery,
            "crashkill-split-rescue" => CrashKillSplitRescue,
"capture-dpi-unaware-guest" => CaptureDpiUnawareGuest,
"capture-dpi-system-guest" => CaptureDpiSystemGuest,
            "three-app-torture" => ThreeAppTorture,
            "torture-tabswitch-rapid" => TortureTabSwitchRapid,
            "torture-tabswitch-random" => TortureTabSwitchRandom,
            "torture-split-member-destroy" => TortureSplitMemberDestroy,
            "torture-closegroup-same-process" => TortureCloseGroupSameProcess,
            "torture-minrestore-soak" => TortureMinRestoreSoak,
            "torture-crash-restart-soak" => TortureCrashRestartSoak,
            "browser-lifecycle" => BrowserLifecycle,
            "browser-tabswitch-hidesafety" => BrowserTabSwitchHideSafety,
            "browser-dragreorder" => BrowserDragReorder,
            "browser-multi" => BrowserMulti,
            "browser-soak" => BrowserSoak,
            "browser-split-persistent-render" => BrowserSplitPersistentRender,
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
            "startup-group-not-hidden-behind-existing-window" => StartupGroupNotHiddenBehindExistingWindow,
            "startup-does-not-steal-foreground-after-external-activation" => StartupDoesNotStealForegroundAfterExternalActivation,
            "startup-local-stack-above-unrelated-when-guest-present" => StartupLocalStackAboveUnrelatedWhenGuestPresent,
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
                ctx.Check(false, "aborted: bounded budget or cancellation");
            GuardedProc.Log("  ABORTED: overall time budget exceeded or Ctrl+C.");
            throw;
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  ERROR: {ex.Message}");
            if (ctx != null)
                ctx.Check(false, $"unhandled exception: {ex.GetType().Name}");
        }
        finally
        {
            if (ctx != null)
            {
                QualificationResultWriter.CaptureLiveEvidence(ctx);
                Cleanup(ctx);
                ctx.Check(NoSpawnedGuestWindowsRemain(ctx), "cleanup left no spawned guest top-level windows");
                ctx.FinishedUtc = DateTimeOffset.UtcNow;
                QualificationResultWriter.WriteScenario(ctx);
            }
            else
            {
                // StartScenario can fail after isolating state.json but before
                // it returns a context (for example, a spawned TabDock never
                // creates its MainWindow). Clean tracked processes first so a
                // partial app cannot write over the restored user state, then
                // apply the snapshot directly.
                GuardedProc.CleanupTrackedProcesses();
                RestoreStateSnapshot();
                GuardedProc.Log("  Cleanup: setup failed before context creation; state snapshot restored.");
            }
            GuardedProc.Log($"SCENARIO {name}: {(ctx == null ? "FAIL" : ctx.Status.ToString().ToUpperInvariant())}");
        }
        return ctx != null
            && (ctx.Status is QualificationStatus.Pass or QualificationStatus.Skip);
    }

    // -------------------------------------------------------------------------
    // Common setup / teardown
    // -------------------------------------------------------------------------
    private static string StateJsonPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabDock", "state.json");

    private static string BackupStateJsonPath => StateJsonPath + ".bak";

    /// <summary>Disk mirror of the state.json snapshot (write-ahead backup; see StartScenario).</summary>
    private static string SnapshotJsonPath => StateJsonPath + ".driver-snapshot";

    private static string SnapshotBackupJsonPath => BackupStateJsonPath + ".driver-snapshot";

    /// <summary>Same-directory staging paths used to keep snapshot moves atomic.</summary>
    private static string SnapshotTempJsonPath => StateJsonPath + ".driver-snapshot.tmp";
    private static string RestoreTempJsonPath => StateJsonPath + ".driver-restore.tmp";
    private static string SnapshotBackupTempJsonPath => BackupStateJsonPath + ".driver-snapshot.tmp";
    private static string RestoreBackupTempJsonPath => BackupStateJsonPath + ".driver-restore.tmp";

    /// <summary>
    /// Per-scenario snapshots of the user's state.json and state.json.bak
    /// (null = file absent), mirrored to disk as write-ahead backups so a driver
    /// crash mid-scenario can never lose the user's persisted state or backup.
    /// </summary>
    private static string? s_savedStateJson;
    private static string? s_savedBackupStateJson;
    private static bool s_snapshotReady;
    private static bool s_backupSnapshotReady;
    private static bool s_isolationReady;

    private static Ctx StartScenario(string name)
    {
        GuardedProc.ResetScenarioBudget();
        Input.ResetIdentityScope(name);
        s_snapshotReady = false;
        s_backupSnapshotReady = false;
        s_isolationReady = false;

        // Hermetic persisted state: snapshot both state.json and its backup to
        // disk and memory before clearing them for this scenario. The product
        // deliberately recovers a valid .bak when the primary is missing, so
        // clearing only state.json would let stale validation data repopulate
        // every supposedly empty run. Cleanup restores both files after the
        // scenario's TabDock has exited.
        try
        {
            // Remove only staging files left by an interrupted copy. The
            // completed disk snapshot remains authoritative until cleanup has
            // restored state.json and removed it.
            if (File.Exists(SnapshotTempJsonPath))
                File.Delete(SnapshotTempJsonPath);
            if (File.Exists(RestoreTempJsonPath))
                File.Delete(RestoreTempJsonPath);
            if (File.Exists(SnapshotBackupTempJsonPath))
                File.Delete(SnapshotBackupTempJsonPath);
            if (File.Exists(RestoreBackupTempJsonPath))
                File.Delete(RestoreBackupTempJsonPath);

            // A previous run may have crashed before Cleanup could restore. In
            // that case the disk snapshot is the authoritative copy of the
            // user's state, even if TabDock managed to write a partial state
            // file before the driver died. Restore through a same-directory
            // temporary file so the destination is never torn.
            if (File.Exists(SnapshotJsonPath))
            {
                File.Copy(SnapshotJsonPath, RestoreTempJsonPath, overwrite: true);
                File.Move(RestoreTempJsonPath, StateJsonPath, overwrite: true);
                GuardedProc.Log($"  RECOVERED user state.json from leftover disk snapshot {SnapshotJsonPath}.");
            }
            if (File.Exists(SnapshotBackupJsonPath))
            {
                File.Copy(SnapshotBackupJsonPath, RestoreBackupTempJsonPath, overwrite: true);
                File.Move(RestoreBackupTempJsonPath, BackupStateJsonPath, overwrite: true);
                GuardedProc.Log($"  RECOVERED user state backup from leftover disk snapshot {SnapshotBackupJsonPath}.");
            }

            s_savedStateJson = File.Exists(StateJsonPath) ? File.ReadAllText(StateJsonPath) : null;
            s_savedBackupStateJson = File.Exists(BackupStateJsonPath) ? File.ReadAllText(BackupStateJsonPath) : null;
            if (s_savedStateJson != null)
            {
                // Keep the original state file intact until the complete
                // write-ahead copy has been atomically moved into place.
                File.Copy(StateJsonPath, SnapshotTempJsonPath, overwrite: true);
                File.Move(SnapshotTempJsonPath, SnapshotJsonPath, overwrite: true);
                s_snapshotReady = true;
                GuardedProc.Log($"  state.json snapshot -> {SnapshotJsonPath}.");
            }
            if (s_savedBackupStateJson != null)
            {
                File.Copy(BackupStateJsonPath, SnapshotBackupTempJsonPath, overwrite: true);
                File.Move(SnapshotBackupTempJsonPath, SnapshotBackupJsonPath, overwrite: true);
                s_backupSnapshotReady = true;
                GuardedProc.Log($"  state.json.bak snapshot -> {SnapshotBackupJsonPath}.");
            }
            if (s_savedStateJson != null)
                File.Delete(StateJsonPath);
            if (s_savedBackupStateJson != null)
                File.Delete(BackupStateJsonPath);
            s_isolationReady = true;
        }
        catch (Exception ex)
        {
            // Running against the user's live state after a partial snapshot is
            // not a safe fallback: TabDock could overwrite that state before
            // cleanup gets a chance to restore it. Abort setup and let the
            // unconditional failure path recover any write-ahead snapshot.
            GuardedProc.Log($"  ERROR: could not establish isolated state.json snapshot: {ex.Message}");
            s_savedStateJson = null;
            s_savedBackupStateJson = null;
            s_snapshotReady = false;
            s_backupSnapshotReady = false;
            s_isolationReady = false;
            throw new InvalidOperationException("ValidationDriver could not establish an isolated state snapshot; refusing to start TabDock.", ex);
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
        if (!TestRunProvenance.RegisterLaunchedProcess(ctx.TabDock, "TabDockUnderTest", out string tabDockProcessReason))
            throw new InvalidOperationException($"TabDock process provenance could not be established: {tabDockProcessReason}.");

        ctx.MainHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "TabDock", 20000);
        if (ctx.MainHwnd == IntPtr.Zero)
            throw new InvalidOperationException("TabDock MainWindow did not appear within 20s.");
        RememberMainWindow(ctx);

        if (!Input.ForceForeground(ctx.MainHwnd))
            throw new InvalidOperationException("ValidationDriver could not establish a verified TabDock foreground target.");

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
                var toClose = new HashSet<IntPtr>();
                for (int i = 0; i < ctx.ContainerIdentities.Count; i++)
                {
                    WindowIdentity identity = ctx.ContainerIdentities[i];
                    if (TryRefreshStableIdentity(identity, out WindowIdentity current))
                    {
                        ctx.ContainerIdentities[i] = current;
                        Input.RegisterIdentity(current, TestRunProvenance.WindowRole(current.Hwnd));
                        toClose.Add(current.Hwnd);
                    }
                    else
                        GuardedProc.Log($"  Cleanup: refusing stale/unverified container HWND 0x{identity.Hwnd.ToInt64():X}.");
                }
                foreach (IntPtr h in Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true))
                {
                    string t = NativeMethods.GetWindowTextString(h) ?? string.Empty;
                    if (h != ctx.MainHwnd &&
                        (t.StartsWith("Group", StringComparison.Ordinal)
                            || t.StartsWith("TDVAL-", StringComparison.Ordinal)
                            || t.StartsWith("TDTEST:", StringComparison.Ordinal)))
                    {
                        toClose.Add(h);
                    }
                }
                foreach (IntPtr h in toClose)
                {
                    GuardedProc.Log($"  Cleanup: WM_CLOSE -> container 0x{h.ToInt64():X}.");
                    VerifiedWindowOps.PostMessage(h, ctx.TabDockPid, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }

                Thread.Sleep(300);
                HandleCloseGroupMessageBox(ctx, 3000);

                if (IsCurrentMainWindow(ctx))
                {
                    GuardedProc.Log("  Cleanup: WM_CLOSE -> MainWindow.");
                    if (Discover.TryCaptureIdentity(ctx.MainHwnd, out WindowIdentity mainIdentity))
                        VerifiedWindowOps.PostMessage(mainIdentity, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
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
                    && g.Identity is WindowIdentity identity
                    && TryRefreshStableIdentity(identity, out WindowIdentity currentGuestIdentity))
                {
                    GuardedProc.Log($"  Cleanup: WM_CLOSE -> guest window 0x{g.Hwnd.ToInt64():X} ('{g.Title}') (shared-host process left untouched).");
                    g.Identity = currentGuestIdentity;
                    Input.RegisterIdentity(currentGuestIdentity, TestRunProvenance.WindowRole(currentGuestIdentity.Hwnd));
                    VerifiedWindowOps.PostMessage(currentGuestIdentity, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
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
            RestoreStateSnapshot();
            GuardedProc.Log("  Cleanup: done.");
        }
    }

    /// <summary>
    /// Restores the user's state.json and state.json.bak from their write-ahead
    /// disk snapshots, with in-memory copies as fallbacks. Idempotent so setup
    /// failures can call it even when no TabDock context was returned.
    /// </summary>
    private static void RestoreStateSnapshot()
    {
        if (!s_isolationReady && !s_snapshotReady && s_savedStateJson == null && !File.Exists(SnapshotJsonPath)
            && !s_backupSnapshotReady && s_savedBackupStateJson == null && !File.Exists(SnapshotBackupJsonPath))
            return;

        try
        {
            bool restoredPrimaryFromDisk = false;
            bool restoredBackupFromDisk = false;
            if (File.Exists(SnapshotJsonPath))
            {
                // Restore before deleting the backup. If the driver dies
                // during cleanup, the complete snapshot remains available
                // for the next scenario run.
                File.Copy(SnapshotJsonPath, RestoreTempJsonPath, overwrite: true);
                File.Move(RestoreTempJsonPath, StateJsonPath, overwrite: true);
                restoredPrimaryFromDisk = true;
                File.Delete(SnapshotJsonPath);
                GuardedProc.Log($"  restored user state.json from disk snapshot {SnapshotJsonPath}.");
            }
            if (File.Exists(SnapshotBackupJsonPath))
            {
                File.Copy(SnapshotBackupJsonPath, RestoreBackupTempJsonPath, overwrite: true);
                File.Move(RestoreBackupTempJsonPath, BackupStateJsonPath, overwrite: true);
                restoredBackupFromDisk = true;
                File.Delete(SnapshotBackupJsonPath);
                GuardedProc.Log($"  restored user state.json.bak from disk snapshot {SnapshotBackupJsonPath}.");
            }

            if (restoredPrimaryFromDisk)
            {
                if (File.Exists(StateJsonPath))
                    GuardedProc.Log($"  user state.json restored ({new FileInfo(StateJsonPath).Length} bytes).");
            }
            else if (s_savedStateJson != null)
            {
                File.WriteAllText(StateJsonPath, s_savedStateJson);
                GuardedProc.Log($"  user state.json restored ({new FileInfo(StateJsonPath).Length} bytes).");
            }
            else if (File.Exists(StateJsonPath))
            {
                // The original primary was absent; remove only the file
                // created by the isolated scenario.
                File.Delete(StateJsonPath);
            }

            if (restoredBackupFromDisk)
            {
                if (File.Exists(BackupStateJsonPath))
                    GuardedProc.Log($"  user state.json.bak restored ({new FileInfo(BackupStateJsonPath).Length} bytes).");
            }
            else if (s_savedBackupStateJson != null)
            {
                File.WriteAllText(BackupStateJsonPath, s_savedBackupStateJson);
                GuardedProc.Log($"  user state.json.bak restored ({new FileInfo(BackupStateJsonPath).Length} bytes).");
            }
            else if (File.Exists(BackupStateJsonPath))
            {
                // The original backup was absent; remove only the backup
                // created by the isolated scenario.
                File.Delete(BackupStateJsonPath);
            }
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  WARNING: could not restore state.json: {ex.Message}");
        }
        finally
        {
            s_snapshotReady = false;
            s_backupSnapshotReady = false;
            s_isolationReady = false;
            s_savedStateJson = null;
            s_savedBackupStateJson = null;
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
                if (!Input.ForceForeground(dlg))
                    throw new InvalidOperationException("Could not bring the cleanup prompt to the foreground; refusing to click.");
                NativeMethods.GetWindowRect(noBtn, out NativeMethods.RECT rc);
                Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
            }
            else
            {
                GuardedProc.Log("  Cleanup: 'No' button not found; sending WM_CLOSE to the dialog.");
                VerifiedWindowOps.PostMessage(dlg, ctx.TabDockPid, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            Thread.Sleep(500);
        }
    }

    // -------------------------------------------------------------------------
    // Guest spawning + capture flow
    // -------------------------------------------------------------------------
    private static GuestInfo SpawnPig(Ctx ctx, string tag, params string[] extraFlags)
    {
        string title = $"TDTEST:{TestRunProvenance.RunIdCompact[..8]}:{tag}-{Rng.Next(0x10000):X4}";
        bool legacyPig = string.Equals(
            Environment.GetEnvironmentVariable("TABDOCK_QA_LEGACY_PIG"), "1", StringComparison.Ordinal);
        string args = $"--title \"{title}\""
            + (legacyPig ? string.Empty : $" --run-id \"{TestRunProvenance.RunIdCompact}\"")
            + (extraFlags.Length > 0 ? " " + string.Join(" ", extraFlags) : string.Empty);
        Process p = GuardedProc.SpawnGuarded(new ProcessStartInfo(PigExe, args) { UseShellExecute = false });
        string role = $"GuineaPig{tag}";
        if (!TestRunProvenance.RegisterLaunchedProcess(p, role, out string processReason))
            throw new InvalidOperationException($"GuineaPig process provenance could not be established: {processReason}.");
        var g = new GuestInfo { Proc = p, Pid = (uint)p.Id, Title = title, IsPig = true, Role = role };
        g.Hwnd = Discover.WaitForTopLevelWindow(g.Pid, t => t == title, 15000);
        if (g.Hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"Pig window '{title}' did not appear within 15s.");
        RememberGuestWindow(g);
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
                return SpawnBrowserGuest(ctx, kind, "CHR");
            case "edge-normal":
                // Chromium-based: same window class, args shape, and fresh-profile
                // rationale as chrome-normal.
                return SpawnBrowserGuest(ctx, kind, "EDG");
            case "brave-normal":
                return SpawnBrowserGuest(ctx, kind, "BRV");
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
                throw new ArgumentException($"Unknown --guest kind '{kind}' (expected pig|wt|chrome-nogpu|chrome-gpu|chrome-normal|edge-normal|brave-normal|firefox-normal|codex|chatgptclassic).");
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

        if (!Discover.TryCaptureIdentity(hwnd, out WindowIdentity attachedIdentity)
            || attachedIdentity.ProcessId != pid)
            throw new InvalidOperationException("Attached real-app window failed immediate identity verification; refusing to touch it.");

        if (!NativeMethods.IsWindowVisible(hwnd))
        {
            GuardedProc.Log($"  '{exactTitle}' window is currently hidden (tray state); revealing with ShowWindow(SW_SHOW).");
            if (!VerifiedWindowOps.ShowWindow(attachedIdentity, NativeMethods.SW_SHOW))
                throw new InvalidOperationException("Attached real-app window changed while being revealed; refusing to continue.");
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
        RememberGuestWindow(g);
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
        string role = BrowserRole(exe);
        if (!TestRunProvenance.RegisterLaunchedProcess(launcher, role + ".Launcher", out string launcherReason))
            throw new InvalidOperationException($"Guest launcher provenance could not be established: {launcherReason}.");

        IntPtr hwnd = IntPtr.Zero;
        Util.WaitUntil(() =>
        {
            foreach (IntPtr candidate in FindNewWindowsByClass(className, existing))
            {
                NativeMethods.GetWindowThreadProcessId(candidate, out uint candidatePid);
                if (TestRunProvenance.RegisterDescendantProcess(
                    candidatePid,
                    role,
                    (uint)launcher.Id,
                    exe,
                    out _))
                {
                    hwnd = candidate;
                    return true;
                }
            }
            return false;
        }, 20000, 150);
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"No new {className} window with proven launcher ancestry appeared for guest '{exe}'.");

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        Process owner = launcher;
        if (pid != 0 && pid != (uint)launcher.Id)
        {
            // The owner process was already registered with exact process
            // identity and launcher ancestry before this window was accepted.
            // Track it for cleanup, but never treat PID alone as provenance.
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
            Role = role,
        };
        if (string.IsNullOrEmpty(g.Title))
            throw new InvalidOperationException("Guest window has no title; cannot match a picker row safely.");
        RememberGuestWindow(g);
        ctx.Guests.Add(g);
        GuardedProc.Log($"  Guest '{g.Title}' PID {g.Pid} HWND 0x{g.Hwnd.ToInt64():X}.");
        return g;
    }

    private static IEnumerable<IntPtr> FindNewWindowsByClass(string className, HashSet<IntPtr> existing)
    {
        var found = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (existing.Contains(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
                return true;
            if (string.Equals(NativeMethods.GetClassNameString(hwnd), className, StringComparison.OrdinalIgnoreCase))
                found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string BrowserRole(string executable)
    {
        string name = Path.GetFileName(executable);
        if (name.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
            return "BrowserEdge";
        if (name.Equals("brave.exe", StringComparison.OrdinalIgnoreCase))
            return "BrowserBrave";
        if (name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase))
            return "BrowserChrome";
        if (name.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase))
            return "BrowserFirefox";
        return "ControlledProcess";
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
        RememberGuestWindow(g);
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
        => CaptureIntoGroupCore(ctx, exactRowMatch: false, guests);

    /// <summary>
    /// CaptureIntoGroup variant whose picker-row lookup requires EXACT title
    /// equality. Required when one process owns multiple capturable windows
    /// whose titles are prefixes of one another ('X' vs 'X-W2'): the default
    /// substring row search is inherently ambiguous there and refuses.
    /// </summary>
    private static (IntPtr Container, IntPtr Host) CaptureIntoGroupExact(Ctx ctx, params GuestInfo[] guests)
        => CaptureIntoGroupCore(ctx, exactRowMatch: true, guests);

    private static (IntPtr Container, IntPtr Host) CaptureIntoGroupCore(Ctx ctx, bool exactRowMatch, GuestInfo[] guests)
    {
        var before = new HashSet<IntPtr>(Discover.GetTopLevelWindowsByPid(ctx.TabDockPid, visibleOnly: true));

        // Foreground handling is arranging (not validating); the hotkey is real input.
        if (!Input.ForceForeground(ctx.MainHwnd))
            throw new InvalidOperationException("Could not bring TabDock to the foreground; refusing to send the capture hotkey.");
        Thread.Sleep(400);
        Input.SendHotkeyCtrlAltG();

        // Find the picker via Win32 first: the managed UIA client's desktop-children
        // snapshot can be stale for freshly created windows (observed: the picker was
        // visible per EnumWindows but absent from RootElement children), so bridge
        // into UIA from the HWND instead of searching the desktop tree.
        IntPtr pickerHwnd = Discover.WaitForTopLevelWindow(ctx.TabDockPid, t => t == "Capture windows", 10000);
        if (pickerHwnd == IntPtr.Zero)
            throw new InvalidOperationException("'Capture windows' picker did not appear within 10s.");
        Input.RegisterDiscoveredWindow(pickerHwnd, "TabDockCapturePicker");
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
                try { row = FindPickerRow(picker, g.Title, exactRowMatch); }
                catch (InvalidOperationException ex)
                {
                    lastMiss = ex;
                    AutomationElement? list = Uia.FindFirstOfType(picker, ControlType.List);
                    if (list != null && scrolls < 8)
                    {
                        Rect lr = Uia.GetElementRect(list);
                        int sx = (int)(lr.X + lr.Width / 2);
                        int sy = (int)(lr.Y + lr.Height / 2);
                        if (!EnsureClickable(pickerHwnd, sx, sy))
                            throw new InvalidOperationException("Picker list was obscured or failed identity proof; refusing to scroll blind.");
                        Input.ScrollWheel(sx, sy, -2);
                        scrolls++;
                    }
                    else
                    {
                        // Exhausted scrolling: re-enumerate via Refresh, reset scroll.
                        AutomationElement? refreshBtn = Uia.FindDescendantByName(picker, ControlType.Button, "Refresh", null, out int rc);
                        if (refreshBtn != null && rc == 1)
                        {
                            (int fx, int fy) = Uia.Center(refreshBtn);
                            if (!EnsureClickable(pickerHwnd, fx, fy))
                                throw new InvalidOperationException("Picker Refresh point was obscured or failed identity proof; refusing to click blind.");
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
            AutomationElement? textEl = Uia.FindDescendantByName(picker, ControlType.Text, exactRowMatch ? g.Title : null, exactRowMatch ? null : g.Title, out int textCount);
            if (textEl == null || textCount != 1)
                throw new InvalidOperationException($"Picker text label for '{g.Title}' not found uniquely (count={textCount}) — cannot toggle safely.");

            bool toggledOn = false;
            for (int attempt = 0; attempt < 3 && !toggledOn; attempt++)
            {
                // A retry is a fresh UIA discovery. Do not reuse an element or
                // rectangle obtained before the failed click; virtualization
                // can recycle the row peer while the picker is settling.
                row = FindPickerRow(picker, g.Title, exactRowMatch);
                textEl = Uia.FindDescendantByName(picker, ControlType.Text, exactRowMatch ? g.Title : null, exactRowMatch ? null : g.Title, out textCount);
                if (textEl == null || textCount != 1)
                    throw new InvalidOperationException($"Picker text label for '{g.Title}' was not uniquely rediscovered before retry (count={textCount}).");
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
                if (!EnsureClickable(pickerHwnd, cx, cy))
                {
                    if (attempt < 2 && RepositionVerifiedWindow(pickerHwnd, "picker"))
                        GuardedProc.Log("  CaptureIntoGroup: moved the verified picker to a virtual-screen corner; the next row point remains guard-gated.");
                    else
                        GuardedProc.Log($"  CaptureIntoGroup: picker row point was not currently clickable on attempt {attempt + 1}; no input sent, trying the next verified point.");
                    Thread.Sleep(150);
                    continue;
                }
                Input.ClickAt(cx, cy);
                Thread.Sleep(350);
                var ts = Uia.GetToggleState(row);
                GuardedProc.Log($"  CaptureIntoGroup: toggle state after attempt {attempt + 1} = {ts?.ToString() ?? "<null>"}.");
                toggledOn = ts == System.Windows.Automation.ToggleState.On;
            }
            if (!toggledOn)
                throw new InvalidOperationException($"Picker row for '{g.Title}' did not toggle on after real clicks.");
            Thread.Sleep(200);
        }

        AutomationElement? groupBtn = Uia.FindDescendantByName(picker, ControlType.Button, "Group these", null, out int btnCount);
        if (groupBtn == null || btnCount != 1)
            throw new InvalidOperationException($"'Group these' button not found uniquely (count={btnCount}).");
        bool pickerClosed = false;
        for (int attempt = 0; attempt < 3 && !pickerClosed; attempt++)
        {
            // Refresh the UIA peer and point on every retry. A WPF command can
            // remain enabled while its first real click is consumed by a stale
            // focus/activation transition; retrying a freshly verified point is
            // safer than assuming the old point still targets the picker.
            if (attempt > 0)
            {
                picker = Uia.FromHwnd(pickerHwnd);
                if (picker == null)
                    break;
                groupBtn = Uia.FindDescendantByName(picker, ControlType.Button, "Group these", null, out btnCount);
                if (groupBtn == null || btnCount != 1)
                    break;
            }

            // Selection-only command requery is dispatcher-queued (R21-018),
            // so the enabled transition can land after the row-toggle settle.
            // Poll the live UIA read instead of sampling once.
            bool groupBtnEnabled = Util.WaitUntil(() =>
            {
                try { return groupBtn.Current.IsEnabled; }
                catch { return false; }
            }, 2000);
            if (!groupBtnEnabled)
                throw new InvalidOperationException("'Group these' button is still disabled 2s after the selected rows were verified On.");
            (int bx, int by) = Uia.Center(groupBtn);
            IntPtr wfp = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = bx, y = by });
            IntPtr wfpRoot = NativeMethods.GetAncestor(wfp, NativeMethods.GA_ROOT);
            GuardedProc.Log($"  Clicking 'Group these' attempt {attempt + 1} at ({bx},{by}); windowFromPoint root=0x{wfpRoot.ToInt64():X} picker=0x{pickerHwnd.ToInt64():X} fg=0x{NativeMethods.GetForegroundWindow().ToInt64():X}.");
            if (!EnsureClickable(pickerHwnd, bx, by))
            {
                if (attempt < 2 && RepositionVerifiedWindow(pickerHwnd, "picker"))
                {
                    GuardedProc.Log("  CaptureIntoGroup: moved the verified picker to a virtual-screen corner before retrying; the next button point remains guard-gated.");
                    continue;
                }
                throw new InvalidOperationException("'Group these' point was obscured or failed identity proof; refusing to click blind.");
            }
            Input.ClickAt(bx, by);
            pickerClosed = Util.WaitUntil(() => !NativeMethods.IsWindow(pickerHwnd), 3000, 50);
            if (!pickerClosed)
            {
                // The button is IsDefault in the production picker.  A real
                // click can be consumed by a just-completed WPF focus/command
                // transition even while the point remains correctly owned by
                // the picker.  Retry through the same guarded foreground path
                // and press the default-button key as a human would; never use
                // UIA Invoke or treat a still-open picker as success.
                if (Input.ForceForeground(pickerHwnd))
                {
                    Input.SendKey(Input.VK_RETURN);
                    pickerClosed = Util.WaitUntil(() => !NativeMethods.IsWindow(pickerHwnd), 3000, 50);
                }
            }
            if (!pickerClosed)
                GuardedProc.Log($"  WARNING: picker still open after 'Group these' attempt {attempt + 1}; rediscovering before retry.");
        }
        if (!pickerClosed)
            throw new InvalidOperationException("'Group these' did not close the verified capture picker after three guarded attempts.");

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
        RememberContainer(ctx, container);
        GuardedProc.Log($"  Captured {guests.Length} guest(s) into container 0x{container.ToInt64():X} (host 0x{host.ToInt64():X}).");
        return (container, host);
    }

    private static void RememberMainWindow(Ctx ctx)
    {
        if (!Discover.TryCaptureIdentity(ctx.MainHwnd, out WindowIdentity identity)
            || identity.ProcessId != ctx.TabDockPid
            || !string.Equals(identity.Title, "TabDock", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("TabDock MainWindow failed process/class/title identity verification.");
        }
        ctx.MainClassName = identity.ClassName;
        ctx.MainIdentity = identity;
        Input.RegisterIdentity(identity, "TabDockMainWindow");
    }

    private static bool IsCurrentMainWindow(Ctx ctx)
    {
        return Discover.TryCaptureIdentity(ctx.MainHwnd, out WindowIdentity identity)
            && identity.ProcessId == ctx.TabDockPid
            && string.Equals(identity.ClassName, ctx.MainClassName, StringComparison.Ordinal)
            && string.Equals(identity.Title, "TabDock", StringComparison.Ordinal);
    }

    private static void RememberGuestWindow(GuestInfo guest)
    {
        if (!Discover.TryCaptureIdentity(guest.Hwnd, out WindowIdentity identity)
            || identity.ProcessId != guest.Pid
            || !string.Equals(identity.Title, guest.Title, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Guest HWND 0x{guest.Hwnd.ToInt64():X} failed process/class/title identity verification.");
        }
        guest.Identity = identity;
        Input.RegisterIdentity(identity, guest.Role);
    }

    private static void RememberContainer(Ctx ctx, IntPtr hwnd)
    {
        if (!Discover.TryCaptureIdentity(hwnd, out WindowIdentity identity)
            || identity.ProcessId != ctx.TabDockPid)
        {
            throw new InvalidOperationException($"Container HWND 0x{hwnd.ToInt64():X} failed process/class/title identity verification.");
        }
        ctx.Containers.Add(hwnd);
        ctx.ContainerIdentities.Add(identity);
        Input.RegisterIdentity(identity, "TabDockContainer");
    }

    private static WindowIdentity GetRememberedContainerIdentity(Ctx ctx, IntPtr hwnd)
    {
        for (int i = 0; i < ctx.ContainerIdentities.Count; i++)
        {
            WindowIdentity identity = ctx.ContainerIdentities[i];
            if (identity.Hwnd == hwnd)
            {
                if (!TryRefreshStableIdentity(identity, out WindowIdentity current))
                    throw new InvalidOperationException($"Container HWND 0x{hwnd.ToInt64():X} changed identity; refusing a native window operation.");
                ctx.ContainerIdentities[i] = current;
                Input.RegisterIdentity(current, TestRunProvenance.WindowRole(current.Hwnd));
                return current;
            }
        }

        throw new InvalidOperationException($"Container HWND 0x{hwnd.ToInt64():X} has no remembered identity; refusing a native window operation.");
    }

    private static bool TryRefreshStableIdentity(WindowIdentity expected, out WindowIdentity current)
    {
        if (!Discover.TryCaptureIdentity(expected.Hwnd, out current))
            return false;
        return current.ProcessId == expected.ProcessId
            && current.WindowThreadId == expected.WindowThreadId
            && current.ProcessStartTimeUtcTicks == expected.ProcessStartTimeUtcTicks
            && string.Equals(current.ClassName, expected.ClassName, StringComparison.Ordinal)
            && string.Equals(current.ExePath, expected.ExePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the CheckBox row for a guest, matched via its inner Text label (the
    /// CheckBox's own UIA Name is empty for image+text content). Refuses to return
    /// an ambiguous match so an unverified row is never clicked. When
    /// <paramref name="exactName"/> is set the label must equal the title exactly,
    /// for same-process windows whose titles are prefixes of one another.
    /// </summary>
    private static AutomationElement FindPickerRow(AutomationElement picker, string title, bool exactName = false)
    {
        // Direct CheckBox-name match first (in case a future template sets it).
        AutomationElement? el = Uia.FindDescendantByName(picker, ControlType.CheckBox, exactName ? title : null, exactName ? null : title, out int count);
        if (el != null && count == 1)
            return el;
        if (count > 1)
            throw new InvalidOperationException($"Picker row for '{title}' is ambiguous ({count} CheckBox matches) — refusing to click an unverified row.");

        // Fall back to the inner Text label, then walk up to its ancestor CheckBox.
        AutomationElement? text = Uia.FindDescendantByName(picker, ControlType.Text, exactName ? title : null, exactName ? null : title, out count);
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

    private static bool WaitForTabCount(IntPtr container, int expected, int timeoutMs)
        => Util.WaitUntil(() => TabCount(container) == expected, timeoutMs);

    private static AutomationElement? FindTabText(IntPtr container, string guestTitle, out int count)
    {
        count = 0;
        AutomationElement? list = GetTabList(container);
        if (list == null)
            return null;
        return Uia.FindDescendantByName(list, ControlType.Text, null, guestTitle, out count);
    }

    /// <summary>
    /// Re-queries the live tab strip until a unique title is exposed. WPF can
    /// rebuild the virtualized ListBoxItem tree after a split presentation
    /// transition; a single UIA snapshot in that interval is not evidence that
    /// the tab disappeared. The predicate is bounded and only accepts a unique
    /// current element, so callers still refuse ambiguous or stale targets.
    /// </summary>
    private static AutomationElement? WaitForTabText(IntPtr container, string guestTitle, int timeoutMs, out int count)
    {
        AutomationElement? found = null;
        int currentCount = 0;
        bool ready = Util.WaitUntil(() =>
        {
            found = FindTabText(container, guestTitle, out currentCount);
            return found != null && currentCount == 1;
        }, timeoutMs);
        count = currentCount;
        return ready ? found : null;
    }

    private static AutomationElement? FindSplitComposite(IntPtr container, out int count)
    {
        count = 0;
        AutomationElement? list = GetTabList(container);
        if (list == null)
            return null;
        return Uia.FindDescendantByAutomationId(list, "SplitCompositeItem", out count);
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
        bool foreground = Input.ForceForeground(target);

        IntPtr atPoint = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
        IntPtr rootAtPoint = NativeMethods.GetAncestor(atPoint, NativeMethods.GA_ROOT);
        bool clickable = rootAtPoint == target;
        if (!clickable)
        {
            // Preserve the guard's refusal as a structured artifact before a
            // retry can let the covering window disappear. This is deliberately
            // diagnostic-only: the caller still sends no input until the live
            // point resolves to the verified test HWND.
            IdentityDiagnostics.RecordPointFailure(
                x,
                y,
                target,
                rootAtPoint == IntPtr.Zero
                    ? "point-has-no-window"
                    : "point-obscured-by-unrelated-window");
        }
        GuardedProc.Log(clickable
            ? foreground
                ? $"  EnsureClickable: 0x{target.ToInt64():X} is foreground and ({x},{y}) resolves to the verified target."
                : $"  EnsureClickable: ForceForeground failed for 0x{target.ToInt64():X}, but ({x},{y}) resolves to it directly (no obscuring window) — proceeding with a real click, as a human user would."
            : foreground
                ? $"  EnsureClickable: 0x{target.ToInt64():X} reported foreground, but ({x},{y}) resolves to 0x{rootAtPoint.ToInt64():X} instead — refusing to click blind."
                : $"  EnsureClickable: ForceForeground failed for 0x{target.ToInt64():X} and ({x},{y}) resolves to 0x{rootAtPoint.ToInt64():X} instead — refusing to click blind.");
        return clickable;
    }

    /// <summary>
    /// Obtains a fresh, bounded real-input point from the current tab-strip UIA
    /// tree. WPF can recycle a text peer while the composite projection is
    /// being rebuilt; a stale peer can report a rectangle at the minimized
    /// sentinel coordinates even though the container itself is still the
    /// intended target. Never turn that rectangle into input. Re-discover the
    /// element, require two nearby rectangle reads to agree, require the
    /// center to be inside the live container, and leave WindowFromPoint plus
    /// the full provenance check to EnsureClickable/Input immediately before
    /// the click.
    /// </summary>
    private static bool TryGetFreshTabPoint(
        IntPtr container,
        string guestTitle,
        out int x,
        out int y,
        out string diagnostic)
    {
        x = 0;
        y = 0;
        diagnostic = "tab-element-unavailable";
        const int attempts = 4;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            AutomationElement? tab = FindTabText(container, guestTitle, out int count);
            if (tab == null || count != 1)
            {
                diagnostic = $"tab-element-count={count}";
                Thread.Sleep(40);
                continue;
            }

            try
            {
                Rect first = Uia.GetElementRect(tab);
                Thread.Sleep(5);
                Rect second = Uia.GetElementRect(tab);
                NativeMethods.GetWindowRect(container, out NativeMethods.RECT containerRect);

                double centerX = second.X + second.Width / 2.0;
                double centerY = second.Y + second.Height / 2.0;
                bool finite = !double.IsNaN(first.X) && !double.IsNaN(first.Y)
                    && !double.IsNaN(first.Width) && !double.IsNaN(first.Height)
                    && !double.IsNaN(second.X) && !double.IsNaN(second.Y)
                    && !double.IsNaN(second.Width) && !double.IsNaN(second.Height)
                    && !double.IsInfinity(first.X) && !double.IsInfinity(first.Y)
                    && !double.IsInfinity(first.Width) && !double.IsInfinity(first.Height)
                    && !double.IsInfinity(second.X) && !double.IsInfinity(second.Y)
                    && !double.IsInfinity(second.Width) && !double.IsInfinity(second.Height);
                bool usable = finite
                    && first.Width >= 4 && first.Height >= 4
                    && second.Width >= 4 && second.Height >= 4
                    && Math.Abs(first.X - second.X) <= 2
                    && Math.Abs(first.Y - second.Y) <= 2
                    && Math.Abs(first.Width - second.Width) <= 2
                    && Math.Abs(first.Height - second.Height) <= 2
                    && centerX >= containerRect.left && centerX < containerRect.right
                    && centerY >= containerRect.top && centerY < containerRect.bottom
                    && NativeMethods.IsWindow(container)
                    && NativeMethods.IsWindowVisible(container)
                    && !NativeMethods.IsIconic(container);

                if (usable)
                {
                    x = (int)Math.Round(centerX);
                    y = (int)Math.Round(centerY);
                    diagnostic = $"point=({x},{y}) rect={first.X:0.##},{first.Y:0.##},{first.Width:0.##}x{first.Height:0.##}";
                    return true;
                }

                diagnostic = $"unstable-or-offscreen rect1={first.X:0.##},{first.Y:0.##},{first.Width:0.##}x{first.Height:0.##} "
                    + $"rect2={second.X:0.##},{second.Y:0.##},{second.Width:0.##}x{second.Height:0.##} "
                    + $"container={Util.FormatRect(containerRect)} visible={NativeMethods.IsWindowVisible(container)} "
                    + $"iconic={NativeMethods.IsIconic(container)}";
            }
            catch (Exception ex)
            {
                diagnostic = $"uia-rectangle-{ex.GetType().Name}";
            }

            if (attempt < attempts)
                Thread.Sleep(60);
        }

        return false;
    }

    /// <summary>
    /// Sends a bounded sequence of independently proven real clicks to a tab
    /// until the requested native presentation predicate settles. A missing
    /// transition is never treated as success and never causes an unguarded
    /// fallback: every retry re-queries UIA and repeats the HWND provenance
    /// proof at the new point.
    /// </summary>
    private static bool ClickTabTextUntil(
        IntPtr container,
        string guestTitle,
        string action,
        Func<bool> settled,
        int maxAttempts = 3)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!TryGetFreshTabPoint(container, guestTitle, out int x, out int y, out string pointDiagnostic))
            {
                GuardedProc.Log($"  {action}: no safe fresh UIA point before attempt {attempt}/{maxAttempts}; {pointDiagnostic}; no input sent.");
                if (attempt < maxAttempts)
                {
                    Thread.Sleep(100);
                    continue;
                }
                return false;
            }

            GuardedProc.Log($"  {action}: guarded tab point attempt {attempt}/{maxAttempts} ({x},{y}); {pointDiagnostic}.");
            if (!EnsureClickable(container, x, y))
            {
                GuardedProc.Log($"  {action}: point failed live WindowFromPoint/provenance proof on attempt {attempt}; no input sent.");
                if (attempt < maxAttempts)
                {
                    Thread.Sleep(100);
                    continue;
                }
                return false;
            }

            Input.ClickAt(x, y);
            if (Util.WaitUntil(settled, 3000, 50))
                return true;

            GuardedProc.Log($"  {action}: expected transition did not settle after attempt {attempt}; re-discovering the tab before any retry.");
            if (attempt < maxAttempts)
                Thread.Sleep(100);
        }

        return settled();
    }

    /// <summary>
    /// Moves only an already identity-registered test window when an
    /// unrelated top-level window covers its current interaction rectangle.
    /// This is test arrangement, not an input-safety exception: the target is
    /// revalidated before the move and every subsequent point still passes
    /// WindowFromPoint/GA_ROOT verification. The move lets a supervised run
    /// recover from a persistent shell/security dialog without ever sending
    /// input to that dialog.
    /// </summary>
    private static bool RepositionVerifiedWindow(IntPtr targetHwnd, string role)
    {
        if (!Discover.TryCaptureIdentity(targetHwnd, out WindowIdentity identity))
            return false;

        if (!NativeMethods.GetWindowRect(targetHwnd, out NativeMethods.RECT current))
            return false;

        int width = current.Width;
        int height = current.Height;
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0 || screenWidth <= width || screenHeight <= height)
            return false;

        int x = left + 20;
        int y = top + 20;
        if (!VerifiedWindowOps.SetWindowPos(
                identity,
                IntPtr.Zero,
                x,
                y,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE))
            return false;

        Thread.Sleep(250);
        bool valid = Discover.MatchesIdentity(identity);
        if (valid)
            GuardedProc.Log($"  Repositioned verified {role} HWND 0x{targetHwnd.ToInt64():X} to the virtual-screen corner; every subsequent point remains guard-gated.");
        return valid;
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
        for (int attempt = 0; attempt < 3; attempt++)
        {
            // Coordinates are resolvable via UIA/GetWindowRect without needing
            // the container to be foreground yet, so compute them first and let
            // EnsureClickable prove that the point is still owned by this
            // registered container immediately before input.
            AutomationElement? containerEl = Uia.FromHwnd(container);
            int count = 0;
            AutomationElement? addBtn = containerEl == null
                ? null
                : Uia.FindDescendantByName(containerEl, ControlType.Button, "Add window to group", null, out count);
            int x, y;
            List<(int X, int Y)> candidatePoints;
            if (addBtn != null && count == 1)
            {
                Rect addRect = Uia.GetElementRect(addBtn);
                (x, y) = Uia.Center(addBtn);
                candidatePoints = new List<(int X, int Y)>
                {
                    (x, y),
                    ((int)(addRect.X + Math.Max(5, addRect.Width * 0.10)), (int)(addRect.Y + addRect.Height / 2)),
                    ((int)(addRect.X + Math.Max(5, addRect.Width * 0.90)), (int)(addRect.Y + addRect.Height / 2)),
                };
            }
            else
            {
                (x, y) = CaptionButtonCenterFromRight(container, 3);
                GuardedProc.Log($"  ClickAddWindowButton: UIA Name lookup found {count} match(es) for 'Add window to group'; falling back to the pixel-offset caption-button position ({x},{y}).");
                candidatePoints = new List<(int X, int Y)> { (x, y) };
            }

            bool clicked = false;
            foreach ((int candidateX, int candidateY) in candidatePoints.Distinct())
            {
                if (!EnsureClickable(container, candidateX, candidateY))
                    continue;
                Input.ClickAt(candidateX, candidateY);
                clicked = true;
                break;
            }
            if (clicked)
                return;

            if (attempt < 2 && RepositionVerifiedWindow(container, "TabDock container"))
                continue;

            throw new InvalidOperationException("Could not bring the container to the foreground and its 'Add window' button is obscured — refusing to click blind.");
        }
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

        // The container's "+" opens the INLINE capture panel, not the standalone
        // "Capture windows" picker (that window remains only for the launcher/
        // hotkey fallback). Drive the panel through the container's own UIA
        // tree: toggle each guest's row, then submit with "Add selected".
        ClickAddWindowButton(existingContainer);
        AutomationElement? root = Uia.FromHwnd(existingContainer);
        if (root == null)
            throw new InvalidOperationException("Container UIA root unavailable while inline capture is open.");

        foreach (GuestInfo g in guests)
        {
            // Row-find with a bounded wait: the picker enumerates windows
            // asynchronously after the panel opens.
            AutomationElement? textEl = null;
            var rowSw = Stopwatch.StartNew();
            while (textEl == null && rowSw.ElapsedMilliseconds < 12000)
            {
                textEl = Uia.FindDescendantByName(root, ControlType.Text, null, g.Title, out int textCount);
                if (textEl == null || textCount != 1)
                {
                    textEl = null;
                    Thread.Sleep(300);
                }
            }
            if (textEl == null)
                throw new InvalidOperationException($"Inline panel row for '{g.Title}' not found within 12s.");
            AutomationElement? row = Uia.NearestAncestorOfType(textEl, ControlType.CheckBox) ?? textEl;

            bool toggledOn = false;
            for (int attempt = 0; attempt < 3 && !toggledOn; attempt++)
            {
                root = Uia.FromHwnd(existingContainer)
                    ?? throw new InvalidOperationException("Inline capture root disappeared before a retry.");
                textEl = Uia.FindDescendantByName(root, ControlType.Text, null, g.Title, out int retryTextCount);
                if (textEl == null || retryTextCount != 1)
                    throw new InvalidOperationException($"Inline panel row for '{g.Title}' was not uniquely rediscovered before retry (count={retryTextCount}).");
                row = Uia.NearestAncestorOfType(textEl, ControlType.CheckBox) ?? textEl;
                (int cx, int cy) = attempt switch
                {
                    0 => Uia.Center(textEl),
                    _ => Uia.Center(row),
                };
                if (!EnsureClickable(existingContainer, cx, cy))
                {
                    if (attempt < 2 && RepositionVerifiedWindow(existingContainer, "TabDock container"))
                    {
                        // Moving the registered container changes the UIA
                        // coordinates. Rediscover the root and row on the next
                        // bounded attempt; no input was sent for this attempt.
                        Thread.Sleep(150);
                        continue;
                    }
                    throw new InvalidOperationException($"Inline panel row for '{g.Title}' was obscured or failed identity proof; refusing to click blind.");
                }
                Input.ClickAt(cx, cy);
                Thread.Sleep(350);
                toggledOn = Uia.GetToggleState(row) == System.Windows.Automation.ToggleState.On;
            }
            if (!toggledOn)
                throw new InvalidOperationException($"Inline panel row for '{g.Title}' did not toggle on after real clicks.");
            Thread.Sleep(200);
        }

        bool added = false;
        for (int attempt = 0; attempt < 3 && !added; attempt++)
        {
            root = Uia.FromHwnd(existingContainer)
                ?? throw new InvalidOperationException("Inline capture root disappeared before submitting.");
            AutomationElement? addBtn = Uia.FindDescendantByName(root, ControlType.Button, "Add selected", null, out int addCount);
            if (addBtn == null || addCount != 1)
                throw new InvalidOperationException($"Inline 'Add selected' button not found uniquely (count={addCount}).");
            Rect addRect = Uia.GetElementRect(addBtn);
            (int ax, int ay) = Uia.Center(addBtn);
            var candidatePoints = new[]
            {
                (X: ax, Y: ay),
                (X: (int)(addRect.X + Math.Max(5, addRect.Width * 0.10)), Y: (int)(addRect.Y + addRect.Height / 2)),
                (X: (int)(addRect.X + Math.Max(5, addRect.Width * 0.90)), Y: (int)(addRect.Y + addRect.Height / 2)),
            };
            foreach ((int candidateX, int candidateY) in candidatePoints.Distinct())
            {
                if (!EnsureClickable(existingContainer, candidateX, candidateY))
                    continue;
                Input.ClickAt(candidateX, candidateY);
                added = true;
                break;
            }
            if (!added)
            {
                if (attempt < 2 && RepositionVerifiedWindow(existingContainer, "TabDock container"))
                {
                    Thread.Sleep(150);
                    continue;
                }
                throw new InvalidOperationException("Inline 'Add selected' point was obscured or failed identity proof; refusing to click blind.");
            }
        }
        if (!added)
            throw new InvalidOperationException("Inline 'Add selected' was not submitted after guarded retries.");
        // The inline panel closes itself after adding.
        Util.WaitUntil(() => Uia.FindDescendantByName(root, ControlType.Button, "Add selected", null, out _) == null, 5000);

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
    /// <summary>
    /// Posts WM_CLOSE to every top-level window of the process (visible or
    /// hidden) repeatedly until the process exits or the timeout elapses.
    /// A single pass is insufficient: closing the last container makes the
    /// launcher REAPPEAR (documented design — App.xaml.cs OnContainerClosed
    /// re-shows the launcher when the last container closes), so a clean exit
    /// can need two waves of WM_CLOSE (container, then launcher).
    /// Returns true if the process exited within the timeout.
    /// </summary>
    private static bool CloseAllWindowsUntilExit(uint pid, Process proc, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (proc.HasExited)
                return true;
            foreach (IntPtr h in Discover.GetTopLevelWindowsByPid(pid, visibleOnly: false))
                VerifiedWindowOps.PostMessage(h, pid, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(300);
        }
        return proc.HasExited;
    }

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

            if (g.Identity is not WindowIdentity identity
                || !TryRefreshStableIdentity(identity, out WindowIdentity currentIdentity)
                || currentIdentity.ProcessId != g.Pid)
            {
                GuardedProc.Log($"  VerifyGuestForKill: refusing PID {g.Proc.Id} — guest HWND identity is no longer stable.");
                return false;
            }
            g.Identity = currentIdentity;
            Input.RegisterIdentity(currentIdentity, TestRunProvenance.WindowRole(currentIdentity.Hwnd));

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
                    if (!Input.ForceForeground(dlg))
                        throw new InvalidOperationException("Could not bring the message box to the foreground; refusing to click.");
                    NativeMethods.GetWindowRect(btn, out NativeMethods.RECT rc);
                    Input.ClickAt(rc.left + rc.Width / 2, rc.top + rc.Height / 2);
                    return true;
                }
            }
            Thread.Sleep(200);
        }
        return false;
    }

    /// <summary>Every visible top-level controlled guest window must belong to a guest this scenario spawned (TabDock's own renamed container excluded).</summary>
    private static bool NoOrphanPigWindows(Ctx ctx)
    {
        var knownPids = new HashSet<uint>(ctx.Guests.Select(g => g.Pid));
        bool ok = true;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;
            string title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
            if (!title.StartsWith("TDVAL-", StringComparison.Ordinal)
                && !title.StartsWith("TDTEST:", StringComparison.Ordinal))
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
    /// Post-cleanup assertion required by e2e-input-safety. Unlike the older
    /// visible-prefix sweep, this checks every top-level window (including
    /// hidden tray-style windows) for every disposable guest PID this scenario
    /// spawned. Explicitly attached real apps are excluded because the driver
    /// is forbidden to close or kill them.
    /// </summary>
    private static bool NoSpawnedGuestWindowsRemain(Ctx ctx)
    {
        bool ok = true;
        foreach (GuestInfo guest in ctx.Guests)
        {
            if (guest.DoNotKill)
                continue;

            foreach (IntPtr hwnd in Discover.GetTopLevelWindowsByPid(guest.Pid, visibleOnly: false))
            {
                GuardedProc.Log($"  Cleanup orphan: spawned guest '{guest.Title}' still owns HWND 0x{hwnd.ToInt64():X}.");
                ok = false;
            }
        }
        return ok;
    }

    /// <summary>
    /// Counts open container/group windows for this TabDock instance (title
    /// starts with "Group", "TDVAL-", or the per-run "TDTEST:" marker, the same prefix convention Cleanup()
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
            if (t.StartsWith("Group", StringComparison.Ordinal)
                || t.StartsWith("TDVAL-", StringComparison.Ordinal)
                || t.StartsWith("TDTEST:", StringComparison.Ordinal))
                n++;
        }
        return n;
    }
}
