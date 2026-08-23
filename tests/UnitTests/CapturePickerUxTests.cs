using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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