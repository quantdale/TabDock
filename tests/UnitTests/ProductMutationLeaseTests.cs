using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former ProductMutationLeaseSelfTest (Wave 4): the
/// SID-scoped, ACL-protected product-mutation lease must be user-scoped,
/// exclusive within a namespace yet reusable across owners and users, and fail
/// closed on every construction/access failure.
/// </summary>
public class ProductMutationLeaseTests
{
    private const string UserSidA = "S-1-5-21-111111111-222222222-333333333-1001";
    private const string UserSidB = "S-1-5-21-111111111-222222222-333333333-1002";

    [Fact]
    public void TryBuildUserScopedName_IsStablePerUserAndDistinctAcrossUsers()
    {
        Assert.True(ProductMutationLease.TryBuildUserScopedName(UserSidA, out string first));
        Assert.True(ProductMutationLease.TryBuildUserScopedName(UserSidA, out string second));
        Assert.Equal(first, second);

        Assert.True(ProductMutationLease.TryBuildUserScopedName(UserSidB, out string other));
        Assert.NotEqual(first, other);
    }

    [Theory]
    [InlineData("S-1-5-21-evil\\Global\\TabDock")]
    [InlineData("not-a-sid")]
    [InlineData("S-1-5-21-\u001B")]
    public void TryBuildUserScopedName_UnsafeIdentityTextIsRejected(string sidText)
    {
        Assert.False(ProductMutationLease.TryBuildUserScopedName(sidText, out _));
    }

    [Fact]
    public void TryBuildMutexSecurity_GrantsOnlyCurrentUserRequiredRights()
    {
        var userSid = new SecurityIdentifier(UserSidA);
        var foreignSid = new SecurityIdentifier(UserSidB);

        Assert.True(ProductMutationLease.TryBuildMutexSecurity(userSid, out MutexSecurity? security));
        Assert.NotNull(security);

        Assert.Equal(userSid, security!.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);

        int allowRuleCount = 0;
        foreach (MutexAccessRule rule in security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            targetType: typeof(SecurityIdentifier)))
        {
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.False(rule.IsInherited);
            Assert.Equal(userSid, rule.IdentityReference);
            Assert.Equal(ProductMutationLease.RequiredMutexRights, rule.MutexRights);
            allowRuleCount++;
        }

        bool foreignNotGranted = !security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<MutexAccessRule>()
            .Any(rule => rule.IdentityReference is SecurityIdentifier ruleSid
                && ruleSid.Equals(foreignSid)
                && rule.AccessControlType == AccessControlType.Allow);
        Assert.Equal(1, allowRuleCount);
        Assert.True(foreignNotGranted);
    }

    [Fact]
    public void Lease_IsExclusiveThenReusableAndRecoversAbandonedOwnership()
    {
        string name = ProductMutationLease.NamePrefix + "lease-test-" + Guid.NewGuid().ToString("N");
        Assert.True(ProductMutationLease.TryAcquire(out ProductMutationLease? first, name));
        using (first)
        {
            bool secondAcquired = false;
            Thread secondThread = new(() =>
            {
                if (ProductMutationLease.TryAcquire(out ProductMutationLease? second, name))
                {
                    secondAcquired = true;
                    second?.Dispose();
                }
            });
            secondThread.Start();
            secondThread.Join();
            Assert.False(secondAcquired, "a second same-user owner must not acquire the held lease");
        }

        Assert.True(ProductMutationLease.TryAcquire(out ProductMutationLease? afterRelease, name));
        afterRelease!.Dispose();

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier? currentUserSid = identity.User;
        Assert.NotNull(currentUserSid);
        Assert.True(ProductMutationLease.TryBuildMutexSecurity(currentUserSid!, out MutexSecurity? ownerSecurity));
        Assert.NotNull(ownerSecurity);

        using var ownerReady = new ManualResetEventSlim(false);
        using var ownerMayExit = new ManualResetEventSlim(false);
        Thread abandonedOwner = new(() =>
        {
            Mutex mutex = MutexAcl.Create(
                initiallyOwned: false,
                name,
                out _,
                ownerSecurity!);
            mutex.WaitOne();
            ownerReady.Set();
            // Deliberately do not release or dispose: Windows abandons the
            // mutex when this owner thread exits, and the next waiter must
            // recover ownership through AbandonedMutexException.
            // Keep the managed handle alive until the delegate returns. Under
            // full-suite GC pressure, allowing the local to become dead after
            // ownerReady.Set() can finalize the handle before thread exit and
            // turn the intended abandoned-owner ordering into an ordinary
            // still-owned/recreated-object race.
            ownerMayExit.Wait();
            GC.KeepAlive(mutex);
        });
        abandonedOwner.Start();
        Assert.True(ownerReady.Wait(TimeSpan.FromSeconds(2)), "abandoned-owner setup timed out");
        ownerMayExit.Set();
        abandonedOwner.Join();

        Assert.True(ProductMutationLease.TryAcquire(out ProductMutationLease? recovered, name));
        recovered!.Dispose();
    }

    [Fact]
    public void Lease_AccessDeniedAndConstructionFailures_FailClosed()
    {
        string deniedName = @"Global\TabDock-lease-denied-test-" + Guid.NewGuid().ToString("N");
        MutexSecurity deniedSecurity = new();
        deniedSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using (Mutex deniedMutex = MutexAcl.Create(
                   initiallyOwned: false,
                   deniedName,
                   out _,
                   deniedSecurity))
        {
            Assert.True(ProductMutationLease.TryAcquire(out _, deniedName) == false,
                "an access-denied pre-existing object must not be acquired or weakened");
        }

        var userSid = new SecurityIdentifier(UserSidA);

        // Synthetic ACL-construction failure must fail closed without creating.
        var factoryFailurePlatform = new CountingPlatform();
        Assert.False(ProductMutationLease.TryAcquire(
            out ProductMutationLease? constructionLease,
            @"Local\TabDock-lease-construction-failure-test-" + Guid.NewGuid().ToString("N"),
            factoryFailurePlatform,
            () => userSid,
            _ => throw new InvalidOperationException("synthetic ACL construction failure")));
        constructionLease?.Dispose();
        Assert.False(factoryFailurePlatform.CreateCalled);

        // Unprovable current-user identity must fail closed without opening.
        var identityFailurePlatform = new CountingPlatform();
        Assert.False(ProductMutationLease.TryAcquire(
            out ProductMutationLease? identityLease,
            @"Local\TabDock-lease-identity-failure-test-" + Guid.NewGuid().ToString("N"),
            identityFailurePlatform,
            () => null,
            null));
        identityLease?.Dispose();
        Assert.False(identityFailurePlatform.OpenCalled);

        // A pre-existing object with unexpected security is closed, never used.
        Mutex leakedHandle = new(initiallyOwned: false);
        var handleFailurePlatform = new CountingPlatform
        {
            OpenResult = true,
            HandleToReturn = leakedHandle,
            SecurityMatches = false,
        };
        Assert.False(ProductMutationLease.TryAcquire(
            out ProductMutationLease? handleLease,
            @"Local\TabDock-lease-handle-failure-test-" + Guid.NewGuid().ToString("N"),
            handleFailurePlatform,
            () => userSid,
            _ => ProductMutationLease.TryBuildMutexSecurity(userSid, out MutexSecurity? security)
                ? security
                : null));
        Assert.Null(handleLease);
        Assert.True(leakedHandle.SafeWaitHandle.IsClosed);
    }

    [Fact]
    public void DiagnosticCommands_DoNotRequireTheMutationLease()
    {
        string name = @"Local\TabDock-lease-diagnostics-test-" + Guid.NewGuid().ToString("N");
        Assert.True(ProductMutationLease.TryAcquire(out ProductMutationLease? lease, name));

        using (lease)
        {
            Assert.Equal(0, DiagnosticCommandLine.Run(new DiagnosticCommandRequest
            {
                Kind = DiagnosticCommandKind.Version,
            }));
        }
    }

    [Fact]
    public void DifferentUserScopedLeases_CanCoexist()
    {
        Assert.True(ProductMutationLease.TryBuildUserScopedName(UserSidA, out string nameA));
        Assert.True(ProductMutationLease.TryBuildUserScopedName(UserSidB, out string nameB));

        Assert.True(ProductMutationLease.TryAcquire(out ProductMutationLease? leaseA, nameA));
        using (leaseA)
        {
            Assert.True(ProductMutationLease.TryAcquire(out ProductMutationLease? leaseB, nameB));
            leaseB!.Dispose();
        }
    }

    private sealed class CountingPlatform : ProductMutationLease.IProductMutationLeasePlatform
    {
        public bool OpenCalled { get; private set; }
        public bool CreateCalled { get; private set; }
        public bool OpenResult { get; init; }
        public Mutex? HandleToReturn { get; init; }
        public bool SecurityMatches { get; init; } = true;

        public bool TryOpenExisting(string name, MutexRights rights, out Mutex? mutex)
        {
            OpenCalled = true;
            mutex = HandleToReturn;
            return OpenResult;
        }

        public Mutex Create(
            bool initiallyOwned,
            string name,
            out bool createdNew,
            MutexSecurity security)
        {
            CreateCalled = true;
            createdNew = true;
            return HandleToReturn ?? new Mutex(initiallyOwned: false);
        }

        public bool HasExpectedSecurity(Mutex mutex, SecurityIdentifier currentUserSid)
            => SecurityMatches;
    }
}
