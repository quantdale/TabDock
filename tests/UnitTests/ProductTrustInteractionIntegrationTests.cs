using System;
using System.IO;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

public sealed class ProductTrustInteractionIntegrationTests
{
    [Fact]
    public void RecoveryBannerProjectsWithZeroGroupsAndRestoredGroups()
    {
        using TestHarness harness = TestHarness.Create();
        var vm = new MainViewModel(harness.Manager);
        Assert.False(vm.HasPendingRecoveryAttention);

        vm.SetPendingRecoveryAttention(new PendingRecoveryAttention(1, false, null));

        Assert.Empty(vm.Groups);
        Assert.True(vm.HasPendingRecoveryAttention);
        Assert.Contains("1 pending recovery item", vm.PendingRecoveryBannerText, StringComparison.Ordinal);

        harness.Manager.Groups.Add(new Group { Name = "restored" });
        Assert.Single(vm.Groups);
        Assert.True(vm.HasPendingRecoveryAttention);

        vm.SetPendingRecoveryAttention(default);
        Assert.False(vm.HasPendingRecoveryAttention);
    }

    [Fact]
    public void CaptureAdmissionAndShortcutAvailabilityRemainSeparateAcrossTransitions()
    {
        using TestHarness harness = TestHarness.Create();
        var vm = new MainViewModel(harness.Manager);
        vm.GlobalHotkeyAvailable = false;
        vm.GlobalTabNavigationHotkeysAvailable = false;

        Assert.True(vm.CaptureAllowed);
        Assert.Contains("shortcut unavailable", vm.CaptureButtonText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local Ctrl+Tab", vm.TabNavigationAvailabilityText, StringComparison.Ordinal);
        Assert.True(vm.CaptureCommand.CanExecute(null));

        harness.Manager.SetCaptureAllowed(false, "journal storage unavailable");
        Assert.False(vm.CaptureAllowed);
        Assert.False(vm.CaptureCommand.CanExecute(null));
        Assert.DoesNotContain("Ctrl+Alt+G", vm.CaptureButtonText, StringComparison.Ordinal);
        Assert.Contains("journal storage unavailable", vm.CaptureButtonToolTip, StringComparison.Ordinal);

        harness.Manager.SetCaptureAllowed(true, "WinEvent retry succeeded");
        Assert.True(vm.CaptureAllowed);
        Assert.True(vm.CaptureCommand.CanExecute(null));
        Assert.Contains("shortcut unavailable", vm.CaptureButtonText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdmissionReenableUpdatesTheSameProjectionWithoutRestart()
    {
        using TestHarness harness = TestHarness.Create();
        var vm = new MainViewModel(harness.Manager);
        var groupVm = new GroupViewModel(new Group { Name = "container" }, harness.Manager, new IconService(harness.Log), harness.Log);

        harness.Manager.SetCaptureAllowed(false, "WinEvent startup retry pending");
        Assert.False(vm.CaptureAllowed);
        Assert.False(groupVm.CaptureAllowed);
        Assert.Contains("retry pending", groupVm.AddWindowToolTip, StringComparison.Ordinal);

        harness.Manager.SetCaptureAllowed(true, "WinEvent retry succeeded");
        Assert.True(vm.CaptureAllowed);
        Assert.True(groupVm.CaptureAllowed);
        Assert.Equal("Add window to group", groupVm.AddWindowToolTip);
        groupVm.Detach();
    }

    [Fact]
    public void CrossFeatureSourceKeepsGlobalNavigationOutOfModalChrome()
    {
        string root = FindRepoRoot();
        string app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        string container = File.ReadAllText(Path.Combine(root, "Views", "ContainerWindow.xaml.cs"));

        Assert.Contains("RefreshLauncherRecoveryAttention", app, StringComparison.Ordinal);
        Assert.Contains("CanReceiveGlobalTabNavigation", app, StringComparison.Ordinal);
        Assert.Contains("!_closePromptOpen", container, StringComparison.Ordinal);
        Assert.Contains("IsContainerChromeInteractionActive()", container, StringComparison.Ordinal);
        Assert.Contains("IsCapturePanelOpen", container, StringComparison.Ordinal);
        Assert.Contains("IsRenaming", container, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockedAdmissionGuardsGlobalHotkeyAndInlineAddSurface()
    {
        string root = FindRepoRoot();
        string app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        string container = File.ReadAllText(Path.Combine(root, "Views", "ContainerWindow.xaml.cs"));
        string main = File.ReadAllText(Path.Combine(root, "Views", "MainWindow.xaml"));
        string chrome = File.ReadAllText(Path.Combine(root, "Views", "ContainerWindow.xaml"));
        string mainVm = File.ReadAllText(Path.Combine(root, "ViewModels", "MainViewModel.cs"));
        string groupVm = File.ReadAllText(Path.Combine(root, "ViewModels", "GroupViewModel.cs"));

        Assert.Contains("if (!_groups.CaptureAllowed)", app, StringComparison.Ordinal);
        Assert.Contains("Capture request ignored because admission is blocked", app, StringComparison.Ordinal);
        Assert.Contains("if (_closePromptOpen || !_manager.CaptureAllowed)", container, StringComparison.Ordinal);
        Assert.Contains("if (_capturePicker != null || !_manager.CaptureAllowed)", container, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CaptureAllowed}\"", main, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CaptureAllowed}\"", chrome, StringComparison.Ordinal);
        Assert.Contains("CaptureAdmissionChanged", mainVm, StringComparison.Ordinal);
        Assert.Contains("CaptureAdmissionChanged", groupVm, StringComparison.Ordinal);
    }

    [Fact]
    public void LifetimeAndMultiContainerGuardsRemainExplicit()
    {
        string root = FindRepoRoot();
        string app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        string container = File.ReadAllText(Path.Combine(root, "Views", "ContainerWindow.xaml.cs"));

        Assert.Contains("_containers.TryGetValue(target.GroupId", app, StringComparison.Ordinal);
        Assert.Contains("foreach ((Guid groupId, ContainerWindow container) in _containers)", app, StringComparison.Ordinal);
        Assert.Contains("if (_containers.Count == 0 && _mainWindow != null)", app, StringComparison.Ordinal);
        Assert.Contains("RefreshLauncherRecoveryAttention();", app, StringComparison.Ordinal);
        Assert.Contains("_mainWindow.Activated", app, StringComparison.Ordinal);
        Assert.Contains("_mainWindow.Show();", app, StringComparison.Ordinal);
        Assert.Contains("&& _containerHwnd != IntPtr.Zero", container, StringComparison.Ordinal);
        Assert.Contains("_containerHwnd = IntPtr.Zero;", container, StringComparison.Ordinal);
        Assert.Contains("if (_splitAffordanceContextMenu is { IsOpen: true }) return true;", container, StringComparison.Ordinal);
        Assert.Contains("if (IsCapturePanelOpen) return true;", container, StringComparison.Ordinal);
        Assert.Contains("if (_viewModel.IsRenaming) return true;", container, StringComparison.Ordinal);
        Assert.Contains("if (_closePromptOpen) return true;", container, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TabDock.sln not found above test output directory.");
    }

    private sealed class TestHarness : IDisposable
    {
        private readonly string _root;
        public LoggingService Log { get; }
        public GroupManager Manager { get; }

        private TestHarness(string root, LoggingService log, GroupManager manager)
        {
            _root = root;
            Log = log;
            Manager = manager;
        }

        public static TestHarness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "tabdock-trust-integration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            return new TestHarness(root, log, new GroupManager(shepherd, persistence, log));
        }

        public void Dispose()
        {
            Log.Dispose();
            try { Directory.Delete(_root, recursive: true); } catch { }
        }
    }
}
