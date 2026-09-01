using System;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualRingBufferTests
{
    private static readonly VisualTargetIdentity Target = new(
        "0x10", 10, 20, "TabDock.Guest", 100, "GuestWindow", "OwnedWindow");

    [Fact]
    public void Ring_EnforcesRateDurationCountAndFlushOrdering()
    {
        VisualEvidencePolicy policy = VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.FLIGHT_RECORDER) with
        {
            RingMaxFrames = 3,
            RingMaxBytes = 1024,
            RingDurationMilliseconds = 2000,
            RingMaxFramesPerSecond = 2,
        };
        policy.Validate();
        using var ring = new VisualRingBuffer(policy);
        DateTimeOffset start = DateTimeOffset.UtcNow;
        ring.Start();

        Assert.True(ring.TryAdd(Frame(start)));
        Assert.False(ring.TryAdd(Frame(start.AddMilliseconds(100))));
        Assert.True(ring.TryAdd(Frame(start.AddMilliseconds(500))));
        Assert.True(ring.TryAdd(Frame(start.AddMilliseconds(1000))));
        Assert.True(ring.TryAdd(Frame(start.AddMilliseconds(1500))));
        Assert.False(ring.TryAdd(Frame(start.AddMilliseconds(2500))));

        VisualRingBufferSnapshot beforeFlush = ring.Snapshot();
        Assert.True(beforeFlush.Running);
        Assert.Equal(3, beforeFlush.Count);
        Assert.Equal(1, beforeFlush.FramesEvicted);
        Assert.True(beforeFlush.Bytes > 0);

        var flushed = ring.FlushForFailure();
        Assert.Equal(3, flushed.Count);
        Assert.Equal(500, flushed[0].RelativeMilliseconds);
        Assert.Equal(1000, flushed[1].RelativeMilliseconds);
        Assert.Equal(1500, flushed[2].RelativeMilliseconds);
        Assert.True(flushed[0].Sequence < flushed[1].Sequence);
        Assert.True(flushed[1].Sequence < flushed[2].Sequence);
        Assert.Equal(3, ring.Snapshot().FramesFlushed);
        Assert.Equal(0, ring.Snapshot().Count);
    }

    [Fact]
    public void HealthyStopDiscardsHistoryAndDisposeStopsRecorder()
    {
        VisualEvidencePolicy policy = VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.FLIGHT_RECORDER);
        using var ring = new VisualRingBuffer(policy);
        ring.Start();
        Assert.True(ring.TryAdd(Frame(DateTimeOffset.UtcNow)));
        ring.Stop();

        VisualRingBufferSnapshot stopped = ring.Snapshot();
        Assert.False(stopped.Running);
        Assert.Equal(0, stopped.Count);
        Assert.Empty(ring.FlushForFailure());

        ring.Start();
        ring.Dispose();
        Assert.False(ring.Snapshot().Running);
        Assert.False(ring.TryAdd(Frame(DateTimeOffset.UtcNow.AddSeconds(1))));
    }

    [Fact]
    public void ConstructorRejectsNonFlightPolicy()
    {
        Assert.Throws<ArgumentException>(() => new VisualRingBuffer(
            VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.CHECKPOINTS)));
    }

    private static VisualFrame Frame(DateTimeOffset capturedUtc)
        => new(
            2,
            1,
            new[] { unchecked((int)0x00FF0000), 0x0000FF00 },
            capturedUtc,
            new VisualRect(10, 20, 12, 21),
            new VisualRect(10, 20, 12, 21),
            VisualCaptureMethod.SYNTHETIC,
            VisualCaptureScopeKind.GUEST_WINDOW,
            Target,
            VisualPrivacyClass.TEST_OWNED,
            96,
            "synthetic-monitor");
}
