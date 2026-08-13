using System;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Deterministic coverage for the two-tier mutation identity gate. It models
/// the native answers instead of creating or targeting a real external HWND.
/// </summary>
internal static class WindowIdentitySelfTest
{
    public static bool CoversIdentityTiers()
    {
        var captured = new CapturedWindow
        {
            Hwnd = new IntPtr(0x101),
            ProcessId = 10,
            WindowThreadId = 20,
            WindowIdentityToken = 1001,
            ProcessStartTimeUtcTicks = 100,
            ExePath = "guest.exe",
            OriginalClassName = "GuestWindow",
        };
        var api = new FakeNativeApi
        {
            Identity = new WindowProcessIdentity(10, 20),
            CaptureToken = new IntPtr(1001),
            ExePath = "guest.exe",
            ClassName = "GuestWindow",
            ProcessStartTicks = 100,
        };

        bool validHot = WindowIdentityGate.Matches(captured, api, verifyExecutable: false, verifyProcessInstance: false);
        bool validSlow = WindowIdentityGate.Matches(captured, api, verifyExecutable: true, verifyProcessInstance: true);
        bool capturedTokenIsNonzero = captured.WindowIdentityToken != 0;
        bool existingTokenRefusesAdmission = !WindowIdentityGate.IsCaptureTokenAvailable(captured.Hwnd, api);
        bool validOutcome = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: true, verifyProcessInstance: true, out _) == WindowIdentityResult.Match;

        api.Identity = new WindowProcessIdentity(11, 20);
        bool differentPidBlocked = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: false, verifyProcessInstance: false, out _) == WindowIdentityResult.Mismatch;

        api.Identity = new WindowProcessIdentity(10, 20);
        api.CaptureToken = new IntPtr(1002);
        bool recycledSameProcessWindowBlocked = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: false, verifyProcessInstance: false, out _) == WindowIdentityResult.Mismatch;

        api.CaptureToken = new IntPtr(1001);
        api.ProcessStartTicks = 101;
        bool recycledProcessBlocked = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: true, verifyProcessInstance: true, out _) == WindowIdentityResult.Mismatch;

        api.ProcessStartTicks = 100;
        api.ExePath = "other.exe";
        bool differentExecutableBlocked = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: true, verifyProcessInstance: true, out _) == WindowIdentityResult.Mismatch;

        api.ExePath = "guest.exe";
        api.ProcessStartTicks = 0;
        bool unavailableProcessStart = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: true, verifyProcessInstance: true, out _) == WindowIdentityResult.Unverifiable;
        captured.ProcessStartTimeUtcTicks = 0;
        bool missingCapturedStartBlocked = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: true, verifyProcessInstance: true, out _) == WindowIdentityResult.Unverifiable;
        captured.ProcessStartTimeUtcTicks = 100;
        api.ProcessStartTicks = 100;
        api.ExePath = string.Empty;
        bool unavailableExecutable = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: true, verifyProcessInstance: true, out _) == WindowIdentityResult.Unverifiable;
        api.ExePath = "guest.exe";
        api.ThrowOnExecutableProbe = true;
        bool executableProbeException = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: true, verifyProcessInstance: true, out _) == WindowIdentityResult.Unverifiable;
        api.ThrowOnExecutableProbe = false;
        api.ThrowOnStartProbe = true;
        bool startProbeException = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: true, verifyProcessInstance: true, out _) == WindowIdentityResult.Unverifiable;
        api.ThrowOnStartProbe = false;
        api.ClassName = "OtherWindow";
        bool differentClassBlocked = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: false, verifyProcessInstance: false, out _) == WindowIdentityResult.Mismatch;
        api.ClassName = string.Empty;
        bool unavailableClass = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: false, verifyProcessInstance: false, out _) == WindowIdentityResult.Unverifiable;
        api.ClassName = "GuestWindow";
        api.Identity = new WindowProcessIdentity(10, 21);
        bool differentThreadBlocked = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: false, verifyProcessInstance: false, out _) == WindowIdentityResult.Mismatch;

        api.Identity = new WindowProcessIdentity(10, 20);
        api.IsWindowAlive = false;
        bool destroyedWindowIsMismatch = WindowIdentityGate.Evaluate(
            captured, api, verifyExecutable: false, verifyProcessInstance: false, out _) == WindowIdentityResult.Mismatch;
        api.IsWindowAlive = true;
        api.StartProbeCount = 0;
        for (int i = 0; i < 512; i++)
        {
            if (!WindowIdentityGate.Matches(captured, api, verifyExecutable: false, verifyProcessInstance: false))
                return false;
        }
        bool hotTierDoesNotProbeProcessStart = api.StartProbeCount == 0;

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
        bool delayedOldObjectBlocked = !binding.IsCurrent(captured)
            && binding.IsCurrent(replacement)
            && binding.ContainsHwnd(replacement.Hwnd);

        return validHot
            && validSlow
            && capturedTokenIsNonzero
            && existingTokenRefusesAdmission
            && validOutcome
            && differentPidBlocked
            && recycledSameProcessWindowBlocked
            && recycledProcessBlocked
            && differentExecutableBlocked
            && unavailableProcessStart
            && missingCapturedStartBlocked
            && unavailableExecutable
            && executableProbeException
            && startProbeException
            && differentClassBlocked
            && unavailableClass
            && differentThreadBlocked
            && destroyedWindowIsMismatch
            && hotTierDoesNotProbeProcessStart
            && delayedOldObjectBlocked;
    }

    private sealed class FakeNativeApi : IWindowIdentityNativeApi
    {
        public WindowProcessIdentity Identity { get; set; }
        public IntPtr CaptureToken { get; set; }
        public string ExePath { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public long ProcessStartTicks { get; set; }
        public int StartProbeCount { get; set; }
        public bool ThrowOnExecutableProbe { get; set; }
        public bool ThrowOnStartProbe { get; set; }
        public bool ThrowOnClassProbe { get; set; }
        public bool IsWindowAlive { get; set; } = true;

        public IntPtr GetCaptureIdentityToken(IntPtr hwnd) => CaptureToken;

        public bool IsWindow(IntPtr hwnd) => IsWindowAlive && hwnd != IntPtr.Zero;

        public WindowProcessIdentity GetProcessIdentity(IntPtr hwnd) => Identity;

        public string? GetProcessImagePath(uint pid)
        {
            if (ThrowOnExecutableProbe)
                throw new InvalidOperationException("synthetic executable probe failure");
            return ExePath;
        }

        public string? GetClassName(IntPtr hwnd)
        {
            if (ThrowOnClassProbe)
                throw new InvalidOperationException("synthetic class probe failure");
            return ClassName;
        }

        public long GetProcessStartTimeUtcTicks(uint pid)
        {
            if (ThrowOnStartProbe)
                throw new InvalidOperationException("synthetic start probe failure");
            StartProbeCount++;
            return ProcessStartTicks;
        }

        public bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken)
        {
            if (CaptureToken != expectedToken)
                return false;
            CaptureToken = IntPtr.Zero;
            return true;
        }
    }
}
