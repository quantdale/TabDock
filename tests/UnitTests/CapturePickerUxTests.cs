using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
using TabDock.Services;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Product-level picker regressions for the ship-readiness UX pass. These stay
/// headless: no real desktop HWNDs are required to prove filtering or selection
/// continuity across a refresh.
/// </summary>
public sealed class CapturePickerUxTests
{
    [Fact]
    public void SearchText_FiltersByTitleExecutableAndClass_WithoutMutatingCandidates()
    {
        string root = CreateTempRoot();
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, _ => null);

            IEnumerable<CapturePickerViewModel.WindowInfo> Candidates() => new[]
            {
                new CapturePickerViewModel.WindowInfo(new IntPtr(0x101), 101, "Chrome_WidgetWin_1", "Project dashboard - Google Chrome", string.Empty),
                new CapturePickerViewModel.WindowInfo(new IntPtr(0x102), 102, "ApplicationFrameWindow", "Spotify", @"C:\Apps\Spotify.exe"),
                new CapturePickerViewModel.WindowInfo(new IntPtr(0x103), 103, "DiscordWindowClass", "Team chat", string.Empty),
            };

            using var picker = new CapturePickerViewModel(manager, icons, log, Candidates);
            Assert.Equal(3, picker.Windows.Count);
            Assert.Equal(3, picker.FilteredWindows.Count);

            picker.SearchText = "spotify";
            Assert.Single(picker.FilteredWindows);
            Assert.Equal("Spotify", picker.FilteredWindows[0].Title);

            picker.SearchText = "discordwindow";
            Assert.Single(picker.FilteredWindows);
            Assert.Equal("Team chat", picker.FilteredWindows[0].Title);

            picker.SearchText = "chrome";
            Assert.Single(picker.FilteredWindows);
            Assert.Contains("Google Chrome", picker.FilteredWindows[0].Title);

            picker.SearchText = string.Empty;
            Assert.Equal(3, picker.FilteredWindows.Count);
            Assert.Equal(3, picker.Windows.Count);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Refresh_PreservesSelectionOnlyForSameWindowIdentity()
    {
        string root = CreateTempRoot();
        int generation = 0;
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, _ => null);

            IEnumerable<CapturePickerViewModel.WindowInfo> Candidates()
            {
                if (generation == 0)
                {
                    return new[]
                    {
                        new CapturePickerViewModel.WindowInfo(new IntPtr(0x201), 201, "Editor", "Notes", string.Empty),
                        new CapturePickerViewModel.WindowInfo(new IntPtr(0x202), 202, "Browser", "Docs", string.Empty),
                    };
                }

                return new[]
                {
                    // Same HWND/PID/path, changed title: preserve the user's check.
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x201), 201, "Editor", "Notes - edited", string.Empty),
                    // Same HWND but a different PID: treat it as a different native window.
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x202), 999, "Browser", "Replacement", string.Empty),
                };
            }

            using var picker = new CapturePickerViewModel(manager, icons, log, Candidates);
            picker.Windows[0].IsSelected = true;
            picker.Windows[1].IsSelected = true;
            Assert.Equal(2, picker.SelectedCount);

            generation = 1;
            picker.Refresh();

            Assert.True(picker.Windows.Single(w => w.Hwnd == new IntPtr(0x201)).IsSelected);
            Assert.False(picker.Windows.Single(w => w.Hwnd == new IntPtr(0x202)).IsSelected);
            Assert.Equal(1, picker.SelectedCount);
            Assert.Equal("1 window selected", picker.SelectionSummary);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Refresh_UsesProcessInstanceClassAndWindowsPathIdentity()
    {
        string root = CreateTempRoot();
        int generation = 0;
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, _ => null);

            IEnumerable<CapturePickerViewModel.WindowInfo> Candidates()
            {
                if (generation == 0)
                {
                    return new[]
                    {
                        new CapturePickerViewModel.WindowInfo(new IntPtr(0x211), 211, "Editor", "Notes", @"C:\Apps\Editor.exe", 311, 411),
                        new CapturePickerViewModel.WindowInfo(new IntPtr(0x212), 212, "Browser", "Docs", @"C:\Apps\Browser.exe", 312, 412),
                        new CapturePickerViewModel.WindowInfo(new IntPtr(0x213), 213, "Browser", "Mail", @"C:\Apps\Browser.exe", 313, 413),
                        new CapturePickerViewModel.WindowInfo(new IntPtr(0x214), 214, "OldClass", "Terminal", @"C:\Apps\Terminal.exe", 314, 414),
                    };
                }

                return new[]
                {
                    // Title and path casing are mutable/presentation details;
                    // the same strong identity keeps this row checked.
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x211), 211, "Editor", "Notes - edited", @"c:\apps\EDITOR.EXE", 311, 411),
                    // PID reuse is not continuity, even when the rest looks
                    // similar.
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x212), 999, "Browser", "Replacement PID", @"C:\Apps\Browser.exe", 312, 412),
                    // A recycled PID in a new process instance is not safe.
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x213), 213, "Browser", "Replacement process", @"C:\Apps\Browser.exe", 313, 999),
                    // A changed window class is a different HWND identity.
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x214), 214, "NewClass", "Replacement class", @"C:\Apps\Terminal.exe", 314, 414),
                };
            }

            using var picker = new CapturePickerViewModel(manager, icons, log, Candidates);
            foreach (CapturePickerViewModel.WindowInfo row in picker.Windows)
                row.IsSelected = true;

            generation = 1;
            picker.Refresh();

            Assert.True(picker.Windows.Single(w => w.Hwnd == new IntPtr(0x211)).IsSelected);
            Assert.False(picker.Windows.Single(w => w.Hwnd == new IntPtr(0x212)).IsSelected);
            Assert.False(picker.Windows.Single(w => w.Hwnd == new IntPtr(0x213)).IsSelected);
            Assert.False(picker.Windows.Single(w => w.Hwnd == new IntPtr(0x214)).IsSelected);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Refresh_DropsSelectionsForTargetsThatDisappear()
    {
        string root = CreateTempRoot();
        int generation = 0;
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, _ => null);

            IEnumerable<CapturePickerViewModel.WindowInfo> Candidates()
                => generation == 0
                    ? new[]
                    {
                        new CapturePickerViewModel.WindowInfo(new IntPtr(0x221), 221, "Editor", "Keep", string.Empty, 321, 421),
                        new CapturePickerViewModel.WindowInfo(new IntPtr(0x222), 222, "Editor", "Gone", string.Empty, 322, 422),
                    }
                    : new[]
                    {
                        new CapturePickerViewModel.WindowInfo(new IntPtr(0x221), 221, "Editor", "Keep", string.Empty, 321, 421),
                    };

            using var picker = new CapturePickerViewModel(manager, icons, log, Candidates);
            picker.Windows[0].IsSelected = true;
            picker.Windows[1].IsSelected = true;
            generation = 1;
            picker.Refresh();

            Assert.Single(picker.Windows);
            Assert.Equal(new IntPtr(0x221), picker.Windows[0].Hwnd);
            Assert.True(picker.Windows[0].IsSelected);
            Assert.Equal(1, picker.SelectedCount);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Refresh_DropsSelectionWhenTargetBecomesCapturedElsewhere()
    {
        string root = CreateTempRoot();
        int generation = 0;
        IntPtr targetHwnd = new(0x231);
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, _ => null);

            IEnumerable<CapturePickerViewModel.WindowInfo> Candidates()
                => new[]
                {
                    new CapturePickerViewModel.WindowInfo(
                        targetHwnd, 231, "Editor", generation == 0 ? "Target" : "Already captured",
                        string.Empty, 331, 431),
                };

            using var picker = new CapturePickerViewModel(manager, icons, log, Candidates);
            picker.Windows[0].IsSelected = true;

            TabDock.Models.Group existing = manager.CreateGroup("Existing");
            existing.Members.Add(new TabDock.Models.CapturedWindow { Hwnd = targetHwnd });
            generation = 1;
            picker.Refresh();

            Assert.Empty(picker.Windows);
            Assert.Empty(picker.FilteredWindows);
            Assert.Equal(0, picker.SelectedCount);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Refresh_PreservesFilteredSelectionAcrossSearchChanges()
    {
        string root = CreateTempRoot();
        int generation = 0;
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, _ => null);

            IEnumerable<CapturePickerViewModel.WindowInfo> Candidates()
                => new[]
                {
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x241), 241, "Editor", generation == 0 ? "Visible target" : "Visible target updated", string.Empty, 341, 441),
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x242), 242, "Editor", "Other target", string.Empty, 342, 442),
                };

            using var picker = new CapturePickerViewModel(manager, icons, log, Candidates);
            picker.SearchText = "Visible";
            picker.SelectAllVisibleCommand.Execute(null);
            Assert.Equal(1, picker.SelectedCount);

            picker.SearchText = "Other";
            generation = 1;
            picker.Refresh();

            Assert.Equal(1, picker.SelectedCount);
            Assert.True(picker.Windows.Single(row => row.Hwnd == new IntPtr(0x241)).IsSelected);
            Assert.Single(picker.FilteredWindows);
            Assert.Equal(new IntPtr(0x242), picker.FilteredWindows[0].Hwnd);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void LargeCandidateSet_PreservesRowsAndBatchesBulkSelection(int count)
    {
        string root = CreateTempRoot();
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, _ => null);

            using var picker = new CapturePickerViewModel(
                manager,
                icons,
                log,
                () => Enumerable.Range(0, count)
                    .Select(i => new CapturePickerViewModel.WindowInfo(
                        new IntPtr(0x500 + i),
                        (uint)(500 + i),
                        "SyntheticWindow",
                        $"Synthetic {i}",
                        string.Empty,
                        (uint)(1500 + i),
                        2500 + i)));

            int selectedCountNotifications = 0;
            picker.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CapturePickerViewModel.SelectedCount))
                    selectedCountNotifications++;
            };

            picker.SelectAllVisibleCommand.Execute(null);
            Assert.Equal(count, picker.Windows.Count);
            Assert.Equal(count, picker.SelectedCount);
            Assert.Equal(1, selectedCountNotifications);

            picker.ClearSelectionCommand.Execute(null);
            Assert.Equal(0, picker.SelectedCount);
            Assert.Equal(2, selectedCountNotifications);

            int filterCollectionNotifications = 0;
            ((INotifyCollectionChanged)picker.FilteredWindows).CollectionChanged += (_, _) => filterCollectionNotifications++;
            picker.SearchText = $"Synthetic {count - 1}";
            Assert.Single(picker.FilteredWindows);
            Assert.Equal(1, filterCollectionNotifications);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void SelectAllVisible_OnlySelectsTheCurrentFilteredSet()
    {
        string root = CreateTempRoot();
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var icons = new IconService(log, _ => null);

            using var picker = new CapturePickerViewModel(
                manager,
                icons,
                log,
                () => new[]
                {
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x301), 301, "Browser", "Docs - Chrome", string.Empty),
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x302), 302, "Browser", "Mail - Chrome", string.Empty),
                    new CapturePickerViewModel.WindowInfo(new IntPtr(0x303), 303, "Editor", "Visual Studio", string.Empty),
                });

            picker.SearchText = "chrome";
            picker.SelectAllVisibleCommand.Execute(null);

            Assert.Equal(2, picker.SelectedCount);
            Assert.All(picker.Windows.Where(w => w.Title.Contains("Chrome", StringComparison.Ordinal)), w => Assert.True(w.IsSelected));
            Assert.False(picker.Windows.Single(w => w.Title == "Visual Studio").IsSelected);

            picker.ClearSelectionCommand.Execute(null);
            Assert.Equal(0, picker.SelectedCount);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-picker-ux-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the product assertion result.
        }
    }
}
