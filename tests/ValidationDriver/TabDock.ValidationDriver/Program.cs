using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace TabDock.ValidationDriver;

/// <summary>
/// Real-input validation driver for TabDock.
///
/// Usage: TabDock.ValidationDriver.exe [options] &lt;scenario|all&gt;
///
/// Spawns a fresh TabDock plus guinea-pig windows, drives them exclusively with real
/// SendInput mouse/keyboard events at UIA-read coordinates, and asserts on window state,
/// pixels (BitBlt), the TabDock log, and the pigs' window-message logs.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // The driver must read screen coordinates in the same (physical-pixel)
        // space TabDock operates in, or pre/post-capture rect comparisons break:
        // a DPI-unaware process gets GetWindowRect results DPI-virtualized (0.8x
        // on a 125% monitor), and WPF/UIA init later flips this process to
        // PerMonitorV2 mid-run — so the same window reads virtualized before a
        // capture and physical after. Declare PerMonitorV2 up front so every
        // read is consistently physical.
        NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorV2);

        var opt = new Options();
        var scenarios = new List<string>();
        string? selfTestSuite = null;
        string? planGate = null;
        bool listRequested = false;
        bool helpRequested = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--yes":
                    opt.Yes = true;
                    break;
                case "--selftest":
                    if (i + 1 >= args.Length)
                        return Usage("--selftest requires split, identity, or all.");
                    selfTestSuite = args[++i];
                    break;
                case "--plan":
                    if (i + 1 >= args.Length)
                        return Usage("--plan requires a gate (physicalMixedDpi|automated|release|all).");
                    planGate = args[++i];
                    break;
                case "--cycles":
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int n) || n < 1)
                        return Usage("--cycles requires a positive integer.");
                    opt.Cycles = n;
                    i++;
                    break;
                case "--resource-soak":
                    opt.ResourceSoak = true;
                    break;
                case "--resource-headless":
                    opt.ResourceSoak = true;
                    opt.ResourceHeadless = true;
                    break;
                case "--profile":
                    if (i + 1 >= args.Length)
                        return Usage("--profile requires a resource profile name.");
                    opt.ResourceProfile = args[++i];
                    break;
                case "--duration-seconds":
                    if (i + 1 >= args.Length
                        || !int.TryParse(args[i + 1], out int duration)
                        || duration < 1
                        || duration > 600)
                    {
                        return Usage("--duration-seconds requires an integer from 1 through 600.");
                    }
                    opt.ResourceDurationSeconds = duration;
                    i++;
                    break;
                case "--artifact-output":
                    if (i + 1 >= args.Length)
                        return Usage("--artifact-output requires a directory path.");
                    opt.ResourceArtifactOutput = args[++i];
                    break;
                case "--seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int seed))
                        return Usage("--seed requires a 32-bit integer.");
                    opt.ResourceSeed = seed;
                    i++;
                    break;
                case "--reruns":
                case "--rerun":
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int reruns) || reruns < 0 || reruns > 5)
                        return Usage("--reruns requires an integer from 0 through 5 (additional investigation attempts).");
                    opt.Reruns = reruns;
                    i++;
                    break;
                case "--guest":
                    if (i + 1 >= args.Length)
                        return Usage("--guest requires a value (pig|wt|chrome-nogpu|chrome-gpu|chrome-normal|edge-normal|firefox-normal|codex|chatgptclassic).");
                    opt.Guest = args[++i];
                    break;
                case "--configuration":
                    if (i + 1 >= args.Length)
                        return Usage("--configuration requires Debug or Release.");
                    opt.Configuration = args[++i];
                    break;
                case "--rid":
                    if (i + 1 >= args.Length)
                        return Usage("--rid requires auto, none, or win-x64.");
                    opt.Rid = args[++i];
                    break;
                case "--tabdock":
                    if (i + 1 >= args.Length)
                        return Usage("--tabdock requires an executable path.");
                    opt.TabDockPath = args[++i];
                    break;
                case "--guineapig":
                    if (i + 1 >= args.Length)
                        return Usage("--guineapig requires an executable path.");
                    opt.GuineaPigPath = args[++i];
                    break;
                case "--shard":
                    if (i + 1 >= args.Length)
                        return Usage("--shard requires a named shard.");
                    opt.Shard = args[++i];
                    break;
                case "--list":
                    listRequested = true;
                    break;
                case "--help":
                case "-h":
                    helpRequested = true;
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                        return Usage($"Unknown option '{args[i]}'.");
                    scenarios.Add(args[i]);
                    break;
            }
        }

        if (!string.Equals(opt.Configuration, "Debug", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(opt.Configuration, "Release", StringComparison.OrdinalIgnoreCase))
            return Usage("--configuration must be Debug or Release.");
        opt.Configuration = opt.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        if (!string.Equals(opt.Rid, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(opt.Rid, "none", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(opt.Rid, "win-x64", StringComparison.OrdinalIgnoreCase))
            return Usage("--rid must be auto, none, or win-x64.");
        opt.Rid = opt.Rid.ToLowerInvariant();
        if (opt.ResourceSoak && opt.Cycles.GetValueOrDefault(100) > 10_000)
            return Usage("resource --cycles is capped at 10000 to keep evidence and process lifetime bounded.");

        try
        {
            Scenarios.ValidateShardCoverage();
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"ValidationDriver registration error: {ex.Message}");
            return 4;
        }

        if (helpRequested)
            return Usage(null, 0);
        if (planGate != null)
            return PrintQualificationPlan(planGate, opt);
        if (selfTestSuite != null)
        {
            // Deterministic runs may still be bound to the exact retained
            // candidate. When --tabdock/--guineapig are supplied, configure
            // those paths before the manifest writer computes executable
            // identity; no native scenario is launched by this branch.
            if (!string.IsNullOrWhiteSpace(opt.TabDockPath)
                || !string.IsNullOrWhiteSpace(opt.GuineaPigPath))
            {
                Scenarios.ConfigureArtifacts(opt.Configuration, opt.Rid, opt.TabDockPath, opt.GuineaPigPath);
            }
            TestRunProvenance.BeginRun();
            QualificationResultWriter.BeginRun();
            GuardedProc.Log($"Deterministic qualification runId={TestRunProvenance.RunId}.");
            return DeterministicSelfTests.Run(selfTestSuite);
        }
        if (opt.ResourceSoak)
            return ResourceSoakRunner.Run(opt);
        if (listRequested)
            return ListScenarios();
        if (scenarios.Count == 0 && opt.Shard == null)
            return Usage(null);

        if (opt.Shard != null)
        {
            opt.Shard = opt.Shard.ToLowerInvariant();
            if (scenarios.Count != 0)
                return Usage("--shard cannot be combined with a scenario argument.");
            if (!ScenarioCatalog.OrchestratedShardNames.Concat(ScenarioCatalog.ExplicitOnlyShardNames)
                .Contains(opt.Shard, StringComparer.Ordinal))
                return Usage($"Unknown shard '{opt.Shard}'.");
            scenarios.AddRange(ScenarioCatalog.GetShardScenarios(opt.Shard));
        }

        if (scenarios.Count == 1 && string.Equals(scenarios[0], "all", StringComparison.Ordinal))
            return RunAllShards(opt);

        Scenarios.ConfigureArtifacts(opt.Configuration, opt.Rid, opt.TabDockPath, opt.GuineaPigPath);
        foreach (string s in scenarios)
        {
            if (!ScenarioCatalog.TryGet(s, out ScenarioDefinition definition))
                return Usage($"Unknown scenario '{s}'.");
        }
        if (scenarios.Any(s => ScenarioCatalog.TryGet(s, out ScenarioDefinition definition)
            && definition.ExecutionClass == ScenarioExecutionClass.UserOwnedApplication)
            && !ScenarioCatalog.RealAppGuestKinds.Contains(opt.Guest, StringComparer.OrdinalIgnoreCase))
            return Usage($"real-app scenarios require --guest {string.Join("|", ScenarioCatalog.RealAppGuestKinds)}.");
        foreach (string s in scenarios)
        {
            if (ScenarioCatalog.TryGet(s, out ScenarioDefinition definition)
                && definition.ExecutionClass == ScenarioExecutionClass.Browser
                && !ScenarioCatalog.BrowserGuestKinds.Contains(opt.Guest, StringComparer.OrdinalIgnoreCase))
                return Usage($"{s} requires --guest {string.Join("|", ScenarioCatalog.BrowserGuestKinds)}.");
        }

        // Single-instance guard (guarded-spawn pattern rule 3).
        using var mutex = new Mutex(true, GuardedProc.SingleInstanceMutexName, out bool isNew);
        if (!isNew)
        {
            Console.WriteLine("Another TabDock.ValidationDriver instance is already running. Aborting.");
            return 2;
        }

        if (!File.Exists(Scenarios.TabDockExe))
        {
            Console.WriteLine($"TabDock build not found: {Scenarios.TabDockExe}");
            Console.WriteLine("Build it first: dotnet build TabDock.csproj");
            return 4;
        }
        if (!File.Exists(Scenarios.PigExe))
        {
            Console.WriteLine($"GuineaPig build not found: {Scenarios.PigExe}");
            Console.WriteLine(@"Build it first: dotnet build tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj");
            return 4;
        }

        Console.WriteLine($"[PID {Environment.ProcessId}] TabDock real-input validation driver ({Scenarios.SelectedConfiguration}, RID {Scenarios.SelectedRid}).");
        Console.WriteLine($"Artifacts: TabDock={Scenarios.TabDockExe}; GuineaPig={Scenarios.PigExe}");
        Console.WriteLine($"Scenarios: {string.Join(", ", scenarios)}");
        if (opt.Reruns > 0)
            Console.WriteLine($"Investigation reruns: {opt.Reruns} additional attempt(s); first-attempt outcomes remain authoritative.");
        Console.WriteLine();
        Console.WriteLine("This run will:");
        Console.WriteLine("  - spawn a fresh TabDock instance (aborts if one is already running) plus guinea-pig windows,");
        Console.WriteLine("  - send REAL mouse and keyboard input (do NOT touch mouse/keyboard during the run),");
        Console.WriteLine("  - kill every process it spawned when each scenario finishes.");
        if (!opt.Yes)
        {
            Console.Write("Type y to continue: ");
            string? answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted by user.");
                return 3;
            }
        }
        else
        {
            GuardedProc.Log("--yes supplied; confirmation skipped (supervised run).");
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            GuardedProc.Log("Ctrl+C pressed — cancelling and cleaning up...");
            GuardedProc.Cts.Cancel();
        };

        Input.SaveCursor();
        TestRunProvenance.BeginRun();
        QualificationResultWriter.BeginRun();
        GuardedProc.Log($"Validation runId={TestRunProvenance.RunId} marker={TestRunProvenance.MarkerName}.");
        int ran = 0;
        try
        {
            foreach (string s in scenarios)
            {
                for (int attempt = 1; attempt <= opt.Reruns + 1; attempt++)
                {
                    Util.ThrowIfCancelled();
                    Scenarios.RunScenario(s, opt, attempt);
                    ran++;
                    if (attempt <= opt.Reruns)
                        GuardedProc.Log($"=== INVESTIGATION RERUN scenario={s} attempt={attempt + 1}/{opt.Reruns + 1} ===");
                }
            }
        }
        catch (OperationCanceledException)
        {
            GuardedProc.Log("Run aborted (overall 10-minute budget exceeded or Ctrl+C).");
        }
        finally
        {
            GuardedProc.CleanupTrackedProcesses();
            Input.RestoreCursor();
        }

        ScenarioOutcome runOutcome = QualificationResultWriter.WriteRunManifest();
        GuardedProc.Log(runOutcome.IsReleasePass
            ? $"ALL {ran} SCENARIO(S) PASSED."
            : $"RUN OUTCOME {runOutcome.Code}: one or more scenarios were not release-pass.");
        return ScenarioOutcomeContract.ExitCode(runOutcome.Kind);
    }

    private static int RunAllShards(Options opt)
    {
        Scenarios.ConfigureArtifacts(opt.Configuration, opt.Rid, opt.TabDockPath, opt.GuineaPigPath);
        if (!File.Exists(Scenarios.TabDockExe))
            return MissingArtifact(Scenarios.TabDockExe, "TabDock", opt.Configuration, opt.Rid);
        if (!File.Exists(Scenarios.PigExe))
            return MissingArtifact(Scenarios.PigExe, "GuineaPig", opt.Configuration, opt.Rid);

        Console.WriteLine($"[PID {Environment.ProcessId}] TabDock real-input shard orchestrator ({Scenarios.SelectedConfiguration}, RID {Scenarios.SelectedRid}).");
        Console.WriteLine($"Artifacts: TabDock={Scenarios.TabDockExe}; GuineaPig={Scenarios.PigExe}");
        Console.WriteLine($"Shards: {string.Join(", ", ScenarioCatalog.OrchestratedShardNames)}");
        Console.WriteLine("Each shard is a separate guarded driver process with its own 12-spawn and 10-minute limits.");
        if (!opt.Yes)
        {
            Console.Write("Type y to continue: ");
            string? answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted by user.");
                return 3;
            }
        }

        TestRunProvenance.BeginRun();
        QualificationResultWriter.BeginRun();
        DateTimeOffset parentStartedUtc = DateTimeOffset.UtcNow;
        string parentRoot = TestRunProvenance.ArtifactDirectory;
        string candidateSha = QualificationResultWriter.CandidateSha();
        string candidateExecutableSha = QualificationResultWriter.Sha256File(Scenarios.TabDockExe);
        string driverSha = QualificationResultWriter.DriverIdentitySha256();
        var imports = new List<ChildManifestVerification>();

        try
        {
            foreach (string shard in ScenarioCatalog.OrchestratedShardNames)
            {
                string childArtifactBase = Path.Combine(parentRoot, "children", shard);
                Directory.CreateDirectory(childArtifactBase);
                Console.WriteLine();
                GuardedProc.Log($"=== SHARD {shard} ({ScenarioCatalog.GetShardScenarios(shard).Count} scenario(s)) ===");
                ChildManifestVerification import;
                Process? child = null;
                try
                {
                    child = GuardedProc.SpawnDriverShard(
                        CreateShardProcessInfo(opt, shard, childArtifactBase, TestRunProvenance.RunId));
                    if (!child.WaitForExit((int)TimeSpan.FromMinutes(11).TotalMilliseconds))
                    {
                        GuardedProc.Log($"SHARD {shard}: timed out at the bounded parent limit; terminating child.");
                        try { child.Kill(entireProcessTree: true); } catch { }
                        import = ChildManifestVerification.Invalid(
                            shard,
                            ScenarioOutcomeContract.ExitCode(ScenarioOutcomeKind.FailHarness),
                            $"shard {shard} exceeded the bounded parent timeout");
                    }
                    else
                    {
                        import = QualificationManifestVerifier.ImportChild(
                            childArtifactBase,
                            parentRoot,
                            shard,
                            TestRunProvenance.RunId,
                            candidateSha,
                            candidateExecutableSha,
                            driverSha,
                            parentStartedUtc,
                            child.ExitCode);
                    }
                }
                catch (Exception ex)
                {
                    import = ChildManifestVerification.Invalid(
                        shard,
                        ScenarioOutcomeContract.ExitCode(ScenarioOutcomeKind.FailHarness),
                        $"shard process could not be supervised: {ex.GetType().Name}");
                }
                finally
                {
                    child?.Dispose();
                }

                imports.Add(import);
                GuardedProc.Log(
                    $"SHARD {shard}: verified={import.Valid} outcome={import.Outcome.Code} exit={import.ExitCode}" +
                    (import.FailureReason == null ? string.Empty : $" reason={import.FailureReason}"));
            }
        }
        catch (OperationCanceledException)
        {
            imports.AddRange(ScenarioCatalog.OrchestratedShardNames
                .Where(shard => !imports.Any(item => item.ExpectedShard == shard))
                .Select(shard => ChildManifestVerification.Invalid(
                    shard,
                    ScenarioOutcomeContract.ExitCode(ScenarioOutcomeKind.FailHarness),
                    "all-run cancellation left the declared shard unlaunched")));
        }
        finally
        {
            GuardedProc.CleanupTrackedProcesses();
        }

        ParentManifestWriteResult parent = QualificationParentManifestWriter.Write(
            parentRoot,
            TestRunProvenance.RunId,
            parentStartedUtc,
            candidateSha,
            candidateExecutableSha,
            driverSha,
            ScenarioCatalog.OrchestratedShardNames,
            imports);
        GuardedProc.Log(
            $"RUN_MANIFEST result={parent.Outcome.Code} artifact=<validation-artifact>/run-manifest.json " +
            $"parentRunId={TestRunProvenance.RunId} childManifests={imports.Count}");
        return ScenarioOutcomeContract.ExitCode(parent.Outcome.Kind);
    }

    private static ProcessStartInfo CreateShardProcessInfo(
        Options opt,
        string shard,
        string childArtifactBase,
        string parentRunId)
    {
        // A managed DLL must never be executed directly: CreateProcess starts a
        // host that fails CLR assembly binding ("System.Runtime, Version=8.0.0.0").
        // Prefer the apphost beside the assembly; fall back to a real dotnet host.
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;
        string? apphost = string.IsNullOrEmpty(assemblyLocation)
            ? null
            : Path.ChangeExtension(assemblyLocation, ".exe");
        bool useApphost = apphost != null && File.Exists(apphost);
        string runner = Environment.ProcessPath ?? string.Empty;
        bool dotnetHost = !string.IsNullOrEmpty(runner)
            && string.Equals(Path.GetFileNameWithoutExtension(runner), "dotnet", StringComparison.OrdinalIgnoreCase);
        if (!useApphost && !dotnetHost)
            throw new InvalidOperationException(
                $"Cannot spawn shard children: no apphost at '{apphost ?? "<none>"}' and process host '{runner}' is not a dotnet host.");
        var psi = new ProcessStartInfo
        {
            FileName = useApphost ? apphost! : runner,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        };
        if (!useApphost)
            psi.ArgumentList.Add(assemblyLocation);
        psi.ArgumentList.Add("--yes");
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(opt.Configuration);
        psi.ArgumentList.Add("--rid");
        psi.ArgumentList.Add(opt.Rid);
        psi.ArgumentList.Add("--tabdock");
        psi.ArgumentList.Add(Scenarios.TabDockExe);
        psi.ArgumentList.Add("--guineapig");
        psi.ArgumentList.Add(Scenarios.PigExe);
        psi.ArgumentList.Add("--shard");
        psi.ArgumentList.Add(shard);
        if (opt.Cycles.HasValue)
        {
            psi.ArgumentList.Add("--cycles");
            psi.ArgumentList.Add(opt.Cycles.Value.ToString());
        }
        if (opt.Reruns > 0)
        {
            psi.ArgumentList.Add("--reruns");
            psi.ArgumentList.Add(opt.Reruns.ToString());
        }
        psi.Environment["TABDOCK_VALIDATION_ARTIFACT_ROOT"] = childArtifactBase;
        psi.Environment.Remove("TABDOCK_VALIDATION_RESULT_ROOT");
        psi.Environment["TABDOCK_VALIDATION_RUN_KIND"] = "shard";
        psi.Environment["TABDOCK_VALIDATION_PARENT_RUN_ID"] = parentRunId;
        psi.Environment["TABDOCK_VALIDATION_SHARD"] = shard;
        return psi;
    }

    private static int MissingArtifact(string path, string label, string configuration, string rid)
    {
        Console.WriteLine($"{label} build not found for configuration={configuration}, rid={rid}: {path}");
        Console.WriteLine("Build the requested projects first, or pass --tabdock/--guineapig with explicit executable paths.");
        return 4;
    }

    private static int Usage(string? error, int exitCode = 1)
    {
        if (error != null)
            Console.WriteLine($"Error: {error}");
        Console.WriteLine("Usage: TabDock.ValidationDriver.exe [options] <scenario|all>");
        Console.WriteLine();
        Console.WriteLine("Scenarios:");
        foreach (string s in ScenarioCatalog.AllOrder)
            Console.WriteLine($"  {s}");
        Console.WriteLine("  all            runs every bounded hermetic shard in separate child processes");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --yes          skip the interactive confirmation (supervised runs)");
        Console.WriteLine("  --selftest NAME run native-free split/identity contracts (split|identity|all)");
        Console.WriteLine("  --resource-headless run bounded CI-safe resource lifecycle profiles with synthetic snapshots");
        Console.WriteLine("  --resource-soak run the same profiles plus read-only counters for a run-owned TabDock process");
        Console.WriteLine("  --profile NAME    resource profile: all, group-capture, split, layout, picker-icon, winevent, diagnostics, persistence, restart");
        Console.WriteLine("  --duration-seconds N  resource sample duration (1-600; opt-in local soak)");
        Console.WriteLine("  --artifact-output PATH  resource evidence root (default: temp TabDock-ResourceStability)");
        Console.WriteLine("  --seed N        deterministic resource-profile seed (default 20260824)");
        Console.WriteLine("  --plan GATE    print a JSON qualification plan without sending input (physicalMixedDpi|automated|release|all)");
        Console.WriteLine("  --cycles N     cycle count for maximize-repro (default 3) and repeat-cycles (default 5)");
        Console.WriteLine("  --reruns N     run N additional bounded investigation attempts; never best-of-N (default 0)");
        Console.WriteLine("  --guest KIND   guest app for scenarios that need one: pig (default), wt, chrome-nogpu, chrome-gpu, chrome-normal, edge-normal, firefox-normal, codex, chatgptclassic");
        Console.WriteLine("  --configuration Debug|Release   select build output (default Debug)");
        Console.WriteLine("  --rid auto|none|win-x64         select RID-specific output (default auto)");
        Console.WriteLine("  --tabdock PATH                  override TabDock.exe discovery");
        Console.WriteLine("  --guineapig PATH                override TabDock.GuineaPig.exe discovery");
        Console.WriteLine("  --shard NAME                    run one bounded shard; use --list for names");
        Console.WriteLine("  --list                          print scenarios and shard assignments, then exit");
        return exitCode;
    }

    private static int ListScenarios()
    {
        Console.WriteLine($"Scenario catalog: {ScenarioCatalog.Generation} ({ScenarioCatalog.All.Count} dispatchable scenario(s))");
        Console.WriteLine();
        Console.WriteLine("AllOrder (what 'all' runs, fresh TabDock per scenario):");
        foreach (string s in ScenarioCatalog.AllOrder)
            Console.WriteLine($"  {s}");
        Console.WriteLine();
        Console.WriteLine("BrowserOnlyScenarios (need --guest chrome-normal|edge-normal|firefox-normal):");
        foreach (string s in ScenarioCatalog.BrowserOnlyScenarios)
            Console.WriteLine($"  {s}");
        Console.WriteLine();
        Console.WriteLine("StandaloneExtraScenarios (each spawns its own guest):");
        foreach (string s in ScenarioCatalog.StandaloneExtraScenarios)
            Console.WriteLine($"  {s}");
        Console.WriteLine();
        Console.WriteLine("RealAppGuestKinds (--guest value for realapp):");
        foreach (string s in ScenarioCatalog.RealAppGuestKinds)
            Console.WriteLine($"  {s}");
        Console.WriteLine();
        Console.WriteLine("Shards:");
        foreach (string shard in ScenarioCatalog.OrchestratedShardNames)
            Console.WriteLine($"  {shard} ({ScenarioCatalog.GetShardScenarios(shard).Count} scenario(s))");
        foreach (string shard in ScenarioCatalog.ExplicitOnlyShardNames)
            Console.WriteLine($"  {shard} (explicit --guest/real-app selection required)");
        Console.WriteLine();
        Console.WriteLine("Also dispatchable but not in the arrays above: realapp, browser-multi.");
        return 0;
    }

    private static int PrintQualificationPlan(string gate, Options options)
    {
        string normalized = gate.Trim().ToLowerInvariant();
        IReadOnlyList<ScenarioDefinition> required = normalized switch
        {
            "physicaldpimixed" or "physicalmixeddpi" or "mixed-dpi" or "mixed_dpi"
                => ScenarioCatalog.All.Where(definition => definition.RequiresMixedDpi
                    || definition.RequiresNonDefaultDpi
                    || definition.RequiresNegativeCoordinates).ToArray(),
            "automated" or "deterministic"
                => ScenarioCatalog.All.Where(definition => definition.IncludeInAll).ToArray(),
            "release" or "production"
                => ScenarioCatalog.All.Where(definition => definition.MayContributeReleaseEvidence
                    && definition.IncludeInAll).ToArray(),
            "all" => ScenarioCatalog.All.ToArray(),
            _ => Array.Empty<ScenarioDefinition>(),
        };
        if (required.Count == 0 && normalized is not "physicaldpimixed" and not "physicalmixeddpi"
            and not "mixed-dpi" and not "mixed_dpi")
            return Usage($"Unknown or empty qualification plan gate '{gate}'.");

        ScenarioCapabilitySnapshot snapshot = ScenarioCapabilities.CaptureCurrent();
        DesktopQualificationSnapshot desktop = new NativeDesktopQualificationProbe().Capture();
        var requiredRows = required.Select(definition => PlanRow(definition, options, snapshot, required: true)).ToArray();
        var optionalRows = ScenarioCatalog.All
            .Where(definition => !required.Contains(definition))
            .Where(definition => definition.ExecutionClass is not ScenarioExecutionClass.Browser
                and not ScenarioExecutionClass.UserOwnedApplication)
            .Select(definition => PlanRow(definition, options, snapshot, required: false))
            .ToArray();
        var payload = new
        {
            schemaVersion = 1,
            planKind = "qualification-plan",
            gate = normalized,
            catalogGeneration = ScenarioCatalog.Generation,
            syntheticTopology = false,
            environment = new
            {
                os = Environment.OSVersion.VersionString,
                osBuild = Environment.OSVersion.Version.Build,
                architecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                interactiveSession = snapshot.InteractiveSessionAvailable,
                workstationLocked = snapshot.WorkstationLocked,
            },
            topology = new
            {
                syntheticTopology = false,
                monitorCount = desktop.Monitors.Count,
                dpiValues = desktop.Monitors.Where(monitor => monitor.Dpi != 0)
                    .Select(monitor => (int)monitor.Dpi).Distinct().OrderBy(value => value).ToArray(),
                mixedDpi = desktop.MixedDpiAvailable,
                negativeCoordinates = desktop.Monitors.Any(monitor => monitor.Left < 0 || monitor.Top < 0),
                monitors = desktop.Monitors.Select(monitor => new
                {
                    left = monitor.Left,
                    top = monitor.Top,
                    right = monitor.Right,
                    bottom = monitor.Bottom,
                    dpi = monitor.Dpi,
                }).ToArray(),
            },
            required = requiredRows,
            optional = optionalRows,
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static object PlanRow(
        ScenarioDefinition definition,
        Options options,
        ScenarioCapabilitySnapshot snapshot,
        bool required)
    {
        ScenarioCapabilityResolution resolution = ScenarioCapabilities.Resolve(
            ScenarioCapabilities.Describe(definition, options), snapshot);
        return new
        {
            id = definition.Id,
            dispatch = definition.DispatchIdentifier,
            shard = definition.Shard,
            required,
            executionClass = definition.ExecutionClass.ToString(),
            inputRequirement = definition.InputRequirement.ToString(),
            requiresInteractiveSession = definition.RequiresInteractiveSession,
            requiresSupervision = definition.RequiresSupervision,
            requiresMultiMonitor = definition.RequiresMultiMonitor,
            requiresMixedDpi = definition.RequiresMixedDpi,
            requiresNonDefaultDpi = definition.RequiresNonDefaultDpi,
            requiresNegativeCoordinates = definition.RequiresNegativeCoordinates,
            destructiveState = definition.DestructiveState.ToString(),
            expectedRuntimeSeconds = definition.ExpectedRuntimeSeconds,
            mayContributeReleaseEvidence = definition.MayContributeReleaseEvidence,
            runnable = resolution.Runnable,
            outcome = resolution.Runnable
                ? ScenarioOutcomeContract.Code(ScenarioOutcomeKind.Pass)
                : ScenarioOutcomeContract.Code(resolution.Outcome ?? ScenarioOutcomeKind.FailHarness),
            reason = resolution.Reason,
        };
    }
}
