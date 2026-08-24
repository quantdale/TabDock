using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TabDock.ValidationDriver;

/// <summary>
/// Reads stable Windows process/UI resource signals for a run-owned process.
/// A failed field remains null and carries a privacy-safe reason; callers must
/// feed the observation to ResourceSeriesAnalyzer, which blocks missing data.
/// </summary>
internal static class ResourceSnapshotProbe
{
    public static bool TryCapture(
        uint processId,
        int sequence,
        string phase,
        out ResourceSnapshot snapshot,
        out string reason)
    {
        var failures = new List<string>();
        long startTicks = NativeMethods.GetProcessStartTimeUtcTicks(processId);
        var identity = new ResourceProcessIdentity(processId, startTicks);
        if (!identity.IsValid)
            failures.Add("process-identity-unavailable");

        long? handles = null;
        long? userObjects = null;
        long? gdiObjects = null;
        long? privateBytes = null;
        long? workingSet = null;
        IntPtr process = IntPtr.Zero;
        try
        {
            process = NativeMethods.OpenProcess(
                ResourceNativeMethods.ProcessQueryInformation | ResourceNativeMethods.ProcessVmRead,
                bInheritHandle: false,
                processId);
            if (process == IntPtr.Zero)
            {
                failures.Add("process-open-failed");
            }
            else
            {
                if (ResourceNativeMethods.GetProcessHandleCount(process, out uint handleCount))
                    handles = handleCount;
                else
                    failures.Add("handle-count-unavailable");

                // GetGuiResources returns zero for a valid process with no
                // objects, so zero is a real observation and not a failure.
                userObjects = ResourceNativeMethods.GetGuiResources(
                    process,
                    ResourceNativeMethods.GuiUserObjects);
                gdiObjects = ResourceNativeMethods.GetGuiResources(
                    process,
                    ResourceNativeMethods.GuiGdiObjects);

                var counters = new ResourceNativeMethods.ProcessMemoryCounters
                {
                    Cb = (uint)Marshal.SizeOf<ResourceNativeMethods.ProcessMemoryCounters>(),
                };
                if (ResourceNativeMethods.GetProcessMemoryInfo(
                    process,
                    out counters,
                    counters.Cb))
                {
                    privateBytes = ToInt64(counters.PagefileUsage);
                    workingSet = ToInt64(counters.WorkingSetSize);
                }
                else
                {
                    failures.Add("memory-counters-unavailable");
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add($"resource-probe-{ex.GetType().Name}");
        }
        finally
        {
            if (process != IntPtr.Zero)
                NativeMethods.CloseHandle(process);
        }

        long? threadCount = TryGetThreadCount(processId, failures);
        long? topLevelWindows = TryGetTopLevelWindowCount(processId, failures);
        reason = string.Join(",", failures);
        snapshot = new ResourceSnapshot(
            sequence,
            string.IsNullOrWhiteSpace(phase) ? "unspecified" : phase,
            DateTimeOffset.UtcNow,
            identity,
            handles,
            userObjects,
            gdiObjects,
            privateBytes,
            workingSet,
            threadCount,
            topLevelWindows,
            string.IsNullOrWhiteSpace(reason) ? null : reason);
        return failures.Count == 0;
    }

    private static long? TryGetThreadCount(uint processId, List<string> failures)
    {
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(
            NativeMethods.TH32CS_SNAPPROCESS,
            0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            failures.Add("thread-count-snapshot-unavailable");
            return null;
        }

        try
        {
            var entry = new NativeMethods.PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>(),
            };
            if (!NativeMethods.Process32First(snapshot, ref entry))
            {
                failures.Add("thread-count-enumeration-unavailable");
                return null;
            }

            do
            {
                if (entry.th32ProcessID == processId)
                    return entry.cntThreads;
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));

            failures.Add("process-not-present-in-thread-snapshot");
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }
    }

    private static long? TryGetTopLevelWindowCount(uint processId, List<string> failures)
    {
        try
        {
            long count = 0;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint ownerPid);
                if (ownerPid == processId && NativeMethods.IsWindow(hwnd))
                    count++;
                return true;
            }, IntPtr.Zero);
            return count;
        }
        catch (Exception ex)
        {
            failures.Add($"top-level-window-count-{ex.GetType().Name}");
            return null;
        }
    }

    private static long ToInt64(UIntPtr value)
    {
        ulong raw = value.ToUInt64();
        return raw > long.MaxValue ? long.MaxValue : (long)raw;
    }
}
