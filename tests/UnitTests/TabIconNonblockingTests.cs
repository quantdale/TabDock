using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class TabIconNonblockingTests
{
    [Fact]
    public async Task SharedPickerProducer_DoesNotBlockMembershipOrExistingGroup_AndDeliversOnDispatcher()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        using var membershipReturned = new ManualResetEventSlim();
        Task sta = Task.Run(() => StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            int producers = 0;
            int iconNotifications = 0;
            bool wrongThread = false;
            var icons = new IconService(fixture.Log, _ =>
            {
                Interlocked.Increment(ref producers);
                entered.Set();
                release.Wait(); // only the external controller can open this gate
                return Image();
            });
            var candidate = new CapturePickerViewModel.WindowInfo(new IntPtr(1), 1, "fixture", "one", "shared.exe");
            using var picker = new CapturePickerViewModel(fixture.Manager, icons, fixture.Log,
                () => new[] { candidate }, dispatcher);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var group = new Group();
            group.Members.Add(Member("SHARED.exe"));
            var vm = new GroupViewModel(group, fixture.Manager, icons, fixture.Log);
            try
            {
                vm.AddCapturedWindow(Member("shared.exe"));
                Assert.Equal(2, vm.Tabs.Count);
                Assert.All(vm.Tabs, tab => Assert.Null(tab.Icon));
                foreach (var tab in vm.Tabs)
                    tab.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName != nameof(TabViewModel.Icon)) return;
                        wrongThread |= !dispatcher.CheckAccess();
                        iconNotifications++;
                    };
                picker.Windows[0].PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName != nameof(CapturePickerViewModel.WindowInfo.Icon)) return;
                    wrongThread |= !dispatcher.CheckAccess();
                    iconNotifications++;
                };
                membershipReturned.Set();
                PumpUntil(dispatcher, () => vm.IconResolutionCompletion.IsCompleted && picker.IconResolutionCompletion.IsCompleted);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.False(wrongThread);
                Assert.Equal(3, iconNotifications);
                Assert.Equal(1, producers);
                Assert.All(vm.Tabs, tab => Assert.True(tab.Icon!.IsFrozen));
                Assert.Same(vm.Tabs[0].Icon, vm.Tabs[1].Icon);
                Assert.Same(vm.Tabs[0].Icon, picker.Windows[0].Icon);
            }
            finally { vm.Detach(); }
        }));
        bool returned;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            returned = membershipReturned.Wait(TimeSpan.FromSeconds(1));
        }
        finally { release.Set(); }
        await sta;
        Assert.True(returned, "membership did not return while the native producer was held");
    }

    [Fact]
    public async Task ProducersQueueAndSingleFlight_AreBounded_CancellationAndRejectionDoNotPoisonCache()
    {
        using var fixture = new Fixture();
        using var release = new ManualResetEventSlim();
        using var entered = new CountdownEvent(2);
        int active = 0;
        int peak = 0;
        int calls = 0;
        var icons = new IconService(fixture.Log, _ =>
        {
            Interlocked.Increment(ref calls);
            int current = Interlocked.Increment(ref active);
            Interlocked.Exchange(ref peak, Math.Max(peak, current));
            if (current <= 2 && !entered.IsSet) entered.Signal();
            release.Wait(TimeSpan.FromSeconds(10));
            Interlocked.Decrement(ref active);
            return Image();
        });
        Task<ImageSource?> first = icons.GetFileIconAsync("first.exe");
        Task<ImageSource?> second = icons.GetFileIconAsync("second.exe");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        using var cancelled = new CancellationTokenSource();
        Task<ImageSource?> abandoned = icons.GetFileIconAsync("FIRST.EXE", cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        Task<ImageSource?>[] queue = Enumerable.Range(0, IconService.MaximumQueuedPaths)
            .Select(i => icons.GetFileIconAsync("queued-" + i)).ToArray();
        try
        {
            Assert.Null(await icons.GetFileIconAsync("rejected.exe"));
            Assert.False(icons.TryGetCachedFileIcon("rejected.exe", out _));
            Assert.Equal(2, Volatile.Read(ref calls));
        }
        finally { release.Set(); }
        await Task.WhenAll(queue.Concat(new[] { first, second })).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(peak <= 2);
        Assert.Same(await first, await icons.GetFileIconAsync("FIRST.exe"));
        Assert.NotNull(await icons.GetFileIconAsync("rejected.exe"));
    }

    [Fact]
    public void RemovalDetachAndDelayedDispatch_RejectStaleResults_AndReleaseVm()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var gate = new ManualResetEventSlim();
            var icons = new IconService(fixture.Log, _ => { gate.Wait(TimeSpan.FromSeconds(10)); return Image(); });
            WeakReference released;
            Task completion;
            try
            {
                (released, completion) = AllocateDetached(fixture, icons);
                StaDispatcherFixture.CollectSettled(dispatcher);
                Assert.False(released.IsAlive);
            }
            finally { gate.Set(); }
            PumpUntil(dispatcher, () => completion.IsCompleted);
            Assert.True(completion.IsCompletedSuccessfully);

            using var secondGate = new ManualResetEventSlim();
            var delayedIcons = new IconService(fixture.Log, _ => { secondGate.Wait(TimeSpan.FromSeconds(10)); return Image(); });
            var group = new Group();
            group.Members.Add(Member("late.exe"));
            var vm = new GroupViewModel(group, fixture.Manager, delayedIcons, fixture.Log);
            var retired = vm.Tabs[0];
            vm.Tabs.RemoveAt(0);
            var replacement = new GroupViewModel(group, fixture.Manager, delayedIcons, fixture.Log);
            secondGate.Set();
            PumpUntil(dispatcher, () => vm.IconResolutionCompletion.IsCompleted && replacement.IconResolutionCompletion.IsCompleted);
            Assert.Null(retired.Icon);
            Assert.NotNull(replacement.Tabs[0].Icon);
            vm.Detach();
            replacement.Detach();
        });
    }

    [Fact]
    public void RolledBackInsertion_DoesNotAdmitCosmeticWorkOrRetainTab()
    {
        StaDispatcherFixture.Run(_ =>
        {
            using var fixture = new Fixture();
            int extractions = 0;
            var icons = new IconService(fixture.Log, path => { Interlocked.Increment(ref extractions); return Image(); });
            var group = new Group();
            var vm = new GroupViewModel(group, fixture.Manager, icons, fixture.Log);
            var member = Member("rollback.exe");
            System.Collections.Specialized.NotifyCollectionChangedEventHandler fail = (_, args) =>
            {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                    throw new InvalidOperationException("fixture insertion failure");
            };
            vm.Tabs.CollectionChanged += fail;
            Assert.Throws<InvalidOperationException>(() => vm.AddCapturedWindow(member));
            Assert.DoesNotContain(member, group.Members);
            Assert.Empty(vm.Tabs);
            Assert.Empty(vm.DisplayTabs);
            Assert.Equal(0, extractions);
            vm.Tabs.CollectionChanged -= fail;
            vm.Detach();
        });
    }

    [Fact]
    public void QueuedDelivery_IsRejectedAfterDetachBeforeDispatcherPumps()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using var fixture = new Fixture();
            using var gate = new ManualResetEventSlim();
            using var queued = new ManualResetEventSlim();
            var icons = new IconService(fixture.Log, _ => { gate.Wait(TimeSpan.FromSeconds(5)); return Image(); });
            var group = new Group();
            group.Members.Add(Member("queued.exe"));
            var vm = new GroupViewModel(group, fixture.Manager, icons, fixture.Log);
            var tab = vm.Tabs[0];
            bool changed = false;
            tab.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(TabViewModel.Icon)) changed = true; };
            DispatcherHookEventHandler onQueued = (_, args) =>
            {
                if (args.Operation.Priority == DispatcherPriority.DataBind) queued.Set();
            };
            dispatcher.Hooks.OperationPosted += onQueued;
            try
            {
                gate.Set();
                Assert.True(queued.Wait(TimeSpan.FromSeconds(3)), "delivery never queued");
                Assert.False(vm.IconResolutionCompletion.IsCompleted);
                vm.Detach();
                PumpUntil(dispatcher, () => vm.IconResolutionCompletion.IsCompleted);
                Assert.False(changed);
                Assert.Null(tab.Icon);
            }
            finally { gate.Set(); vm.Detach(); dispatcher.Hooks.OperationPosted -= onQueued; }
        });
    }

    [Fact]
    public async Task CachedSuccessNullAndThrownFailure_AreReused_AndImagesFrozen()
    {
        using var fixture = new Fixture();
        int count = 0;
        var icons = new IconService(fixture.Log, path =>
        {
            Interlocked.Increment(ref count);
            return path == "fail" ? throw new IOException("cosmetic") : path == "null" ? null : Image();
        });
        Assert.Null(await icons.GetFileIconAsync("fail"));
        Assert.Null(await icons.GetFileIconAsync("FAIL"));
        Assert.Null(await icons.GetFileIconAsync("null"));
        Assert.Null(await icons.GetFileIconAsync("NULL"));
        ImageSource? success = await icons.GetFileIconAsync("success");
        Assert.True(success!.IsFrozen);
        Assert.Same(success, await icons.GetFileIconAsync("SUCCESS"));
        Assert.Equal(3, count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, Task) AllocateDetached(Fixture fixture, IconService icons)
    {
        var group = new Group();
        group.Members.Add(Member("held.exe"));
        var vm = new GroupViewModel(group, fixture.Manager, icons, fixture.Log);
        Task task = vm.IconResolutionCompletion;
        vm.Detach();
        return (new WeakReference(vm), task);
    }

    internal static void PumpUntil(Dispatcher dispatcher, Func<bool> condition)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < limit)
            StaDispatcherFixture.Pump(dispatcher, TimeSpan.FromMilliseconds(10));
        Assert.True(condition(), "asynchronous operation did not settle");
    }

    private static ImageSource Image() => new DrawingImage(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 2, 2))));
    private static CapturedWindow Member(string path) => new() { ExePath = path, OriginalTitle = "fixture" };

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "TabDock-tab-icons-" + Guid.NewGuid().ToString("N"));
        public LoggingService Log { get; }
        public GroupManager Manager { get; }
        public Fixture()
        {
            Log = new LoggingService(Path.Combine(_root, "logs"));
            Manager = new GroupManager(new WindowShepherdService(Log, Path.Combine(_root, "journal.json")),
                new PersistenceService(Log, Path.Combine(_root, "state.json")), Log);
        }
        public void Dispose()
        {
            Log.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
