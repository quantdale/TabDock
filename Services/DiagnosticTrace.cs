using System;
using System.Collections.Generic;
using System.Linq;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Fixed-capacity, thread-safe significant-event trace. It is intentionally
/// separate from the rotating text logger so exporting it never needs to parse
/// free-form log text and hot WinEvent callbacks do not allocate whole reports.
/// </summary>
public sealed class DiagnosticTrace
{
    public const int DefaultCapacity = 1024;

    private readonly object _gate = new();
    private readonly DiagnosticEventRecord[] _buffer;
    private long _nextSequence;
    private int _count;

    public DiagnosticTrace(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new DiagnosticEventRecord[capacity];
    }

    public int Capacity => _buffer.Length;

    public long Record(
        string kind,
        IntPtr containerHwnd = default,
        IntPtr guestHwnd = default,
        IntPtr foregroundHwnd = default,
        string? groupId = null,
        string? action = null,
        string? result = null,
        IReadOnlyDictionary<string, string>? data = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
            kind = "unknown";

        var record = new DiagnosticEventRecord
        {
            Sequence = System.Threading.Interlocked.Increment(ref _nextSequence),
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            Kind = kind,
            GroupId = groupId,
            ContainerHwnd = containerHwnd.ToInt64(),
            GuestHwnd = guestHwnd.ToInt64(),
            ForegroundHwnd = foregroundHwnd.ToInt64(),
            Action = action,
            Result = result,
            Data = data == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(data, StringComparer.Ordinal),
        };

        lock (_gate)
        {
            int index = (int)((record.Sequence - 1) % _buffer.Length);
            _buffer[index] = record;
            if (_count < _buffer.Length)
                _count++;
        }
        return record.Sequence;
    }

    public IReadOnlyList<DiagnosticEventRecord> Snapshot()
    {
        lock (_gate)
        {
            return _buffer
                .Where(e => e != null)
                .OrderBy(e => e.Sequence)
                .Select(Clone)
                .ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_buffer);
            _count = 0;
        }
    }

    private static DiagnosticEventRecord Clone(DiagnosticEventRecord source)
        => new()
        {
            Sequence = source.Sequence,
            TimestampUtc = source.TimestampUtc,
            Kind = source.Kind,
            GroupId = source.GroupId,
            ContainerHwnd = source.ContainerHwnd,
            GuestHwnd = source.GuestHwnd,
            ForegroundHwnd = source.ForegroundHwnd,
            Action = source.Action,
            Result = source.Result,
            Data = new Dictionary<string, string>(source.Data, StringComparer.Ordinal),
        };
}

/// <summary>Process-wide diagnostic sinks used by normal TabDock runtime paths.</summary>
public static class DiagnosticRuntime
{
    public static DiagnosticTrace Trace { get; } = new();

    public static Func<IReadOnlyList<LogicalPresentationSnapshot>>? LogicalSnapshotProvider { get; set; }

    public static void Record(string kind, IntPtr container = default, IntPtr guest = default,
        IntPtr foreground = default, string? group = null, string? action = null,
        string? result = null, IReadOnlyDictionary<string, string>? data = null)
        => Trace.Record(kind, container, guest, foreground, group, action, result, data);
}
