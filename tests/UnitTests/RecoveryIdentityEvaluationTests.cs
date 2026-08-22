using System;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Deterministic coverage for the consolidated recovery identity evaluation
/// (Wave 2A). Both tiers — the strong transaction-entry identity and the cheap
/// mutation-boundary generation gate — now share one core evaluator. These
/// tests pin the tier contract with a RECORDING fake so a future edit cannot:
///
///  - make the cheap boundary start performing expensive executable /
///    process-start probes on every native-write boundary;
///  - make the strong path lose evidence while sharing its implementation;
///  - weaken Mismatch vs Unverifiable semantics (positive staleness evidence
///    vs "required evidence unobtainable", which must fail closed);
///  - let a probe exception be treated as anything but Unverifiable.
/// </summary>
public sealed class RecoveryIdentityEvaluationTests
{
    private static HiddenWindowEntry Entry() => new()
    {
        Hwnd = 0x5150,
        Pid = 10,
        WindowThreadId = 20,
        WindowIdentityToken = 1001,
        ExePath = "guest.exe",
        ClassName = "GuestWindow",
        ProcessStartTimeUtcTicks = 100,
    };

    private static IntPtr Hwnd(HiddenWindowEntry entry) => new(entry.Hwnd);

    /// <summary>Recording IRecoveryNativeApi that counts every probe.</summary>
    private sealed class RecordingRecoveryApi : IRecoveryNativeApi
    {
        public bool Alive { get; set; } = true;
        public uint Pid { get; set; } = 10;
        public uint ThreadId { get; set; } = 20;
        public string? ExePath { get; set; } = "guest.exe";
        public string? ClassName { get; set; } = "GuestWindow";
        public long ProcessStartTicks { get; set; } = 100;
        public IntPtr CaptureToken { get; set; } = new(1001);

        public int IsWindowCalls { get; private set; }
        public int GetProcessIdCalls { get; private set; }
        public int GetWindowThreadIdCalls { get; private set; }
        public int GetProcessImagePathCalls { get; private set; }
        public int GetClassNameCalls { get; private set; }
        public int GetProcessStartTimeUtcTicksCalls { get; private set; }
        public int GetCaptureIdentityTokenCalls { get; private set; }

        public Func<string?, InvalidOperationException>? ThrowOnImagePath { get; set; }

        public bool IsWindow(IntPtr hwnd)
        {
            IsWindowCalls++;
            return Alive && hwnd != IntPtr.Zero;
        }

        public uint GetProcessId(IntPtr hwnd) { GetProcessIdCalls++; return Pid; }
        public uint GetWindowThreadId(IntPtr hwnd) { GetWindowThreadIdCalls++; return ThreadId; }

        public string? GetProcessImagePath(uint pid)
        {
            GetProcessImagePathCalls++;
            if (ThrowOnImagePath != null)
                throw ThrowOnImagePath(ExePath);
            return ExePath;
        }

        public string? GetClassName(IntPtr hwnd) { GetClassNameCalls++; return ClassName; }

        public long GetProcessStartTimeUtcTicks(uint pid)
        {
            GetProcessStartTimeUtcTicksCalls++;
            return ProcessStartTicks;
        }

        public IntPtr GetCaptureIdentityToken(IntPtr hwnd) { GetCaptureIdentityTokenCalls++; return CaptureToken; }

        // Mutation seams are never reached by identity evaluation.
        public bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken) => throw new NotSupportedException();
        public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement) => throw new NotSupportedException();
        public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags) => throw new NotSupportedException();
        public bool ShowWindow(IntPtr hwnd, int command) => throw new NotSupportedException();
        public bool IsWindowVisible(IntPtr hwnd) => true;
        public int SetTransitionsDisabled(IntPtr hwnd, int value) => throw new NotSupportedException();
    }

    // ---- strong tier --------------------------------------------------------

    [Fact]
    public void Strong_AllEvidenceMatches_ReturnsMatch()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi();

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.Strong, out string reason);

        Assert.Equal(WindowIdentityResult.Match, result);
        Assert.Equal("all recovery identity evidence matched", reason);
        // The strong path queries every required evidence source exactly once.
        Assert.Equal(1, api.GetProcessImagePathCalls);
        Assert.Equal(1, api.GetProcessStartTimeUtcTicksCalls);
        Assert.Equal(1, api.GetCaptureIdentityTokenCalls);
    }

    [Fact]
    public void Strong_ExecutableMismatch_IsMismatch()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi { ExePath = "other.exe" };

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.Strong, out string reason);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
        Assert.Equal("executable identity differs", reason);
        // Positive staleness evidence short-circuits: the process-start probe
        // after it never runs.
        Assert.Equal(0, api.GetProcessStartTimeUtcTicksCalls);
    }

    [Fact]
    public void Strong_ProcessStartMismatch_IsMismatch()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi { ProcessStartTicks = 101 };

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.Strong, out string reason);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
        Assert.Equal("process-start identity differs", reason);
    }

    [Fact]
    public void Strong_UnreadableExecutable_IsUnverifiable()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi { ExePath = null };

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.Strong, out string reason);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
        Assert.Equal("executable identity could not be read", reason);
    }

    [Fact]
    public void Strong_UnreadableProcessStart_IsUnverifiable()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi { ProcessStartTicks = 0 };

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.Strong, out string reason);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
        Assert.Equal("process-start identity could not be read", reason);
    }

    [Fact]
    public void Strong_ExecutableProbeException_FailsClosedUnverifiable()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi
        {
            ThrowOnImagePath = _ => new InvalidOperationException("synthetic probe failure"),
        };

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.Strong, out string reason);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
        Assert.Equal("recovery identity probe threw InvalidOperationException", reason);
    }

    // ---- cheap mutation-boundary tier ---------------------------------------

    [Fact]
    public void Cheap_GenerationMatches_ReturnsMatch_WithoutExpensiveProbes()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi();

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.MutationBoundary, out string reason);

        Assert.Equal(WindowIdentityResult.Match, result);
        Assert.Equal("cheap recovery generation matched", reason);
        // The whole point of the boundary tier: NO executable or process-start
        // probes on the per-write hot path.
        Assert.Equal(0, api.GetProcessImagePathCalls);
        Assert.Equal(0, api.GetProcessStartTimeUtcTicksCalls);
        Assert.Equal(1, api.GetCaptureIdentityTokenCalls);
    }

    [Fact]
    public void Cheap_IgnoresExecutableAndProcessStartDivergence()
    {
        var entry = Entry();
        // A divergent exe/start would mean a recycled process — but at the
        // mutation boundary the strong check already ran; the cheap tier is a
        // deliberate HWND-generation re-check and must not re-derive those.
        var api = new RecordingRecoveryApi { ExePath = "other.exe", ProcessStartTicks = 999999 };

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.MutationBoundary, out _);

        Assert.Equal(WindowIdentityResult.Match, result);
        Assert.Equal(0, api.GetProcessImagePathCalls);
        Assert.Equal(0, api.GetProcessStartTimeUtcTicksCalls);
    }

    [Fact]
    public void Cheap_ProbeException_FailsClosedUnverifiable()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi { ClassName = null }; // class read returns null -> Unverifiable

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.MutationBoundary, out string reason);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
        Assert.Equal("window class identity could not be read", reason);
    }

    [Fact]
    public void Cheap_CoalesceProbeException_ProducesGenerationReasonString()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi
        {
            ThrowOnImagePath = _ => new InvalidOperationException("unreachable on cheap tier"),
            Pid = 0, // PID unreadable short-circuits before the exe probe would even run
        };

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.MutationBoundary, out string reason);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
        Assert.Equal("live PID could not be read", reason);
        Assert.Equal(0, api.GetProcessImagePathCalls);
    }

    // ---- shared evidence semantics (both tiers) -----------------------------

    /// <summary>Runs the core under both tiers; asserts identically via <paramref name="verify"/>.</summary>
    private void ForBothTiers(Action<WindowShepherdService.RecoveryEvidenceTier> verify)
    {
        verify(WindowShepherdService.RecoveryEvidenceTier.Strong);
        verify(WindowShepherdService.RecoveryEvidenceTier.MutationBoundary);
    }

    [Fact]
    public void DestroyedHwnd_IsMismatch_BothTiers()
    {
        ForBothTiers(tier =>
        {
            var entry = Entry();
            var api = new RecordingRecoveryApi { Alive = false };

            var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
                entry, Hwnd(entry), api, tier, out string reason);

            Assert.Equal(WindowIdentityResult.Mismatch, result);
            Assert.Equal("HWND no longer exists", reason);
        });
    }

    [Fact]
    public void PidMismatch_IsMismatch_BothTiers()
    {
        ForBothTiers(tier =>
        {
            var entry = Entry();
            var api = new RecordingRecoveryApi { Pid = 11 };

            var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
                entry, Hwnd(entry), api, tier, out string reason);

            Assert.Equal(WindowIdentityResult.Mismatch, result);
            Assert.Equal("PID differs", reason);
        });
    }

    [Fact]
    public void UnreadablePid_IsUnverifiable_StrongTier()
    {
        var entry = Entry();
        var api = new RecordingRecoveryApi { Pid = 0 };

        var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.Strong, out string reason);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
        Assert.Equal("live PID could not be read", reason);
    }

    [Fact]
    public void GuiThreadMismatch_IsMismatch_BothTiers()
    {
        ForBothTiers(tier =>
        {
            var entry = Entry();
            var api = new RecordingRecoveryApi { ThreadId = 21 };

            var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
                entry, Hwnd(entry), api, tier, out string reason);

            Assert.Equal(WindowIdentityResult.Mismatch, result);
            Assert.Equal("GUI thread identity differs", reason);
        });
    }

    [Fact]
    public void ClassMismatch_IsMismatch_BothTiers()
    {
        ForBothTiers(tier =>
        {
            var entry = Entry();
            var api = new RecordingRecoveryApi { ClassName = "RecycledWindow" };

            var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
                entry, Hwnd(entry), api, tier, out string reason);

            Assert.Equal(WindowIdentityResult.Mismatch, result);
            Assert.Equal("window class differs", reason);
        });
    }

    [Fact]
    public void GenerationTokenMismatch_IsMismatch_BothTiers()
    {
        ForBothTiers(tier =>
        {
            var entry = Entry();
            var api = new RecordingRecoveryApi { CaptureToken = new IntPtr(1002) };

            var result = WindowShepherdService.EvaluateRecoveryIdentityCore(
                entry, Hwnd(entry), api, tier, out string reason);

            Assert.Equal(WindowIdentityResult.Mismatch, result);
            Assert.Equal("HWND generation token differs", reason);
        });
    }

    [Fact]
    public void ProbeOrder_StrongTier_FollowsDocumentedSequence()
    {
        // Order matters for diagnostics (which reason wins when several pieces
        // of evidence are stale). Documented sequence: HWND → PID → thread →
        // exe → class → start → token.
        var entry = Entry();
        var api = new RecordingRecoveryApi { ExePath = "other.exe", ClassName = "OtherWindow" };

        WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api, WindowShepherdService.RecoveryEvidenceTier.Strong, out string reason);

        Assert.Equal("executable identity differs", reason); // exe precedes class

        var api2 = new RecordingRecoveryApi { ProcessStartTicks = 101, CaptureToken = new IntPtr(1002) };
        WindowShepherdService.EvaluateRecoveryIdentityCore(
            entry, Hwnd(entry), api2, WindowShepherdService.RecoveryEvidenceTier.Strong, out string reason2);

        Assert.Equal("process-start identity differs", reason2); // start precedes token
    }
}
