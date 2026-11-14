using System;
using System.IO;
using System.Runtime.CompilerServices;
using TabDock.Infrastructure;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using TabDock.Views;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class ContainerLifetimeTests
{
    [Fact]
    public void ClosedContainer_IsCollectibleWhileItsViewModelRemainsAlive()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            string root = Path.Combine(Path.GetTempPath(), "TabDock-container-lifetime-" + Guid.NewGuid().ToString("N"));
            using var log = new LoggingService(Path.Combine(root, "logs"));
            using var motion = MotionProfile.OverrideReducedMotion(true);
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "journal.json"));
            var manager = new GroupManager(shepherd, new PersistenceService(log, Path.Combine(root, "state.json")), log);
            var icons = new IconService(log, _ => null);
            var vm = new GroupViewModel(new Group(), manager, icons, log);
            try
            {
                WeakReference closed = CreateAndClose(vm, manager, shepherd, log, icons);
                StaDispatcherFixture.CollectSettled(dispatcher);
                Assert.False(closed.IsAlive, "VM-owned collections must not retain the closed container's event handlers");
                GC.KeepAlive(vm);
            }
            finally
            {
                vm.Detach();
                log.Dispose();
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndClose(GroupViewModel vm, GroupManager manager,
        WindowShepherdService shepherd, LoggingService log, IconService icons)
    {
        var container = new ContainerWindow(vm, manager, shepherd, log, icons, SharedTemplateLoader.Load());
        Assert.False(container.IsVisible);
        container.Close();
        return new WeakReference(container);
    }
}
