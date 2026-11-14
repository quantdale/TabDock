using System;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

public sealed class ZOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChurningWindowList_IsBoundedRatherThanHangingTheDispatcher(bool cycle)
    {
        int probes = 0;
        IntPtr Next(IntPtr current)
        {
            if (++probes > 4096)
                throw new InvalidOperationException("z-order traversal exceeded the native-probe budget");
            return cycle
                ? new IntPtr(current.ToInt64() == 1 ? 2 : 1)
                : new IntPtr(current.ToInt64() + 1);
        }

        Assert.False(ZOrder.IsOrderedAbove(new IntPtr(1), new IntPtr(-1), Next));
        Assert.InRange(probes, 1, 4096);
    }

    [Fact]
    public void StableList_StillFindsTheTargetThroughManyIntermediateWindows()
    {
        Assert.True(ZOrder.IsOrderedAbove(new IntPtr(1), new IntPtr(2000),
            current => new IntPtr(current.ToInt64() + 1)));
    }
}
