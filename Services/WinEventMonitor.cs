using System;
using System.Threading;
using System.Windows.Threading;

namespace TabDock.Services;

/// <summary>
/// Out-of-process SetWinEventHook wrapper. Marshals events to the UI thread
/// so the GroupManager can react to destroyed/renamed/minimized/foregrounded
/// captured windows.
/// </summary>
public sealed class WinEventMonitor : IDisposable
{
    private readonly LoggingService _log;
    private readonly Func<IntPtr, bool> _isCapturedWindow;
    private readonly NativeMethods.WinEventProc _callback;
    private SynchronizationContext? _uiContext;
    private IntPtr _hookDestroy;
    private IntPtr _hookForeground;
    private IntPtr _hookNameChange;
    private IntPtr _hookMinimize;
    private IntPtr _hookHide;
    private bool _disposed;

    public event EventHandler<WindowEventArgs>? WindowDestroyed;
    public event EventHandler<WindowEventArgs>? WindowForegroundChanged;
    public event EventHandler<WindowEventArgs>? WindowNameChanged;
    public event EventHandler<WindowEventArgs>? WindowMinimized;

    /// <summary>
    /// Raised when a captured window loses WS_VISIBLE. Note that
    /// WINEVENT_SKIPOWNPROCESS does NOT filter TabDock-initiated hides
    /// (show/hide events are raised in the context of the thread owning the
    /// window, i.e. the guest), so the subscriber must distinguish
    /// guest-initiated hides (tray-style close) from TabDock's own tab-switch
    /// and release hides.
    /// </summary>
    public event EventHandler<WindowEventArgs>? WindowHidden;

    public WinEventMonitor(Func<IntPtr, bool> isCapturedWindow, LoggingService log)
    {
        _isCapturedWindow = isCapturedWindow;
        _log = log;
        _callback = new NativeMethods.WinEventProc(OnWinEvent);
    }

    public void Start()
    {
        if (_hookDestroy != IntPtr.Zero)
            return;

        _uiContext = SynchronizationContext.Current;

        uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
        _hookDestroy = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY, IntPtr.Zero, _callback, 0, 0, flags);
        _hookForeground = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, flags);
        _hookNameChange = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_NAMECHANGE, NativeMethods.EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _callback, 0, 0, flags);
        _hookMinimize = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, _callback, 0, 0, flags);
        _hookHide = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_HIDE, NativeMethods.EVENT_OBJECT_HIDE, IntPtr.Zero, _callback, 0, 0, flags);

        _log.Log($"WinEventMonitor started (hooks: {_hookDestroy.ToInt64():X}, {_hookForeground.ToInt64():X}, {_hookNameChange.ToInt64():X}, {_hookMinimize.ToInt64():X}, {_hookHide.ToInt64():X})");
    }

    public void Stop()
    {
        if (_hookDestroy != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookDestroy);
            _hookDestroy = IntPtr.Zero;
        }
        if (_hookForeground != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookForeground);
            _hookForeground = IntPtr.Zero;
        }
        if (_hookNameChange != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookNameChange);
            _hookNameChange = IntPtr.Zero;
        }
        if (_hookMinimize != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookMinimize);
            _hookMinimize = IntPtr.Zero;
        }
        if (_hookHide != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookHide);
            _hookHide = IntPtr.Zero;
        }
        _log.Log("WinEventMonitor stopped.");
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 || idChild != 0)
            return;
        if (hwnd == IntPtr.Zero)
            return;

        // Every consumer of these events reacts only to captured member windows,
        // so filter by direct HWND match. Do NOT resolve GetAncestor(GA_ROOT) here:
        // a captured window is a WS_CHILD of the TabDock container (its root is our
        // own window, which the old filter rejected), and a window that just fired
        // EVENT_OBJECT_DESTROY has no ancestors to walk at all.
        if (!_isCapturedWindow(hwnd))
            return;

        var args = new WindowEventArgs(hwnd, eventType);
        if (_uiContext != null)
        {
            // The Post hop is load-bearing beyond thread affinity: the hide
            // handler relies on events being dispatched AFTER the UI operation
            // that caused them completed (e.g. a tab switch has already moved
            // the active tab by the time its SW_HIDE event is handled).
            // Do not replace this with a synchronous Send/direct call.
            _uiContext.Post(_ => Raise(args), null);
        }
        else
        {
            Raise(args);
        }
    }

    private void Raise(WindowEventArgs args)
    {
        switch (args.EventType)
        {
            case NativeMethods.EVENT_OBJECT_DESTROY:
                WindowDestroyed?.Invoke(this, args);
                break;
            case NativeMethods.EVENT_SYSTEM_FOREGROUND:
                WindowForegroundChanged?.Invoke(this, args);
                break;
            case NativeMethods.EVENT_OBJECT_NAMECHANGE:
                WindowNameChanged?.Invoke(this, args);
                break;
            case NativeMethods.EVENT_SYSTEM_MINIMIZESTART:
                WindowMinimized?.Invoke(this, args);
                break;
            case NativeMethods.EVENT_OBJECT_HIDE:
                WindowHidden?.Invoke(this, args);
                break;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }
}

public sealed class WindowEventArgs : EventArgs
{
    public IntPtr Hwnd { get; }
    public uint EventType { get; }

    public WindowEventArgs(IntPtr hwnd, uint eventType)
    {
        Hwnd = hwnd;
        EventType = eventType;
    }
}
