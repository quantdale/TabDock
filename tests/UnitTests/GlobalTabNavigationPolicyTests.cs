using System;
using System.IO;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

public sealed class GlobalTabNavigationPolicyTests
{
    private static CapturedWindow Window(int id) => new()
    {
        Hwnd = new IntPtr(0x5000 + id),
        ProcessId = (uint)(7000 + id),
        WindowThreadId = (uint)(8000 + id),
        WindowIdentityToken = 9000 + id,
        ExePath = $"guest{id}.exe",
        OriginalClassName = "Pig",
        OriginalTitle = $"Guest {id}",
    };

    [Fact]
    public void CapturedGuest_ResolvesItsOwningGroup()
    {
        var guest = Window(1);
        Guid groupId = Guid.NewGuid();
        bool containerResolverCalled = false;

        bool resolved = GlobalTabNavigationPolicy.TryResolve(
            guest.Hwnd,
            _ => guest,
            _ => groupId,
            _ =>
            {
                containerResolverCalled = true;
                return null;
            },
            _ => true,
            out GlobalTabNavigationTarget target);

        Assert.True(resolved);
        Assert.Equal(groupId, target.GroupId);
        Assert.Same(guest, target.CapturedGuest);
        Assert.False(containerResolverCalled);
    }

    [Fact]
    public void ContainerChrome_ResolvesItsOwningGroupWhenNoGuestMatches()
    {
        Guid groupId = Guid.NewGuid();
        bool resolved = GlobalTabNavigationPolicy.TryResolve(
            new IntPtr(0x6001),
            _ => null,
            _ => null,
            _ => groupId,
            _ => true,
            out GlobalTabNavigationTarget target);

        Assert.True(resolved);
        Assert.Equal(groupId, target.GroupId);
        Assert.Null(target.CapturedGuest);
    }

    [Fact]
    public void UnrelatedForeground_IsStrictNoOp()
    {
        bool resolved = GlobalTabNavigationPolicy.TryResolve(
            new IntPtr(0x6002),
            _ => null,
            _ => null,
            _ => null,
            _ => true,
            out GlobalTabNavigationTarget target);

        Assert.False(resolved);
        Assert.Equal(default, target);
    }

    [Fact]
    public void StaleOrRecycledCapturedGuest_IsRejectedBeforeGroupResolution()
    {
        var stale = Window(2);
        bool groupResolverCalled = false;

        bool resolved = GlobalTabNavigationPolicy.TryResolve(
            stale.Hwnd,
            _ => stale,
            _ =>
            {
                groupResolverCalled = true;
                return Guid.NewGuid();
            },
            _ => Guid.NewGuid(),
            _ => false,
            out _);

        Assert.False(resolved);
        Assert.False(groupResolverCalled);
    }

    [Fact]
    public void StaleCapturedGuest_DoesNotFallBackToAContainerGroup()
    {
        var stale = Window(4);
        bool containerResolverCalled = false;

        bool resolved = GlobalTabNavigationPolicy.TryResolve(
            stale.Hwnd,
            _ => stale,
            _ => Guid.NewGuid(),
            _ =>
            {
                containerResolverCalled = true;
                return Guid.NewGuid();
            },
            _ => false,
            out _);

        Assert.False(resolved);
        Assert.False(containerResolverCalled);
    }

    [Fact]
    public void MissingGroupIdentity_IsFailClosed()
    {
        var guest = Window(3);
        bool resolved = GlobalTabNavigationPolicy.TryResolve(
            guest.Hwnd,
            _ => guest,
            _ => null,
            _ => Guid.NewGuid(),
            _ => true,
            out _);

        Assert.False(resolved);
    }
}

public sealed class HotkeyRegistrationPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void PartialOrFailedRegistration_IsUnavailable(bool previous, bool next)
        => Assert.False(HotkeyRegistrationPolicy.IsTabNavigationPairAvailable(previous, next));

    [Fact]
    public void BothDirectionsRegistered_AreAvailable()
        => Assert.True(HotkeyRegistrationPolicy.IsTabNavigationPairAvailable(true, true));

    [Fact]
    public void RegistrationUsesPageKeysNoRepeatAndDoesNotAdvertiseArrowKeys()
    {
        string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Services", "HotkeyService.cs"));
        Assert.Contains("MOD_NOREPEAT", source, StringComparison.Ordinal);
        Assert.Contains("VK_PRIOR", source, StringComparison.Ordinal);
        Assert.Contains("VK_NEXT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VK_LEFT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VK_RIGHT", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TabDock.sln not found above test output directory.");
    }
}
