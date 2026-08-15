using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Out-of-process SetWinEventHook wrapper. Marshals events to the UI thread
/// so GuestLifecycleService can react to destroyed/renamed/minimized/
/// foregrounded captured windows.
/// </summary>
public sealed class WinEventMonitor : IDisposable
{
    private const int MaxInstallAttempts = 3;
    private readonly LoggingService _log;
    private readonly IWinEventHookApi _api;
    private readonly Func<IntPtr, bool> _isCapturedWindow;
    private readonly Func<IntPtr, CapturedWindow?> _resolveCapturedWindow;
    private readonly Func<IntPtr> _getDesktopWindow;
    private readonly Func<IntPtr> _getForegroundWindow;
    private readonly NativeMethods.WinEventProc _callback;
    private SynchronizationContext? _uiContext;
    private IntPtr _hookDestroy;
    private IntPtr _hookForeground;
    private IntPtr _hookReorder;
    private IntPtr _hookNameChange;
    private IntPtr _hookMinimize;
    private IntPtr _hookHide;
    private IntPtr _hookMoveSize;
    private bool _running;
    private bool _disposed;

    public event EventHandler<WindowEventArgs>? WindowDestroyed;
    public event EventHandler<WindowEventArgs>? WindowForegroundChanged;
    public event EventHandler<WindowEventArgs>? WindowZOrderChanged;
    public event EventHandler<WindowEventArgs>? WindowNameChanged;
    public event EventHandler<WindowEventArgs>? WindowMinimized;

    /// <summary>
    /// Raised when a captured window enters its interactive move/size modal
    /// loop (e.g. the user drags a guest by its client-drawn caption — Chrome
    /// hit-tests its tab strip as HTCAPTION, and DefWindowProc's SC_MOVE loop
    /// works for an independent top-level guest too).
    /// </summary>
    public event EventHandler<WindowEventArgs>? WindowMoveSizeStarted;

    /// <summary>
    /// Raised when a captured window leaves its interactive move/size modal
    /// loop; the subscriber re-clamps the guest to fill the content host.
    /// </summary>
    public event EventHandler<WindowEventArgs>? WindowMoveSizeEnded;

    /// <summary>
    /// Raised when a captured window loses WS_VISIBLE. Note that
    /// WINEVENT_SKIPOWNPROCESS does NOT filter TabDock-initiated hides
    /// (show/hide events are raised in the context of the thread owning the
    /// window, i.e. the guest), so the subscriber must distinguish
    /// guest-initiated hides (tray-style close) from TabDock's own tab-switch
    /// and release hides.
    /// </summary>
    public event EventHandler<WindowEventArgs>? WindowHidden;

    /// <summary>True only when the complete hook set is installed and dispatching.</summary>
    public bool IsRunning => _running && HasAllHooks;

    public WinEventMonitor(
        Func<IntPtr, bool> isCapturedWindow,
        Func<IntPtr, CapturedWindow?> resolveCapturedWindow,
        LoggingService log)
        : this(isCapturedWindow, resolveCapturedWindow, log, api: null)
    {
    }

    internal WinEventMonitor(
        Func<IntPtr, bool> isCapturedWindow,
        Func<IntPtr, CapturedWindow?> resolveCapturedWindow,
        LoggingService log,
        IWinEventHookApi? api,
        Func<IntPtr>? getDesktopWindow = null,
        Func<IntPtr>? getForegroundWindow = null)
    {
        _isCapturedWindow = isCapturedWindow;
        _resolveCapturedWindow = resolveCapturedWindow;
        _log = log;
        _api = api ?? NativeWinEventHookApi.Instance;
        _getDesktopWindow = getDesktopWindow ?? NativeMethods.GetDesktopWindow;
        _getForegroundWindow = getForegroundWindow ?? NativeMethods.GetForegroundWindow;
        _callback = new NativeMethods.WinEventProc(OnWinEvent);
    }

    public string? LastStartFailure { get; private set; }

    /// <summary>
    /// Installs the hooks. Idempotent, and safe to call again after
    /// <see cref="Stop"/> — App starts and stops the monitor as captured
    /// windows come and go, so that an idle TabDock is not paying to have every
    /// menu, tooltip and title change on the desktop marshalled into its
    /// message loop only to be filtered out (PERF25-03).
    /// </summary>
    public bool Start()
    {
        if (_running || _disposed)
            return IsRunning;

        SynchronizationContext? uiContext = SynchronizationContext.Current;
        if (uiContext == null)
        {
            // Without a UI-thread SynchronizationContext, OnWinEvent's only
            // remaining path is to Raise on the WinEvent callback thread,
            // which breaks UI-thread affinity for every subscriber. Refuse to
            // start instead of silently degrading.
            _log.Log("WinEventMonitor.Start: no SynchronizationContext on the calling thread; hooks not installed.");
            LastStartFailure = "no-ui-synchronization-context";
            return false;
        }

        // Hook installation is a small native transaction. A transient partial
        // failure is retried a bounded number of times, but a failed unhook is
        // never overwritten with a new handle: retaining that handle is the
        // only way to retry cleanup safely.
        for (int attempt = 1; attempt <= MaxInstallAttempts; attempt++)
        {
            if (HasInstalledHooks)
            {
                Stop();
                if (HasInstalledHooks)
                {
                    _log.Log("WinEventMonitor.Start: residual hook could not be removed; refusing to overwrite its handle.");
                    LastStartFailure = "residual-hook-could-not-be-removed";
                    return false;
                }
            }

            _uiContext = uiContext;
            _running = true;
            uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
            _hookDestroy = _api.Set(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY, _callback, flags);
            _hookForeground = _api.Set(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, _callback, flags);
            _hookReorder = _api.Set(NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.EVENT_OBJECT_REORDER, _callback, flags);
            _hookNameChange = _api.Set(NativeMethods.EVENT_OBJECT_NAMECHANGE, NativeMethods.EVENT_OBJECT_NAMECHANGE, _callback, flags);
            _hookMinimize = _api.Set(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZESTART, _callback, flags);
            _hookHide = _api.Set(NativeMethods.EVENT_OBJECT_HIDE, NativeMethods.EVENT_OBJECT_HIDE, _callback, flags);
            // One ranged hook covers MOVESIZESTART (0x000A) and MOVESIZEEND (0x000B).
            // These fire once per interactive drag start/end system-wide — low volume,
            // unlike EVENT_OBJECT_LOCATIONCHANGE, which is deliberately not hooked.
            _hookMoveSize = _api.Set(NativeMethods.EVENT_SYSTEM_MOVESIZESTART, NativeMethods.EVENT_SYSTEM_MOVESIZEEND, _callback, flags);

            if (HasAllHooks)
            {
                _log.Log($"WinEventMonitor started (hooks: {_hookDestroy.ToInt64():X}, {_hookForeground.ToInt64():X}, {_hookReorder.ToInt64():X}, {_hookNameChange.ToInt64():X}, {_hookMinimize.ToInt64():X}, {_hookHide.ToInt64():X}, {_hookMoveSize.ToInt64():X})");
                LastStartFailure = null;
                return true;
            }

            // A partial hook set silently drops whole event classes (e.g. no
            // destroy hook means dead tabs never tear down). Unwind whatever
            // did install and retry only if every handle was released.
            _log.Log($"WinEventMonitor.Start attempt {attempt}/{MaxInstallAttempts}: incomplete hook installation (hooks: {_hookDestroy.ToInt64():X}, {_hookForeground.ToInt64():X}, {_hookReorder.ToInt64():X}, {_hookNameChange.ToInt64():X}, {_hookMinimize.ToInt64():X}, {_hookHide.ToInt64():X}, {_hookMoveSize.ToInt64():X}); unwinding.");
            Stop();
            if (HasInstalledHooks)
            {
                _log.Log("WinEventMonitor.Start: incomplete installation left a hook that could not be removed; retry stopped.");
                LastStartFailure = "partial-install-unhook-failed";
                return false;
            }
        }

        LastStartFailure = "hook-installation-failed";
        _log.Log($"WinEventMonitor.Start: hook installation failed after {MaxInstallAttempts} bounded attempts; monitoring is unhealthy.");
        return false;
    }

    /// <summary>
    /// Removes the hooks. Idempotent. Must run on the thread that installed
    /// them (the UI thread) — which every caller does, since the presence
    /// notification that drives it is raised from the UI-thread-only group
    /// collections.
    /// </summary>
    public void Stop()
    {
        if (!_running && !HasInstalledHooks)
            return;
        _running = false;
        // Drop the dispatch target so an in-flight native callback after this
        // point cannot post onto a context we no longer own; Raise's _running
        // guard discards anything already posted.
        _uiContext = null;

        Unhook(ref _hookDestroy, "destroy");
        Unhook(ref _hookForeground, "foreground");
        Unhook(ref _hookReorder, "reorder");
        Unhook(ref _hookNameChange, "namechange");
        Unhook(ref _hookMinimize, "minimize");
        Unhook(ref _hookHide, "hide");
        Unhook(ref _hookMoveSize, "movesize");
        _log.Log("WinEventMonitor stopped.");
    }

    private bool HasInstalledHooks => _hookDestroy != IntPtr.Zero || _hookForeground != IntPtr.Zero
        || _hookReorder != IntPtr.Zero || _hookNameChange != IntPtr.Zero
        || _hookMinimize != IntPtr.Zero || _hookHide != IntPtr.Zero
        || _hookMoveSize != IntPtr.Zero;

    private bool HasAllHooks => _hookDestroy != IntPtr.Zero && _hookForeground != IntPtr.Zero
        && _hookReorder != IntPtr.Zero && _hookNameChange != IntPtr.Zero
        && _hookMinimize != IntPtr.Zero && _hookHide != IntPtr.Zero
        && _hookMoveSize != IntPtr.Zero;

    /// <summary>
    /// Unhooks one WinEvent hook, zeroing the field only on success — a failed
    /// UnhookWinEvent leaves the hook installed, and zeroing anyway would leak
    /// it with no handle left to retry.
    /// </summary>
    private void Unhook(ref IntPtr hook, string name)
    {
        if (hook == IntPtr.Zero)
            return;
        if (_api.Unhook(hook))
            hook = IntPtr.Zero;
        else
            _log.Log($"WinEventMonitor: UnhookWinEvent({name}, 0x{hook.ToInt64():X}) failed: {NativeMethods.FormatLastError()}");
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero)
            return;

        bool traceEvent = IsDiagnosticEvent(eventType);

        // EVENT_SYSTEM_FOREGROUND identifies the foreground top-level window.
        // EVENT_OBJECT_REORDER is different: for a top-level z-order change,
        // Windows reports the desktop's client object (OBJID_CLIENT), not the
        // guest HWND. This is the bounded signal needed when direct activation
        // raises a guest but the foreground notification is coalesced or late.
        bool desktopReorder = eventType == NativeMethods.EVENT_OBJECT_REORDER
            && hwnd == _getDesktopWindow()
            && idObject == NativeMethods.OBJID_CLIENT
            && idChild == NativeMethods.CHILDID_SELF;
        if (desktopReorder)
        {
            // The desktop is the event source, not the reordered top-level
            // window. Capture the foreground at callback time so the posted
            // UI handler cannot accidentally pair a different window if a
            // later activation is queued before this event is dispatched.
            IntPtr foreground = _getForegroundWindow();
            CapturedWindow? desktopCapturedMember = _resolveCapturedWindow(foreground);
            // The desktop-wide reorder hook is intentionally retained because
            // it is the earliest reliable z-order signal, but the desktop is
            // also the event source for every unrelated reorder. Resolve the
            // callback-time foreground before doing any diagnostic allocation
            // or dispatcher work. A null result is the complete fail-closed
            // membership proof for the production GroupManager index.
            if (desktopCapturedMember == null)
                return;

            if (traceEvent)
            {
                DiagnosticRuntime.Record($"{EventName(eventType)}.callback", guest: foreground, foreground: foreground,
                    action: "observe", data: new Dictionary<string, string>
                    {
                        ["source"] = "desktop-reorder",
                        ["eventTime"] = dwmsEventTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    });
            }
            Post(new WindowEventArgs(hwnd, eventType, foreground, desktopCapturedMember, eventTime: dwmsEventTime));
            return;
        }

        if (idObject != 0 || idChild != 0)
            return;

        // Every consumer of these events reacts only to captured member windows,
        // so filter by direct HWND match. Do NOT resolve GetAncestor(GA_ROOT) here:
        // under the Shepherd model a captured window is never reparented, so it is
        // its own root already (GetAncestor would just return the window itself,
        // never a TabDock container, making it useless as an ownership filter) —
        // and a window that just fired EVENT_OBJECT_DESTROY has no ancestors to
        // walk at all regardless.
        CapturedWindow? capturedMember = _resolveCapturedWindow(hwnd);
        if (!_isCapturedWindow(hwnd) || capturedMember == null)
            return;

        if (traceEvent)
        {
            DiagnosticRuntime.Record($"{EventName(eventType)}.callback", guest: hwnd,
                foreground: _getForegroundWindow(), action: "observe",
                data: new Dictionary<string, string>
                {
                    ["eventTime"] = dwmsEventTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
        }

        bool? visibleAtCallback = eventType == NativeMethods.EVENT_OBJECT_HIDE
            ? NativeMethods.IsWindowVisible(hwnd)
            : null;
        Post(new WindowEventArgs(
            hwnd,
            eventType,
            capturedMember: capturedMember,
            visibleAtCallback: visibleAtCallback,
            eventTime: dwmsEventTime));
    }

    private void Post(WindowEventArgs args)
    {
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
        bool traceEvent = IsDiagnosticEvent(args.EventType);
        IntPtr traceGuest = args.EventType == NativeMethods.EVENT_OBJECT_REORDER ? args.RelatedHwnd : args.Hwnd;
        if (traceEvent)
        {
            DiagnosticRuntime.Record($"{EventName(args.EventType)}.dispatch", guest: traceGuest,
                foreground: _getForegroundWindow(), action: "dispatch");
        }

        // A posted dispatch can outlive Stop() (its guard drops it here), and
        // the guest may have been released between the native event and this
        // hop — Windows aggressively recycles HWND values, so re-verify the
        // HWND still names a captured window instead of acting on a stale
        // snapshot of desktop state.
        if (!_running)
            return;

        bool desktopReorder = args.EventType == NativeMethods.EVENT_OBJECT_REORDER
            && args.Hwnd == _getDesktopWindow();
        if (desktopReorder)
        {
            // The foreground snapshot may have changed before dispatch. If
            // it was a captured member at callback time, require the same
            // object to still own that HWND; otherwise a recycled handle must
            // not receive the old reorder event.
            if (args.RelatedHwnd == IntPtr.Zero
                || args.CapturedMember == null
                || !ReferenceEquals(_resolveCapturedWindow(args.RelatedHwnd), args.CapturedMember))
                return;
        }
        else if (!_isCapturedWindow(args.Hwnd)
            || !ReferenceEquals(_resolveCapturedWindow(args.Hwnd), args.CapturedMember))
        {
            return;
        }

        switch (args.EventType)
        {
            case NativeMethods.EVENT_OBJECT_DESTROY:
                WindowDestroyed?.Invoke(this, args);
                break;
            case NativeMethods.EVENT_SYSTEM_FOREGROUND:
                WindowForegroundChanged?.Invoke(this, args);
                break;
            case NativeMethods.EVENT_OBJECT_REORDER:
                WindowZOrderChanged?.Invoke(this, args);
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
            case NativeMethods.EVENT_SYSTEM_MOVESIZESTART:
                WindowMoveSizeStarted?.Invoke(this, args);
                break;
            case NativeMethods.EVENT_SYSTEM_MOVESIZEEND:
                WindowMoveSizeEnded?.Invoke(this, args);
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

    private static bool IsDiagnosticEvent(uint eventType)
        => eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND
            || eventType == NativeMethods.EVENT_OBJECT_REORDER
            || eventType == NativeMethods.EVENT_SYSTEM_MOVESIZESTART
            || eventType == NativeMethods.EVENT_SYSTEM_MOVESIZEEND
            || eventType == NativeMethods.EVENT_SYSTEM_MINIMIZESTART
            || eventType == NativeMethods.EVENT_OBJECT_DESTROY
            || eventType == NativeMethods.EVENT_OBJECT_HIDE;

    private static string EventName(uint eventType)
        => eventType switch
        {
            NativeMethods.EVENT_SYSTEM_FOREGROUND => "EVENT_SYSTEM_FOREGROUND",
            NativeMethods.EVENT_OBJECT_REORDER => "EVENT_OBJECT_REORDER",
            NativeMethods.EVENT_SYSTEM_MOVESIZESTART => "EVENT_SYSTEM_MOVESIZESTART",
            NativeMethods.EVENT_SYSTEM_MOVESIZEEND => "EVENT_SYSTEM_MOVESIZEEND",
            NativeMethods.EVENT_SYSTEM_MINIMIZESTART => "EVENT_SYSTEM_MINIMIZESTART",
            NativeMethods.EVENT_OBJECT_DESTROY => "EVENT_OBJECT_DESTROY",
            NativeMethods.EVENT_OBJECT_HIDE => "EVENT_OBJECT_HIDE",
            _ => $"EVENT_0x{eventType:X}",
        };
}

/// <summary>Small native seam for deterministic hook-installation failure tests.</summary>
internal interface IWinEventHookApi
{
    IntPtr Set(uint eventMin, uint eventMax, NativeMethods.WinEventProc callback, uint flags);
    bool Unhook(IntPtr hook);
}

internal sealed class NativeWinEventHookApi : IWinEventHookApi
{
    public static NativeWinEventHookApi Instance { get; } = new();

    private NativeWinEventHookApi() { }

    public IntPtr Set(uint eventMin, uint eventMax, NativeMethods.WinEventProc callback, uint flags)
        => NativeMethods.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, callback, 0, 0, flags);

    public bool Unhook(IntPtr hook) => NativeMethods.UnhookWinEvent(hook);
}

public sealed class WindowEventArgs : EventArgs
{
    public IntPtr Hwnd { get; }
    public uint EventType { get; }
    public IntPtr RelatedHwnd { get; }
    public CapturedWindow? CapturedMember { get; }
    public bool? VisibleAtCallback { get; }
    /// <summary>
    /// Native WinEvent callback timestamp (the USER32 dwmsEventTime value).
    /// The hide lifecycle uses it to distinguish a queued TabDock-issued hide
    /// from a later guest-issued hide after a container restore.
    /// </summary>
    public uint EventTime { get; }

    public WindowEventArgs(
        IntPtr hwnd,
        uint eventType,
        IntPtr relatedHwnd = default,
        CapturedWindow? capturedMember = null,
        bool? visibleAtCallback = null,
        uint eventTime = 0)
    {
        Hwnd = hwnd;
        EventType = eventType;
        RelatedHwnd = relatedHwnd;
        CapturedMember = capturedMember;
        VisibleAtCallback = visibleAtCallback;
        EventTime = eventTime;
    }
}
