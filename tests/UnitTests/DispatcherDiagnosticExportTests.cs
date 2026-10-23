using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using TabDock.Views;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class DispatcherDiagnosticExportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualContainer_IsCapturedOnOwner_ArchiveYieldsAndKeepsDetachedEvidence(bool partial)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            string root = Path.Combine(Path.GetTempPath(), "TabDock-diagnostic-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            using var log = new LoggingService(Path.Combine(root, "logs"));
            using var release = new ManualResetEventSlim();
            using var entered = new ManualResetEventSlim();
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "journal.json"));
            var manager = new GroupManager(shepherd, new PersistenceService(log, Path.Combine(root, "state.json")), log);
            var group = new Group();
            var icons = new IconService(log, _ => null);
            var vm = new GroupViewModel(group, manager, icons, log);
            var container = new ContainerWindow(vm, manager, shepherd, log, icons, SharedTemplateLoader.Load());
            try
            {
                Assert.False(container.IsVisible);
                Assert.Throws<InvalidOperationException>(() => Task.Run(container.CreateDiagnosticSnapshot).GetAwaiter().GetResult());
                LogicalPresentationSnapshot mutable = container.CreateDiagnosticSnapshot();
                mutable.Members.Add(new DiagnosticMemberSnapshot { Hwnd = 42, MemberKey = "captured", ExpectedPaneRect = new DiagnosticRect { Left = 7 } });
                LogicalSnapshotCapture captured = LogicalSnapshotCapture.Collect(new Func<LogicalPresentationSnapshot>[]
                {
                    () => { Assert.True(dispatcher.CheckAccess()); return mutable; },
                    () => throw new IOException("SECRET-TOKEN C:\\Users\\private\\data"),
                });
                Assert.Equal("partial", captured.Status);
                // Same provider orchestration used by the public API; collect
                // actual ContainerWindow state only on its owning dispatcher.
                Task<string> export = DiagnosticReportService.ExportFromProviderAsync(Path.Combine(root, "support.zip"),
                    () => dispatcher.InvokeAsync(() =>
                    {
                        Assert.True(dispatcher.CheckAccess());
                        Func<LogicalPresentationSnapshot> liveSource = () =>
                        {
                            LogicalPresentationSnapshot live = container.CreateDiagnosticSnapshot();
                            live.Members = mutable.Members;
                            return live;
                        };
                        return LogicalSnapshotCapture.Collect(partial ? new Func<LogicalPresentationSnapshot>[]
                        {
                            liveSource, () => throw new IOException("SECRET-TOKEN private failure"),
                        } : new[] { liveSource });
                    }).Task, () =>
                {
                    Assert.False(dispatcher.CheckAccess());
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                    return new DiagnosticReport(); // explicit observation fixture, no production storage or native probe
                });
                TabIconNonblockingTests.PumpUntil(dispatcher, () => entered.IsSet);
                Assert.False(export.IsCompleted);
                Assert.False(File.Exists(Path.Combine(root, "support.zip")));
                bool yielded = false;
                dispatcher.BeginInvoke(() => yielded = true);
                StaDispatcherFixture.Settle(dispatcher);
                Assert.True(yielded);
                mutable.Members[0].ExpectedPaneRect!.Left = 999;
                mutable.Members.Clear();
                container.Close(); // closing cannot alter captured archive data
                release.Set();
                TabIconNonblockingTests.PumpUntil(dispatcher, () => export.IsCompleted);
                string path = export.GetAwaiter().GetResult();
                using ZipArchive zip = ZipFile.OpenRead(path);
                using JsonDocument logical = JsonDocument.Parse(Read(zip, "logical-snapshot.json"));
                Assert.Equal(7, logical.RootElement[0].GetProperty("members")[0].GetProperty("expectedPaneRect").GetProperty("left").GetInt32());
                using JsonDocument status = JsonDocument.Parse(Read(zip, "logical-status.json"));
                Assert.Equal(partial ? "partial" : "available", status.RootElement.GetProperty("status").GetString());
                if (partial)
                    Assert.Contains("container-collection-failed", Read(zip, "doctor.txt"));
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string content = Read(zip, entry.FullName);
                    Assert.DoesNotContain("SECRET-TOKEN", content);
                    Assert.DoesNotContain("C:\\Users\\private", content);
                }
                Assert.Empty(Directory.GetFiles(root, "*.tmp"));
            }
            finally
            {
                release.Set();
                vm.Detach();
                container.Close();
                log.Dispose();
                Directory.Delete(root, true);
            }
        });
    }

    [Theory]
    [InlineData("available", false, false)]
    [InlineData("empty", true, false)]
    [InlineData("unavailable", true, true)]
    public async Task StatusAndObservedFailures_AreInTheActualZip(string expected, bool empty, bool failed)
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-diagnostic-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            LogicalSnapshotCapture capture = failed ? LogicalSnapshotCapture.NoProvider : LogicalSnapshotCapture.Collect(
                empty ? Array.Empty<Func<LogicalPresentationSnapshot>>() : new Func<LogicalPresentationSnapshot>[] { () => new LogicalPresentationSnapshot() });
            Task<string>[] exports = Enumerable.Range(0, 8).Select(_ =>
                DiagnosticReportService.ExportCapturedBundleAsync(Path.Combine(root, "support.zip"), capture, () => new DiagnosticReport())).ToArray();
            string[] paths = await Task.WhenAll(exports);
            Assert.Equal(8, paths.Distinct().Count());
            string second = paths[1];
            using ZipArchive zip = ZipFile.OpenRead(second);
            using JsonDocument status = JsonDocument.Parse(Read(zip, "logical-status.json"));
            Assert.Equal(expected, status.RootElement.GetProperty("status").GetString());
            await Assert.ThrowsAsync<IOException>(() => DiagnosticReportService.ExportCapturedBundleAsync(Path.Combine(root, "fail.zip"), capture,
                () => throw new IOException("fixture failure")));
            Assert.False(File.Exists(Path.Combine(root, "fail.zip")));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
