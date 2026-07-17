using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace TabDock.ValidationDriver;

/// <summary>
/// Real-input validation driver for TabDock.
///
/// Usage: TabDock.ValidationDriver.exe [--yes] [--cycles N] [--guest pig|wt|chrome-nogpu|chrome-gpu] &lt;scenario|all&gt;
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
        var opt = new Options();
        var scenarios = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--yes":
                    opt.Yes = true;
                    break;
                case "--cycles":
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int n) || n < 1)
                        return Usage("--cycles requires a positive integer.");
                    opt.Cycles = n;
                    i++;
                    break;
                case "--guest":
                    if (i + 1 >= args.Length)
                        return Usage("--guest requires a value (pig|wt|chrome-nogpu|chrome-gpu).");
                    opt.Guest = args[++i];
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                        return Usage($"Unknown option '{args[i]}'.");
                    scenarios.Add(args[i]);
                    break;
            }
        }

        if (scenarios.Count == 0)
            return Usage(null);
        if (scenarios.Count == 1 && scenarios[0] == "all")
        {
            scenarios.Clear();
            scenarios.AddRange(Scenarios.AllOrder);
        }
        foreach (string s in scenarios)
        {
            bool known = Array.IndexOf(Scenarios.AllOrder, s) >= 0
                || s == "realapp" || s == "browser-multi"
                || Array.IndexOf(Scenarios.BrowserOnlyScenarios, s) >= 0;
            if (!known)
                return Usage($"Unknown scenario '{s}'.");
        }
        if (scenarios.Contains("realapp") && Array.IndexOf(Scenarios.RealAppGuestKinds, opt.Guest) < 0)
            return Usage($"realapp requires --guest {string.Join("|", Scenarios.RealAppGuestKinds)}.");
        foreach (string s in Scenarios.BrowserOnlyScenarios)
        {
            if (scenarios.Contains(s) && Array.IndexOf(Scenarios.BrowserGuestKinds, opt.Guest) < 0)
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

        Console.WriteLine($"[PID {Environment.ProcessId}] TabDock real-input validation driver.");
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

    private static int Usage(string? error)
    {
        if (error != null)
            Console.WriteLine($"Error: {error}");
        Console.WriteLine("Usage: TabDock.ValidationDriver.exe [--yes] [--cycles N] [--guest pig|wt|chrome-nogpu|chrome-gpu] <scenario|all>");
        Console.WriteLine();
        Console.WriteLine("Scenarios:");
        foreach (string s in Scenarios.AllOrder)
            Console.WriteLine($"  {s}");
        Console.WriteLine("  all            runs every scenario above in order (fresh TabDock per scenario)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --yes          skip the interactive confirmation (supervised runs)");
        Console.WriteLine("  --cycles N     cycle count for maximize-repro (default 3) and repeat-cycles (default 5)");
        Console.WriteLine("  --guest KIND   guest app for maximize-repro: pig (default), wt, chrome-nogpu, chrome-gpu");
        return 1;
    }
}
