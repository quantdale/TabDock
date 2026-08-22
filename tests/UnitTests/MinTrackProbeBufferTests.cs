using System;
using System.Runtime.InteropServices;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former MinTrackProbeSelfTest (Wave 4): the synthetic
/// WM_GETMINMAXINFO probe buffer must be fully initialized — poisoned
/// allocation storage proves the helper writes every field rather than relying
/// on allocator zeroing. A real USER32 message supplies initialized MINMAXINFO
/// storage; the manual probe buffer must match that contract exactly.
/// </summary>
public class MinTrackProbeBufferTests
{
    [Fact]
    public void InitializeMinTrackProbeBuffer_WritesEveryFieldOfAPoisonedBuffer()
    {
        int size = Marshal.SizeOf<NativeMethods.MINMAXINFO>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < size; i++)
                Marshal.WriteByte(buffer, i, 0xA5);

            WindowShepherdService.InitializeMinTrackProbeBuffer(buffer);

            NativeMethods.MINMAXINFO value = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(buffer);
            Assert.Equal(0, value.ptReserved.x);
            Assert.Equal(0, value.ptReserved.y);
            Assert.Equal(0, value.ptMaxSize.x);
            Assert.Equal(0, value.ptMaxSize.y);
            Assert.Equal(0, value.ptMaxPosition.x);
            Assert.Equal(0, value.ptMaxPosition.y);
            Assert.Equal(0, value.ptMinTrackSize.x);
            Assert.Equal(0, value.ptMinTrackSize.y);
            Assert.Equal(0, value.ptMaxTrackSize.x);
            Assert.Equal(0, value.ptMaxTrackSize.y);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void MinTrackProbeTimeout_StaysBounded()
    {
        Assert.True(
            WindowShepherdService.MinTrackProbeTimeoutMilliseconds <= 100,
            $"min-track SendMessageTimeout must stay bounded (was {WindowShepherdService.MinTrackProbeTimeoutMilliseconds}ms)");
    }
}
