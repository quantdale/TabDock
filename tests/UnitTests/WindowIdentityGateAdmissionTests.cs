using System;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Coverage migrated from the former WindowIdentitySelfTest (Wave 4) for the
/// contracts its replacement did not already own: capture-token admission
/// refusal, probe-exception fail-closed behavior, and the zero-allocation
/// HWND→object binding used for delayed-callback identity. The full
/// Evaluate/EvaluateBeforeCaptureToken matrix lives in WindowIdentityGateTests.
/// </summary>
public class WindowIdentityGateAdmissionTests
{
    private static CapturedWindow Captured() => new()
    {
        Hwnd = new IntPtr(0x101),
        ProcessId = 10,
        WindowThreadId = 20,
        WindowIdentityToken = 1001,
        ProcessStartTimeUtcTicks = 100,
        ExePath = "guest.exe",
        OriginalClassName = "GuestWindow",
    };

    private sealed class FakeIdentityApi : IWindowIdentityNativeApi
    {
        public bool Alive { get; set; } = true;
        public IntPtr CaptureToken { get; set; }
        public WindowProcessIdentity Identity { get; set; } = new(10, 20);
        public string? ExePath { get; set; } = "guest.exe";
        public string? ClassName { get; set; } = "GuestWindow";
        public long ProcessStartTicks { get; set; } = 100;
        public bool ThrowOnExecutableProbe { get; set; }
        public bool ThrowOnStartProbe { get; set; }

        public IntPtr GetCaptureIdentityToken(IntPtr hwnd) => CaptureToken;
        public bool IsWindow(IntPtr hwnd) => Alive && hwnd != IntPtr.Zero;
        public WindowProcessIdentity GetProcessIdentity(IntPtr hwnd) => Identity;
        public string? GetProcessImagePath(uint pid)
        {
            if (ThrowOnExecutableProbe)
                throw new InvalidOperationException("synthetic executable probe failure");
            return ExePath;
        }
        public string? GetClassName(IntPtr hwnd) => ClassName;
        public long GetProcessStartTimeUtcTicks(uint pid)
        {
            if (ThrowOnStartProbe)
                throw new InvalidOperationException("synthetic start probe failure");
            return ProcessStartTicks;
        }
        public bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken) => false;
        public IntPtr GetReleasedCloseNonce(IntPtr hwnd) => IntPtr.Zero;
        public bool InstallReleasedCloseNonce(IntPtr hwnd, IntPtr nonce) => false;
        public bool ConsumeReleasedCloseNonce(IntPtr hwnd, IntPtr expectedNonce) => false;
    }

    [Fact]
    public void IsCaptureTokenAvailable_ExistingToken_RefusesAdmission()
    {
        var api = new FakeIdentityApi { CaptureToken = new IntPtr(1001) };
        Assert.False(WindowIdentityGate.IsCaptureTokenAvailable(Captured().Hwnd, api));
    }

    [Fact]
    public void IsCaptureTokenAvailable_NoInstalledToken_AllowsAdmission()
    {
        var api = new FakeIdentityApi { CaptureToken = IntPtr.Zero };
        Assert.True(WindowIdentityGate.IsCaptureTokenAvailable(Captured().Hwnd, api));
    }

    [Fact]
    public void Evaluate_ExecutableProbeException_IsUnverifiable()
    {
        var api = new FakeIdentityApi { CaptureToken = new IntPtr(1001), ThrowOnExecutableProbe = true };
        WindowIdentityResult result = WindowIdentityGate.Evaluate(
            Captured(), api, verifyExecutable: true, verifyProcessInstance: true, out _);
        Assert.Equal(WindowIdentityResult.Unverifiable, result);
    }

    [Fact]
    public void Evaluate_ProcessStartProbeException_IsUnverifiable()
    {
        var api = new FakeIdentityApi { CaptureToken = new IntPtr(1001), ThrowOnStartProbe = true };
        WindowIdentityResult result = WindowIdentityGate.Evaluate(
            Captured(), api, verifyExecutable: true, verifyProcessInstance: true, out _);
        Assert.Equal(WindowIdentityResult.Unverifiable, result);
    }

    [Fact]
    public void Binding_ReplacementBindsAndOldObjectCannotAct()
    {
        var captured = Captured();
        var binding = new WindowIdentityBinding();
        binding.Bind(captured);

        var replacement = new CapturedWindow
        {
            Hwnd = captured.Hwnd,
            ProcessId = captured.ProcessId,
            WindowThreadId = captured.WindowThreadId,
            WindowIdentityToken = captured.WindowIdentityToken + 1,
            ProcessStartTimeUtcTicks = captured.ProcessStartTimeUtcTicks + 1,
            ExePath = captured.ExePath,
            OriginalClassName = captured.OriginalClassName,
        };
        binding.Bind(replacement);
        binding.Unbind(captured);

        // A delayed callback holding the pre-recycle object must be rejected;
        // only the current object for that HWND remains authoritative.
        Assert.False(binding.IsCurrent(captured));
        Assert.True(binding.IsCurrent(replacement));
        Assert.True(binding.ContainsHwnd(replacement.Hwnd));
    }
}
