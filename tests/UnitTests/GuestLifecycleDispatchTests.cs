using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using TabDock.Views;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class GuestLifecycleDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedMoveSizeEnd_DoesNotEndAReplacementMembersNewGesture(bool replacementAlsoQueuesRepair)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            string root = Path.Combine(Path.GetTempPath(), "TabDock-lifecycle-dispatch-" + Guid.NewGuid().ToString("N"));
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "journal.json"));
            var manager = new GroupManager(shepherd, new PersistenceService(log, Path.Combine(root, "state.json")), log);
            var group = new Group();
            var oldMember = new CapturedWindow { Hwnd = new IntPtr(0x1234), OriginalTitle = "Old" };
            var newMember = new CapturedWindow { Hwnd = oldMember.Hwnd, OriginalTitle = "Replacement" };
            group.Members.Add(oldMember);
            manager.Groups.Add(group);
            var icons = new IconService(log, _ => null);
            var vm = new GroupViewModel(group, manager, icons, log);
            var container = new ContainerWindow(vm, manager, shepherd, log, icons, SharedTemplateLoader.Load());
            var containers = new Dictionary<Guid, ContainerWindow> { [group.Id] = container };
            var lifecycle = new GuestLifecycleService(manager, containers, log);
            var api = new HookApi();
            var context = new RecordingContext();
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            using var monitor = new WinEventMonitor(manager.GetCapturedWindow, log, api,
                () => new IntPtr(0xD), () => oldMember.Hwnd);
            try
            {
                Assert.False(container.IsVisible);
                lifecycle.Attach(monitor);
                Assert.True(monitor.Start());
                api.Raise(oldMember.Hwnd, NativeMethods.EVENT_SYSTEM_MOVESIZEEND);
                context.DispatchNext(); // monitor proof passes; lifecycle's second hop is now queued

                group.Members.Clear();
                group.Members.Add(newMember); // exact same numeric HWND, different captured generation
                container.NoteGuestMoveSize(newMember, started: true);
                Assert.True(IsMoveSizeActive(container));
                if (replacementAlsoQueuesRepair)
                {
                    api.Raise(newMember.Hwnd, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE);
                    context.DispatchNext(); // new generation must not inherit the old END flag
                }
                StaDispatcherFixture.Settle(dispatcher); // drain the original member's coalesced repair

                Assert.True(IsMoveSizeActive(container));
                api.Raise(newMember.Hwnd, NativeMethods.EVENT_SYSTEM_MOVESIZEEND);
                context.DispatchNext();
                StaDispatcherFixture.Settle(dispatcher);
                Assert.False(IsMoveSizeActive(container)); // current-generation END remains actionable
                Assert.False(container.IsVisible); // never show or mutate a desktop guest
            }
            finally
            {
                monitor.Dispose();
                SynchronizationContext.SetSynchronizationContext(previous);
                containers.Clear();
                group.Members.Clear();
                vm.Tabs.Clear();
                vm.Detach();
                container.Close();
                log.Dispose();
                Directory.Delete(root, recursive: true);
            }
        });
    }

    private static bool IsMoveSizeActive(ContainerWindow container)
        => (bool)typeof(ContainerWindow).GetField("_guestMoveSizeActive", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(container)!;

    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly Queue<Action> _pending = new();
        public override void Post(SendOrPostCallback callback, object? state) => _pending.Enqueue(() => callback(state));
        public void DispatchNext() => _pending.Dequeue()();
    }

    private sealed class HookApi : IWinEventHookApi
    {
        private NativeMethods.WinEventProc? _callback;
        private int _nextHook;
        public IntPtr Set(uint eventMin, uint eventMax, NativeMethods.WinEventProc callback, uint flags)
        {
            _callback = callback;
            return new IntPtr(++_nextHook);
        }
        public bool Unhook(IntPtr hook) => true;
        public void Raise(IntPtr hwnd, uint eventType) => _callback!.Invoke(IntPtr.Zero, eventType, hwnd, 0, 0, 0, 1);
    }
}
