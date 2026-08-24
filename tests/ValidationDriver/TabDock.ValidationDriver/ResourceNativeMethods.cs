using System;
using System.Runtime.InteropServices;

namespace TabDock.ValidationDriver;

/// <summary>
/// Driver-only resource interop. These declarations intentionally do not enter
/// production NativeMethods: resource qualification observes a target process
/// and never becomes a second TabDock mutation authority.
/// </summary>
internal static class ResourceNativeMethods
{
    internal const uint ProcessQueryInformation = 0x0400;
    internal const uint ProcessVmRead = 0x0010;
    internal const uint GuiUserObjects = 1;
    internal const uint GuiGdiObjects = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessHandleCount(IntPtr processHandle, out uint handleCount);

    [DllImport("user32.dll")]
    internal static extern uint GetGuiResources(IntPtr processHandle, uint flags);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessMemoryInfo(
        IntPtr processHandle,
        out ProcessMemoryCounters counters,
        uint size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessMemoryCounters
    {
        internal uint Cb;
        internal uint PageFaultCount;
        internal UIntPtr PeakWorkingSetSize;
        internal UIntPtr WorkingSetSize;
        internal UIntPtr QuotaPeakPagedPoolUsage;
        internal UIntPtr QuotaPagedPoolUsage;
        internal UIntPtr QuotaPeakNonPagedPoolUsage;
        internal UIntPtr QuotaNonPagedPoolUsage;
        internal UIntPtr PagefileUsage;
        internal UIntPtr PeakPagefileUsage;
    }
}
