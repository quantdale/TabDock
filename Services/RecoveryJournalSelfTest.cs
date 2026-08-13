using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Deterministic rescue tests for identity gating and partial-restore retry.
/// Native desktop mutation stays behind IRecoveryNativeApi, so this fixture
/// never touches a real HWND while proving the journal's entry-by-entry policy.
/// </summary>
internal static class RecoveryJournalSelfTest
{
    public static (int Checks, int Failures) Run()
    {
        int checks = 0;
        int failures = 0;
        void Check(bool condition)
        {
            checks++;
            if (!condition)
                failures++;
        }

        string root = Path.Combine(Path.GetTempPath(), "TabDock-journal-selftest-" + Guid.NewGuid().ToString("N"));
        string journalPath = Path.Combine(root, "hidden-windows.json");
        Directory.CreateDirectory(root);
        try
        {
            var file = new HiddenWindowJournalFile
            {
                Entries = new List<HiddenWindowEntry>
                {
                    Entry(1, 11, 101),
                    Entry(2, 22, 202),
                    Entry(3, 33, 303),
                },
            };
            using var log = new LoggingService(Path.Combine(root, "logs"));
            File.WriteAllText(journalPath, JsonSerializer.Serialize(file, TabDockJsonContext.Default.HiddenWindowJournalFile));
            var api = new FakeRecoveryApi
            {
                FailingPlacement = new IntPtr(2),
            };
            api.Identity[new IntPtr(3)] = (999, "wrong.exe", "Pig", 303);

            WindowShepherdService.RescueOrphanedWindows(log, journalPath, api);
            Check(api.Shown.Contains(new IntPtr(1)));
            Check(!api.Shown.Contains(new IntPtr(3))); // recycled PID gate refused native mutation
            Check(File.Exists(journalPath));

            HiddenWindowJournalFile retry = JsonSerializer.Deserialize(
                File.ReadAllText(journalPath),
                TabDockJsonContext.Default.HiddenWindowJournalFile)!;
            Check(retry.Entries.Count == 1 && retry.Entries[0].Hwnd == 2);
            Check(api.PlacementCalls.Contains(new IntPtr(1)) && api.PlacementCalls.Contains(new IntPtr(2)));
        }
        catch
        {
            checks++;
            failures++;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }

        return (checks, failures);
    }

    private static HiddenWindowEntry Entry(long hwnd, uint pid, long startTicks)
        => new()
        {
            Hwnd = hwnd,
            Pid = pid,
            ExePath = $"guest-{pid}.exe",
            ClassName = "Pig",
            ProcessStartTimeUtcTicks = startTicks,
            OriginallyVisible = true,
            HasOriginalPlacement = true,
            OriginalShowCommand = NativeMethods.SW_SHOW,
            OriginalNormalRight = 400,
            OriginalNormalBottom = 300,
        };

    private sealed class FakeRecoveryApi : IRecoveryNativeApi
    {
        public IntPtr FailingPlacement { get; init; }
        public Dictionary<IntPtr, (uint Pid, string Exe, string ClassName, long StartTicks)> Identity { get; } = new()
        {
            [new IntPtr(1)] = (11, "guest-11.exe", "Pig", 101),
            [new IntPtr(2)] = (22, "guest-22.exe", "Pig", 202),
        };
        public HashSet<IntPtr> Shown { get; } = new();
        public HashSet<IntPtr> PlacementCalls { get; } = new();

        public bool IsWindow(IntPtr hwnd) => Identity.ContainsKey(hwnd);
        public uint GetProcessId(IntPtr hwnd) => Identity[hwnd].Pid;
        public string? GetProcessImagePath(uint pid) => Identity.Values.FirstOrDefault(x => x.Pid == pid).Exe;
        public string? GetClassName(IntPtr hwnd) => Identity[hwnd].ClassName;
        public long GetProcessStartTimeUtcTicks(uint pid) => Identity.Values.FirstOrDefault(x => x.Pid == pid).StartTicks;
        public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement)
        {
            PlacementCalls.Add(hwnd);
            return hwnd != FailingPlacement;
        }
        public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
        {
            PlacementCalls.Add(hwnd);
            return hwnd != FailingPlacement;
        }
        public void ShowWindow(IntPtr hwnd, int command) => Shown.Add(hwnd);
        public bool IsWindowVisible(IntPtr hwnd) => Shown.Contains(hwnd);
        public int SetTransitionsDisabled(IntPtr hwnd, int value) => 0;
    }
}
