using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former DiagnosticSelfTest aggregator (Wave 4): ring
/// semantics, defensive snapshot copies, clear, and bounded concurrency of the
/// in-memory diagnostic trace.
/// </summary>
public class DiagnosticTraceTests
{
    [Fact]
    public void Record_SequenceNumbersAreStrictlyMonotonic()
    {
        var trace = new DiagnosticTrace(3);
        long first = trace.Record("one");
        long second = trace.Record("two");

        Assert.Equal(first + 1, second);
    }

    [Fact]
    public void Record_BeyondCapacity_DropsOldestRecords()
    {
        var trace = new DiagnosticTrace(3);
        trace.Record("one");
        trace.Record("two");
        trace.Record("three");
        trace.Record("four");

        IReadOnlyList<DiagnosticEventRecord> events = trace.Snapshot();

        Assert.Equal(3, events.Count);
        Assert.Equal("two", events[0].Kind);
        Assert.Equal("four", events[^1].Kind);
        Assert.True(events[0].Sequence < events[^1].Sequence);
    }

    [Fact]
    public void Snapshot_DataDictionaryIsDefensivelyCopiedOnBothSides()
    {
        var callerData = new Dictionary<string, string> { ["key"] = "before" };
        var trace = new DiagnosticTrace(2);

        trace.Record("defensive", data: callerData);
        callerData["key"] = "after";
        IReadOnlyList<DiagnosticEventRecord> firstSnapshot = trace.Snapshot();
        Assert.Equal("before", firstSnapshot[0].Data["key"]);

        firstSnapshot[0].Data["key"] = "mutated-snapshot";
        Assert.Equal("before", trace.Snapshot()[0].Data["key"]);
    }

    [Fact]
    public void Clear_RemovesAllRecordedEvents()
    {
        var trace = new DiagnosticTrace(2);
        trace.Record("event");
        trace.Clear();
        Assert.Empty(trace.Snapshot());
    }

    [Fact]
    public async Task Record_ConcurrentWriters_RespectCapacityAndSequenceOrder()
    {
        var trace = new DiagnosticTrace(128);

        await Task.Run(() => Parallel.For(0, 512, i => trace.Record("concurrent")));

        IReadOnlyList<DiagnosticEventRecord> snapshot = trace.Snapshot();
        Assert.Equal(128, snapshot.Count);
        Assert.True(
            snapshot.Zip(snapshot.Skip(1), (left, right) => left.Sequence < right.Sequence).All(ordered => ordered),
            "concurrent recordings must retain strictly increasing sequence order");
    }
}
