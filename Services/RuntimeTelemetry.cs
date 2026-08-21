using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace TabDock.Services;

/// <summary>
/// Low-overhead, opt-in telemetry for the real TabDock presentation path.
///
/// Everything here is a no-op while <see cref="Enabled"/> is false, so the
/// production runtime pays nothing. Enable it (e.g. behind a diagnostic switch)
/// to record per-transition timings and native-operation counts, then read
/// p50/p95/p99 latency over a run. No data is written to disk by this class;
/// callers decide whether to export it.
///
/// Thread-safe: transition records keyed by an opaque id, counters via
/// interlocked increments, latencies in a bounded concurrent queue.
/// </summary>
public sealed class RuntimeTelemetry
{
    public static RuntimeTelemetry Instance { get; } = new();

    /// <summary>
    /// Master switch. Defaults to false (zero overhead). Enable explicitly via
    /// the TABDOCK_RUNTIME_TELEMETRY=1 environment variable (checked once at
    /// type initialization) or by setting this property before the
    /// transitions/counters you want to observe.
    /// </summary>
    public static bool Enabled { get; set; } = string.Equals(
        Environment.GetEnvironmentVariable("TABDOCK_RUNTIME_TELEMETRY"),
        "1",
        StringComparison.Ordinal);

    /// <summary>Upper bound on in-flight transition records. A caller that
    /// abandons a transition without CompleteTransition cannot grow storage
    /// without limit; the oldest record is dropped first.</summary>
    private const int MaxInFlightTransitions = 256;

    public enum TransitionStage
    {
        InputDown,
        Classified,
        JournalGateComplete,
        OldHides,
        TargetGeometry,
        TargetVisible,
        ForegroundRequested,
        ForegroundObserved,
        Stable,
    }

    private const int MaxSamples = 4096;

    private readonly ConcurrentDictionary<int, TransitionRecord> _transitions = new();
    private readonly ConcurrentQueue<long> _latencyTicks = new();
    private int _nextId = 1;

    private long _showWindow;
    private long _setWindowPos;
    private long _deferBatch;
    private long _setForeground;
    private long _journalCommit;
    private long _stateJsonCommit;
    private long _requestRelayout;
    private long _relayoutGuests;

    private sealed class TransitionRecord
    {
        public long InputDown = Stopwatch.GetTimestamp();
        public long Classified;
        public long JournalGateComplete;
        public long OldHides;
        public long TargetGeometry;
        public long TargetVisible;
        public long ForegroundRequested;
        public long ForegroundObserved;
        public long Stable;
    }

    /// <summary>Begins a transition; returns its id, or -1 when telemetry is disabled.</summary>
    public int BeginTransition()
    {
        if (!Enabled)
            return -1;
        // Bound abandoned transitions: drop the oldest record when over the cap.
        while (_transitions.Count >= MaxInFlightTransitions)
        {
            int oldest = int.MaxValue;
            foreach (int key in _transitions.Keys)
                if (key < oldest) oldest = key;
            if (oldest == int.MaxValue || !_transitions.TryRemove(oldest, out _))
                break;
        }
        int id = System.Threading.Interlocked.Increment(ref _nextId);
        _transitions[id] = new TransitionRecord();
        return id;
    }

    public void Mark(int id, TransitionStage stage)
    {
        if (!Enabled || id < 0)
            return;
        if (!_transitions.TryGetValue(id, out TransitionRecord? rec))
            return;
        long ts = Stopwatch.GetTimestamp();
        switch (stage)
        {
            case TransitionStage.InputDown: rec.InputDown = ts; break;
            case TransitionStage.Classified: rec.Classified = ts; break;
            case TransitionStage.JournalGateComplete: rec.JournalGateComplete = ts; break;
            case TransitionStage.OldHides: rec.OldHides = ts; break;
            case TransitionStage.TargetGeometry: rec.TargetGeometry = ts; break;
            case TransitionStage.TargetVisible: rec.TargetVisible = ts; break;
            case TransitionStage.ForegroundRequested: rec.ForegroundRequested = ts; break;
            case TransitionStage.ForegroundObserved: rec.ForegroundObserved = ts; break;
            case TransitionStage.Stable: rec.Stable = ts; break;
        }
    }

    /// <summary>Closes a transition, recording its end-to-end latency (InputDown -> Stable).</summary>
    public void CompleteTransition(int id)
    {
        if (!Enabled || id < 0)
            return;
        if (_transitions.TryRemove(id, out TransitionRecord? rec) && rec.Stable != 0 && rec.Stable >= rec.InputDown)
        {
            long ms = (long)Stopwatch.GetElapsedTime(rec.InputDown, rec.Stable).TotalMilliseconds;
            _latencyTicks.Enqueue(ms);
            while (_latencyTicks.Count > MaxSamples)
                _latencyTicks.TryDequeue(out _);
        }
    }

    public void RecordShowWindow() { if (Enabled) System.Threading.Interlocked.Increment(ref _showWindow); }
    public void RecordSetWindowPos() { if (Enabled) System.Threading.Interlocked.Increment(ref _setWindowPos); }
    public void RecordDeferBatch() { if (Enabled) System.Threading.Interlocked.Increment(ref _deferBatch); }
    public void RecordSetForeground() { if (Enabled) System.Threading.Interlocked.Increment(ref _setForeground); }
    public void RecordJournalCommit() { if (Enabled) System.Threading.Interlocked.Increment(ref _journalCommit); }
    public void RecordStateJsonCommit() { if (Enabled) System.Threading.Interlocked.Increment(ref _stateJsonCommit); }
    public void RecordRequestRelayout() { if (Enabled) System.Threading.Interlocked.Increment(ref _requestRelayout); }
    public void RecordRelayoutGuests() { if (Enabled) System.Threading.Interlocked.Increment(ref _relayoutGuests); }

    public long ShowWindowCount => Volatile.Read(ref _showWindow);
    public long SetWindowPosCount => Volatile.Read(ref _setWindowPos);
    public long DeferBatchCount => Volatile.Read(ref _deferBatch);
    public long SetForegroundCount => Volatile.Read(ref _setForeground);
    public long JournalCommitCount => Volatile.Read(ref _journalCommit);
    public long StateJsonCommitCount => Volatile.Read(ref _stateJsonCommit);
    public long RequestRelayoutCount => Volatile.Read(ref _requestRelayout);
    public long RelayoutGuestsCount => Volatile.Read(ref _relayoutGuests);

    /// <summary>Returns p50/p95/p99 latency in milliseconds over collected samples (0 when none).</summary>
    public (double p50, double p95, double p99) LatencyPercentiles()
    {
        if (_latencyTicks.IsEmpty)
            return (0, 0, 0);
        long[] samples = _latencyTicks.ToArray();
        Array.Sort(samples);
        return (Percentile(samples, 50), Percentile(samples, 95), Percentile(samples, 99));
    }

    public int SampleCount => _latencyTicks.Count;

    private static double Percentile(long[] sorted, double p)
    {
        if (sorted.Length == 0)
            return 0;
        double rank = (p / 100.0) * (sorted.Length - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        if (lo == hi)
            return sorted[lo];
        double frac = rank - lo;
        return sorted[lo] * (1 - frac) + sorted[hi] * frac;
    }

    public void Reset()
    {
        _transitions.Clear();
        while (_latencyTicks.TryDequeue(out _)) { }
        _showWindow = _setWindowPos = _deferBatch = _setForeground = 0;
        _journalCommit = _stateJsonCommit = _requestRelayout = _relayoutGuests = 0;
    }
}
