using System;
using System.Windows;
using System.Windows.Interop;

namespace TabDock.Services;

/// <summary>
/// Registers a global Ctrl+Alt+G hotkey and raises an event when it is pressed.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x7AAD;

    private readonly GroupManager _groups;
    private readonly LoggingService _log;
    private HwndSourceHook? _hook;
    private IntPtr _hwnd;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public HotkeyService(GroupManager groups, LoggingService log)
    {
        _groups = groups;
        _log = log;
    }

    public void Attach(Window window)
    {
        if (_registered)
            return;

        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (_hwnd == IntPtr.Zero)
            return;

        if (NativeMethods.RegisterHotKey(_hwnd, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
            NativeMethods.VK_G))
        {
            _registered = true;
            _log.Log("Global hotkey Ctrl+Alt+G registered.");
        }
        else
        {
            _log.Log($"RegisterHotKey failed: {NativeMethods.FormatLastError()}");
            return;
        }

        _hook = new HwndSourceHook(WndProcHook);
        HwndSource source = PresentationSource.FromVisual(window) as HwndSource
            ?? HwndSource.FromHwnd(_hwnd);
        source?.AddHook(_hook);
    }

    public void Detach()
    {
        if (_hook != null)
        {
            if (HwndSource.FromHwnd(_hwnd) is HwndSource src)
                src.RemoveHook(_hook);
            _hook = null;
        }

        if (_registered && _hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
            _log.Log("Global hotkey unregistered.");
        }
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _log.Log("Global hotkey Ctrl+Alt+G pressed.");
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Detach();
    }
}
