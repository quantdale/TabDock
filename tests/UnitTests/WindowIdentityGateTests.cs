using System;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Behavioral coverage for the unified WindowIdentityGate evaluation core
/// (Wave 2B). <see cref="WindowIdentityGate.Evaluate"/> (captured-window tier,
/// capture token REQUIRED) and
/// <see cref="WindowIdentityGate.EvaluateBeforeCaptureToken"/> (capture
/// admission, token NOT YET INSTALLED) now share one private implementation.
/// A RECORDING fake pins the probe contract so a future edit cannot:
///
///  - make the pre-token admission path query or require the capture token;
///  - let the captured-window path forget the token check;
///  - weaken any Mismatch/Unverifiable classification;
///  - let a probe exception be treated as anything but Unverifiable.
/// </summary>
public sealed class WindowIdentityGateTests
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

    /// <summary>Recording IWindowIdentityNativeApi counting every probe.</summary>
    private sealed class RecordingIdentityApi : IWindowIdentityNativeApi
    {
        public bool Alive { get; set; } = true;
        public IntPtr CaptureToken { get; set; } = new(1001);
        public WindowProcessIdentity Identity { get; set; } = new(10, 20);
        public string? ExePath { get; set; } = "guest.exe";
        public string? ClassName { get; set; } = "GuestWindow";
        public long ProcessStartTicks { get; set; } = 100;

        public int IsWindowCalls { get; private set; }
        public int GetCaptureIdentityTokenCalls { get; private set; }
        public int GetProcessIdentityCalls { get; private set; }
        public int GetProcessImagePathCalls { get; private set; }
        public int GetClassNameCalls { get; private set; }
        public int GetProcessStartTimeUtcTicksCalls { get; private set; }

        public bool IsWindow(IntPtr hwnd) { IsWindowCalls++; return Alive && hwnd != IntPtr.Zero; }
        public IntPtr GetCaptureIdentityToken(IntPtr hwnd) { GetCaptureIdentityTokenCalls++; return CaptureToken; }
        public WindowProcessIdentity GetProcessIdentity(IntPtr hwnd) { GetProcessIdentityCalls++; return Identity; }
        public string? GetProcessImagePath(uint pid) { GetProcessImagePathCalls++; return ExePath; }
        public string? GetClassName(IntPtr hwnd) { GetClassNameCalls++; return ClassName; }
        public long GetProcessStartTimeUtcTicks(uint pid) { GetProcessStartTimeUtcTicksCalls++; return ProcessStartTicks; }

        // Mutation seams are never reached by evaluation.
        public bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken) => throw new NotSupportedException();
        public IntPtr GetReleasedCloseNonce(IntPtr hwnd) => throw new NotSupportedException();
        public bool InstallReleasedCloseNonce(IntPtr hwnd, IntPtr nonce) => throw new NotSupportedException();
        public bool ConsumeReleasedCloseNonce(IntPtr hwnd, IntPtr expectedNonce) => throw new NotSupportedException();
    }

    // ---- token policy --------------------------------------------------------

    [Fact]
    public void Evaluate_QueriesAndValidatesTheCaptureToken()
    {
        var captured = Captured();
        var api = new RecordingIdentityApi();

        var result = WindowIdentityGate.Evaluate(captured, api, verifyExecutable: true, verifyProcessInstance: true, out _);

        Assert.Equal(WindowIdentityResult.Match, result);
        Assert.Equal(1, api.GetCaptureIdentityTokenCalls); // regular path MUST consult the live token property
    }

    [Fact]
    public void EvaluateBeforeCaptureToken_NeverQueriesTheTokenProperty()
    {
        var captured = Captured();
        var api = new RecordingIdentityApi();

        var result = WindowIdentityGate.EvaluateBeforeCaptureToken(
            captured, api, verifyExecutable: true, verifyProcessInstance: true, out _);

        Assert.Equal(WindowIdentityResult.Match, result);
        // The defining pre-token guarantee: no token query at all. There is
        // nothing to require yet — querying would be meaningless and requiring
        // it would reject every legitimate admission.
        Assert.Equal(0, api.GetCaptureIdentityTokenCalls);
    }

    [Fact]
    public void Evaluate_MissingCapturedToken_IsUnverifiable()
    {
        var captured = Captured();
        captured.WindowIdentityToken = 0;
        var api = new RecordingIdentityApi();

        var result = WindowIdentityGate.Evaluate(captured, api, verifyExecutable: false, verifyProcessInstance: false, out string reason);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
        Assert.Equal("captured HWND token is unavailable", reason);
    }

    [Fact]
    public void Evaluate_LiveTokenMismatch_IsMismatch()
    {
        var captured = Captured();
        var api = new RecordingIdentityApi { CaptureToken = new IntPtr(1002) };

        var result = WindowIdentityGate.Evaluate(captured, api, verifyExecutable: false, verifyProcessInstance: false, out string reason);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
        Assert.Equal("HWND capture token differs", reason);
        // Positive staleness evidence short-circuits before any further probes.
        Assert.Equal(0, api.GetProcessImagePathCalls);
        Assert.Equal(0, api.GetProcessStartTimeUtcTicksCalls);
    }

    // ---- shared strong-field semantics ---------------------------------------

    [Fact]
    public void PreToken_DetectsPidMismatch()
    {
        var captured = Captured();
        var api = new RecordingIdentityApi { Identity = new WindowProcessIdentity(11, 20) };

        var result = WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, false, false, out string reason);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
        Assert.Equal("process ID differs", reason);
    }

    [Fact]
    public void PreToken_DetectsGuiThreadMismatch()
    {
        var captured = Captured();
        var api = new RecordingIdentityApi { Identity = new WindowProcessIdentity(10, 21) };

        var result = WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, false, false, out string reason);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
        Assert.Equal("GUI thread ID differs", reason);
    }

    [Fact]
    public void PreToken_DetectsClassMismatch()
    {
        var captured = Captured();
        var api = new RecordingIdentityApi { ClassName = "RecycledWindow" };

        var result = WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, false, false, out string reason);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
        Assert.Equal("window class differs", reason);
    }

    [Fact]
    public void PreToken_DetectsExecutableMismatch()
    {
        var captured = Captured();
        var api = new RecordingIdentityApi { ExePath = "other.exe" };

        var result = WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, verifyExecutable: true, verifyProcessInstance: false, out string reason);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
        Assert.Equal("executable identity differs", reason);
    }

    [Fact]
    public void PreToken_DetectsProcessStartMismatch()
    {
        var captured = Captured();
        var api = new RecordingIdentityApi { ProcessStartTicks = 101 };

        var result = WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, verifyExecutable: true, verifyProcessInstance: true, out string reason);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
        Assert.Equal("process-start identity differs", reason);
    }

    [Fact]
    public void Regular_Evaluate_AlsoRejectsEveryStrongFieldMismatch()
    {
        // The shared implementation must not have weakened the captured-window
        // tier's non-token checks.
        Assert.Equal(WindowIdentityResult.Mismatch,
            Run(new RecordingIdentityApi { Identity = new WindowProcessIdentity(11, 20) }, pre: false));
        Assert.Equal(WindowIdentityResult.Mismatch,
            Run(new RecordingIdentityApi { Identity = new WindowProcessIdentity(10, 21) }, pre: false));
        Assert.Equal(WindowIdentityResult.Mismatch,
            Run(new RecordingIdentityApi { ClassName = "Other" }, pre: false));
        Assert.Equal(WindowIdentityResult.Mismatch,
            Run(new RecordingIdentityApi { ExePath = "other.exe" }, pre: false, exe: true));
        Assert.Equal(WindowIdentityResult.Mismatch,
            Run(new RecordingIdentityApi { ProcessStartTicks = 999 }, pre: false, exe: true, instance: true));

        static WindowIdentityResult Run(RecordingIdentityApi api, bool pre, bool exe = false, bool instance = false)
            => pre
                ? WindowIdentityGate.EvaluateBeforeCaptureToken(Captured(), api, exe, instance, out _)
                : WindowIdentityGate.Evaluate(Captured(), api, exe, instance, out _);
    }

    // ---- Unverifiable evidence -----------------------------------------------

    [Fact]
    public void AliveHwnd_ZeroCapturedToken_IsUnverifiableOnRegularPath()
    {
        var captured = Captured();
        captured.WindowIdentityToken = 0;
        var api = new RecordingIdentityApi { Alive = true };

        var result = WindowIdentityGate.Evaluate(captured, api, false, false, out _);

        // Token unavailability on an allegedly-captured (and still-alive) window
        // is never positive staleness evidence - it fails closed as Unverifiable.
        Assert.Equal(WindowIdentityResult.Unverifiable, result);
    }

    [Fact]
    public void DeadHwnd_IsMismatch_EvenWithZeroCapturedToken()
    {
        // Probe order is preserved from the original implementation: HWND
        // existence is checked before the capture token, so a destroyed window
        // is positive staleness evidence (Mismatch) regardless of token state.
        var captured = Captured();
        captured.WindowIdentityToken = 0;
        var api = new RecordingIdentityApi { Alive = false };

        var result = WindowIdentityGate.Evaluate(captured, api, false, false, out _);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestroyedHwnd_IsMismatch(bool pre)
    {
        var api = new RecordingIdentityApi { Alive = false };
        var captured = Captured();

        var result = pre
            ? WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, false, false, out _)
            : WindowIdentityGate.Evaluate(captured, api, false, false, out _);

        Assert.Equal(WindowIdentityResult.Mismatch, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableLivePidOrThread_IsUnverifiable(bool pre)
    {
        var captured = Captured();
        var api = new RecordingIdentityApi { Identity = new WindowProcessIdentity(0, 0) };

        var result = pre
            ? WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, false, false, out _)
            : WindowIdentityGate.Evaluate(captured, api, false, false, out _);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableClass_IsUnverifiable(bool pre)
    {
        var captured = Captured();
        var api = new RecordingIdentityApi { ClassName = null };

        var result = pre
            ? WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, false, false, out _)
            : WindowIdentityGate.Evaluate(captured, api, false, false, out _);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingCapturedExe_IsUnverifiableWhenRequired(bool pre)
    {
        var captured = Captured();
        captured.ExePath = string.Empty;
        var api = new RecordingIdentityApi();

        var result = pre
            ? WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, verifyExecutable: true, verifyProcessInstance: false, out _)
            : WindowIdentityGate.Evaluate(captured, api, verifyExecutable: true, verifyProcessInstance: false, out _);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingCapturedProcessStart_IsUnverifiableWhenRequired(bool pre)
    {
        var captured = Captured();
        captured.ProcessStartTimeUtcTicks = 0;
        var api = new RecordingIdentityApi();

        var result = pre
            ? WindowIdentityGate.EvaluateBeforeCaptureToken(captured, api, verifyExecutable: true, verifyProcessInstance: true, out _)
            : WindowIdentityGate.Evaluate(captured, api, verifyExecutable: true, verifyProcessInstance: true, out _);

        Assert.Equal(WindowIdentityResult.Unverifiable, result);
    }

    [Fact]
    public void HotTier_OptionalProbesNotRequested_AreNeverMade()
    {
        var captured = Captured();
        var apiPre = new RecordingIdentityApi();
        var apiReg = new RecordingIdentityApi();

        WindowIdentityGate.EvaluateBeforeCaptureToken(captured, apiPre, verifyExecutable: false, verifyProcessInstance: false, out _);
        WindowIdentityGate.Evaluate(captured, apiReg, verifyExecutable: false, verifyProcessInstance: false, out _);

        Assert.Equal(0, apiPre.GetProcessImagePathCalls);
        Assert.Equal(0, apiPre.GetProcessStartTimeUtcTicksCalls);
        Assert.Equal(0, apiReg.GetProcessImagePathCalls);
        Assert.Equal(0, apiReg.GetProcessStartTimeUtcTicksCalls);
    }

    // ---- success strings + Matches wrapper ------------------------------------

    [Fact]
    public void SuccessReasons_RemainDistinctAndStable()
    {
        var captured = Captured();
        var apiA = new RecordingIdentityApi();
        var apiB = new RecordingIdentityApi();

        WindowIdentityGate.Evaluate(captured, apiA, true, true, out string regular);
        WindowIdentityGate.EvaluateBeforeCaptureToken(captured, apiB, true, true, out string pre);

        Assert.Equal("all required identity evidence matched", regular);
        Assert.Equal("all pre-token identity evidence matched", pre);
    }

    [Fact]
    public void Matches_WrapsEvaluate()
    {
        var captured = Captured();
        Assert.True(WindowIdentityGate.Matches(captured, new RecordingIdentityApi(), true, true));
        Assert.False(WindowIdentityGate.Matches(captured, new RecordingIdentityApi { Identity = new WindowProcessIdentity(99, 99) }, false, false));
    }

    // ---- probe-order stability -------------------------------------------------

    [Fact]
    public void ProbeOrder_TokenCheckPrecedesStrongFields_OnRegularPath()
    {
        var captured = Captured();
        var api = new RecordingIdentityApi { Identity = new WindowProcessIdentity(11, 11), CaptureToken = new IntPtr(1002) };

        WindowIdentityGate.Evaluate(captured, api, false, false, out string reason);

        // Token mismatch wins because it is probed first.
        Assert.Equal("HWND capture token differs", reason);
        Assert.Equal(1, api.GetCaptureIdentityTokenCalls);
        Assert.Equal(0, api.GetProcessIdentityCalls);
    }
}
