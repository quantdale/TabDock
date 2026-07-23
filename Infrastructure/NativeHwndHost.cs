using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TabDock.Infrastructure;

/// <summary>
/// A plain native child window that marks exactly where the container's content
/// area sits on screen — nothing is ever reparented into it. It exists so that
/// "where is the docked position" has a single, externally-discoverable answer
/// (a real HWND with a known class name, findable via EnumChildWindows) that
/// both production code (<see cref="TabDock.Views.ContainerWindow.GetContentAreaScreenRect"/>)
/// and the real-input test harness can query identically via GetClientRect +
/// ClientToScreen, in physical pixels, with no DPI math needed on either side.
///
/// This used to be a ~500-line cross-thread input-attachment state machine for
/// the "Reparent" capture backend (SetParent a guest into this host, join its
/// input queue via AttachThreadInput, forward synthetic WM_ACTIVATE). That
/// backend and everything it required was deleted:
/// docs/internal/deep-audit-2026-07-17.md traces the recurring keyboard-input
/// bugs directly to cross-process SetParent + AttachThreadInput, and TabDock
/// now exclusively uses the "Shepherd" model (Services/WindowShepherdService.cs)
/// — a captured guest stays a completely unmodified top-level window; Windows
/// manages its focus and activation exactly as if TabDock didn't exist.
/// </summary>
public class NativeHwndHost : HwndHost
{
    private const string WindowClass = "TabDockContentHost";
    private static readonly NativeMethods.WndProc s_wndProc = new(NativeMethods.DefWindowProc);
    private static bool s_classRegistered;

    private IntPtr _hwnd;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        if (!s_classRegistered)
        {
            IntPtr hInstance = NativeMethods.GetModuleHandle(null);
            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                hInstance = hInstance,
                hCursor = NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW),
                // A brush handed to RegisterClassEx is owned by the system for the
                // class's lifetime — it must never be DeleteObject'd. Matches the
                // WPF ContentBorder background #1E1E1E so there is no visible seam.
                hbrBackground = NativeMethods.CreateSolidBrush(0x001E1E1E),
                lpszClassName = WindowClass,
            };

            if (NativeMethods.RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() != 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed: {NativeMethods.FormatLastError()}");
            }
            s_classRegistered = true;
        }

        _hwnd = NativeMethods.CreateWindowEx(
            0,
            WindowClass,
            "TabDock Content Host",
            NativeMethods.WS_CHILD | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS | NativeMethods.WS_VISIBLE,
            0,
            0,
            100,
            100,
            hwndParent.Handle,
            IntPtr.Zero,
            NativeMethods.GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {NativeMethods.FormatLastError()}");

        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        NativeMethods.DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    public IntPtr HostWindowHandle => _hwnd;

    /// <summary>
    /// Sizes the host HWND in physical pixels so it matches the WPF element bounds
    /// on high-DPI monitors — this is the only thing the marker needs to keep in
    /// sync; there is no guest content to re-layout alongside it.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size result = base.ArrangeOverride(finalSize);
        if (_hwnd != IntPtr.Zero)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int width = (int)Math.Max(1, Math.Round(finalSize.Width * dpi.DpiScaleX));
            int height = (int)Math.Max(1, Math.Round(finalSize.Height * dpi.DpiScaleY));

            NativeMethods.GetClientRect(_hwnd, out NativeMethods.RECT rc);
            if (rc.Width != width || rc.Height != height)
            {
                NativeMethods.SetWindowPos(
                    _hwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    width,
                    height,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
            }
        }
        return result;
    }
}
