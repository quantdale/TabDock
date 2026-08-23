using System;
using System.Collections.Generic;
using System.IO;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Verifies the manager-owned admission state and event boundary used by all
/// launcher/container capture projections.
/// </summary>
public sealed class CaptureAdmissionStateTests
{
    [Fact]
    public void AllowedToBlockedToAllowed_PublishesEveryStateAndReason()
    {
        string root = CreateRoot();
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            var states = new List<CaptureAdmissionState>();
            manager.CaptureAdmissionChanged += (_, e) => states.Add(e.State);

            manager.SetCaptureAllowed(false, "durable crash-recovery journal storage is unavailable.");
            manager.SetCaptureAllowed(false, "WinEvent monitor installation is pending retry.");
            manager.SetCaptureAllowed(true, "WinEvent monitor retry succeeded.");

            Assert.Equal(3, states.Count);
            Assert.False(states[0].Allowed);
            Assert.Equal("durable crash-recovery journal storage is unavailable.", states[0].Reason);
            Assert.False(states[1].Allowed);
            Assert.Equal("WinEvent monitor installation is pending retry.", states[1].Reason);
            Assert.True(states[2].Allowed);
            Assert.Equal("WinEvent monitor retry succeeded.", states[2].Reason);
            Assert.True(manager.CaptureAllowed);
            Assert.Equal(states[2], manager.CaptureAdmission);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void PermanentMonitorFailure_RemainsBlockedWithReason()
    {
        string root = CreateRoot();
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);

            manager.SetCaptureAllowed(false, "WinEvent monitor failed its bounded retry budget.");

            Assert.False(manager.CaptureAllowed);
            Assert.Equal("WinEvent monitor failed its bounded retry budget.", manager.CaptureAdmissionReason);
            Assert.False(manager.CaptureAdmission.Allowed);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void SameBooleanReasonChange_IsObservable()
    {
        string root = CreateRoot();
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));
            var shepherd = new WindowShepherdService(log, Path.Combine(root, "hidden-windows.json"));
            var persistence = new PersistenceService(log, Path.Combine(root, "state.json"));
            var manager = new GroupManager(shepherd, persistence, log);
            int changes = 0;
            manager.CaptureAdmissionChanged += (_, _) => changes++;

            manager.SetCaptureAllowed(true, "healthy after retry");

            Assert.Equal(1, changes);
            Assert.Equal("healthy after retry", manager.CaptureAdmissionReason);
        }
        finally { DeleteRoot(root); }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch { }
    }
}
