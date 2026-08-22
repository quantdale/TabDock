using System;
using System.IO;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Wave-0C coverage: when the global Ctrl+Alt+G registration fails (another
/// process owns the combination), the launcher must not advertise the
/// shortcut as available. The Capture button remains the always-available
/// fallback; only the hint text changes.
/// </summary>
public class HotkeyAvailabilityTests
{
    private static MainViewModel MakeViewModel()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tabdock-hotkey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var log = new LoggingService(Path.Combine(dir, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(dir, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(dir, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            return new MainViewModel(manager);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DefaultState_AdvertisesShortcutHint()
    {
        var vm = MakeViewModel();
        Assert.True(vm.GlobalHotkeyAvailable);
        Assert.Equal("Capture windows (Ctrl+Alt+G)", vm.CaptureButtonText);
    }

    [Fact]
    public void RegistrationFailed_HintIsDroppedButButtonRemains()
    {
        var vm = MakeViewModel();
        vm.GlobalHotkeyAvailable = false; // App sets this after Register() failed
        Assert.Equal("Capture windows (shortcut unavailable)", vm.CaptureButtonText);
        // The command itself is unaffected: CaptureCommand is the fallback path.
        Assert.True(vm.CaptureCommand.CanExecute(null));
    }
}
