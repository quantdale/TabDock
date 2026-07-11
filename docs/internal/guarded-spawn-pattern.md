# Guarded Process-Spawn Pattern (Mandatory for TabDock)

## Background

During the `SurvivalSpike` work a runaway spawn incident occurred because the spike launched child copies of itself without an upper bound, and a command-line argument ambiguity caused child processes to re-enter the orchestrator path. The root cause was **self-recursion with no spawn cap** — not a `SetWinEventHook` fan-out, not an unbounded retry, and not a polling loop.

This document records the guardrails that must be applied to **any** code in this project that calls `Process.Start`, including:

- The capture picker's "Group these" action.
- Any future template/wizard launch logic.
- Any watchdog/recovery helper that spawns child processes.
- Test harnesses and spikes.

## Required guardrails

### 1. Hard spawn cap enforced by a counter

```csharp
private const int MaxTotalSpawns = 3; // set per scenario
private static readonly object SpawnLock = new object();
private static int _spawnCount = 0;

private static Process SpawnGuarded(Func<Process> spawn)
{
    lock (SpawnLock)
    {
        if (_spawnCount >= MaxTotalSpawns)
            throw new InvalidOperationException($"Spawn cap of {MaxTotalSpawns} exceeded — aborting.");

        Process p = spawn();
        if (p == null)
            throw new InvalidOperationException("SpawnGuarded received a null Process.");

        _spawnCount++;
        return p;
    }
}
```

No code path may call `Process.Start` directly; everything routes through `SpawnGuarded` (or an equivalent named wrapper).

### 2. No bare retry loops

Any retry logic must have:
- An explicit `maxRetries` constant.
- A counter that is incremented each attempt.
- A loud throw/abort the moment the cap is hit.

```csharp
const int maxRetries = 10;
for (int i = 0; i < maxRetries; i++)
{
    if (TryOperation())
        break;
    if (i == maxRetries - 1)
        throw new InvalidOperationException("Operation failed after maximum retries.");
}
```

### 3. Single-instance guard for standalone tools

Any spike, watchdog, or standalone helper that could be double-launched must use a named mutex:

```csharp
using var mutex = new Mutex(true, "Global\\TabDockUniqueName", out bool isNew);
if (!isNew)
{
    Console.WriteLine("Already running elsewhere — aborting.");
    return 2;
}
```

### 4. Track spawned processes and kill them on exit

```csharp
private static readonly List<Process> SpawnedProcesses = new List<Process>();

try
{
    Process p = SpawnGuarded(() => Process.Start(...));
    SpawnedProcesses.Add(p);
    // ... work ...
}
finally
{
    foreach (var p in SpawnedProcesses)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* log, don't throw */ }
    }
}
```

### 5. Hard overall timeout

Use a `CancellationTokenSource` with a fixed budget and cancel/kill on timeout:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
try
{
    await WorkAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // kill tracked, then report
}
```

### 6. Ctrl+C handling

Register `Console.CancelKeyPress` to cancel and clean up before exiting.

### 7. Visible logging

Every spawn, check, and kill must print immediately (flushed). No silent or batched logging for process-lifetime operations.

### 8. Manual confirmation for one-off tests

Spikes and destructive tests must print exactly what they are about to do and require the user to type `y` before proceeding.

## Survival spike outcome

The rebuilt spike was run once with all guardrails in place. Result:

```
SURVIVAL SPIKE OUTCOME: Command Prompt HWND died with the host.
WaitForSingleObject=0, IsWindow=False, IsWindowVisible=False, ChildPid=0
```

A `WS_CHILD` window reparented into a host window does **not** survive an abrupt `taskkill /F` of the host process. Windows destroys the child HWND as part of cleaning up the dead parent's window tree. Because there is no surviving HWND for an external watchdog to re-parent, a watchdog cannot rescue captured windows from a Task Manager kill. This is a genuine OS-level limitation, not a recoverable race condition.

Therefore `WatchdogService` is **not being built**. Cleanup is handled only in-process for normal exit, unhandled exceptions, and session-ending events.
