using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using TabDock.Services;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former CapturePickerSelfTest and the aggregator's picker
/// selection checks (Wave 4): the refresh-time group selection policy plus
/// generation-safe background icon resolution (a stale extraction must never
/// win against a newer refresh, cached rows must resolve immediately, and icon
/// failures must not break refresh).
/// </summary>
public class CapturePickerViewModelTests
{
    [Fact]
    public void SelectGroupAfterRefresh_KeepsTheStillExistingSelection()
    {
        Guid selectedGroupId = Guid.NewGuid();
        var options = new[]
        {
            new CapturePickerViewModel.GroupOption(Guid.Empty, "<New group>"),
            new CapturePickerViewModel.GroupOption(selectedGroupId, "Existing"),
        };

        Assert.Equal(selectedGroupId, CapturePickerViewModel.SelectGroupAfterRefresh(options, selectedGroupId)?.Id);
    }

    [Fact]
    public void SelectGroupAfterRefresh_FallsBackToNewGroupWhenSelectionVanished()
    {
        Guid selectedGroupId = Guid.NewGuid();
        var options = new[]
        {
            new CapturePickerViewModel.GroupOption(Guid.Empty, "<New group>"),
            new CapturePickerViewModel.GroupOption(selectedGroupId, "Existing"),
        };

        Assert.Equal(Guid.Empty, CapturePickerViewModel.SelectGroupAfterRefresh(options, Guid.NewGuid())?.Id);
    }

    [Fact]
    public void BackgroundIconResolution_IsGenerationSafe()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-picker-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstExtractionStarted = new ManualResetEventSlim();
        var releaseFirstExtraction = new ManualResetEventSlim();
        int extractionCount = 0;
        int sourceGeneration = 0;
        DrawingImage oldIcon = FrozenImage();
        DrawingImage currentIcon = FrozenImage();

        try
        {
            // Bind one dispatcher for the whole test. The method stays fully
            // synchronous so every statement runs on the dispatcher's own
            // thread — an await would resume on a pool thread and break
            // Dispatcher.PushFrame affinity below.
            Dispatcher testDispatcher = Dispatcher.CurrentDispatcher;
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, path =>
            {
                int call = Interlocked.Increment(ref extractionCount);
                if (call == 1)
                {
                    firstExtractionStarted.Set();
                    releaseFirstExtraction.Wait(TimeSpan.FromSeconds(2));
                    return oldIcon;
                }
                return currentIcon;
            });

            IEnumerable<CapturePickerViewModel.WindowInfo> Candidates()
            {
                string path = Volatile.Read(ref sourceGeneration) == 0
                    ? @"C:\Perf\Old.exe"
                    : @"C:\Perf\Current.exe";
                return new[]
                {
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x101), 101, "Perf", "First", path),
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x102), 102, "Perf", "Second", path),
                };
            }

            using var picker = new CapturePickerViewModel(manager, icons, log, Candidates, testDispatcher);
            Assert.Equal(2, picker.Windows.Count);
            Assert.Equal("First", picker.Windows[0].Title);
            Assert.Equal("Second", picker.Windows[1].Title);
            Assert.All(picker.Windows, row => Assert.Null(row.Icon));
            Assert.True(firstExtractionStarted.Wait(TimeSpan.FromSeconds(2)), "first background extraction never started");

            // Invalidate refresh N while its old executable is still being
            // extracted. Refresh N+1 has a different path and must win.
            Volatile.Write(ref sourceGeneration, 1);
            picker.Refresh();
            Assert.True(
                PumpUntil(
                    testDispatcher,
                    () => picker.IconResolutionCompletion.IsCompleted
                        && picker.Windows.All(row => ReferenceEquals(row.Icon, currentIcon)),
                    2000),
                "the newest generation must own every row icon");

            releaseFirstExtraction.Set();
            Assert.True(
                PumpUntil(testDispatcher, () => picker.IconResolutionCompletion.IsCompleted, 2000),
                "the superseded generation must still complete after its blocked extraction is released");

            int callsAfterColdRefresh = Volatile.Read(ref extractionCount);
            picker.Refresh();
            bool cachedRowsAreImmediate = picker.IconResolutionCompletion.IsCompleted
                && picker.Windows.All(row => ReferenceEquals(row.Icon, currentIcon))
                && Volatile.Read(ref extractionCount) == callsAfterColdRefresh;
            Assert.True(cachedRowsAreImmediate, "cached rows must not re-extract on a warm refresh");

            var failingIcons = new IconService(log, _ => throw new InvalidOperationException("test icon failure"));
            using var failingPicker = new CapturePickerViewModel(
                manager,
                failingIcons,
                log,
                () => new[]
                {
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x103), 103, "Perf", "Failure", @"C:\Perf\Failure.exe"),
                },
                testDispatcher);
            Assert.True(
                PumpUntil(testDispatcher, () => failingPicker.IconResolutionCompletion.IsCompleted, 2000),
                "icon extraction failure must complete the refresh instead of hanging it");
        }
        finally
        {
            releaseFirstExtraction.Set();
            firstExtractionStarted.Dispose();
            releaseFirstExtraction.Dispose();
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    private static DrawingImage FrozenImage()
    {
        var image = new DrawingImage();
        image.Freeze();
        return image;
    }

    private static bool PumpUntil(Dispatcher dispatcher, Func<bool> condition, int timeoutMilliseconds)
    {
        if (condition())
            return true;

        var frame = new DispatcherFrame();
        Stopwatch stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (condition() || stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
            {
                frame.Continue = false;
            }
        }, dispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return condition();
    }
}

