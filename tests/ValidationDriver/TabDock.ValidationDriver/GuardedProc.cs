using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace TabDock.ValidationDriver;

/// <summary>
/// Guarded process-spawn pattern per docs/internal/guarded-spawn-pattern.md:
/// hard spawn cap behind a lock, tracked-process list, kill-on-exit, single-instance
/// mutex name, overall time budget, and flushed logging for every spawn/kill.
/// No code in this driver may call Process.Start directly; everything routes through
/// <see cref="SpawnGuarded"/>.
/// </summary>
internal static class GuardedProc
{
    /// <summary>Spawn budget per scenario; the counter resets after each scenario's cleanup.</summary>
    public const int MaxTotalSpawns = 12;

    /// <summary>Absolute cap across a whole run (never reset) as a second line of defense for "all".</summary>
    public const int MaxTotalSpawnsHard = 60;

    public const string SingleInstanceMutexName = "Global\\TabDockValidationDriver";

    /// <summary>Overall run budget. Cancelled by Ctrl+C as well.</summary>
    public static readonly CancellationTokenSource Cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

    private static readonly object SpawnLock = new object();
    private static int _scenarioSpawnCount;
    private static int _totalSpawnCount;
    private static readonly List<Process> Tracked = new List<Process>();

    public static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        Console.Out.Flush();
    }

    public static Process SpawnGuarded(ProcessStartInfo psi)
    {
        lock (SpawnLock)
        {
            if (_scenarioSpawnCount >= MaxTotalSpawns)
                throw new InvalidOperationException($"Per-scenario spawn cap of {MaxTotalSpawns} exceeded — aborting.");
            if (_totalSpawnCount >= MaxTotalSpawnsHard)
                throw new InvalidOperationException($"Hard total spawn cap of {MaxTotalSpawnsHard} exceeded — aborting.");

            Log($"Spawning {_scenarioSpawnCount + 1}/{MaxTotalSpawns} (scenario), {_totalSpawnCount + 1}/{MaxTotalSpawnsHard} (run): {psi.FileName} {psi.Arguments}");
            Process? p = Process.Start(psi);
            if (p == null)
                throw new InvalidOperationException("SpawnGuarded received a null Process.");

            _scenarioSpawnCount++;
            _totalSpawnCount++;
            Tracked.Add(p);
            Log($"Spawned PID {p.Id}.");
            return p;
        }
    }

    /// <summary>
    /// Track a process this driver caused to exist but did not start directly
    /// (e.g. wt.exe hands its window to WindowsTerminal.exe) so cleanup can kill it.
    /// Only PIDs registered here or via SpawnGuarded are ever killed.
    /// </summary>
    public static void Track(Process p)
    {
        lock (SpawnLock)
        {
            Tracked.Add(p);
            Log($"Tracking external PID {p.Id} ({SafeName(p)}) for cleanup.");
        }
    }

    /// <summary>Resets the per-scenario spawn budget. Call only after the previous scenario's cleanup.</summary>
    public static void ResetScenarioBudget()
    {
        lock (SpawnLock)
        {
            _scenarioSpawnCount = 0;
        }
    }

    /// <summary>Kills every still-running tracked process (and its tree). Never touches untracked PIDs.</summary>
    public static void CleanupTrackedProcesses()
    {
        List<Process> copy;
        lock (SpawnLock)
        {
            copy = new List<Process>(Tracked);
        }

        foreach (var p in copy)
        {
            try
            {
                if (!p.HasExited)
                {
                    if (IsAncestorOfCurrentProcess(p.Id))
                    {
                        Log($"SAFETY: REFUSING to kill tracked PID {p.Id} ({SafeName(p)}) — it is this driver's own " +
                            "process or an ancestor of it (shared-instance host, e.g. Windows Terminal's monarch " +
                            "process), not an isolated spawned child. Skipping kill; any captured window was " +
                            "already closed via WM_CLOSE where applicable.");
                        continue;
                    }
                    Log($"Killing tracked process {p.Id} ({SafeName(p)})...");
                    p.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to kill tracked process: {ex.Message}");
            }
        }

        lock (SpawnLock)
        {
            Tracked.RemoveAll(p =>
            {
                try { return p.HasExited; }
                catch { return true; }
            });
        }
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName; }
        catch { return "?"; }
    }

    /// <summary>
    /// True if <paramref name="candidatePid"/> is this driver process itself or
    /// an ancestor of it. Discovered live: wt.exe hands new windows to an
    /// already-running WindowsTerminal.exe "monarch" process rather than
    /// spawning a fresh one, and that monarch process can be the very process
    /// hosting the shell this driver was launched from. A naive
    /// Kill(entireProcessTree: true) against such a PID would try to kill an
    /// ancestor of the calling process — .NET happens to refuse that specific
    /// case, but this check makes the refusal explicit and proactive instead
    /// of relying on catching that one exception message, and it covers
    /// pattern-alike shared-instance hosts beyond just Windows Terminal.
    /// </summary>
    public static bool IsAncestorOfCurrentProcess(int candidatePid)
    {
        uint current = GetCurrentProcessIdSafe();
        if ((uint)candidatePid == current)
            return true;

        Dictionary<uint, uint> parentOf = BuildParentMap();
        uint walk = current;
        for (int hops = 0; hops < 64; hops++) // hard bound: never walk an unbounded/cyclic chain
        {
            if (!parentOf.TryGetValue(walk, out uint parent) || parent == 0)
                return false;
            if (parent == (uint)candidatePid)
                return true;
            walk = parent;
        }
        return false;
    }

    private static uint GetCurrentProcessIdSafe()
    {
        try { return (uint)Process.GetCurrentProcess().Id; }
        catch { return 0; }
    }

    private static Dictionary<uint, uint> BuildParentMap()
    {
        var map = new Dictionary<uint, uint>();
        IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1))
            return map;
        try
        {
            var entry = new NativeMethods.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>() };
            if (!NativeMethods.Process32First(snap, ref entry))
                return map;
            do
            {
                map[entry.th32ProcessID] = entry.th32ParentProcessID;
            } while (NativeMethods.Process32Next(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }
        return map;
    }
}

/// <summary>Small shared wait/format helpers.</summary>
internal static class Util
{
    public static void ThrowIfCancelled()
    {
        if (GuardedProc.Cts.IsCancellationRequested)
            throw new OperationCanceledException("Overall time budget exceeded or Ctrl+C pressed.");
    }

    /// <summary>Polls <paramref name="condition"/> until true or the timeout elapses. Honors the run budget.</summary>
    public static bool WaitUntil(Func<bool> condition, int timeoutMs, int pollMs = 100)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            ThrowIfCancelled();
            bool ok;
            try { ok = condition(); }
            catch { ok = false; }
            if (ok)
                return true;
            if (sw.ElapsedMilliseconds >= timeoutMs)
                return false;
            Thread.Sleep(pollMs);
        }
    }

    public static bool RectNear(NativeMethods.RECT a, NativeMethods.RECT b, int tolerance)
    {
        return Math.Abs(a.left - b.left) <= tolerance
            && Math.Abs(a.top - b.top) <= tolerance
            && Math.Abs(a.right - b.right) <= tolerance
            && Math.Abs(a.bottom - b.bottom) <= tolerance;
    }

    public static string FormatRect(NativeMethods.RECT r)
    {
        return $"({r.left},{r.top})-({r.right},{r.bottom}) {r.Width}x{r.Height}";
    }
}
