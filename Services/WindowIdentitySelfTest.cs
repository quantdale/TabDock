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

        api.Identity = new WindowProcessIdentity(11, 20);
        bool differentPidBlocked = !WindowIdentityGate.Matches(captured, api, verifyExecutable: false, verifyProcessInstance: false);

        api.Identity = new WindowProcessIdentity(10, 20);
        api.CaptureToken = new IntPtr(1002);
        bool recycledSameProcessWindowBlocked = !WindowIdentityGate.Matches(captured, api, verifyExecutable: false, verifyProcessInstance: false);

        api.CaptureToken = new IntPtr(1001);
        api.ProcessStartTicks = 101;
        bool recycledProcessBlocked = !WindowIdentityGate.Matches(captured, api, verifyExecutable: true, verifyProcessInstance: true);

        api.ProcessStartTicks = 100;
        api.ExePath = "other.exe";
        bool differentExecutableBlocked = !WindowIdentityGate.Matches(captured, api, verifyExecutable: true, verifyProcessInstance: true);

        api.ExePath = "guest.exe";
        captured.ProcessStartTimeUtcTicks = 0;
        bool missingCapturedStartBlocked = !WindowIdentityGate.Matches(captured, api, verifyExecutable: true, verifyProcessInstance: true);
        captured.ProcessStartTimeUtcTicks = 100;
        api.ClassName = "OtherWindow";
        bool differentClassBlocked = !WindowIdentityGate.Matches(captured, api, verifyExecutable: false, verifyProcessInstance: false);

        api.ClassName = "GuestWindow";
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
            && differentPidBlocked
            && recycledSameProcessWindowBlocked
            && recycledProcessBlocked
            && differentExecutableBlocked
            && missingCapturedStartBlocked
            && differentClassBlocked
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

        public IntPtr GetCaptureIdentityToken(IntPtr hwnd) => CaptureToken;

        public bool IsWindow(IntPtr hwnd) => hwnd != IntPtr.Zero;

        public WindowProcessIdentity GetProcessIdentity(IntPtr hwnd) => Identity;

        public string? GetProcessImagePath(uint pid) => ExePath;

        public string? GetClassName(IntPtr hwnd) => ClassName;

        public long GetProcessStartTimeUtcTicks(uint pid)
        {
            StartProbeCount++;
            return ProcessStartTicks;
        }
    }
}
