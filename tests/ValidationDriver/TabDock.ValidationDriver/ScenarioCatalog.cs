using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TabDock.ValidationDriver;

internal enum ScenarioExecutionClass
{
    Hermetic,
    Standalone,
    Browser,
    UserOwnedApplication,
}

internal enum ScenarioInputRequirement
{
    None,
    UiAutomationRead,
    SendInput,
}

internal enum ScenarioDestructiveState
{
    NonDestructive,
    TestOwnedMutation,
    CrashRecovery,
    ExternalBrowser,
    UserOwnedExternal,
}

internal sealed record ScenarioDefinition(
    string Id,
    string DispatchIdentifier,
    string Shard,
    ScenarioExecutionClass ExecutionClass,
    string? GuestFamily,
    IReadOnlyList<string> RequiredApplications,
    IReadOnlyList<string> RequiredBrowsers,
    IReadOnlyDictionary<string, string> ApplicationsByGuest,
    bool RequiresInteractiveSession,
    ScenarioInputRequirement InputRequirement,
    bool RequiresMultiMonitor,
    bool RequiresMixedDpi,
    bool RequiresNonDefaultDpi,
    bool RequiresNegativeCoordinates,
    bool RequiresSupervision,
    bool RequiresSigning,
    bool RequiresStageB,
    ScenarioDestructiveState DestructiveState,
    int ExpectedRuntimeSeconds,
    bool IncludeInAll,
    bool MayContributeReleaseEvidence);

internal sealed record ScenarioShardDefinition(
    string Name,
    bool IncludedInAll,
    bool ExplicitOnly,
    int MaximumScenarioCount,
    int MaximumExpectedRuntimeSeconds);

/// <summary>
/// The one authoritative scenario registry. All command-line, capability,
/// shard, documentation, and manifest projections are derived from this list.
/// </summary>
internal static class ScenarioCatalog
{
    public const string Generation = "scenario-catalog-2026-08-24-v1";

    private static readonly IReadOnlyList<ScenarioShardDefinition> Shards =
        new[]
        {
            new ScenarioShardDefinition("core-lifecycle", true, false, 64, 7_200),
            new ScenarioShardDefinition("capture-group", true, false, 64, 7_200),
            new ScenarioShardDefinition("split-core", true, false, 64, 7_200),
            new ScenarioShardDefinition("split-render", true, false, 64, 7_200),
            new ScenarioShardDefinition("split-focus", true, false, 64, 7_200),
            new ScenarioShardDefinition("drag-z-order", true, false, 64, 7_200),
            new ScenarioShardDefinition("crash-recovery", true, false, 64, 7_200),
            new ScenarioShardDefinition("keyboard-input", true, false, 64, 7_200),
            new ScenarioShardDefinition("dpi-multi-monitor", true, false, 64, 7_200),
            new ScenarioShardDefinition("startup", true, false, 64, 7_200),
            new ScenarioShardDefinition("diagnostics", true, false, 64, 7_200),
            new ScenarioShardDefinition("browser", false, true, 16, 3_600),
            new ScenarioShardDefinition("real-app", false, true, 8, 3_600),
        };

    private static readonly IReadOnlyList<ScenarioDefinition> Definitions = CreateDefinitions();
    private static readonly IReadOnlyDictionary<string, ScenarioDefinition> ById =
        Definitions.ToDictionary(item => item.Id, StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, ScenarioShardDefinition> ShardByName =
        Shards.ToDictionary(item => item.Name, StringComparer.Ordinal);

    public static IReadOnlyList<ScenarioDefinition> All => Definitions;

    public static IReadOnlyList<ScenarioShardDefinition> AllShards => Shards;

    public static IReadOnlyList<string> OrchestratedShardNames
        => Shards.Where(shard => shard.IncludedInAll).Select(shard => shard.Name).ToArray();

    public static IReadOnlyList<string> ExplicitOnlyShardNames
        => Shards.Where(shard => shard.ExplicitOnly).Select(shard => shard.Name).ToArray();

    public static IReadOnlyList<string> AllOrder
        => Definitions.Where(item => item.IncludeInAll).Select(item => item.Id).ToArray();

    public static IReadOnlyList<string> BrowserOnlyScenarios
        => Definitions.Where(item => item.ExecutionClass == ScenarioExecutionClass.Browser)
            .Where(item => item.Id != "browser-multi")
            .Select(item => item.Id)
            .ToArray();

    public static IReadOnlyList<string> StandaloneExtraScenarios
        => Definitions.Where(item => item.ExecutionClass == ScenarioExecutionClass.Standalone && !item.IncludeInAll)
            .Select(item => item.Id)
            .ToArray();

    public static IReadOnlyList<string> RealAppScenarios
        => Definitions.Where(item => item.ExecutionClass == ScenarioExecutionClass.UserOwnedApplication)
            .Select(item => item.Id)
            .ToArray();

    public static IReadOnlyList<string> BrowserGuestKinds
        => new[] { "chrome-normal", "edge-normal", "brave-normal", "firefox-normal" };

    public static IReadOnlyList<string> RealAppGuestKinds
        => new[] { "codex", "chatgptclassic" };

    public static bool TryGet(string scenario, out ScenarioDefinition definition)
        => ById.TryGetValue(scenario, out definition!);

    public static IReadOnlyList<string> GetShardScenarios(string shard)
        => Definitions.Where(item => string.Equals(item.Shard, shard, StringComparison.Ordinal))
            .Where(item => ShardByName[shard].ExplicitOnly
                || item.IncludeInAll
                || (item.ExecutionClass != ScenarioExecutionClass.Browser
                    && item.ExecutionClass != ScenarioExecutionClass.UserOwnedApplication))
            .Select(item => item.Id)
            .ToArray();

    public static bool TryResolve(
        string scenario,
        out Action<Ctx, Options>? handler,
        out ScenarioDefinition? definition)
    {
        handler = null;
        definition = null;
        if (!TryGet(scenario, out ScenarioDefinition resolved))
            return false;

        definition = resolved;
        MethodInfo? method = typeof(Scenarios).GetMethod(
            resolved.DispatchIdentifier,
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(Ctx), typeof(Options) },
            modifiers: null);
        if (method == null || method.ReturnType != typeof(void))
            return false;

        try
        {
            handler = method.CreateDelegate<Action<Ctx, Options>>();
            return true;
        }
        catch (ArgumentException)
        {
            handler = null;
            return false;
        }
    }

    public static void Validate()
    {
        IReadOnlyList<string> errors = ValidateDefinitions(Definitions, Shards);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join("; ", errors));

        foreach (ScenarioDefinition definition in Definitions)
        {
            if (!TryResolve(definition.Id, out _, out _))
                throw new InvalidOperationException(
                    $"Scenario '{definition.Id}' dispatch '{definition.DispatchIdentifier}' is not resolvable.");
        }
    }

    public static IReadOnlyList<string> ValidateDefinitions(
        IReadOnlyList<ScenarioDefinition> definitions,
        IReadOnlyList<ScenarioShardDefinition> shards)
    {
        var errors = new List<string>();
        string[] duplicateIds = definitions
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (duplicateIds.Length != 0)
            errors.Add("duplicate scenario IDs: " + string.Join(", ", duplicateIds));

        var shardMap = shards.ToDictionary(item => item.Name, StringComparer.Ordinal);
        foreach (ScenarioDefinition definition in definitions)
        {
            if (!shardMap.TryGetValue(definition.Shard, out ScenarioShardDefinition? shard))
                errors.Add($"{definition.Id}: unknown shard '{definition.Shard}'");
            else
            {
                if (definition.IncludeInAll && !shard.IncludedInAll)
                    errors.Add($"{definition.Id}: all-run inclusion requires shard '{definition.Shard}' to be orchestrated");
                if (definition.ExecutionClass == ScenarioExecutionClass.Browser && !shard.ExplicitOnly)
                    errors.Add($"{definition.Id}: browser scenario must be explicit-only");
                if (definition.ExecutionClass == ScenarioExecutionClass.UserOwnedApplication && !shard.ExplicitOnly)
                    errors.Add($"{definition.Id}: user-owned scenario must be explicit-only");
            }

            if (string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.DispatchIdentifier))
                errors.Add("scenario metadata contains an empty ID or dispatch identifier");
            if (definition.ExpectedRuntimeSeconds <= 0)
                errors.Add($"{definition.Id}: expected runtime must be positive");
            if (definition.RequiresSupervision && definition.InputRequirement == ScenarioInputRequirement.None)
                errors.Add($"{definition.Id}: supervised scenario must declare an input requirement");
        }

        foreach (ScenarioShardDefinition shard in shards)
        {
            ScenarioDefinition[] members = definitions.Where(item => item.Shard == shard.Name).ToArray();
            if (members.Length == 0)
                errors.Add($"shard '{shard.Name}' has no catalog members");
            if (members.Length > shard.MaximumScenarioCount)
                errors.Add($"shard '{shard.Name}' exceeds scenario-count budget");
            if (members.Sum(item => item.ExpectedRuntimeSeconds) > shard.MaximumExpectedRuntimeSeconds)
                errors.Add($"shard '{shard.Name}' exceeds expected-runtime budget");
        }

        return errors;
    }

    private static IReadOnlyList<ScenarioDefinition> CreateDefinitions()
    {
        var list = new List<ScenarioDefinition>();

        void Add(
            string id,
            string dispatch,
            string shard,
            bool includeInAll = false,
            ScenarioExecutionClass executionClass = ScenarioExecutionClass.Hermetic,
            string? guestFamily = null,
            string[]? applications = null,
            string[]? browsers = null,
            Dictionary<string, string>? applicationsByGuest = null,
            ScenarioInputRequirement inputRequirement = ScenarioInputRequirement.SendInput,
            bool requiresMultiMonitor = false,
            bool requiresMixedDpi = false,
            bool requiresNonDefaultDpi = false,
            bool requiresNegativeCoordinates = false,
            bool requiresSupervision = true,
            bool requiresSigning = false,
            bool requiresStageB = false,
            ScenarioDestructiveState destructiveState = ScenarioDestructiveState.TestOwnedMutation,
            int expectedRuntimeSeconds = 60,
            bool mayContributeReleaseEvidence = true)
        {
            list.Add(new ScenarioDefinition(
                id,
                dispatch,
                shard,
                executionClass,
                guestFamily,
                applications ?? Array.Empty<string>(),
                browsers ?? Array.Empty<string>(),
                applicationsByGuest ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                RequiresInteractiveSession: true,
                inputRequirement,
                requiresMultiMonitor,
                requiresMixedDpi,
                requiresNonDefaultDpi,
                requiresNegativeCoordinates,
                requiresSupervision,
                requiresSigning,
                requiresStageB,
                destructiveState,
                expectedRuntimeSeconds,
                includeInAll,
                mayContributeReleaseEvidence));
        }

        // Default all-order hermetic scenarios. Keeping the declaration in
        // catalog order preserves the long-standing CLI/all execution order.
        Add("rename", "Rename", "core-lifecycle", includeInAll: true);
        Add("popout", "PopOut", "core-lifecycle", includeInAll: true);
        Add("closewin", "CloseWin", "core-lifecycle", includeInAll: true);
        Add("closewin-hide", "CloseWinHide", "core-lifecycle", includeInAll: true);
        Add("selfclose", "SelfClose", "core-lifecycle", includeInAll: true);
        Add("selfhide", "SelfHide", "core-lifecycle", includeInAll: true);
        Add("selfminhide", "SelfMinHide", "core-lifecycle", includeInAll: true);
        Add("tabswitch-hidesafety", "TabSwitchHideSafety", "core-lifecycle", includeInAll: true);
        Add("minrestore", "MinRestore", "dpi-multi-monitor", includeInAll: true);
        Add("maximize-repro", "MaximizeRepro", "dpi-multi-monitor", includeInAll: true,
            applicationsByGuest: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["wt"] = "windows-terminal" });
        Add("repeat-cycles", "RepeatCycles", "core-lifecycle", includeInAll: true);
        Add("crossfeature", "CrossFeature", "core-lifecycle", includeInAll: true);
        Add("hotkey-afterclose", "HotkeyAfterClose", "core-lifecycle", includeInAll: true);
        Add("persist-kill", "PersistKill", "capture-group", includeInAll: true);
        Add("dragreorder", "DragReorder", "drag-z-order", includeInAll: true);
        Add("chrometabdrag", "ChromeTabDrag", "drag-z-order", includeInAll: true, browsers: new[] { "chrome-normal" });
        Add("closegroupprompt", "CloseGroupPrompt", "capture-group", includeInAll: true);
        Add("exitpopulated", "ExitPopulated", "core-lifecycle", includeInAll: true);
        Add("container-minimize-retains-tabs", "ContainerMinimizeRetainsTabs", "dpi-multi-monitor", includeInAll: true);
        Add("hotkey-hold-single-picker", "HotkeyHoldSinglePicker", "core-lifecycle", includeInAll: true);
        Add("popout-inactive-keeps-active", "PopOutInactiveKeepsActive", "core-lifecycle", includeInAll: true);
        Add("double-capture-refused", "DoubleCaptureRefused", "capture-group", includeInAll: true);
        Add("persist-active-tab-index", "PersistActiveTabIndex", "capture-group", includeInAll: true);
        Add("restored-group-survives-member-reclose", "RestoredGroupSurvivesMemberReclose", "capture-group", includeInAll: true);
        Add("selfminimize-timer-vs-teardown", "SelfMinimizeTimerVsTeardown", "dpi-multi-monitor", includeInAll: true);
        Add("launcher-empty-state-hint", "LauncherEmptyStateHint", "diagnostics", includeInAll: true,
            inputRequirement: ScenarioInputRequirement.UiAutomationRead);
        Add("split-single-disabled", "SplitSingleDisabled", "split-core", includeInAll: true);
        Add("split-two-auto", "SplitTwoAuto", "split-core", includeInAll: true);
        Add("split-select-partner", "SplitSelectPartner", "split-core", includeInAll: true);
        Add("split-exit", "SplitExit", "split-core", includeInAll: true);
        Add("split-resize", "SplitResize", "split-core", includeInAll: true);
        Add("split-move", "SplitMove", "split-core", includeInAll: true);
        Add("split-minrestore", "SplitMinRestore", "split-core", includeInAll: true);
        Add("split-reorder", "SplitReorder", "split-core", includeInAll: true);
        Add("split-popout-left", "SplitPopoutLeft", "split-core", includeInAll: true);
        Add("split-popout-right", "SplitPopoutRight", "split-core", includeInAll: true);
        Add("split-selfclose", "SplitSelfClose", "split-render", includeInAll: true);
        Add("split-native-move-reassert", "SplitNativeMoveReassert", "split-render", includeInAll: true);
        Add("split-native-resize-reassert", "SplitNativeResizeReassert", "split-render", includeInAll: true);
        Add("split-contextmenu-render-stability", "SplitContextMenuRenderStability", "split-render", includeInAll: true);
        Add("split-closebutton-left", "SplitCloseButtonLeft", "split-render", includeInAll: true);
        Add("split-closebutton-right", "SplitCloseButtonRight", "split-render", includeInAll: true);
        Add("split-click-third", "SplitClickThird", "split-render", includeInAll: true);
        Add("split-third-tab-hover-persists", "SplitThirdTabHoverPersists", "split-render", includeInAll: true);
        Add("split-third-tab-click-persists", "SplitThirdTabClickPersists", "split-render", includeInAll: true);
        Add("split-four-tab-nonmember-switching", "SplitFourTabNonmemberSwitching", "split-render", includeInAll: true);
        Add("split-three-app-client-settle", "SplitThreeAppClientSettle", "split-render", includeInAll: true);
        Add("split-diagnostic-snapshot", "SplitDiagnosticSnapshot", "split-render", includeInAll: true);
        Add("split-dormant-member-removal", "SplitDormantMemberRemoval", "split-render", includeInAll: true);
        Add("split-drag-release-render-stability", "SplitDragReleaseRenderStability", "split-render", includeInAll: true);
        Add("drag-release-render-stability", "DragReleaseRenderStability", "split-render", includeInAll: true);
        Add("split-directclick", "SplitDirectClick", "split-focus", includeInAll: true);
        Add("split-repeat-cycles", "SplitRepeatCycles", "split-focus", includeInAll: true);
        Add("contextmenu-render-stability", "ContextMenuRenderStability", "drag-z-order", includeInAll: true);
        Add("chrome-click-render-stability", "ChromeClickRenderStability", "drag-z-order", includeInAll: true);
        Add("tab-closebutton-popout", "TabCloseButtonPopout", "capture-group", includeInAll: true);
        Add("tab-middleclick-popout", "TabMiddleClickPopout", "drag-z-order", includeInAll: true);
        Add("capture-inline-ui", "CaptureInlineUi", "diagnostics", includeInAll: true);
        Add("group-create-inline", "GroupCreateInline", "capture-group", includeInAll: true);
        Add("three-app-torture", "ThreeAppTorture", "capture-group", includeInAll: true, expectedRuntimeSeconds: 180);
        Add("group-dropdown-stability", "GroupDropdownStability", "capture-group", includeInAll: true);
        Add("add-window-toggle", "AddWindowToggle", "diagnostics", includeInAll: true);
        Add("group-rename-menu", "GroupRenameMenu", "capture-group", includeInAll: true);
        Add("group-delete-populated", "GroupDeletePopulated", "capture-group", includeInAll: true);
        Add("split-composite", "SplitComposite", "split-focus", includeInAll: true);
        Add("split-three-tab-partner-popout", "SplitThreeTabPartnerPopout", "split-focus", includeInAll: true);
        Add("split-focus-bidirectional", "SplitFocusBidirectional", "split-focus", includeInAll: true);
        Add("split-partner-permutation", "SplitPartnerPermutation", "split-focus", includeInAll: true);
        Add("split-maximize-restore-no-overlap", "SplitMaximizeRestoreNoOverlap", "split-focus", includeInAll: true);
        Add("split-guest-does-not-overflow-pane", "SplitGuestDoesNotOverflowPane", "split-render", includeInAll: true);
        Add("split-narrow-container-constraints", "SplitNarrowContainerConstraints", "split-render", includeInAll: true);
        Add("single-guest-does-not-overflow-content", "SingleGuestDoesNotOverflowContent", "core-lifecycle", includeInAll: true);
        Add("hung-guest-mintrack", "HungGuestMinTrack", "dpi-multi-monitor", includeInAll: true);
        Add("crashkill-maximized-recovery", "CrashKillMaximizedRecovery", "crash-recovery", includeInAll: true, destructiveState: ScenarioDestructiveState.CrashRecovery);
        Add("crashkill-minimized-recovery", "CrashKillMinimizedRecovery", "crash-recovery", includeInAll: true, destructiveState: ScenarioDestructiveState.CrashRecovery);
        Add("crashkill-split-rescue", "CrashKillSplitRescue", "crash-recovery", includeInAll: true, destructiveState: ScenarioDestructiveState.CrashRecovery);
        Add("startup-local-stack-above-unrelated-when-guest-present", "StartupLocalStackAboveUnrelatedWhenGuestPresent", "startup", includeInAll: true);
        Add("torture-tabswitch-rapid", "TortureTabSwitchRapid", "keyboard-input", includeInAll: true, expectedRuntimeSeconds: 180);
        Add("torture-tabswitch-random", "TortureTabSwitchRandom", "keyboard-input", includeInAll: true, expectedRuntimeSeconds: 180);
        Add("torture-split-member-destroy", "TortureSplitMemberDestroy", "split-core", includeInAll: true, expectedRuntimeSeconds: 180);
        Add("torture-closegroup-same-process", "TortureCloseGroupSameProcess", "capture-group", includeInAll: true, expectedRuntimeSeconds: 180);
        Add("torture-minrestore-soak", "TortureMinRestoreSoak", "split-focus", includeInAll: true, expectedRuntimeSeconds: 180);
        Add("torture-crash-restart-soak", "TortureCrashRestartSoak", "crash-recovery", includeInAll: true, expectedRuntimeSeconds: 180, destructiveState: ScenarioDestructiveState.CrashRecovery);

        Add("browser-lifecycle", "BrowserLifecycle", "browser", executionClass: ScenarioExecutionClass.Browser, guestFamily: "browser", destructiveState: ScenarioDestructiveState.ExternalBrowser, mayContributeReleaseEvidence: false);
        Add("browser-tabswitch-hidesafety", "BrowserTabSwitchHideSafety", "browser", executionClass: ScenarioExecutionClass.Browser, guestFamily: "browser", destructiveState: ScenarioDestructiveState.ExternalBrowser, mayContributeReleaseEvidence: false);
        Add("browser-dragreorder", "BrowserDragReorder", "browser", executionClass: ScenarioExecutionClass.Browser, guestFamily: "browser", destructiveState: ScenarioDestructiveState.ExternalBrowser, mayContributeReleaseEvidence: false);
        Add("browser-multi", "BrowserMulti", "browser", executionClass: ScenarioExecutionClass.Browser, browsers: new[] { "chrome-and-edge" }, destructiveState: ScenarioDestructiveState.ExternalBrowser, mayContributeReleaseEvidence: false);
        Add("browser-soak", "BrowserSoak", "browser", executionClass: ScenarioExecutionClass.Browser, guestFamily: "browser", expectedRuntimeSeconds: 180, destructiveState: ScenarioDestructiveState.ExternalBrowser, mayContributeReleaseEvidence: false);
        Add("browser-split-persistent-render", "BrowserSplitPersistentRender", "browser", executionClass: ScenarioExecutionClass.Browser, guestFamily: "browser", destructiveState: ScenarioDestructiveState.ExternalBrowser, mayContributeReleaseEvidence: false);
        Add("realapp", "RealAppFillMaxHide", "real-app", executionClass: ScenarioExecutionClass.UserOwnedApplication, guestFamily: "real-app", destructiveState: ScenarioDestructiveState.UserOwnedExternal, mayContributeReleaseEvidence: false);
        Add("realapp-multi-render", "RealAppMultiRender", "real-app", executionClass: ScenarioExecutionClass.UserOwnedApplication, guestFamily: "real-app", destructiveState: ScenarioDestructiveState.UserOwnedExternal, mayContributeReleaseEvidence: false);

        Add("contentinput", "ContentInput", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone);
        Add("chromeinput", "ChromeInput", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone, browsers: new[] { "chrome-normal" });
        Add("alttabinput", "AltTabInput", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone);
        Add("keyboardinput", "KeyboardInput", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone);
        Add("keyboardinput-chrome", "KeyboardInputChrome", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone, browsers: new[] { "chrome-normal" });
        Add("keyboardinput-notepad", "KeyboardInputNotepad", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone, applications: new[] { "notepad-broker" });
        Add("keyboardinput-rapid-switch", "KeyboardInputRapidSwitch", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone);
        Add("keyboardinput-chrome-altswitch", "KeyboardInputChromeAltSwitch", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone, browsers: new[] { "chrome-normal" });
        Add("keyboardinput-edge-altswitch", "KeyboardInputEdgeAltSwitch", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone, browsers: new[] { "edge-normal" });
        Add("keyboardinput-chrome-omnibox-altswitch", "KeyboardInputChromeOmniboxAltSwitch", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone, browsers: new[] { "chrome-normal" });
        Add("realworkflow-altswitch", "RealWorkflowAltSwitch", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone);
        Add("directclick-foreground-pairing", "DirectClickForegroundPairing", "drag-z-order", executionClass: ScenarioExecutionClass.Standalone);
        Add("dragout-by-titlebar", "DragOutByTitlebar", "drag-z-order", executionClass: ScenarioExecutionClass.Standalone);
        Add("crashkill-rescue", "CrashKillRescue", "crash-recovery", executionClass: ScenarioExecutionClass.Standalone, destructiveState: ScenarioDestructiveState.CrashRecovery);
        Add("crashkill-rapidswitch-rescue", "CrashKillRapidSwitchRescue", "crash-recovery", executionClass: ScenarioExecutionClass.Standalone, destructiveState: ScenarioDestructiveState.CrashRecovery);
        Add("crashkill-selfhide-not-rescued", "CrashKillSelfHideNotRescued", "crash-recovery", executionClass: ScenarioExecutionClass.Standalone, destructiveState: ScenarioDestructiveState.CrashRecovery);
        Add("instant-tabswitch", "InstantTabSwitch", "capture-group", executionClass: ScenarioExecutionClass.Standalone);
        Add("reattach-thenclick-othertab", "ReattachThenClickOtherTab", "capture-group", executionClass: ScenarioExecutionClass.Standalone);
        Add("reattach-repeated-cycles", "ReattachRepeatedCycles", "capture-group", executionClass: ScenarioExecutionClass.Standalone, expectedRuntimeSeconds: 180);
        Add("picker-owner-is-requesting-container", "PickerOwnerIsRequestingContainer", "diagnostics", executionClass: ScenarioExecutionClass.Standalone);
        Add("picker-owner-falls-back-when-container-closed", "PickerOwnerFallsBackWhenContainerClosed", "diagnostics", executionClass: ScenarioExecutionClass.Standalone);
        Add("rename-edge-cases", "RenameEdgeCases", "core-lifecycle", executionClass: ScenarioExecutionClass.Standalone);
        Add("multi-group-independent-interaction", "MultiGroupIndependentInteraction", "capture-group", executionClass: ScenarioExecutionClass.Standalone);
        Add("dragreorder-then-immediate-popout", "DragReorderThenImmediatePopOut", "drag-z-order", executionClass: ScenarioExecutionClass.Standalone);
        Add("keyboard-only-tab-navigation", "KeyboardOnlyTabNavigation", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone);
        Add("crashkill-during-active-drag", "CrashKillDuringActiveDrag", "crash-recovery", executionClass: ScenarioExecutionClass.Standalone, destructiveState: ScenarioDestructiveState.CrashRecovery);
        Add("dwm-transitions-disabled-on-capture", "DwmTransitionsDisabledOnCapture", "diagnostics", executionClass: ScenarioExecutionClass.Standalone);
        Add("dragprobe", "DragProbe", "drag-z-order", executionClass: ScenarioExecutionClass.Standalone);
        Add("capture-dpi-unaware-guest", "CaptureDpiUnawareGuest", "dpi-multi-monitor", executionClass: ScenarioExecutionClass.Standalone, requiresNonDefaultDpi: true);
        Add("capture-dpi-system-guest", "CaptureDpiSystemGuest", "dpi-multi-monitor", executionClass: ScenarioExecutionClass.Standalone, requiresNonDefaultDpi: true);
        Add("split-comparison-observe", "SplitComparisonObserve", "split-render", executionClass: ScenarioExecutionClass.Standalone);
        Add("startup-group-not-hidden-behind-existing-window", "StartupGroupNotHiddenBehindExistingWindow", "startup", executionClass: ScenarioExecutionClass.Standalone);
        Add("startup-does-not-steal-foreground-after-external-activation", "StartupDoesNotStealForegroundAfterExternalActivation", "startup", executionClass: ScenarioExecutionClass.Standalone);
        Add("global-tab-navigation", "GlobalTabNavigation", "keyboard-input", executionClass: ScenarioExecutionClass.Standalone);
        Add("split-affordance", "SplitAffordance", "split-focus", executionClass: ScenarioExecutionClass.Standalone);
        Add("capture-admission-blocked", "CaptureAdmissionBlocked", "diagnostics", executionClass: ScenarioExecutionClass.Standalone);

        return list;
    }
}

internal static partial class Scenarios
{
    // Compatibility projections. They are generated from ScenarioCatalog and
    // intentionally contain no independent scenario metadata.
    public static string[] AllOrder => ScenarioCatalog.AllOrder.ToArray();
    public static string[] BrowserOnlyScenarios => ScenarioCatalog.BrowserOnlyScenarios.ToArray();
    public static string[] StandaloneExtraScenarios => ScenarioCatalog.StandaloneExtraScenarios.ToArray();
    public static string[] RealAppGuestKinds => ScenarioCatalog.RealAppGuestKinds.ToArray();
    public static string[] BrowserGuestKinds => ScenarioCatalog.BrowserGuestKinds.ToArray();
    public static string[] OrchestratedShardNames => ScenarioCatalog.OrchestratedShardNames.ToArray();
    public static string[] ExplicitOnlyShardNames => ScenarioCatalog.ExplicitOnlyShardNames.ToArray();

    public static IReadOnlyList<string> GetShardScenarios(string shard)
        => ScenarioCatalog.GetShardScenarios(shard);

    public static void ValidateShardCoverage()
        => ScenarioCatalog.Validate();

    /// <summary>
    /// Selects the application and GuineaPig artifacts for one driver process.
    /// The catalog migration keeps this artifact discovery contract unchanged.
    /// </summary>
    public static void ConfigureArtifacts(string configuration, string rid, string? tabDockPath, string? guineaPigPath)
    {
        SelectedConfiguration = configuration;
        SelectedRid = rid;
        TabDockExe = ResolveArtifact(
            tabDockPath,
            Path.Combine("bin", configuration, "net8.0-windows"),
            "TabDock.exe",
            rid);
        bool shouldResolveDefaultPig = string.IsNullOrWhiteSpace(guineaPigPath)
            && string.IsNullOrWhiteSpace(tabDockPath);
        if (string.IsNullOrWhiteSpace(guineaPigPath) && !string.IsNullOrWhiteSpace(tabDockPath))
        {
            try
            {
                string root = Path.GetFullPath(RepoRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(tabDockPath);
                shouldResolveDefaultPig = candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (InvalidOperationException)
            {
                shouldResolveDefaultPig = false;
            }
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(guineaPigPath) || shouldResolveDefaultPig)
            {
                PigExe = ResolveArtifact(
                    guineaPigPath,
                    Path.Combine("tests", "ValidationDriver", "TabDock.GuineaPig", "bin", configuration, "net8.0-windows"),
                    "TabDock.GuineaPig.exe",
                    rid);
            }
            else
            {
                PigExe = string.Empty;
            }
        }
        catch (InvalidOperationException) when (!string.IsNullOrWhiteSpace(tabDockPath)
            && string.IsNullOrWhiteSpace(guineaPigPath))
        {
            // Native-free self-tests and qualification-plan commands need only
            // the explicitly bound candidate. A copied driver package may not
            // contain a TabDock.sln marker, so absence of an optional GuineaPig
            // must not make those commands depend on the original repository.
            PigExe = string.Empty;
        }
    }

    private static string ResolveArtifact(string? explicitPath, string relativeDirectory, string fileName, string rid)
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

        return candidates[0];
    }
}
