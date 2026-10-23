using System;
using System.IO;
using System.Threading;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

public sealed class TabIconNonblockingTests
{
    [Fact]
    public void AddCapturedWindow_ReturnsBeforeUncachedIconExtraction()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-tab-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var log = new LoggingService(Path.Combine(root, "logs"));
        var release = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        try
        {
            var icons = new IconService(log, _ =>
            {
                started.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)), "icon gate was not released");
                return null;
            });
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var group = new Group { Name = "icons" };
            manager.Groups.Add(group);
            var vm = new GroupViewModel(group, manager, icons, log);
            var window = new CapturedWindow
            {
                Hwnd = new IntPtr(0x5101),
                ProcessId = 5101,
                ExePath = @"C:\Apps\slow.exe",
                OriginalTitle = "Slow",
                OriginalClassName = "SlowWindow",
            };

            var returned = new ManualResetEventSlim(false);
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    vm.AddCapturedWindow(window);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    returned.Set();
                }
            });
            thread.Start();
            Assert.True(returned.Wait(TimeSpan.FromMilliseconds(750)), "membership waited for icon extraction");
            Assert.Null(failure);
            Assert.Single(vm.Tabs);
            Assert.Contains(window, group.Members);
        }
        finally
        {
            release.Set();
            started.Wait(TimeSpan.FromSeconds(2));
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
