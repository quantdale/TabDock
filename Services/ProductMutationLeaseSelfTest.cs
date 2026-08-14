using System;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace TabDock.Services;

internal static class ProductMutationLeaseSelfTest
{
    internal static bool UserScopedNameRules()
    {
        const string userA = "S-1-5-21-111111111-222222222-333333333-1001";
        const string userB = "S-1-5-21-111111111-222222222-333333333-1002";
        bool sameUserStable = ProductMutationLease.TryBuildUserScopedName(userA, out string first)
            && ProductMutationLease.TryBuildUserScopedName(userA, out string second)
            && string.Equals(first, second, StringComparison.Ordinal);
        bool differentUsersSeparate = ProductMutationLease.TryBuildUserScopedName(userB, out string other)
            && !string.Equals(first, other, StringComparison.Ordinal);
        bool unsafeRejected = !ProductMutationLease.TryBuildUserScopedName(
                "S-1-5-21-evil\\Global\\TabDock",
                out _)
            && !ProductMutationLease.TryBuildUserScopedName("not-a-sid", out _)
            && !ProductMutationLease.TryBuildUserScopedName("S-1-5-21-\u001B", out _);
        return sameUserStable && differentUsersSeparate && unsafeRejected;
    }

    internal static bool AccessControlRulesAreUserScoped()
    {
        const string userSidText = "S-1-5-21-111111111-222222222-333333333-1001";
        const string foreignSidText = "S-1-5-21-111111111-222222222-333333333-1002";
        var userSid = new SecurityIdentifier(userSidText);
        var foreignSid = new SecurityIdentifier(foreignSidText);
        if (!ProductMutationLease.TryBuildMutexSecurity(userSid, out MutexSecurity? security)
            || security == null)
        {
            return false;
        }

        try
        {
            IdentityReference? owner = security.GetOwner(typeof(SecurityIdentifier));
            if (owner is not SecurityIdentifier ownerSid || !ownerSid.Equals(userSid))
                return false;
            if (!security.AreAccessRulesProtected)
                return false;

            int allowRuleCount = 0;
            foreach (MutexAccessRule rule in security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow
                    || rule.IsInherited
                    || rule.IdentityReference is not SecurityIdentifier ruleSid
                    || !ruleSid.Equals(userSid)
                    || rule.MutexRights != ProductMutationLease.RequiredMutexRights)
                {
                    return false;
                }

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
            return allowRuleCount == 1 && foreignNotGranted;
        }
        catch
        {
            return false;
        }
    }

    internal static bool ExclusiveAndReusable()
    {
        string name = ProductMutationLease.NamePrefix + "lease-selftest-" + Guid.NewGuid().ToString("N");
        if (!ProductMutationLease.TryAcquire(out ProductMutationLease? first, name))
            return false;
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
            if (secondAcquired)
                return false;
        }

        if (!ProductMutationLease.TryAcquire(out ProductMutationLease? afterRelease, name))
            return false;
        afterRelease!.Dispose();

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier? currentUserSid = identity.User;
        if (currentUserSid == null
            || !ProductMutationLease.TryBuildMutexSecurity(currentUserSid, out MutexSecurity? ownerSecurity)
            || ownerSecurity == null)
        {
            return false;
        }

        using var ownerReady = new ManualResetEventSlim(false);
        Thread abandonedOwner = new(() =>
        {
            Mutex mutex = MutexAcl.Create(
                initiallyOwned: false,
                name,
                out _,
                ownerSecurity);
            mutex.WaitOne();
            ownerReady.Set();
            // Deliberately do not release or dispose: Windows abandons the
            // mutex when this owner thread exits, and the next waiter must
            // recover ownership through AbandonedMutexException.
        });
        abandonedOwner.Start();
        if (!ownerReady.Wait(TimeSpan.FromSeconds(2)))
            return false;
        abandonedOwner.Join();

        if (!ProductMutationLease.TryAcquire(out ProductMutationLease? recovered, name))
            return false;
        recovered!.Dispose();
        return true;
    }

    internal static bool AccessDeniedAndConstructionFailuresFailClosed()
    {
        string deniedName = @"Global\TabDock-lease-denied-selftest-" + Guid.NewGuid().ToString("N");
        MutexSecurity deniedSecurity = new();
        deniedSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using (Mutex deniedMutex = MutexAcl.Create(
                   initiallyOwned: false,
                   deniedName,
                   out _,
                   deniedSecurity))
        {
            if (ProductMutationLease.TryAcquire(out ProductMutationLease? deniedLease, deniedName))
            {
                deniedLease?.Dispose();
                return false;
            }
        }

        const string userSidText = "S-1-5-21-111111111-222222222-333333333-1001";
        var userSid = new SecurityIdentifier(userSidText);
        var factoryFailurePlatform = new CountingPlatform();
        bool constructionFailedClosed = !ProductMutationLease.TryAcquire(
            out ProductMutationLease? constructionLease,
            @"Local\TabDock-lease-construction-failure-selftest-" + Guid.NewGuid().ToString("N"),
            factoryFailurePlatform,
            () => userSid,
            _ => throw new InvalidOperationException("synthetic ACL construction failure"));
        constructionLease?.Dispose();

        var identityFailurePlatform = new CountingPlatform();
        bool identityFailedClosed = !ProductMutationLease.TryAcquire(
            out ProductMutationLease? identityLease,
            @"Local\TabDock-lease-identity-failure-selftest-" + Guid.NewGuid().ToString("N"),
            identityFailurePlatform,
            () => null,
            null);
        identityLease?.Dispose();

        Mutex leakedHandle = new(initiallyOwned: false);
        var handleFailurePlatform = new CountingPlatform
        {
            OpenResult = true,
            HandleToReturn = leakedHandle,
            SecurityMatches = false,
        };
        bool handleFailureClosed = !ProductMutationLease.TryAcquire(
            out ProductMutationLease? handleLease,
            @"Local\TabDock-lease-handle-failure-selftest-" + Guid.NewGuid().ToString("N"),
            handleFailurePlatform,
            () => userSid,
            _ => ProductMutationLease.TryBuildMutexSecurity(userSid, out MutexSecurity? security)
                ? security
                : null)
            && handleLease == null
            && leakedHandle.SafeWaitHandle.IsClosed;
        handleLease?.Dispose();

        return constructionFailedClosed
            && !factoryFailurePlatform.CreateCalled
            && identityFailedClosed
            && !identityFailurePlatform.OpenCalled
            && handleFailureClosed;
    }

    internal static bool DiagnosticCommandsRemainLeaseIndependent()
    {
        string name = @"Local\TabDock-lease-diagnostics-selftest-" + Guid.NewGuid().ToString("N");
        if (!ProductMutationLease.TryAcquire(out ProductMutationLease? lease, name))
            return false;

        using (lease)
        {
            return DiagnosticCommandLine.Run(new DiagnosticCommandRequest
            {
                Kind = DiagnosticCommandKind.Version,
            }) == 0;
        }
    }

    internal static bool DifferentUserScopedLeasesCanCoexist()
    {
        const string userA = "S-1-5-21-111111111-222222222-333333333-2001";
        const string userB = "S-1-5-21-111111111-222222222-333333333-2002";
        if (!ProductMutationLease.TryBuildUserScopedName(userA, out string nameA)
            || !ProductMutationLease.TryBuildUserScopedName(userB, out string nameB))
        {
            return false;
        }

        if (!ProductMutationLease.TryAcquire(out ProductMutationLease? leaseA, nameA))
            return false;
        using (leaseA)
        {
            if (!ProductMutationLease.TryAcquire(out ProductMutationLease? leaseB, nameB))
                return false;
            leaseB!.Dispose();
        }
        return true;
    }

    private sealed class CountingPlatform : ProductMutationLease.IProductMutationLeasePlatform
    {
        internal bool OpenCalled { get; private set; }
        internal bool CreateCalled { get; private set; }
        internal bool OpenResult { get; init; }
        internal Mutex? HandleToReturn { get; init; }
        internal bool SecurityMatches { get; init; } = true;

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
