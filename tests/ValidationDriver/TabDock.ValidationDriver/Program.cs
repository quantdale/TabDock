using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
                case "--cycles":
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int n) || n < 1)
                        return Usage("--cycles requires a positive integer.");
                    opt.Cycles = n;
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
        if (selfTestSuite != null)
        {
            TestRunProvenance.BeginRun();
            GuardedProc.Log($"Deterministic qualification runId={TestRunProvenance.RunId}.");
            return DeterministicSelfTests.Run(selfTestSuite);
        }
        if (listRequested)
            return ListScenarios();
        if (scenarios.Count == 0 && opt.Shard == null)
            return Usage(null);

        if (opt.Shard != null)
        {
            opt.Shard = opt.Shard.ToLowerInvariant();
            if (scenarios.Count != 0)
                return Usage("--shard cannot be combined with a scenario argument.");
            if (!Scenarios.OrchestratedShardNames.Concat(Scenarios.ExplicitOnlyShardNames)
                .Contains(opt.Shard, StringComparer.Ordinal))
                return Usage($"Unknown shard '{opt.Shard}'.");
            scenarios.AddRange(Scenarios.GetShardScenarios(opt.Shard));
        }

        if (scenarios.Count == 1 && string.Equals(scenarios[0], "all", StringComparison.Ordinal))
            return RunAllShards(opt);

        Scenarios.ConfigureArtifacts(opt.Configuration, opt.Rid, opt.TabDockPath, opt.GuineaPigPath);
        foreach (string s in scenarios)
        {
            bool known = Array.IndexOf(Scenarios.AllOrder, s) >= 0
                || s == "realapp" || s == "browser-multi"
                || Array.IndexOf(Scenarios.BrowserOnlyScenarios, s) >= 0
                || Array.IndexOf(Scenarios.StandaloneExtraScenarios, s) >= 0;
            if (!known)
                return Usage($"Unknown scenario '{s}'.");
        }
        if (scenarios.Contains("realapp") && Array.IndexOf(Scenarios.RealAppGuestKinds, opt.Guest) < 0)
            return Usage($"realapp requires --guest {string.Join("|", Scenarios.RealAppGuestKinds)}.");
        foreach (string s in scenarios)
        {
            if ((s == "browser-multi" || Array.IndexOf(Scenarios.BrowserOnlyScenarios, s) >= 0)
                && Array.IndexOf(Scenarios.BrowserGuestKinds, opt.Guest) < 0)
                return Usage($"{s} requires --guest {string.Join("|", Scenarios.BrowserGuestKinds)}.");
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
        GuardedProc.Log($"Validation runId={TestRunProvenance.RunId} marker={TestRunProvenance.MarkerName}.");
        bool allPassed = true;
        int ran = 0;
        try
        {
            foreach (string s in scenarios)
            {
                Util.ThrowIfCancelled();
                allPassed &= Scenarios.RunScenario(s, opt);
                ran++;
            }
        }
        catch (OperationCanceledException)
        {
            GuardedProc.Log("Run aborted (overall 10-minute budget exceeded or Ctrl+C).");
            allPassed = false;
        }
        finally
        {
            GuardedProc.CleanupTrackedProcesses();
            Input.RestoreCursor();
        }

        GuardedProc.Log(allPassed
            ? $"ALL {ran} SCENARIO(S) PASSED."
            : "ONE OR MORE SCENARIOS FAILED.");
        return allPassed ? 0 : 5;
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
        Console.WriteLine($"Shards: {string.Join(", ", Scenarios.OrchestratedShardNames)}");
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

        bool allPassed = true;
        int completed = 0;
        try
        {
            foreach (string shard in Scenarios.OrchestratedShardNames)
            {
                Console.WriteLine();
                GuardedProc.Log($"=== SHARD {shard} ({Scenarios.GetShardScenarios(shard).Count} scenario(s)) ===");
                using Process child = GuardedProc.SpawnDriverShard(CreateShardProcessInfo(opt, shard));
                if (!child.WaitForExit((int)TimeSpan.FromMinutes(11).TotalMilliseconds))
                {
                    GuardedProc.Log($"SHARD {shard}: timed out at the bounded 11-minute parent limit; terminating child.");
                    try { child.Kill(entireProcessTree: true); } catch { }
                    allPassed = false;
                    break;
                }

                completed++;
                bool passed = child.ExitCode == 0;
                GuardedProc.Log($"SHARD {shard}: {(passed ? "PASS" : $"FAIL (exit {child.ExitCode})")}");
                allPassed &= passed;
                if (!passed)
                    break;
            }
        }
        finally
        {
            GuardedProc.CleanupTrackedProcesses();
        }

        GuardedProc.Log(allPassed
            ? $"ALL {completed} SHARD(S) PASSED."
            : "ONE OR MORE SHARDS FAILED.");
        return allPassed ? 0 : 5;
    }

    private static ProcessStartInfo CreateShardProcessInfo(Options opt, string shard)
    {
        string runner = Environment.ProcessPath ?? string.Empty;
        bool dotnetHost = string.Equals(Path.GetFileNameWithoutExtension(runner), "dotnet", StringComparison.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo
        {
            FileName = dotnetHost ? runner : Assembly.GetExecutingAssembly().Location,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        };
        if (dotnetHost)
            psi.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
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
        foreach (string s in Scenarios.AllOrder)
            Console.WriteLine($"  {s}");
        Console.WriteLine("  all            runs every bounded hermetic shard in separate child processes");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --yes          skip the interactive confirmation (supervised runs)");
        Console.WriteLine("  --selftest NAME run native-free split/identity contracts (split|identity|all)");
        Console.WriteLine("  --cycles N     cycle count for maximize-repro (default 3) and repeat-cycles (default 5)");
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
        Console.WriteLine("AllOrder (what 'all' runs, fresh TabDock per scenario):");
        foreach (string s in Scenarios.AllOrder)
            Console.WriteLine($"  {s}");
        Console.WriteLine();
        Console.WriteLine("BrowserOnlyScenarios (need --guest chrome-normal|edge-normal|firefox-normal):");
        foreach (string s in Scenarios.BrowserOnlyScenarios)
            Console.WriteLine($"  {s}");
        Console.WriteLine();
        Console.WriteLine("StandaloneExtraScenarios (each spawns its own guest):");
        foreach (string s in Scenarios.StandaloneExtraScenarios)
            Console.WriteLine($"  {s}");
        Console.WriteLine();
        Console.WriteLine("RealAppGuestKinds (--guest value for realapp):");
        foreach (string s in Scenarios.RealAppGuestKinds)
            Console.WriteLine($"  {s}");
        Console.WriteLine();
        Console.WriteLine("Shards:");
        foreach (string shard in Scenarios.OrchestratedShardNames)
            Console.WriteLine($"  {shard} ({Scenarios.GetShardScenarios(shard).Count} scenario(s))");
        foreach (string shard in Scenarios.ExplicitOnlyShardNames)
            Console.WriteLine($"  {shard} (explicit --guest/real-app selection required)");
        Console.WriteLine();
        Console.WriteLine("Also dispatchable but not in the arrays above: realapp, browser-multi.");
        return 0;
    }
}
