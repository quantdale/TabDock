using TabDock.Services;
using TabDock.Views;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former ContainerGeometrySelfTest (Wave 4): WM_GETMINMAXINFO
/// maximize bounds must come from the CONTAINING monitor's work area (negative
/// multi-monitor origins included), not the primary monitor.
/// </summary>
public class ContainerMaximizeBoundsTests
{
    [Fact]
    public void ApplyMonitorMaximizeBounds_UsesContainingMonitorWorkArea()
    {
        var monitor = new NativeMethods.MONITORINFO
        {
            rcMonitor = new NativeMethods.RECT { left = 1920, top = -300, right = 3840, bottom = 1140 },
            rcWork = new NativeMethods.RECT { left = 1920, top = -260, right = 3840, bottom = 1100 },
        };
        var minMax = new NativeMethods.MINMAXINFO
        {
            ptMaxPosition = new NativeMethods.POINT { x = -1, y = -1 },
            ptMaxSize = new NativeMethods.POINT { x = -1, y = -1 },
        };

        ContainerWindow.ApplyMonitorMaximizeBounds(monitor, ref minMax);

        Assert.Equal(0, minMax.ptMaxPosition.x);
        Assert.Equal(40, minMax.ptMaxPosition.y); // rcWork.top - rcMonitor.top taskbar clearance
        Assert.Equal(1920, minMax.ptMaxSize.x);
        Assert.Equal(1360, minMax.ptMaxSize.y);
    }
}
