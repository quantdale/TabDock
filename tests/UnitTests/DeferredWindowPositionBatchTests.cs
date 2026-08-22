using System;
using System.Collections.Generic;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former DeferredWindowPositionSelfTest (Wave 4): the
/// BeginDeferWindowPos/DeferWindowPos/EndDeferWindowPos chaining policy —
/// every returned HDWP is chained, a failed Defer abandons without End (Win32's
/// own no-End path), a stale guest is never queued and a still-valid batch is
/// closed, and the per-guest validator runs immediately before each queue.
/// </summary>
public class DeferredWindowPositionBatchTests
{
    [Fact]
    public void Apply_ChangedHandlesAreChainedThroughEveryReturnedHdwp()
    {
        var api = new FakeApi(new[] { new IntPtr(0x22), new IntPtr(0x33), new IntPtr(0x44) });

        DeferredWindowPositionResult result = DeferredWindowPositionBatch.Apply(api, Entries());

        Assert.Equal(DeferredWindowPositionResult.Applied, result);
        Assert.Equal(3, api.DeferInputs.Count);
        Assert.Equal(new IntPtr(0x11), api.DeferInputs[0]);
        Assert.Equal(new IntPtr(0x22), api.DeferInputs[1]);
        Assert.Equal(new IntPtr(0x33), api.DeferInputs[2]);
        Assert.Equal(new IntPtr(0x44), api.EndInput);
    }

    [Fact]
    public void Apply_FailedDeferAbandonsWithoutEnd()
    {
        var api = new FakeApi(new[] { new IntPtr(0x22), IntPtr.Zero, new IntPtr(0x44) });

        DeferredWindowPositionResult result = DeferredWindowPositionBatch.Apply(api, Entries());

        Assert.Equal(DeferredWindowPositionResult.DeferFailed, result);
        Assert.Equal(2, api.DeferInputs.Count);
        Assert.Equal(new IntPtr(0x11), api.DeferInputs[0]);
        Assert.Equal(new IntPtr(0x22), api.DeferInputs[1]);
        Assert.Equal(IntPtr.Zero, api.EndInput);
    }

    [Fact]
    public void Apply_StaleGuestIsNotQueuedAndValidBatchIsStillClosed()
    {
        var api = new FakeApi(new[] { new IntPtr(0x22), new IntPtr(0x33), new IntPtr(0x44) });

        DeferredWindowPositionResult result = DeferredWindowPositionBatch.Apply(
            api,
            Entries(),
            beforeDefer: index => index != 0);

        Assert.Equal(DeferredWindowPositionResult.ValidationFailed, result);
        Assert.Empty(api.DeferInputs);
        Assert.Equal(new IntPtr(0x11), api.EndInput);
    }

    [Fact]
    public void Apply_ValidatorRunsImmediatelyBeforeEachQueue()
    {
        var api = new FakeApi(new[] { new IntPtr(0x22), new IntPtr(0x33), new IntPtr(0x44) });
        var calls = new List<int>();

        DeferredWindowPositionResult result = DeferredWindowPositionBatch.Apply(
            api,
            Entries(),
            beforeDefer: index =>
            {
                calls.Add(index);
                return index != 1;
            });

        Assert.Equal(DeferredWindowPositionResult.ValidationFailed, result);
        Assert.Equal(new[] { 0, 1 }, calls);
        Assert.Single(api.DeferInputs);
        Assert.Equal(new IntPtr(0x22), api.EndInput);
    }

    private static IReadOnlyList<DeferredWindowPositionEntry> Entries()
        => new[]
        {
            new DeferredWindowPositionEntry(new IntPtr(0x101), IntPtr.Zero, 1, 2, 3, 4, 5),
            new DeferredWindowPositionEntry(new IntPtr(0x102), IntPtr.Zero, 6, 7, 8, 9, 10),
            new DeferredWindowPositionEntry(new IntPtr(0x103), IntPtr.Zero, 11, 12, 13, 14, 15),
        };

    private sealed class FakeApi : IDeferredWindowPositionApi
    {
        private readonly IReadOnlyList<IntPtr> _returns;
        private int _deferIndex;

        public FakeApi(IReadOnlyList<IntPtr> returns)
        {
            _returns = returns;
        }

        public List<IntPtr> DeferInputs { get; } = new();
        public IntPtr EndInput { get; private set; }

        public IntPtr Begin(int windowCount) => new(0x11);

        public IntPtr Defer(IntPtr hdwp, IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
        {
            DeferInputs.Add(hdwp);
            return _returns[_deferIndex++];
        }

        public bool End(IntPtr hdwp)
        {
            EndInput = hdwp;
            return true;
        }
    }
}
