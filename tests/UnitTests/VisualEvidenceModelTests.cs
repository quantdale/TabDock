using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualEvidenceModelTests
{
    [Fact]
    public void Policy_NormalizesPortablePathsAndRejectsUnsafeForms()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-visual-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            var policy = new VisualPathPolicy(root);
            Assert.Equal("visual/frame.png", policy.NormalizeRelative(@"visual\frame.png"));
            Assert.Equal(Path.Combine(policy.Root, "visual", "frame.png"), policy.Resolve("visual/frame.png"));
            Assert.Throws<ArgumentException>(() => policy.NormalizeRelative("../frame.png"));
            Assert.Throws<ArgumentException>(() => policy.NormalizeRelative("visual//frame.png"));
            Assert.Throws<ArgumentException>(() => policy.NormalizeRelative("C:/frame.png"));
            Assert.Throws<ArgumentException>(() => policy.NormalizeRelative("/frame.png"));
            Assert.Throws<ArgumentException>(() => policy.RelativeFromFullPath(Path.Combine(policy.Root, "..", "outside.png")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Model_SerializesStableSchemaEnumsAndIdentity()
    {
        VisualTargetIdentity target = Target();
        VisualCaptureScope scope = VisualCaptureScope.ForWindow(
            VisualCaptureScopeKind.GUEST_WINDOW,
            target,
            VisualPrivacyClass.TEST_OWNED);
        var request = new VisualCheckpointRequest(
            "guest-settled",
            VisualCheckpointPhase.AFTER_ACTION_SETTLED,
            "the selected guest fills the declared host content region",
            new[] { scope },
            VisualCaptureRequiredness.REQUIRED);
        request.Validate();

        string json = JsonSerializer.Serialize(request, VisualJson.Options);
        Assert.Contains("guest-settled", json, StringComparison.Ordinal);
        Assert.Contains("AFTER_ACTION_SETTLED", json, StringComparison.Ordinal);
        Assert.Contains("TEST_OWNED", json, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VisualCheckpointRequest>(
            json.Replace("AFTER_ACTION_SETTLED", "NOT_A_PHASE", StringComparison.Ordinal), VisualJson.Options));
    }

    [Fact]
    public void Frame_RejectsEmptyDimensionsMalformedRectanglesAndMismatchedPixels()
    {
        VisualTargetIdentity target = Target();
        Assert.Throws<ArgumentOutOfRangeException>(() => new VisualFrame(
            0, 1, new[] { 0 }, DateTimeOffset.UtcNow,
            new VisualRect(0, 0, 1, 1), new VisualRect(0, 0, 1, 1),
            VisualCaptureMethod.SYNTHETIC, VisualCaptureScopeKind.GUEST_WINDOW, target,
            VisualPrivacyClass.TEST_OWNED, 96, "monitor-1"));
        Assert.Throws<ArgumentException>(() => new VisualFrame(
            2, 1, new[] { 0 }, DateTimeOffset.UtcNow,
            new VisualRect(0, 0, 2, 1), new VisualRect(0, 0, 2, 1),
            VisualCaptureMethod.SYNTHETIC, VisualCaptureScopeKind.GUEST_WINDOW, target,
            VisualPrivacyClass.TEST_OWNED, 96, "monitor-1"));
        Assert.Throws<ArgumentException>(() => new VisualFrame(
            1, 1, new[] { 0 }, DateTimeOffset.UtcNow,
            new VisualRect(0, 0, 0, 1), new VisualRect(0, 0, 1, 1),
            VisualCaptureMethod.SYNTHETIC, VisualCaptureScopeKind.GUEST_WINDOW, target,
            VisualPrivacyClass.TEST_OWNED, 96, "monitor-1"));
    }

    [Fact]
    public void Scope_VirtualDesktopRequiresExplicitAuthorization()
    {
        Assert.Throws<ArgumentException>(() => VisualCaptureScope.ForVirtualDesktop(false));
        VisualCaptureScope authorized = VisualCaptureScope.ForVirtualDesktop(true);
        authorized.Validate();
        Assert.Equal(VisualPrivacyClass.DESKTOP_RESTRICTED, authorized.Privacy);
    }

    [Fact]
    public void EnabledPolicy_HasFiniteBoundedDefaults()
    {
        VisualEvidencePolicy policy = VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.FLIGHT_RECORDER, true);
        policy.Validate();
        Assert.True(policy.Enabled);
        Assert.InRange(policy.MaxBytes, 1, 256L * 1024 * 1024);
        Assert.InRange(policy.RingMaxFramesPerSecond, 0.1, 30);
        Assert.Equal(VisualEvidenceLevel.FLIGHT_RECORDER, policy.Level);
    }

    private static VisualTargetIdentity Target()
        => new("0x1234", 42, 43, "TDVAL", 44, "Guest", "OwnedProcess");

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup; the policy itself has already been exercised.
        }
    }
}
