using System;
using System.Collections.Generic;
using System.Linq;

namespace TabDock.ValidationDriver;

internal sealed record VisualRingBufferSnapshot(
    bool Running,
    int Count,
    long Bytes,
    int FramesEvicted,
    int FramesFlushed);

/// <summary>
/// In-memory, transition-scoped visual history. It has hard count, byte, rate,
/// and duration ceilings and never persists frames by itself.
/// </summary>
internal sealed class VisualRingBuffer : IDisposable
{
    private readonly int _maximumFrames;
    private readonly long _maximumBytes;
    private readonly long _maximumDurationMilliseconds;
    private readonly double _maximumFramesPerSecond;
    private readonly Queue<VisualFrame> _frames = new();
    private readonly object _sync = new();
    private DateTimeOffset? _startedUtc;
    private DateTimeOffset? _lastCapturedUtc;
    private long _bytes;
    private long _sequence;
    private int _framesEvicted;
    private int _framesFlushed;
    private bool _running;

    public VisualRingBuffer(VisualEvidencePolicy policy)
    {
        policy.Validate();
        if (policy.Level != VisualEvidenceLevel.FLIGHT_RECORDER)
            throw new ArgumentException("ring buffer requires the flight-recorder visual level.", nameof(policy));
        _maximumFrames = policy.RingMaxFrames;
        _maximumBytes = policy.RingMaxBytes;
        _maximumDurationMilliseconds = policy.RingDurationMilliseconds;
        _maximumFramesPerSecond = policy.RingMaxFramesPerSecond;
    }

    public void Start()
    {
        lock (_sync)
        {
            ClearFrames();
            _startedUtc = null;
            _lastCapturedUtc = null;
            _sequence = 0;
            _running = true;
        }
    }

    public bool TryAdd(VisualFrame frame)
        => TryAdd(frame, forceRateLimit: false);

    public bool TryAdd(VisualFrame frame, bool forceRateLimit)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));
        long frameBytes = checked((long)frame.Pixels.Length * sizeof(int));
        if (frameBytes <= 0 || frameBytes > _maximumBytes)
            return false;

        lock (_sync)
        {
            if (!_running)
                return false;
            if (_startedUtc is null)
                _startedUtc = frame.CapturedUtc;
            long relativeMilliseconds = checked((frame.CapturedUtc - _startedUtc.Value).Ticks / TimeSpan.TicksPerMillisecond);
            if (relativeMilliseconds < 0 || relativeMilliseconds > _maximumDurationMilliseconds)
                return false;
            if (!forceRateLimit && _lastCapturedUtc is DateTimeOffset previous)
            {
                double minimumIntervalMilliseconds = 1000.0 / _maximumFramesPerSecond;
                if ((frame.CapturedUtc - previous).TotalMilliseconds < minimumIntervalMilliseconds)
                    return false;
            }

            while (_frames.Count >= _maximumFrames || _bytes + frameBytes > _maximumBytes)
            {
                VisualFrame evicted = _frames.Dequeue();
                _bytes -= checked((long)evicted.Pixels.Length * sizeof(int));
                _framesEvicted++;
            }

            var retained = new VisualFrame(
                frame.Width,
                frame.Height,
                frame.Pixels.Span,
                frame.CapturedUtc,
                frame.RequestedRect,
                frame.ActualRect,
                frame.Method,
                frame.ScopeKind,
                frame.Target,
                frame.Privacy,
                frame.Dpi,
                frame.MonitorId,
                sequence: ++_sequence,
                relativeMilliseconds: relativeMilliseconds,
                captureDurationMilliseconds: frame.CaptureDurationMilliseconds);
            _frames.Enqueue(retained);
            _bytes += frameBytes;
            _lastCapturedUtc = frame.CapturedUtc;
            return true;
        }
    }

    public IReadOnlyList<VisualFrame> FlushForFailure()
    {
        lock (_sync)
        {
            VisualFrame[] flushed = _frames.ToArray();
            _frames.Clear();
            _bytes = 0;
            _framesFlushed += flushed.Length;
            return flushed;
        }
    }

    public VisualRingBufferSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new VisualRingBufferSnapshot(
                _running,
                _frames.Count,
                _bytes,
                _framesEvicted,
                _framesFlushed);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            ClearFrames();
            _startedUtc = null;
            _lastCapturedUtc = null;
            _running = false;
        }
    }

    public void Dispose() => Stop();

    private void ClearFrames()
    {
        _frames.Clear();
        _bytes = 0;
    }
}
