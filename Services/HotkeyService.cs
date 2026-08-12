using System;
using System.Windows.Interop;

namespace TabDock.Services;

/// <summary>
/// Registers the capture hotkey and a diagnostic export hotkey and raises an
/// event when either is pressed. The diagnostic combination is deliberately
/// independent of container chrome so it remains usable when a header is
/// hidden or covered.
/// The hotkey is hosted on a dedicated message-only window owned by this service,
/// NOT on a UI window: a hotkey registered against the launcher's HWND dies
/// silently when the launcher closes (WM_HOTKEY posted to a destroyed window is
/// dropped), even though the app stays alive with containers open.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x7AAD;
    private const int DiagnosticHotkeyId = 0x7AAE;

    private readonly LoggingService _log;
    private HwndSource? _source;
    private HwndSourceHook? _hook;
    private bool _registered;
    private bool _diagnosticRegistered;

    public event EventHandler? HotkeyPressed;
    public event EventHandler? DiagnosticHotkeyPressed;

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
        try
        {
            _source = new HwndSource(parameters);
        }
        catch (Exception ex)
        {
            _log.LogException("Hotkey sink window creation failed; global hotkey unavailable.", ex);
            return;
        }

        // MOD_NOREPEAT: without it, holding the combination auto-repeats WM_HOTKEY
        // at the keyboard repeat rate. Every repeat is one more capture-picker
        // request, and they queue up behind the picker's own modal loop instead of
        // being coalesced.
        if (NativeMethods.RegisterHotKey(_source.Handle, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
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

        _diagnosticRegistered = NativeMethods.RegisterHotKey(_source.Handle, DiagnosticHotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_D);
        _log.Log(_diagnosticRegistered
            ? "Diagnostic hotkey Ctrl+Alt+Shift+D registered."
            : $"Diagnostic hotkey registration failed: {NativeMethods.FormatLastError()}");

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

        if (_diagnosticRegistered && _source != null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, DiagnosticHotkeyId);
            _diagnosticRegistered = false;
            _log.Log("Diagnostic hotkey unregistered.");
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
        else if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == DiagnosticHotkeyId)
        {
            handled = true;
            _log.Log("Diagnostic hotkey Ctrl+Alt+Shift+D pressed.");
            DiagnosticHotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Detach();
    }
}
