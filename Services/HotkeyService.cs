using System;
using System.Windows.Interop;

namespace TabDock.Services;

/// <summary>
/// Registers a global Ctrl+Alt+G hotkey and raises an event when it is pressed.
/// The hotkey is hosted on a dedicated message-only window owned by this service,
/// NOT on a UI window: a hotkey registered against the launcher's HWND dies
/// silently when the launcher closes (WM_HOTKEY posted to a destroyed window is
/// dropped), even though the app stays alive with containers open.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x7AAD;

    private readonly LoggingService _log;
    private HwndSource? _source;
    private HwndSourceHook? _hook;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public HotkeyService(LoggingService log)
    {
        _log = log;
    }

    /// <summary>
    /// Creates the message-only hotkey sink and registers the global hotkey.
    /// Must be called on the UI thread (the hook delivers on the creating thread).
    /// </summary>
    public void Register()
    {
        if (_registered)
            return;

        var parameters = new HwndSourceParameters("TabDockHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = NativeMethods.HWND_MESSAGE,
        };
        _source = new HwndSource(parameters);
        if (_source.Handle == IntPtr.Zero)
        {
            _log.Log("Hotkey sink window could not be created; global hotkey unavailable.");
            return;
        }

        if (NativeMethods.RegisterHotKey(_source.Handle, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
            NativeMethods.VK_G))
        {
            _registered = true;
            _log.Log("Global hotkey Ctrl+Alt+G registered.");
        }
        else
        {
            _log.Log($"RegisterHotKey failed: {NativeMethods.FormatLastError()}");
            _source.Dispose();
            _source = null;
            return;
        }

        _hook = new HwndSourceHook(WndProcHook);
        _source.AddHook(_hook);
    }

    public void Detach()
    {
        if (_source != null && _hook != null)
        {
            _source.RemoveHook(_hook);
            _hook = null;
        }

        if (_registered && _source != null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
            _log.Log("Global hotkey unregistered.");
        }

        _source?.Dispose();
        _source = null;
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
