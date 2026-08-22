using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former WinEventMonitorSelfTest (Wave 4): hook-install
/// failure unwinds every installed hook and fails closed, and the desktop
/// REORDER path drops uncaptured/stale dispatches while preserving the
/// callback/dispatch trace for captured members.
/// </summary>
public class WinEventMonitorTests
{
    [Fact]
    public void FailedHookInstall_UnwindsAllHooksAndFailsClosed()
    {
        string root = CreateRoot("TabDock-winevent-test-");
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            var api = new FakeApi(failOnHookInAttempt: 4);
            using var log = new LoggingService(root);
            using var monitor = new WinEventMonitor(_ => false, _ => null, log, api);

            bool started = monitor.Start();

            Assert.False(started);
            Assert.False(monitor.IsRunning);
            Assert.True(api.SetCount >= 7, $"expected bounded retry attempts, set={api.SetCount}");
            Assert.True(api.UnhookCount >= 3, "every installed hook must be unhooked on unwind");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            DeleteRoot(root);
        }
    }

    [Fact]
    public void DesktopReorder_DropsUncapturedAndRejectsStaleDispatch()
    {
        string root = CreateRoot("TabDock-winevent-reorder-test-");
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            var context = new RecordingContext();
            SynchronizationContext.SetSynchronizationContext(context);
            var api = new EventApi();
            IntPtr desktop = new(0xD); // test-only sentinel, not a native HWND
            IntPtr foreground = new(0x9999);
            var members = new Dictionary<IntPtr, CapturedWindow?>();
            var memberA = new CapturedWindow { Hwnd = foreground };
            var memberB = new CapturedWindow { Hwnd = foreground };

            using var log = new LoggingService(root);
            using var monitor = new WinEventMonitor(
                hwnd => members.TryGetValue(hwnd, out CapturedWindow? member) && member != null,
                hwnd => members.TryGetValue(hwnd, out CapturedWindow? member) ? member : null,
                log,
                api,
                () => desktop,
                () => foreground);

            int dispatched = 0;
            monitor.WindowZOrderChanged += (_, _) => dispatched++;
            Assert.True(monitor.Start());

            int callbackTraceCount = CountReorderTrace("callback", foreground);

            // Uncaptured: nothing is posted or traced.
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            bool uncapturedDropped = context.PostCount == 0
                && CountReorderTrace("callback", foreground) == callbackTraceCount;
            Assert.True(uncapturedDropped, "an uncaptured desktop reorder must produce no dispatch");

            // Captured: one post, one dispatch.
            members[foreground] = memberA;
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            Assert.Equal(1, context.PostCount);
            context.DispatchNext();
            Assert.Equal(1, dispatched);

            // Release the captured object before the queued UI hop.
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            members.Remove(foreground);
            context.DispatchNext();
            Assert.Equal(1, dispatched);

            // Recycle the same numeric HWND to a different CapturedWindow.
            members[foreground] = memberA;
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            members[foreground] = memberB;
            context.DispatchNext();
            Assert.Equal(1, dispatched);

            // The original object still resolves and dispatches normally.
            members[foreground] = memberA;
            api.Raise(desktop, NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.OBJID_CLIENT, NativeMethods.CHILDID_SELF);
            context.DispatchNext();

            bool relevantTracePreserved = CountReorderTrace("callback", foreground) >= callbackTraceCount + 3
                && CountReorderTrace("dispatch", foreground) >= 2;
            Assert.True(relevantTracePreserved, "callback/dispatch trace for the captured member must be retained");
            Assert.Equal(2, dispatched);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            DeleteRoot(root);
        }
    }

    private static string CreateRoot(string prefix)
    {
        string root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch { }
    }

    private static int CountReorderTrace(string phase, IntPtr hwnd)
        => DiagnosticRuntime.Trace.Snapshot().Count(eventRecord =>
            eventRecord.Kind == "EVENT_OBJECT_REORDER." + phase
            && eventRecord.GuestHwnd == hwnd.ToInt64());

    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly Queue<Action> _pending = new();

        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            PostCount++;
            _pending.Enqueue(() => callback(state));
        }

        public void DispatchNext()
        {
            if (_pending.Count == 0)
                throw new InvalidOperationException("Expected a posted WinEvent dispatch.");
            _pending.Dequeue()();
        }
    }

    private sealed class EventApi : IWinEventHookApi
    {
        private NativeMethods.WinEventProc? _callback;
        private int _nextHook;

        public IntPtr Set(uint eventMin, uint eventMax, NativeMethods.WinEventProc callback, uint flags)
        {
            _callback = callback;
            return new IntPtr(++_nextHook);
        }

        public bool Unhook(IntPtr hook) => true;

        public void Raise(IntPtr hwnd, uint eventType, int idObject, int idChild)
            => _callback?.Invoke(IntPtr.Zero, eventType, hwnd, idObject, idChild, 0, 1);
    }

    private sealed class FakeApi : IWinEventHookApi
    {
        private readonly int _failOnHookInAttempt;
        private int _hookInAttempt;

        public FakeApi(int failOnHookInAttempt)
        {
            _failOnHookInAttempt = failOnHookInAttempt;
        }

        public int SetCount { get; private set; }
        public int UnhookCount { get; private set; }

        public IntPtr Set(uint eventMin, uint eventMax, NativeMethods.WinEventProc callback, uint flags)
        {
            SetCount++;
            _hookInAttempt++;
            if (_hookInAttempt == 7)
                _hookInAttempt = 0;
            return _hookInAttempt == _failOnHookInAttempt
                ? IntPtr.Zero
                : new IntPtr(SetCount);
        }

        public bool Unhook(IntPtr hook)
        {
            UnhookCount++;
            return true;
        }
    }
}




