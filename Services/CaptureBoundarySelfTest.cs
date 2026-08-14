using System;
using System.IO;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Deterministic coverage for the JournalCapture -> SetProp -> DWM boundary.
/// The injected APIs model HWND reuse without touching a real external window.
/// </summary>
internal static class CaptureBoundarySelfTest
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

        Check(ValidTargetIsTaggedAndDwmMutated());
        Check(TargetExitAfterJournalNeverGetsSetProp());
        Check(PidChangeAfterJournalNeverGetsSetProp());
        Check(ThreadChangeAfterJournalNeverGetsSetProp());
        Check(ProcessStartChangeAfterJournalNeverGetsSetProp());
        Check(TokenChangeBeforeDwmStopsDwmMutation());
        Check(PendingRecoveryTokenBlocksCapture());
        return (checks, failures);
    }

    private static bool ValidTargetIsTaggedAndDwmMutated()
    {
        CaptureCaseResult result = RunCase(null);
        return result.Completed
            && result.SetPropertyCount == 1
            && result.DwmMutationCount == 1
            && result.CaptureToken == new IntPtr(1001);
    }

    private static bool TargetExitAfterJournalNeverGetsSetProp()
    {
        CaptureCaseResult result = RunCase(api => api.IsWindowAlive = false);
        return !result.Completed && result.SetPropertyCount == 0 && result.DwmMutationCount == 0;
    }

    private static bool PidChangeAfterJournalNeverGetsSetProp()
    {
        CaptureCaseResult result = RunCase(api => api.Identity = new WindowProcessIdentity(99, 20));
        return !result.Completed && result.SetPropertyCount == 0 && result.DwmMutationCount == 0;
    }

    private static bool ThreadChangeAfterJournalNeverGetsSetProp()
    {
        CaptureCaseResult result = RunCase(api => api.Identity = new WindowProcessIdentity(10, 21));
        return !result.Completed && result.SetPropertyCount == 0 && result.DwmMutationCount == 0;
    }

    private static bool ProcessStartChangeAfterJournalNeverGetsSetProp()
    {
        CaptureCaseResult result = RunCase(api => api.ProcessStartTicks = 202);
        return !result.Completed && result.SetPropertyCount == 0 && result.DwmMutationCount == 0;
    }

    private static bool TokenChangeBeforeDwmStopsDwmMutation()
    {
        CaptureCaseResult result = RunCase(null, mutateBeforeDwm: api => api.CaptureToken = new IntPtr(2002));
        return !result.Completed
            && result.SetPropertyCount == 1
            && result.DwmMutationCount == 0
            && result.CaptureToken == new IntPtr(2002);
    }

    private static bool PendingRecoveryTokenBlocksCapture()
    {
        CaptureCaseResult result = RunCase(api => api.PendingRecoveryToken = new IntPtr(3003));
        return !result.Completed
            && result.SetPropertyCount == 0
            && result.DwmMutationCount == 0;
    }

    private static CaptureCaseResult RunCase(
        Action<FakeCaptureApi>? mutateAfterJournal,
        Action<FakeCaptureApi>? mutateBeforeDwm = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-capture-boundary-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string journalPath = Path.Combine(root, "hidden-windows.json");
        using var log = new LoggingService(Path.Combine(root, "logs"));
        var api = new FakeCaptureApi();
        var captured = new CapturedWindow
        {
            Hwnd = new IntPtr(1),
            ProcessId = 10,
            WindowThreadId = 20,
            WindowIdentityToken = 1001,
            ProcessStartTimeUtcTicks = 101,
            ExePath = "guest.exe",
            OriginalClassName = "GuestWindow",
            OriginalTitle = "Guest",
            OriginallyVisible = true,
        };
        Action<string> hook = stage =>
        {
            if (stage == "JournalCapture.committed")
                mutateAfterJournal?.Invoke(api);
            else if (stage == "capture-before-dwm")
                mutateBeforeDwm?.Invoke(api);
        };
        var service = new WindowShepherdService(
            log,
            journalPath,
            api,
            new FakeMonitorDpiProbe(),
            new FakeReleaseApi(),
            api,
            hook);
        service.BindCapturedWindowForTesting(captured);
        bool completed = service.CompleteCaptureAfterJournalForTesting(captured, out _, out _);
        CaptureCaseResult result = new(completed, api.SetPropertyCount, api.DwmMutationCount, api.CaptureToken);
        log.Dispose();
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch { }
        return result;
    }

    private readonly record struct CaptureCaseResult(
        bool Completed,
        int SetPropertyCount,
        int DwmMutationCount,
        IntPtr CaptureToken);

    private sealed class FakeCaptureApi : IWindowIdentityNativeApi, IWindowCaptureNativeApi
    {
        public WindowProcessIdentity Identity { get; set; } = new(10, 20);
        public IntPtr CaptureToken { get; set; }
        public IntPtr PendingRecoveryToken { get; set; }
        public string ExePath { get; set; } = "guest.exe";
        public string ClassName { get; set; } = "GuestWindow";
        public long ProcessStartTicks { get; set; } = 101;
        public bool IsWindowAlive { get; set; } = true;
        public int SetPropertyCount { get; private set; }
        public int DwmMutationCount { get; private set; }

        public IntPtr GetCaptureIdentityToken(IntPtr hwnd) => CaptureToken;
        public IntPtr GetPendingRecoveryToken(IntPtr hwnd) => PendingRecoveryToken;
        public bool IsWindow(IntPtr hwnd) => IsWindowAlive;
        public WindowProcessIdentity GetProcessIdentity(IntPtr hwnd) => Identity;
        public string? GetProcessImagePath(uint pid) => ExePath;
        public string? GetClassName(IntPtr hwnd) => ClassName;
        public long GetProcessStartTimeUtcTicks(uint pid) => ProcessStartTicks;
        public bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken)
        {
            if (CaptureToken != expectedToken)
                return false;
            CaptureToken = IntPtr.Zero;
            return true;
        }

        public bool SetCaptureIdentityToken(IntPtr hwnd, IntPtr token)
        {
            SetPropertyCount++;
            if (CaptureToken != IntPtr.Zero)
                return false;
            CaptureToken = token;
            return true;
        }

        public int SetTransitionsDisabled(IntPtr hwnd, int value)
        {
            DwmMutationCount++;
            return 0;
        }
    }

    private sealed class FakeReleaseApi : IWindowReleaseNativeApi
    {
        public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement) => true;
        public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags) => true;
        public bool ShowWindow(IntPtr hwnd, int command) => false;
        public bool IsWindowVisible(IntPtr hwnd) => true;
        public bool SetForegroundWindow(IntPtr hwnd) => true;
        public IntPtr GetForegroundWindow() => IntPtr.Zero;
        public int SetTransitionsDisabled(IntPtr hwnd, int value) => 0;
        public string DescribeWindow(IntPtr hwnd) => "fake";
    }

    private sealed class FakeMonitorDpiProbe : IMonitorDpiProbe
    {
        public uint GetEffectiveDpi(IntPtr monitor) => 96;
    }
}
