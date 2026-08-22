using System;
using System.IO;
using TabDock.Models;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former CaptureBoundarySelfTest (Wave 4): deterministic
/// coverage for the JournalCapture → SetProp → DWM boundary. The injected APIs
/// model HWND reuse without touching a real external window; every failure case
/// must stop before the next mutation stage.
/// </summary>
public class CaptureBoundaryTests
{
    [Fact]
    public void CompleteCapture_ValidTarget_TagsTokenAndMutatesDwm()
    {
        CaptureCaseResult result = RunCase(null);
        Assert.True(result.Completed);
        Assert.Equal(1, result.SetPropertyCount);
        Assert.Equal(1, result.DwmMutationCount);
        Assert.Equal(new IntPtr(1001), result.CaptureToken);
        Assert.NotEqual(IntPtr.Zero, result.ReleasedCloseNonce);
    }

    [Fact]
    public void CompleteCapture_TargetExitAfterJournal_NeverInstallsTokenOrDwm()
    {
        CaptureCaseResult result = RunCase(api => api.IsWindowAlive = false);
        Assert.False(result.Completed);
        Assert.Equal(0, result.SetPropertyCount);
        Assert.Equal(0, result.DwmMutationCount);
    }

    [Fact]
    public void CompleteCapture_PidChangeAfterJournal_NeverInstallsTokenOrDwm()
    {
        CaptureCaseResult result = RunCase(api => api.Identity = new WindowProcessIdentity(99, 20));
        Assert.False(result.Completed);
        Assert.Equal(0, result.SetPropertyCount);
        Assert.Equal(0, result.DwmMutationCount);
    }

    [Fact]
    public void CompleteCapture_ThreadChangeAfterJournal_NeverInstallsTokenOrDwm()
    {
        CaptureCaseResult result = RunCase(api => api.Identity = new WindowProcessIdentity(10, 21));
        Assert.False(result.Completed);
        Assert.Equal(0, result.SetPropertyCount);
        Assert.Equal(0, result.DwmMutationCount);
    }

    [Fact]
    public void CompleteCapture_ProcessStartChangeAfterJournal_NeverInstallsTokenOrDwm()
    {
        CaptureCaseResult result = RunCase(api => api.ProcessStartTicks = 202);
        Assert.False(result.Completed);
        Assert.Equal(0, result.SetPropertyCount);
        Assert.Equal(0, result.DwmMutationCount);
    }

    [Fact]
    public void CompleteCapture_TokenChangeBeforeDwm_StopsDwmMutationAndCleansNonce()
    {
        CaptureCaseResult result = RunCase(null, mutateBeforeDwm: api => api.CaptureToken = new IntPtr(2002));
        Assert.False(result.Completed);
        Assert.Equal(1, result.SetPropertyCount);
        Assert.Equal(0, result.DwmMutationCount);
        Assert.Equal(new IntPtr(2002), result.CaptureToken);
        // The installed released-close nonce is cleaned up when the capture
        // boundary rejects after installation.
        Assert.Equal(IntPtr.Zero, result.ReleasedCloseNonce);
    }

    [Fact]
    public void CompleteCapture_PendingRecoveryToken_BlocksCapture()
    {
        CaptureCaseResult result = RunCase(api => api.PendingRecoveryToken = new IntPtr(3003));
        Assert.False(result.Completed);
        Assert.Equal(0, result.SetPropertyCount);
        Assert.Equal(0, result.DwmMutationCount);
    }

    private static CaptureCaseResult RunCase(
        Action<FakeCaptureApi>? mutateAfterJournal,
        Action<FakeCaptureApi>? mutateBeforeDwm = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-capture-boundary-test-" + Guid.NewGuid().ToString("N"));
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
            new FakeReleaseOnlyApi(),
            api,
            hook);
        service.BindCapturedWindowForTesting(captured);
        bool completed = service.CompleteCaptureAfterJournalForTesting(captured, out _, out _);
        var result = new CaptureCaseResult(completed, api.SetPropertyCount, api.DwmMutationCount, api.CaptureToken, api.ReleasedCloseNonce);
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
        IntPtr CaptureToken,
        IntPtr ReleasedCloseNonce);

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

        public IntPtr ReleasedCloseNonce { get; private set; }

        public IntPtr GetReleasedCloseNonce(IntPtr hwnd) => ReleasedCloseNonce;

        public bool InstallReleasedCloseNonce(IntPtr hwnd, IntPtr nonce)
        {
            if (nonce == IntPtr.Zero)
                return false;
            ReleasedCloseNonce = nonce;
            return true;
        }

        public bool ConsumeReleasedCloseNonce(IntPtr hwnd, IntPtr expectedNonce)
        {
            if (expectedNonce == IntPtr.Zero || ReleasedCloseNonce != expectedNonce)
                return false;
            ReleasedCloseNonce = IntPtr.Zero;
            return true;
        }

        public int SetTransitionsDisabled(IntPtr hwnd, int value)
        {
            DwmMutationCount++;
            return 0;
        }
    }

    private sealed class FakeReleaseOnlyApi : IWindowReleaseNativeApi
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
}
