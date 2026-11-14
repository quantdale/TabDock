using System;
using System.Linq;
using TabDock.Models;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;

namespace TabDock.UnitTests;

public sealed class ShutdownLayoutIntentTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void EmergencyReleaseAndCleanupSaves_PreserveFullLiveLayoutIntent(bool pendingMember, bool normalize, bool restoredGroup)
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create(twoEntries: true);
        var persistence = new PersistenceService(fixture.Log, fixture.StatePath);
        var manager = new GroupManager(fixture.Service, persistence, fixture.Log);
        Group group = manager.CreateGroup("Shutdown layout", "#ABCDEF");
        // Re-population must supersede older persisted intent, not revive it.
        if (restoredGroup)
            group.PersistedTabs.Add(new PersistedTabMetadata { ExePath = "obsolete.exe" });
        group.Members.Add(fixture.Captured);
        group.Members.Add(fixture.CapturedSecond!);
        fixture.Captured.CustomLabel = "First label";
        fixture.CapturedSecond!.CustomLabel = "Active label";
        group.ActiveIndex = 1;
        manager.SaveState();
        if (pendingMember)
            fixture.IdentityForSecond!.ProcessStartTicks = 0;

        manager.EmergencyReleaseAll();
        if (normalize)
            manager.ClearCapturedMembersAfterSessionEnding();
        // SessionEnding -> Application_Exit and crash cleanup may repeat the
        // entire sequence. A retained pending subset must not shrink the intent.
        manager.EmergencyReleaseAll();
        manager.SaveState();

        Group restored = Assert.Single(new PersistenceService(fixture.Log, fixture.StatePath).Load());
        Assert.Equal(group.Id, restored.Id);
        Assert.Equal("Shutdown layout", restored.Name);
        Assert.Equal("#ABCDEF", restored.AccentColor);
        Assert.Equal(new[] { fixture.Captured.ExePath, fixture.CapturedSecond.ExePath },
            restored.PersistedTabs.Select(tab => tab.ExePath));
        Assert.Equal(new[] { "First label", "Active label" }, restored.PersistedTabs.Select(tab => tab.CustomLabel));
        Assert.Equal(1, restored.PersistedActiveIndex);
        if (pendingMember)
            Assert.Single(fixture.ReadEntries()); // metadata preservation does not retire native recovery evidence
    }

    [Fact]
    public void NewCaptureAfterEmergencyRelease_RetiresTheSnapshotAndPersistsCurrentMembership()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        var persistence = new PersistenceService(fixture.Log, fixture.StatePath);
        var manager = new GroupManager(fixture.Service, persistence, fixture.Log);
        Group group = manager.CreateGroup("Reuse");
        group.Members.Add(fixture.Captured);
        manager.EmergencyReleaseAll();
        Assert.True(group.HasEmergencyLayoutSnapshot);

        group.Members.Add(new CapturedWindow { Hwnd = new IntPtr(123), ExePath = "new-capture.exe" });
        group.ActiveIndex = 0;
        manager.SaveState();

        Assert.False(group.HasEmergencyLayoutSnapshot);
        Group restored = Assert.Single(new PersistenceService(fixture.Log, fixture.StatePath).Load());
        Assert.Equal("new-capture.exe", Assert.Single(restored.PersistedTabs).ExePath);
    }

    [Fact]
    public void OrdinaryPopOut_DoesNotCreateEmergencyLayoutIntent()
    {
        using ReleaseTestFixture fixture = ReleaseTestFixture.Create();
        var manager = new GroupManager(fixture.Service, new PersistenceService(fixture.Log, fixture.StatePath), fixture.Log);
        Group group = manager.CreateGroup();
        group.Members.Add(fixture.Captured);

        Assert.Equal(WindowReleaseOutcome.Released, manager.ReleaseTab(group, 0));

        Assert.False(group.HasEmergencyLayoutSnapshot);
        Assert.Empty(group.PersistedTabs);
        Assert.Empty(new PersistenceService(fixture.Log, fixture.StatePath).Load());
    }
}
